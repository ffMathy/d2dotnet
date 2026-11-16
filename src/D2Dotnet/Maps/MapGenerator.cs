// Port of the generation driver of libd2 packages/drlg/src/lib.zig (MIT, (c) 2026 jaenster):
// generateActFull / collectLevelFull / collectLevelAdjacents / collectLevelPresets /
// buildLevelRoomColl / linkLevelWarpNodes / roomWindow / roomPMap / loadLevelDts / maskDts /
// roomDts / compositeLevelRaw / the seam pass (appendSeamAdjacents, buildSeamConn) and
// appendOutdoorShrines.

using D2Dotnet.Data;
using D2Dotnet.Files;
using D2Dotnet.Maps.Engine;
using EngineDrlg = D2Dotnet.Maps.Engine.Drlg;

namespace D2Dotnet.Maps;

/// <summary>
/// Generates the maps of Diablo II: every level of an act, for a seed and a difficulty, with its rooms, collision map,
/// preset units, exits and seeded shrines, as the game's level generator (DRLG) builds them.
/// </summary>
/// <remarks>
/// <para>
/// The generator is a port of <see href="https://github.com/jaenster/libd2">libd2</see>'s DRLG (the 1.14d engine, bit
/// for bit). It reads levels.txt and the other level tables, and the <c>.ds1</c>/<c>.dt1</c> tile files, from the game:
/// the tables when it is created, the tile files the first time a level needs them.
/// </para>
/// <para>
/// Thread-safe: each call runs on its own state (a native memory pool freed at the end of the call), so one generator
/// can serve several threads at once; the tables and the parsed tile files are shared.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var game = GameInstall.Open(@"C:\Program Files (x86)\Diablo II Resurrected");
/// GeneratedAct act = game.Maps.Generate(seed: 0x1234, Difficulty.Hell, ActId.Act2);
/// MapLevel? sewers = act.Find(AreaId.SewersLevel1Act2);
/// </code>
/// </example>
public sealed class MapGenerator
{
    private const int SUB = 5;

    private readonly DrlgData _data;

    /// <summary>Waypoint objects (Objects.txt rows of the waypoint class, all acts).</summary>
    private static readonly HashSet<int> WaypointObjectIds = [119, 145, 156, 157, 237, 238, 288, 323, 324, 398, 402, 429, 494, 496, 511, 539];

    /// <summary>A generator over the tables and tile files of an install (see also <see cref="GameInstall.Maps"/>).</summary>
    /// <param name="game">The game.</param>
    /// <exception cref="FileNotFoundException">A level table is missing.</exception>
    /// <exception cref="InvalidDataException">The game has no DS1 object table (objpreset.txt).</exception>
    public MapGenerator(GameInstall game)
        : this((game ?? throw new ArgumentNullException(nameof(game))).Data)
    {
    }

    /// <summary>A generator over the tables of <paramref name="data"/> and the tile files of its <see cref="GameDatabase.Files"/>.</summary>
    /// <param name="data">The game data.</param>
    /// <param name="presetObjectTable">
    /// The objects.txt row each DS1 object id below 150 places, for each act (750 entries, index <c>act * 150 + id</c>;
    /// the game's <c>gpsPresetObjectTable</c>). Null to read it from <c>objpreset.txt</c> (or <c>obj.txt</c>).
    /// </param>
    /// <exception cref="FileNotFoundException">A level table is missing.</exception>
    /// <exception cref="InvalidDataException">There is no DS1 object table.</exception>
    /// <exception cref="ArgumentException"><paramref name="presetObjectTable"/> has fewer than 750 entries.</exception>
    public MapGenerator(GameDatabase data, IReadOnlyList<int>? presetObjectTable = null)
        : this(new GameDataSource(data ?? throw new ArgumentNullException(nameof(data)), presetObjectTable))
    {
    }

    /// <summary>A generator over any data source (the tests feed libd2's own baked data through this).</summary>
    internal MapGenerator(IMapDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _data = new DrlgData(source);
    }

    /// <summary>The levels of an act (the levels.txt rows whose <c>Act</c> is <paramref name="act"/>), in file order.</summary>
    public IReadOnlyList<AreaId> GetAreas(ActId act)
    {
        var ids = new List<AreaId>();
        var tb = _data.Tables;
        for (int row = 0; row < tb.LevelCount; row++)
            if (tb.LevelAtRow(row) is LevelRow lv && lv.Act == (int)act) ids.Add((AreaId)lv.Id);
        return ids;
    }

    /// <summary>Generate every level of an act.</summary>
    /// <param name="seed">The map seed.</param>
    /// <param name="difficulty">The difficulty (level sizes and maze room counts differ per difficulty).</param>
    /// <param name="act">The act.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="difficulty"/> or <paramref name="act"/> is not defined.</exception>
    public GeneratedAct Generate(uint seed, Difficulty difficulty, ActId act) => Generate(seed, difficulty, act, null);

    /// <summary>Test seam: <paramref name="beforeCollision"/> sees each level (a D2DrlgLevelStrc*) after
    /// its rooms and warp exits are read and before its rooms are built for collision.</summary>
    internal GeneratedAct Generate(uint seed, Difficulty difficulty, ActId act, Action<nint>? beforeCollision)
    {
        if (act < ActId.Act1 || act > ActId.Act5) throw new ArgumentOutOfRangeException(nameof(act), act, "The act must be Act1 to Act5.");
        if (difficulty < Difficulty.Normal || difficulty > Difficulty.Hell) throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "Unknown difficulty.");

        var savedPool = Pool.Current;
        var pool = new NativePool();
        Pool.Current = pool;
        try
        {
            GenContext.Bind(_data);
            DrlgTables.Bind(_data.Tables);
            return Generate(seed, difficulty, (int)act, beforeCollision);
        }
        finally
        {
            TileGen.ResetLibs();
            DrlgTables.Unbind();
            GenContext.Bind(null);
            Pool.Current = savedPool;
            pool.Dispose();
        }
    }

    private sealed class LevelWork
    {
        public int Id;
        public LevelRow Row;
        public bool Placed;
        public int OriginX, OriginY, Width, Height;
        public MapRoom[] Rooms = [];
        public List<LevelExit> Exits = [];
        public PresetUnit[] Presets = [];
        public Shrine[] Shrines = [];
        public CollisionMap? Collision;
    }

    private unsafe GeneratedAct Generate(uint seed, Difficulty difficulty, int actNo, Action<nint>? beforeCollision)
    {
        var tb = _data.Tables;
        var act = ActGraph.Build(tb, actNo, seed);

        var pDrlg = Pool.Alloc<D2DrlgStrc>();
        EngineDrlg.AllocDrlgActMisc(pDrlg, 1, seed, 0, 0, (byte)difficulty);

        var ids = GetAreas((ActId)actNo).Select(a => (int)a).ToList();

        // Pass 1: real world coords BEFORE orths.
        foreach (int lid in ids)
        {
            var pLevel = EngineDrlg.GetLevelAndAlloc(pDrlg, lid);
            var c = act.LevelCoords(tb, lid);
            pLevel->sCoordinatesAndSize.WorldPosition.x = c.x;
            pLevel->sCoordinatesAndSize.WorldPosition.y = c.y;
            pLevel->sCoordinatesAndSize.WorldSize.x = c.w;
            pLevel->sCoordinatesAndSize.WorldSize.y = c.h;
        }
        if (actNo == 0) EngineDrlg.ApplyAct1PresetPicks(pDrlg, tb, seed);
        if (actNo == 1) EngineDrlg.ApplyAct2PresetPicks(pDrlg, tb, seed);
        EngineDrlg.BuildInterLevelOrths(pDrlg);

        // Pass 2: generate each level once; pull rooms + adjacents + collision + presets from the
        // live rooms.
        var works = new List<LevelWork>(ids.Count);
        foreach (int lid in ids)
        {
            var pLevel = EngineDrlg.GetLevelAndAlloc(pDrlg, lid);
            // On-demand, like the engine: the collision builder's warp-node linking inits
            // warp-DESTINATION levels early, and a second InitLevel would regenerate and corrupt
            // them.
            if (pLevel->pRoomExFirst == null) EngineDrlg.InitLevel(pLevel);
            works.Add(CollectLevelFull(act, pLevel, lid, beforeCollision));
        }

        // Pass 3: cross-level SEAM adjacency. Pure geometry over the placed rooms — no engine
        // call, no RNG. Union with the per-room warp slots already gathered.
        var conns = new List<int[]>(works.Count);
        foreach (var w in works) conns.Add(BuildSeamConn(pDrlg, act.Warps, w.Id, ids));
        for (int i = 0; i < works.Count; i++) AppendSeamAdjacents(works[i].Exits, works, conns, i);

        // Outdoor shrines: a read of the generated rooms (no RNG of the generation).
        foreach (var w in works)
        {
            var pLevel = EngineDrlg.GetLevelAndAlloc(pDrlg, w.Id);
            w.Shrines = OutdoorShrines(pLevel);
        }

        var levels = new List<MapLevel>(works.Count);
        foreach (var w in works)
        {
            var wps = new List<PresetUnit>();
            foreach (var p in w.Presets)
                if (p.Kind == PresetKind.Object && WaypointObjectIds.Contains(p.ClassId)) wps.Add(p);
            levels.Add(new MapLevel
            {
                Id = (AreaId)w.Id,
                Layout = (LevelLayout)w.Row.DrlgType,
                IsPlaced = w.Placed,
                Bounds = new Rect(w.OriginX, w.OriginY, w.Width, w.Height),
                Rooms = w.Rooms,
                Presets = w.Presets,
                Exits = w.Exits.ToArray(),
                Shrines = w.Shrines,
                Waypoints = wps.ToArray(),
                Collision = w.Collision,
            });
        }
        int staffTomb = pDrlg->nStaffTombLevel;
        return new GeneratedAct
        {
            Act = (ActId)actNo,
            Seed = seed,
            Difficulty = difficulty,
            // The engine draws the staff tomb for every act; only Act II has the tombs.
            StaffTomb = actNo == (int)ActId.Act2 && staffTomb != 0 ? (AreaId)staffTomb : null,
            Levels = levels,
        };
    }

    /// <summary>A room's DS1/theme selector: a preset room (nPresetType==2) reports its picked DS1
    /// file variant; an outdoor/maze room (nPresetType==1) reports its picked subtheme variant.</summary>
    private static unsafe int RoomPickedFile(D2RoomExStrc* p)
    {
        if (p->pRoomExData == null) return -1;
        if (p->nPresetType == 2) return ((D2DrlgPresetRoomStrc*)p->pRoomExData)->nPickedFile;
        if (p->nPresetType == 1) return ((D2DrlgRoomExDataMazeStrc*)p->pRoomExData)->nSubThemePicked;
        return -1;
    }

    /// <summary>collectLevelFull: rooms/meta, warp adjacents, the raw CollMap, then the preset
    /// units (after the rooms are materialized: that is when an outdoor room takes the preset
    /// units of its LvlSub files, as the engine's room build does).</summary>
    private unsafe LevelWork CollectLevelFull(ActGraph act, D2DrlgLevelStrc* pLevel, int lid, Action<nint>? beforeCollision)
    {
        var rooms = new List<MapRoom>();
        for (var p = pLevel->pRoomExFirst; p != null; p = p->pRoomExNext)
            rooms.Add(new MapRoom(new Rect(p->sCoords.WorldPosition.x, p->sCoords.WorldPosition.y, p->sCoords.WorldSize.x, p->sCoords.WorldSize.y),
                p->nType, p->nPresetType, RoomPickedFile(p)));
        var row = _data.Tables.Level(lid)!.Value;
        var w = new LevelWork
        {
            Id = lid,
            Row = row,
            Placed = act.Positions.ContainsKey(lid),
            OriginX = pLevel->sCoordinatesAndSize.WorldPosition.x,
            OriginY = pLevel->sCoordinatesAndSize.WorldPosition.y,
            Width = pLevel->sCoordinatesAndSize.WorldSize.x,
            Height = pLevel->sCoordinatesAndSize.WorldSize.y,
            Rooms = rooms.ToArray(),
        };
        w.Exits.AddRange(CollectLevelAdjacents(pLevel));
        beforeCollision?.Invoke((nint)pLevel);
        w.Collision = MaterializeLevelColl(pLevel, lid);
        w.Presets = CollectLevelPresets(pLevel);
        return w;
    }

    // ---- presets / adjacents ------------------------------------------------------------

    /// <summary>collectLevelPresets: each room's preset list as its build left it, in level-local
    /// subtiles, clipped to the level rect and deduped by (etype,class,x,y).</summary>
    private static unsafe PresetUnit[] CollectLevelPresets(D2DrlgLevelStrc* pLevel)
    {
        var outList = new List<PresetUnit>();
        var seen = new HashSet<ulong>();
        int ox = pLevel->sCoordinatesAndSize.WorldPosition.x * SUB;
        int oy = pLevel->sCoordinatesAndSize.WorldPosition.y * SUB;
        int bw = pLevel->sCoordinatesAndSize.WorldSize.x * SUB;
        int bh = pLevel->sCoordinatesAndSize.WorldSize.y * SUB;
        for (var p = pLevel->pRoomExFirst; p != null; p = p->pRoomExNext)
        {
            int rx = p->sCoords.WorldPosition.x * SUB - ox;
            int ry = p->sCoords.WorldPosition.y * SUB - oy;
            for (var u = p->pPresetUnit; u != null; u = u->pPresetUnitNext)
            {
                int et = u->eType;
                if (et != 1 && et != 2) continue;
                int code = et == 1 && u->nDbmCode >= 0 ? u->nDbmCode : u->nClassId;
                int lx = rx + u->nPosX, ly = ry + u->nPosY;
                if (lx < 0 || ly < 0 || lx >= bw || ly >= bh) continue; // outside the level rect
                ulong k = ((ulong)(long)et << 56) ^ ((ulong)(long)code << 40) ^ ((ulong)(lx & 0xfffff) << 20) ^ (ulong)(ly & 0xfffff);
                if (!seen.Add(k)) continue;
                outList.Add(new PresetUnit((PresetKind)et, code, new Point(lx, ly)));
            }
        }
        return outList.ToArray();
    }

    /// <summary>collectLevelAdjacents: one entry per (room, set warp slot) whose destination
    /// resolves (getWarpDestinationFromArray != -1), at the room centre in level-local subtiles;
    /// the destination is the vis-array entry.</summary>
    private static unsafe List<LevelExit> CollectLevelAdjacents(D2DrlgLevelStrc* pLevel)
    {
        var outList = new List<LevelExit>();
        int ox = pLevel->sCoordinatesAndSize.WorldPosition.x * SUB;
        int oy = pLevel->sCoordinatesAndSize.WorldPosition.y * SUB;
        var vis = DrlgRoom.GetVisArrayFromLevelId(pLevel->pDrlg, pLevel->eD2LevelId);
        for (var p = pLevel->pRoomExFirst; p != null; p = p->pRoomExNext)
        {
            if (!p->eRoomExFlags.AnyWarp()) continue;
            int cx = (p->sCoords.WorldSize.x / 2 + p->sCoords.WorldPosition.x) * SUB - ox;
            int cy = (p->sCoords.WorldSize.y / 2 + p->sCoords.WorldPosition.y) * SUB - oy;
            for (byte slot = 0; slot < 8; slot++)
            {
                if (!p->eRoomExFlags.WarpSlot(slot)) continue;
                if (DrlgRoom.GetWarpDestinationFromArray(pLevel, slot) != -1)
                {
                    int dest = vis[slot];
                    if (dest != 0) outList.Add(new LevelExit((AreaId)dest, new Point(cx, cy), ExitKind.Warp));
                }
            }
        }
        return outList;
    }

    /// <summary>Signed axis gap between two world-TILE spans, as DrlgRoom.cpp DefineRoomsNear
    /// (0x66bc20) computes it.</summary>
    private static int AxisGap(int ap, int aw, int bp, int bw) => ap < bp ? bp - aw - ap : ap - bw - bp;

    /// <summary>appendSeamAdjacents: a bridge for every pair (room R of the target level, room S
    /// of a STITCHED neighbour level) that DefineRoomsNear deems NEAR — inter-box gap &lt; 6 tiles
    /// on BOTH axes — at R's centre in the target's level-local subtile frame.</summary>
    private static void AppendSeamAdjacents(List<LevelExit> outList, List<LevelWork> boxes, List<int[]> conns, int target_idx)
    {
        var t = boxes[target_idx];
        var conn = conns[target_idx];
        foreach (var room in t.Rooms)
        {
            var r = room.Bounds;
            int cx = (r.X - t.OriginX) * SUB + r.Width * SUB / 2;
            int cy = (r.Y - t.OriginY) * SUB + r.Height * SUB / 2;
            for (int bi = 0; bi < boxes.Count; bi++)
            {
                if (bi == target_idx) continue;
                var b = boxes[bi];
                if (Array.IndexOf(conn, b.Id) < 0) continue;
                foreach (var other in b.Rooms)
                {
                    var o = other.Bounds;
                    if (AxisGap(r.X, r.Width, o.X, o.Width) < 6 && AxisGap(r.Y, r.Height, o.Y, o.Height) < 6)
                        outList.Add(new LevelExit((AreaId)b.Id, new Point(cx, cy), ExitKind.Seam));
                }
            }
        }
    }

    /// <summary>Level-id ranges swept by DRLGACT_SetWarpConnectionsBetweenTwoAreas (0x6775c0).
    /// Acts I/II/IV stitch through the placement chain instead; Act III (0x6789b0) and Act V
    /// (0x678ad0) have no chain and get this pairwise sweep.</summary>
    private static readonly (int Lo, int Hi)[] WARP_CONNECTION_SWEEPS =
    {
        (75, 83), // Kurast Docktown .. Travincal
        (111, 112), // Frigid Highlands .. Arreat Plateau
        (110, 111), // Bloody Foothills .. Frigid Highlands
        (109, 110), // Harrogath .. Bloody Foothills
    };

    /// <summary>DRLGCOORDS_IsAdjacentOrOverlapping (0x66b880) at nMaxDist == -1: touching on one
    /// axis (gap == 0) and strictly overlapping on the other.</summary>
    private static bool LevelBoxesTouch(D2DrlgCoordsStrc a, D2DrlgCoordsStrc b)
    {
        int gx = AxisGap(a.WorldPosition.x, a.WorldSize.x, b.WorldPosition.x, b.WorldSize.x);
        int gy = AxisGap(a.WorldPosition.y, a.WorldSize.y, b.WorldPosition.y, b.WorldSize.y);
        if (gx == 0 && gy <= -1) return true;
        if (gy != 0) return false;
        return gx <= -1;
    }

    /// <summary>buildSeamConn: the seed-invariant STITCH set for a level — forward + reverse Vis,
    /// the act placement chain, and the WARP_CONNECTION_SWEEPS pairwise level-box sweep.</summary>
    private static unsafe int[] BuildSeamConn(D2DrlgStrc* pDrlg, List<(long From, long To)> warps, int level_id, List<int> all_ids)
    {
        var set = new List<int>();
        void Add(int id)
        {
            if (id != 0 && !set.Contains(id)) set.Add(id);
        }
        var vis = DrlgRoom.GetVisArrayFromLevelId(pDrlg, level_id);
        for (int i = 0; i < 8; i++) Add(vis[i]);
        foreach (int oid in all_ids)
        {
            if (oid == level_id) continue;
            var vo = DrlgRoom.GetVisArrayFromLevelId(pDrlg, oid);
            for (int i = 0; i < 8; i++)
            {
                if (vo[i] == level_id)
                {
                    Add(oid);
                    break;
                }
            }
        }
        foreach (var (from, to) in warps)
        {
            if ((int)from == level_id) Add((int)to);
            if ((int)to == level_id) Add((int)from);
        }
        var a = EngineDrlg.GetLevelAndAlloc(pDrlg, level_id)->sCoordinatesAndSize;
        foreach (var (lo, hi) in WARP_CONNECTION_SWEEPS)
        {
            if (level_id < lo || level_id > hi) continue;
            for (int other = lo; other <= hi; other++)
            {
                if (other == level_id) continue;
                var b = EngineDrlg.GetLevelAndAlloc(pDrlg, other)->sCoordinatesAndSize;
                if (LevelBoxesTouch(a, b)) Add(other);
            }
        }
        return set.ToArray();
    }

    // ---- shrines --------------------------------------------------------------------------

    /// <summary>The four Act-1/2 outdoor shrine LvlSub Type-5 variants, indexed by the style bit
    /// SpawnAct12Shrines cycles into eRoomExFlags (0x1000-&gt;0 ShrineW .. 0x8000-&gt;3 ShrineH).
    /// `groups`/`box` are the DS1 substitution-group count + square tile box; `classes` map the
    /// rolled substgroup index -&gt; resolved objects.txt row. ShrineW carries two 3x3 groups: g0 =
    /// Well (class 130), g1 = Shrine (class 84). ShrineD/F/H each carry one 4x4 group with a single
    /// Shrine (class 2/81/83). Every DS1 object sits at +(7,7) subtiles from its group base.</summary>
    private static readonly (uint groups, int box, int c0, int c1)[] SHRINE_VARIANTS =
    {
        (2, 3, 130, 84),
        (1, 4, 2, 2),
        (1, 4, 81, 81),
        (1, 4, 83, 83),
    };

    /// <summary>appendOutdoorShrines: faithful replay of InitGridCells (0067d2d0) -&gt;
    /// SubTypeWpShrine (006707a0) -&gt; DoNotCheckAll (00670170) for the rooms SpawnAct12Shrines
    /// (00674e40) flagged: roll the LvlSub Type-5 placement on the room's {nSeed,0x29a} seed and
    /// place the variant's object at the first shuffled position +(7,7) subtiles.
    /// DRLGOUTDOOR_CheckSubTileOverlap rejection is not modelled (as in libd2).</summary>
    /// <param name="pLevel">The generated level.</param>
    /// <param name="act1ShrineFilesOnly">The SubShrine == 5 gate (libd2 f6f6f79); false reproduces the
    /// earlier behaviour that also rolled the Act I files for Act II rooms (tests only).</param>
    internal static unsafe Shrine[] OutdoorShrines(D2DrlgLevelStrc* pLevel, bool act1ShrineFilesOnly = true)
    {
        var outList = new List<Shrine>();
        if (pLevel->eDrlgType != 3) return outList.ToArray(); // wilderness only
        // SHRINE_VARIANTS models the Act I shrine files (LvlSub Type 5) only.
        var def = DrlgTables.LevelDefsGetLine(pLevel->eD2LevelId);
        if (act1ShrineFilesOnly && (def == null || def->SubShrine != 5)) return outList.ToArray();
        int lox = pLevel->sCoordinatesAndSize.WorldPosition.x * SUB;
        int loy = pLevel->sCoordinatesAndSize.WorldPosition.y * SUB;
        for (var p = pLevel->pRoomExFirst; p != null; p = p->pRoomExNext)
        {
            uint bits = (p->eRoomExFlags.Raw >> 0xc) & 0xf;
            if (bits == 0) continue;
            int style = System.Numerics.BitOperations.TrailingZeroCount(bits);
            if (style > 3) continue;
            var v = SHRINE_VARIANTS[style];
            int nMaxX = p->sCoords.WorldSize.x - v.box;
            int nMaxY = p->sCoords.WorldSize.y - v.box;
            if (nMaxX < 1 || nMaxY < 1) continue;
            var s = new D2SeedStrc { nSeedLow = p->nSeed, nSeedHigh = 0x29a };
            uint groupIdx = DrlgRng.RandomNumberSelector(&s, v.groups);
            int nTotal = nMaxX * nMaxY;
            var cx = new int[nTotal];
            var cy = new int[nTotal];
            for (int i = 0; i < nTotal; i++)
            {
                cx[i] = i % nMaxX;
                cy[i] = i / nMaxX;
            }
            for (int rem = nTotal; rem > 0; rem--)
            {
                uint ia = DrlgRng.RandomNumberSelector(&s, (uint)nTotal);
                uint ib = DrlgRng.RandomNumberSelector(&s, (uint)nTotal);
                (cx[ia], cx[ib]) = (cx[ib], cx[ia]);
                (cy[ia], cy[ib]) = (cy[ib], cy[ia]);
            }
            int class_id = Math.Min(groupIdx, 1u) == 0 ? v.c0 : v.c1;
            int wx = (p->sCoords.WorldPosition.x + cx[0] + 1) * 5 + 7;
            int wy = (p->sCoords.WorldPosition.y + cy[0] + 1) * 5 + 7;
            outList.Add(new Shrine((ObjectId)class_id, new Point(wx - lox, wy - loy)));
        }
        return outList.ToArray();
    }

    // ---- collision ------------------------------------------------------------------------

    /// <summary>The DT1s the engine ALWAYS appends to every room's tile library (verified in 1.14d
    /// Game.exe: Blank.dt1, InvisWal.dt1, Warp.dt1).</summary>
    private static readonly string[] GLOBAL_DT1_PATHS =
    {
        "act1/outdoors/blank.dt1",
        "act1/barracks/inviswal.dt1",
        "act1/barracks/warp.dt1",
    };

    /// <summary>loadLevelDts: a LevelType's DT1 set, recording per loaded entry its LvlTypes File
    /// COLUMN bit (`File N` -&gt; N-1); the always-appended globals get 0xff. Missing files are
    /// skipped.</summary>
    private void LoadLevelDts(string[] files, byte[] cols, int nf, List<NativeDt1> dts, List<byte> bits)
    {
        for (int i = 0; i < nf; i++)
        {
            var d = _data.GetNativeDt1(files[i]);
            if (d == null) continue;
            dts.Add(d);
            bits.Add(cols[i]);
        }
        foreach (var g in GLOBAL_DT1_PATHS)
        {
            var d = _data.GetNativeDt1(g);
            if (d != null) dts.Add(d);
        }
        while (bits.Count < dts.Count) bits.Add(0xff);
    }

    /// <summary>maskDts: the room-scoped DT1 view — entries whose LvlTypes File bit is set in the
    /// mask, plus the globals.</summary>
    private static nint MaskDts(List<NativeDt1> dts, List<byte> bits, uint mask)
    {
        var o = new List<NativeDt1>(dts.Count);
        for (int i = 0; i < dts.Count; i++)
        {
            byte b = bits[i];
            if (b == 0xff || (b < 32 && ((mask >> b) & 1) != 0)) o.Add(dts[i]);
        }
        return TileGen.RegisterLib(o.ToArray());
    }

    private static unsafe nint RoomDts(List<NativeDt1> dts, List<byte> bits, D2DrlgMapStrc* pmap)
    {
        var pt = pmap->pTxtLevelPrest;
        if (pt == null) return TileGen.RegisterLib(dts.ToArray());
        return MaskDts(dts, bits, (uint)pt->Dt1Mask);
    }

    /// <summary>linkLevelWarpNodes: for every warp-flagged room, link each set vis slot to the
    /// destination level's rooms (LinkNearRoomsByVis hangs a warp node carrying the LvlWarp row on
    /// the room's +0x4C chain). Level 0x85 is excluded like the engine.</summary>
    private static unsafe void LinkLevelWarpNodes(D2DrlgLevelStrc* pLevel)
    {
        for (var p = pLevel->pRoomExFirst; p != null; p = p->pRoomExNext)
        {
            if (pLevel->eD2LevelId == 0x85) return;
            var flags = p->eRoomExFlags;
            if (!flags.AnyWarp()) continue;
            for (byte slot = 0; slot < 8; slot++)
                if (flags.WarpSlot(slot)) DrlgRoom.LinkNearRoomsByVis(null, p, slot);
        }
    }

    /// <summary>roomWindow: InitializePresetRoom 0x666ac0 — FillBlanks goes to floor layer 0;
    /// KillEdge drops the +1 col/row when the room touches the pMap's far edge. The InitGridCells
    /// wrapper 0x66ee40 RE-INITIALIZES the room seed as {pRoomEx-&gt;nSeed, 0x29a} before tile
    /// resolution.</summary>
    private static unsafe Ds1RoomWindow RoomWindow(D2RoomExStrc* p, D2DrlgMapStrc* pmap)
    {
        var pt = pmap->pTxtLevelPrest;
        bool kill = pt != null && pt->KillEdge != 0;
        bool farX = p->sCoords.WorldSize.x + p->sCoords.WorldPosition.x == pmap->nSizeX + pmap->nRealOffsetX;
        bool farY = p->sCoords.WorldSize.y + p->sCoords.WorldPosition.y == pmap->nSizeY + pmap->nRealOffsetY;
        return new Ds1RoomWindow
        {
            off_x = p->sCoords.WorldPosition.x - pmap->nRealOffsetX,
            off_y = p->sCoords.WorldPosition.y - pmap->nRealOffsetY,
            size_x = p->sCoords.WorldSize.x,
            size_y = p->sCoords.WorldSize.y,
            seed = new D2SeedStrc { nSeedLow = p->nSeed, nSeedHigh = 0x29a },
            fill_blanks = pt != null ? pt->FillBlanks : 0,
            // The +1 ring is scanned ONLY by the room at the pMap's FAR edge (and LvlPrest KillEdge
            // suppresses it there too).
            kill_x = kill || !farX ? 1 : 0,
            kill_y = kill || !farY ? 1 : 0,
            // Wall layers >= 1: the engine's literal kill rule.
            kill1_x = kill && farX ? 1 : 0,
            kill1_y = kill && farY ? 1 : 0,
            level_id = p->pLevel != null ? p->pLevel->eD2LevelId : 0,
            warp_nodes = p->pTileGrid,
            real_drlg = p->pLevel != null ? p->pLevel->pDrlg : null,
        };
    }

    /// <summary>A room's preset DrlgMap DS1, if it carries one (preset / maze / preset-border).</summary>
    private static unsafe D2DrlgMapStrc* RoomPMap(D2RoomExStrc* p)
    {
        if (p->pRoomExData == null) return null;
        var pmap = ((D2DrlgPresetRoomStrc*)p->pRoomExData)->pMap;
        if (pmap == null) return null;
        if (pmap->pTxtLevelPrest == null || pmap->nSizeX <= 0 || pmap->nSizeY <= 0) return null;
        return pmap;
    }

    private sealed class RoomBuild
    {
        public int wpx, wpy, wtx, hty;
        public CollTile[] tiles = Array.Empty<CollTile>();
        public bool is_ds1;
        public bool foreign;
    }

    /// <summary>materializeLevelColl + compositeLevelRaw: the level's per-room CollMaps composited
    /// into one level-local u16 grid sized to the level's WorldSize*5. Uncovered subtiles keep
    /// 0xFFFF; covered subtiles carry the OR of every contributing room's raw byte.</summary>
    private unsafe CollisionMap? MaterializeLevelColl(D2DrlgLevelStrc* pLevel, int lid)
    {
        if (_data.Tables.Level(lid) == null) return null;
        var rooms = BuildLevelRoomColl(pLevel, lid);
        if (rooms.Count == 0) return null;

        int lx = pLevel->sCoordinatesAndSize.WorldPosition.x * SUB;
        int ly = pLevel->sCoordinatesAndSize.WorldPosition.y * SUB;
        int width = pLevel->sCoordinatesAndSize.WorldSize.x;
        int height = pLevel->sCoordinatesAndSize.WorldSize.y;
        int W, H;
        if (width > 0 && height > 0)
        {
            W = width * SUB;
            H = height * SUB;
        }
        else
        {
            int maxX = 0, maxY = 0;
            foreach (var r in rooms)
            {
                maxX = Math.Max(maxX, r.px - lx + r.w);
                maxY = Math.Max(maxY, r.py - ly + r.h);
            }
            if (maxX <= 0 || maxY <= 0) return null;
            W = maxX;
            H = maxY;
        }

        var cells = new ushort[W * H];
        Array.Fill(cells, (ushort)0xFFFF);
        foreach (var g in rooms)
        {
            int gx = g.px - lx, gy = g.py - ly;
            int gw = g.w, gh = g.h;
            if (gx >= W || gy >= H) continue;
            int x0 = Math.Max(gx, 0), y0 = Math.Max(gy, 0);
            int skip_x = x0 - gx, skip_y = y0 - gy;
            if (skip_x >= gw || skip_y >= gh) continue;
            int span_w = Math.Min(gw - skip_x, W - x0);
            int span_h = Math.Min(gh - skip_y, H - y0);
            for (int y = 0; y < span_h; y++)
            {
                int src = (y + skip_y) * gw + skip_x;
                int dst = (y0 + y) * W + x0;
                for (int x = 0; x < span_w; x++)
                {
                    ushort v = g.cells[src + x];
                    ref ushort d = ref cells[dst + x];
                    d = d == 0xFFFF ? v : (ushort)(d | v);
                }
            }
        }
        return new CollisionMap(W, H, cells);
    }

    private readonly record struct RoomColl(int px, int py, int w, int h, byte[] cells);

    /// <summary>buildLevelRoomColl: build one level's per-room CollMap set exactly as the engine
    /// does (AllocRoomCollisionGrid 0x64c900): materialize every room, then stamp each tile into
    /// every room whose subtile rect contains its origin.</summary>
    private unsafe List<RoomColl> BuildLevelRoomColl(D2DrlgLevelStrc* pLevel, int lid)
    {
        var result = new List<RoomColl>();
        LinkLevelWarpNodes(pLevel);
        if (_data.Tables.Level(lid) is not LevelRow tlv) return result;
        int nLevelType = pLevel->nLevelType;

        // Level DT1 library (LvlTypes File1..32 for this LevelType).
        var files = new string[32];
        var file_cols = new byte[32];
        int nf = _data.Tables.TypeFilesCols(tlv.LvlType, files, file_cols);
        var dts = new List<NativeDt1>();
        var dts_bits = new List<byte>();
        LoadLevelDts(files, file_cols, nf, dts, dts_bits);

        // Phase A: materialize every room in the level to its placed FLOOR/WALL/ROOF tile list
        // (room-local tile coords, incl. the +1 kill-edge). Phase B: for each room R, zero a
        // WorldSize*5 grid and stamp every tile whose WORLD origin subtile falls inside R.
        var rbs = new List<RoomBuild>();

        // Level-wide tile index for the engine's UpdateOrAddTile seam handling.
        var near_tiles = new NearTileIndex();

        // Which room of a gated map loads it decides that map's seeded presets: replay the order a
        // build of every room reaches the rooms' presets.
        Preset.TouchPresetsInBuildOrder(pLevel);
        for (var p = pLevel->pRoomExFirst; p != null; p = p->pRoomExNext)
        {
            // Materializing a room stands in for its InitRoomEx, which runs once per room
            // (tilesLoaded, 0x1000000): only the first build gives the room its preset units.
            bool first_build = !p->eRoomExFlags.TilesLoaded;
            p->eRoomExFlags.TilesLoaded = true;
            CollTile[] tiles;
            var pmap = RoomPMap(p);
            var nearRooms = new NearRooms(near_tiles, p->sCoords.WorldPosition.x, p->sCoords.WorldPosition.y, p->sCoords.WorldSize.x, p->sCoords.WorldSize.y);
            if (pmap != null)
            {
                var path = Preset.PresetDs1Path(pmap);
                if (path == null) continue;
                var d = Preset.CachedDs1(path);
                if (d == null) continue;
                // InitRoomEx order: CreateMonsterPresets, then InitGridCells, whose
                // InitGridsFromDS1File ends in AddAndSetPresetUnits, then the tiles.
                if (first_build)
                {
                    if (!p->eRoomExFlags.PresetsAdded) Preset.CreateMonsterPresets(p, pmap);
                    Preset.AddAndSetPresetUnits(p, pmap);
                }
                var win = RoomWindow(p, pmap);
                win.preset_room = first_build ? p : null;
                tiles = Materialize.MaterializeDs1(d, RoomDts(dts, dts_bits, pmap), win, nearRooms);
            }
            else if (p->eRoomExFlags.NoLos)
            {
                int sub_type2 = -1, sub_theme2 = 0, sub_picked2 = 0;
                if (p->pRoomExData != null)
                {
                    var maze = (D2DrlgRoomExDataMazeStrc*)p->pRoomExData;
                    sub_type2 = maze->nSubType;
                    sub_theme2 = maze->nSubTheme;
                    sub_picked2 = maze->nSubThemePicked;
                }
                // The live room takes the preset units its sub files carry (the outdoor waypoint,
                // the Act I shrines and wells).
                tiles = Materialize.MaterializeOutdoorFloorRoom(MaskDts(dts, dts_bits, (uint)p->nDT1Mask), p->sCoords.WorldSize.x, p->sCoords.WorldSize.y,
                    nLevelType, lid, p->nSeed, Materialize.OutdoorOverlayFor(pLevel, p), sub_type2, sub_theme2, sub_picked2,
                    (int)p->eRoomExFlags.Waypoint, (int)p->eRoomExFlags.ShrineRows, nearRooms, first_build ? p : null);
            }
            else
            {
                continue;
            }
            rbs.Add(new RoomBuild
            {
                wpx = p->sCoords.WorldPosition.x,
                wpy = p->sCoords.WorldPosition.y,
                wtx = p->sCoords.WorldSize.x,
                hty = p->sCoords.WorldSize.y,
                tiles = tiles,
                is_ds1 = pmap != null,
            });
        }

        // Cross-LEVEL contributors. A room's near list (DRLGROOMEX_LinkNearRoomsByVis) reaches
        // into other levels, and AllocRoomCollisionGrid gathers from all of them — so a
        // neighbouring level's +1-ring wall stamps into this level's rooms. Gathered FROM, never
        // emitted.
        var flibs = new List<(int lid, List<NativeDt1> dts, List<byte> bits)>();
        var seen_foreign = new List<nint>();
        for (var p = pLevel->pRoomExFirst; p != null; p = p->pRoomExNext)
        {
            int n = p->nDrlgRoomsExNearCount;
            if (n <= 0 || p->ppDrlgRoomsExNear == null) continue;
            for (int i = 0; i < n; i++)
            {
                var near = p->ppDrlgRoomsExNear[i];
                if (near == null) continue;
                var nlv = near->pLevel;
                if (nlv == null) continue;
                if (nlv == pLevel) continue;
                if (seen_foreign.Contains((nint)near)) continue;
                seen_foreign.Add((nint)near);
                int flid = nlv->eD2LevelId;
                (int lid, List<NativeDt1> dts, List<byte> bits)? found = null;
                foreach (var fl in flibs)
                {
                    if (fl.lid == flid)
                    {
                        found = fl;
                        break;
                    }
                }
                if (found == null)
                {
                    if (_data.Tables.Level(flid) is not LevelRow ftlv) continue;
                    var ffiles = new string[32];
                    var fcols = new byte[32];
                    int fnf = _data.Tables.TypeFilesCols(ftlv.LvlType, ffiles, fcols);
                    var fdts = new List<NativeDt1>();
                    var fbits = new List<byte>();
                    LoadLevelDts(ffiles, fcols, fnf, fdts, fbits);
                    found = (flid, fdts, fbits);
                    flibs.Add(found.Value);
                }
                var lib = found.Value;
                var pmap = RoomPMap(near);
                if (pmap == null) continue;
                var path = Preset.PresetDs1Path(pmap);
                if (path == null) continue;
                var fd = Preset.CachedDs1(path);
                if (fd == null) continue;
                var ftiles = Materialize.MaterializeDs1(fd, RoomDts(lib.dts, lib.bits, pmap), RoomWindow(near, pmap), null);
                rbs.Add(new RoomBuild
                {
                    wpx = near->sCoords.WorldPosition.x,
                    wpy = near->sCoords.WorldPosition.y,
                    wtx = near->sCoords.WorldSize.x,
                    hty = near->sCoords.WorldSize.y,
                    tiles = ftiles,
                    is_ds1 = true,
                    foreign = true,
                });
            }
        }

        if (rbs.Count == 0) return result;

        // Allocate + zero each room's CollMap. `floors[i]` tracks, per room tile-cell, whether a
        // FLOOR tile covered it — the engine stamps Blank.dt1 (solid rock) into every floorless
        // cell, so cells left uncovered are void-filled below.
        var grids = new byte[rbs.Count][];
        var floors = new bool[rbs.Count][];
        for (int i = 0; i < rbs.Count; i++)
        {
            var rb = rbs[i];
            grids[i] = new byte[rb.wtx * SUB * rb.hty * SUB];
            floors[i] = new bool[rb.wtx * rb.hty];
        }

        // Faithful AllocRoomCollisionGrid: for each room R, stamp every tile (of R and its
        // neighbours) whose WORLD origin subtile lands inside R — TileLibrary_SetupCollision's
        // AreSubtileCoordinatesInsideRoom clip. A tile-origin may lie in more than one room when
        // rooms overlap, and the engine stamps it into each.
        for (int ri = 0; ri < rbs.Count; ri++)
        {
            var R = rbs[ri];
            if (R.foreign) continue; // gathered FROM, never emitted
            int gw = R.wtx * SUB;
            int gh = R.hty * SUB;
            foreach (var A in rbs)
            {
                // A reaches R if A's tile rect EXTENDED by the +1 far-edge col/row (where its wall
                // tiles may live) overlaps R's.
                if (A.wpx + A.wtx + 1 <= R.wpx || A.wpx >= R.wpx + R.wtx) continue;
                if (A.wpy + A.hty + 1 <= R.wpy || A.wpy >= R.wpy + R.hty) continue;
                foreach (var ct in A.tiles)
                {
                    int otx = ct.nPosX + A.wpx;
                    int oty = ct.nPosY + A.wpy;
                    if (otx < R.wpx || otx >= R.wpx + R.wtx || oty < R.wpy || oty >= R.wpy + R.hty) continue;
                    int rtx = otx - R.wpx;
                    int rty = oty - R.wpy;
                    // UpdateTileType's bNeedUpdate swap: a seam hit on a Blank(30,0) FLOOR
                    // re-resolves the OWNER's tile against the visiting room's grid flags.
                    var ct2 = ct;
                    if (ct.is_floor && ct.tile->main == 30)
                    {
                        if (near_tiles.BlankReplacement(otx, oty) is Swap sw && sw.ox == A.wpx && sw.oy == A.wpy)
                        {
                            // GetTileLibraryEntry never yields nothing in-engine (it falls back to
                            // the zero-collision type-10 tile), so an unresolved identity falls
                            // back the same way.
                            ct2.tile = sw.tile != null ? sw.tile : TileGen.zero_coll_tile;
                            ct2.nFlags = sw.n_flags;
                        }
                    }
                    Materialize.StampCollTile(grids[ri], gw, gh, rtx * SUB, rty * SUB, ct2);
                    if (ct2.is_floor) floors[ri][rty * R.wtx + rtx] = true;
                }
            }
        }

        // Void-fill: any subtile whose tile-cell got no floor tile is engine void — the
        // always-appended Blank.dt1 (main=30) stamped into uncovered cells. Arcane Sanctuary
        // (lvl_type 19) uses Blank sub=1 = 0x01; every other tileset uses sub=0 = 0x05 (solid
        // rock). OR (not set-when-0). DS1/pMap rooms are excluded: their fill is the faithful
        // ProcessTile blank branch inside MaterializeDs1.
        byte void_flag = tlv.LvlType == 19 ? (byte)0x01 : (byte)0x05;
        for (int i = 0; i < rbs.Count; i++)
        {
            var rb = rbs[i];
            if (rb.is_ds1) continue;
            int gw = rb.wtx * SUB;
            var g = grids[i];
            var f = floors[i];
            for (int ci = 0; ci < g.Length; ci++)
            {
                int sx = ci % gw;
                int sy = ci / gw;
                if (!f[(sy / SUB) * rb.wtx + (sx / SUB)]) g[ci] |= void_flag;
            }
        }

        for (int i = 0; i < rbs.Count; i++)
        {
            var rb = rbs[i];
            if (rb.foreign) continue; // belongs to its own level; gathered FROM only
            result.Add(new RoomColl(rb.wpx * SUB, rb.wpy * SUB, rb.wtx * SUB, rb.hty * SUB, grids[i]));
        }
        return result;
    }
}

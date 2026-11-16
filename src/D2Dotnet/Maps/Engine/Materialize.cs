// Port of libd2 packages/drlg/src/drlg/materialize.zig (MIT, (c) 2026 jaenster).
//
// Post-generation tile materialization: turns a room's DS1/DrlgMap grid cells into its actual
// tile-data list (floors / walls / shadows), by faithfully transforming the reconstructed 1.14d
// path:
//
//   DS1 layers --> D2DrlgGridStrc sub-grids  (Preset::InitGridsFromDS1File +
//                                              DrlgGrid::InitGridFromTileData)
//              --> CountTilesFromGrid / CountWallTilesFromGrid  (size the arrays)
//              --> AllocTileDataArrays
//              --> InitRoomTiles -> processTile -> the TileGen tile BUILDERS
//                                  (GetTileLibraryEntry / FillTileData /
//                                   Create{Wall,Shadow}TileData).
//
// The count / InitRoomTiles / processTile bodies are 1:1 transforms of recon/closure/RoomTile.cpp.
// The grid setup mirrors Preset.cpp DRLGPRESET_InitGridsFromDS1File (006667d0) collapsed to a
// single whole-room grid per layer.
//
// Port notes:
//   * The materializers return only the placed tile list (`CollTile`), which is what the
//     per-room CollMap build in MapGenerator consumes. The Zig source also rasterizes a
//     standalone per-room collision grid for its cross-check tests; that rasterization is pure
//     (no seed, no state) and is not ported.
//   * The transient grids/arrays live in a per-call native scratch arena (the Zig ArenaAllocator).

using System.Runtime.InteropServices;
using D2Dotnet.Maps.Engine.Outdoors;
using D2Dotnet.Maps.Formats;

namespace D2Dotnet.Maps.Engine;

/// <summary>A per-call native arena (zeroed allocations, all freed on Dispose).</summary>
internal sealed unsafe class Scratch : IDisposable
{
    private readonly List<nint> _allocs = new();

    public void* Alloc(nuint bytes)
    {
        var p = NativeMemory.AllocZeroed(bytes == 0 ? 1 : bytes);
        _allocs.Add((nint)p);
        return p;
    }

    public T* Alloc<T>(int count) where T : unmanaged => (T*)Alloc((nuint)(Math.Max(count, 1) * sizeof(T)));

    public void Dispose()
    {
        foreach (var p in _allocs) NativeMemory.Free((void*)p);
        _allocs.Clear();
    }
}

/// <summary>One placed tile in room-local TILE coords, carrying its resolved DT1 art and the draw
/// nFlags — the exact inputs the engine's per-room CollMap builder consumes
/// (AllocRoomCollisionGrid 0x64c900 -> TileLibrary_SetupCollision 0x64c790).</summary>
internal unsafe struct CollTile
{
    public int nPosX;
    public int nPosY;
    public int nFlags;
    public NTile* tile;
    /// <summary>True for FLOOR-layer tiles.</summary>
    public bool is_floor;
}

internal readonly record struct MatRect(int x, int y, int w, int h);

/// <summary>A re-resolved seam tile: the new entry plus the OWNER whose tile it belongs to — the
/// engine mutates exactly that one tile, not every blank sharing the world cell.</summary>
internal readonly unsafe struct Swap
{
    public readonly NTile* tile;
    public readonly int n_flags;
    public readonly int ox;
    public readonly int oy;

    public Swap(NTile* tile, int nFlags, int ox, int oy)
    {
        this.tile = tile;
        n_flags = nFlags;
        this.ox = ox;
        this.oy = oy;
    }
}

internal readonly record struct LinkedTile(int wx, int wy, int n_tile_type, int n_flags, bool is_blank);

/// <summary>Cross-room tile index backing the engine's UpdateOrAddTile path (RoomTile.cpp
/// FindTileInNearRooms 0x66e580 -> FindTileAtPosition 0x66e4c0).
/// DRLGPRESET_InitGridsFromDS1File ORs 0x84 into every BORDER cell of a preset room's
/// wall/floor/cell grids, so every cell on the room's ring carries CELLFLAGS_0x04 and routes
/// through UpdateOrAddTile: if an ALREADY-BUILT near room owns a tile at that world position the
/// engine updates it in place — creating no tile here and consuming no rarity roll. Rooms sharing
/// a border therefore emit the seam tile exactly once, and the receiving room's seed stream stays
/// aligned. The caller populates this after each room is done (FindTileInNearRooms skips the
/// searching room itself).</summary>
internal sealed unsafe class NearTileIndex
{
    /// <summary>One already-built room, kept alive past its own materialization: the engine's
    /// UpdateTileType re-resolve rolls on the OWNER room's seed and resolves in the OWNER's tile
    /// library, so both have to outlive the room that produced them.</summary>
    private sealed class Owner
    {
        public nint lib;
        public D2SeedStrc seed;
        public MatRect rect;
    }

    public sealed class Entry
    {
        public int n_tile_type, n_flags, ox, oy, ow, oh;
        public bool is_blank;
        public int owner_idx;
    }

    /// <summary>A matched owner tile and the type UpdateTileType leaves it with (null when it
    /// returns early).</summary>
    public readonly record struct Hit(Entry entry, int? final);

    private readonly List<Owner> _owners = new();
    /// <summary>key = world tile (x,y) + link class; value = the owning tile's identity bits.</summary>
    private readonly Dictionary<ulong, Entry> _map = new();
    /// <summary>Seam hits where UpdateTileType's bNeedUpdate fired: the owner's tile is a FLOOR
    /// carrying the Blank (main 30, sub 0) entry, so the engine re-resolves it against the
    /// VISITING room's grid flags and swaps the owner tile's library entry.</summary>
    private readonly Dictionary<ulong, Swap> _blankReplaced = new();
    /// <summary>The owner tile the last successful `Find` matched.</summary>
    public Hit? hit;

    /// <summary>gnRoomTileMappingByTypeAndLayer (Game.exe 1.14d @0x6ef620): visiting LAYER ->
    /// transition-table row. -1 keeps the layer as-is, -2 aborts the update entirely.</summary>
    private static readonly int[] kMappingByLayer = { -1, 0, 1, 2, -1, 3, 4, 5, -2, -2, -1, -1, -1, -2, -1, -1, -1, -1, -1, -1 };

    /// <summary>gnRoomTileMappingTransitionByType (@0x6ef574): [row * 7 + existing nTileType] ->
    /// the layer the owner's tile is re-typed to. Six rows of seven, plus a lone 7th.</summary>
    private static readonly int[] kMappingTransition =
    {
        0, 1, 3, 3, 4, 1, 3,
        1, 1, 2, 3, 4, 3, 2,
        2, 3, 3, 3, 4, 3, 3,
        3, 1, 3, 3, 4, 5, 6,
        1, 3, 2, 3, 4, 3, 6,
        2, 1, 2, 3, 4, 1, 2,
        7,
    };

    /// <summary>The layer DRLGROOMTILE_UpdateTileType (0x66e740) settles on for the owner's tile,
    /// or null when it returns without touching it.</summary>
    private static int? ResolveLayer(int visit_layer, int held, int held_flags, int gf, MatRect searcher, int ox, int oy, int wx, int wy)
    {
        // nFlags bit 0 (grid flag 0x80) pins the tile: only wall types 8/9 get their draw flags
        // recomputed, and the library entry is never swapped.
        if ((held_flags & 1) != 0) return held == 8 || held == 9 ? held : null;
        if ((gf & 0x80) == 0)
        {
            // Wall types 8/9 on a room's top/left edge skip the mapping and keep the layer.
            bool edge_skip = (visit_layer == 8 || visit_layer == 9) && (wx == searcher.x || wy == searcher.y);
            if (!edge_skip)
            {
                if ((held == 8 || held == 9) && (wx == ox || wy == oy)) return null;
                int row = visit_layer >= 0 && visit_layer < kMappingByLayer.Length ? kMappingByLayer[visit_layer] : -1;
                if (row < 0 || held > 7)
                {
                    if (row != -1) return null;
                }
                else
                {
                    return kMappingTransition[row * 7 + held];
                }
            }
        }
        return visit_layer;
    }

    private static ulong Key(int wx, int wy, bool floor_link) =>
        ((ulong)(uint)wx << 33) | ((ulong)(uint)wy << 1) | (floor_link ? 1ul : 0ul);

    /// <summary>DRLGROOMTILE_UpdateTileType (0x66e740), the bNeedUpdate arm: re-resolve the
    /// owner's Blank floor for the VISITING room's grid flags on the OWNER's seed + library.</summary>
    private NTile* ReresolveOnOwner(int owner_idx, int n_tile_type, int gf)
    {
        if (owner_idx >= _owners.Count) return null;
        var o = _owners[owner_idx];
        D2RoomExStrc room = default;
        room.sSeed = o.seed;
        room.apTiles[0] = o.lib;
        room.sCoords.WorldPosition.x = o.rect.x;
        room.sCoords.WorldPosition.y = o.rect.y;
        room.sCoords.WorldSize.x = o.rect.w;
        room.sCoords.WorldSize.y = o.rect.h;
        var e = TileGen.GetTileLibraryEntry(&room, n_tile_type, (uint)gf);
        o.seed = room.sSeed; // the roll is permanent on the owner's stream
        return e;
    }

    /// <summary>The entry a seam hit re-resolved the blank floor owned at this world cell to.</summary>
    public Swap? BlankReplacement(int wx, int wy) => _blankReplaced.TryGetValue(Key(wx, wy, true), out var s) ? s : null;

    public void Add(int wx, int wy, int n_tile_type, int n_flags, MatRect owner, bool is_blank, int owner_idx)
    {
        // Companions (type 4) are never matched by FindTileAtPosition, so they are not owners
        // either. First writer wins: the engine walks a room's tile list and returns the first hit.
        if (n_tile_type == 4) return;
        var k = Key(wx, wy, n_tile_type == 0);
        if (!_map.ContainsKey(k))
            _map[k] = new Entry { n_tile_type = n_tile_type, n_flags = n_flags, ox = owner.x, oy = owner.y, ow = owner.w, oh = owner.h, is_blank = is_blank, owner_idx = owner_idx };
    }

    public int AddOwner(nint lib, D2SeedStrc seed, MatRect rect)
    {
        _owners.Add(new Owner { lib = lib, seed = seed, rect = rect });
        return _owners.Count - 1;
    }

    /// <summary>FindTileAtPosition's match: same world cell, same link class, owner type != 4, a
    /// shadow request (nGridFlags 0x8000000) only matches a shadow tile, and the owner's layer
    /// index (nFlags bits 0x1c000, written as (layer+1)&lt;&lt;0xe) must either be absent or equal
    /// the requesting cell's layer ((gf&gt;&gt;0x12)&amp;3). Applies UpdateTileType's bNeedUpdate
    /// swap.</summary>
    public bool Find(int wx, int wy, int n_tile_type, int gf, MatRect searcher)
    {
        if (!_map.TryGetValue(Key(wx, wy, n_tile_type == 0), out var ep)) return false;
        // FindTileInNearRooms only walks ppDrlgRoomsExNear, so an owner that is not a neighbour of
        // the searching room is invisible. Rooms are "near" when their tile rects touch (the
        // shared seam is one tile wide).
        if (ep.ox + ep.ow < searcher.x || searcher.x + searcher.w < ep.ox) return false;
        if (ep.oy + ep.oh < searcher.y || searcher.y + searcher.h < ep.oy) return false;
        // Snapshot of the entry as it was looked up (the Zig `const e = ep.*`).
        int e_tt = ep.n_tile_type, e_flags = ep.n_flags, e_ox = ep.ox, e_oy = ep.oy;
        if (ep.is_blank)
        {
            // UpdateTileType ends in SetWallTileFlags(pTileData, tileType, ..., nGridFlags): the
            // owner's tile keeps its identity slot but its DRAW flags are recomputed from the
            // VISITING cell's grid flags. The two mapping tables decide what the owner's tile
            // becomes. When they land on a WALL type the owner room has no such entry and the
            // re-resolve yields the zero-collision fallback.
            if (ResolveLayer(n_tile_type, e_tt, e_flags, gf, searcher, e_ox, e_oy, wx, wy) is int layer)
            {
                D2DrlgTileDataStrc td = default;
                TileGen.SetWallTileFlags(&td, layer, (uint)gf);
                var swapped = ReresolveOnOwner(ep.owner_idx, layer, gf);
                _blankReplaced[Key(wx, wy, true)] = new Swap(swapped, td.nFlags, e_ox, e_oy);
                // UpdateTileType mutates the owner's tile in place, so every LATER hit on this
                // cell sees the updated type/flags — most importantly the pin bit (nFlags 0x1).
                ep.n_tile_type = layer;
                ep.n_flags = td.nFlags;
                ep.is_blank = layer == 0 && swapped != null && swapped->main == 30 && swapped->sub == 0;
            }
        }
        if (e_tt == 4) return false;
        if (e_tt != 0xd && (gf & 0x8000000) != 0) return false;
        if ((e_flags & 0x1c000) != 0)
        {
            uint owner_layer = unchecked((((uint)e_flags >> 0xe) & 7) - 1);
            if (owner_layer != (((uint)gf >> 0x12) & 3)) return false;
        }
        hit = new Hit(ep, ResolveLayer(n_tile_type, e_tt, e_flags, gf, searcher, e_ox, e_oy, wx, wy));
        return true;
    }
}

/// <summary>The room's world tile origin plus the index of every tile already built by the
/// level's earlier rooms — what FindTileInNearRooms searches.</summary>
internal readonly record struct NearRooms(NearTileIndex index, int wx, int wy, int w, int h);

/// <summary>A room's WINDOW into the shared level DS1 (the engine's per-room CollMap model).</summary>
internal unsafe struct Ds1RoomWindow
{
    public int off_x;
    public int off_y;
    public int size_x;
    public int size_y;
    public D2SeedStrc seed;
    /// <summary>LvlPrest FillBlanks — threaded to FLOOR LAYER 0 ONLY (InitializePresetRoom 0x666ac0).</summary>
    public int fill_blanks;
    /// <summary>LvlPrest KillEdge &amp;&amp; the room touches the pMap's far X/Y edge.</summary>
    public int kill_x;
    public int kill_y;
    /// <summary>The room's level id — the blank fill is level-aware.</summary>
    public int level_id;
    /// <summary>The REAL generation room's warp node chain (RoomEx+0x4C).</summary>
    public D2DrlgTileGridStrc* warp_nodes;
    /// <summary>The REAL act's D2DrlgStrc (getWarpsIdIfExists consults pDrlg.pWarpsInfo).</summary>
    public D2DrlgStrc* real_drlg;
    /// <summary>The generated room the materialized room's preset units go to; null drops them.</summary>
    public D2RoomExStrc* preset_room;
    /// <summary>Kill flags for WALL LAYERS &gt;= 1, which follow the raw engine rule
    /// (InitializePresetRoom 0x666ac0: kill only when LvlPrest KillEdge is set AND the room
    /// touches the pMap far edge on that axis). Wall layers &gt;= 1 are NEVER border-marked
    /// (InitGridsFromDS1File 0x6667d0 marks wall grid [0], the floor grids and the shadow grid
    /// only), so their interior-seam cells DO materialize inline.</summary>
    public int kill1_x;
    public int kill1_y;
}

/// <summary>The inputs the ACT_I wall/edge overlay needs beyond a bare floor room.</summary>
internal readonly unsafe struct OutdoorOverlay
{
    public readonly POINT room_pos;
    public readonly D2DrlgVertexStrc*[] vertices;

    public OutdoorOverlay(POINT pos, D2DrlgVertexStrc*[] v)
    {
        room_pos = pos;
        vertices = v;
    }
}

internal static unsafe class Materialize
{
    // eD2GridCellFlags bit constants (RoomTile.cpp naming).
    private const int CELLFLAGS_NONE = 0;
    private const int CELLFLAGS_0x01 = 0x01;
    private const int CELLFLAGS_0x02 = 0x02;
    private const int CELLFLAGS_0x04 = 0x04;
    private const int CELLFLAGS_0x08 = 0x08;
    private const int CELLFLAGS_0x80 = 0x80;
    private const int CELLFLAGS_0x8000000 = 0x8000000;
    private const int CELLFLAGS_0x80000000 = unchecked((int)0x80000000);

    private sealed class MatCtx
    {
        public NearTileIndex? near;
        public int room_wx;
        public int room_wy;
        public MatRect room_rect;
        /// <summary>Tiles this room created through AddTileData — the ONLY ones the engine links
        /// into pRoomTiles->pMapLinks, and therefore the only ones FindTileAtPosition can ever
        /// return. Published to the level index once the room is finished.</summary>
        public readonly List<LinkedTile> linked = new();
        /// <summary>The +1 column / row of wall layer 0 this port leaves out although the engine
        /// visits it (an interior seam): only the preset spawns the engine makes there are
        /// replayed (SeamPresetCell).</summary>
        public bool seam_ring_x;
        public bool seam_ring_y;
    }

    // Set at the top of each materialize entry (a poor-man's closure for the C-style tilegen
    // callbacks) and read only within that same synchronous call.
    [ThreadStatic] private static MatCtx? g_ctx;

    private static MatCtx Ctx => g_ctx!;

    // ===========================================================================
    // RoomTile.cpp count/init/processTile — faithful transforms wired to the TileGen builders.
    // ---------------------------------------------------------------------------

    /// <summary>RoomTile.cpp:825 (0066ece0) — sizes nWall/nFloor/nRoof Tiles Max from a grid.</summary>
    private static void CountTilesFromGrid(D2RoomExStrc* pRoomEx, D2DrlgGridStrc* pGrid, int bCountFloors, int bKillEdgeX_in, int bKillEdgeY)
    {
        int nWorldPosX = pRoomEx->sCoords.WorldPosition.x;
        var pTileGrid = pRoomEx->pRoomTiles;
        int nWidth = (bKillEdgeX_in == 0 ? 1 : 0) + pRoomEx->sCoords.WorldSize.x;
        int nWorldPosY = pRoomEx->sCoords.WorldPosition.y;
        int nHeight = (bKillEdgeY == 0 ? 1 : 0) + pRoomEx->sCoords.WorldSize.y;
        for (int nRow = 0; nRow < nHeight; nRow++)
        {
            for (int nX = 0; nX < nWidth; nX++)
            {
                int nCell = DrlgGrid.GetGridFlags(pGrid, nX, nRow);
                if ((nCell & 1) != 0) pTileGrid->nWallTilesMax += 1;
                if ((nCell & 2) != 0 || (bCountFloors != 0 && DrlgRoom.AreXYInsideCoordinates(&pRoomEx->sCoords, nX + nWorldPosX, nRow + nWorldPosY) != 0))
                    pTileGrid->nFloorTilesMax += 1;
                if ((nCell & 0x8000000) != 0) pTileGrid->nRoofTilesMax += 1;
            }
        }
    }

    /// <summary>RoomTile.cpp:864 (0066edb0) — counts the extra wall tiles (orient 3/10/11).</summary>
    private static void CountWallTilesFromGrid(D2RoomExStrc* pRoomEx, D2DrlgGridStrc* pFloorGrid, D2DrlgGridStrc* pOrientGrid, int bKillEdgeX, int bKillEdgeY)
    {
        var pTileGrid = pRoomEx->pRoomTiles;
        int nWidth = (bKillEdgeX == 0 ? 1 : 0) + pRoomEx->sCoords.WorldSize.x;
        int nHeight = (bKillEdgeY == 0 ? 1 : 0) + pRoomEx->sCoords.WorldSize.y;
        for (int nY = 0; nY < nHeight; nY++)
        {
            for (int nColIdx = 0; nColIdx < nWidth; nColIdx++)
            {
                int nTileType = DrlgGrid.GetGridFlags(pOrientGrid, nColIdx, nY);
                bool bCountWall = false;
                if (nTileType == 3)
                {
                    bCountWall = true;
                }
                else if (unchecked((uint)(nTileType - 10)) < 2)
                {
                    nTileType = DrlgGrid.GetGridFlags(pFloorGrid, nColIdx, nY);
                    if (-1 < nTileType) bCountWall = true; else pTileGrid->nFloorTilesMax += 6;
                }
                if (bCountWall) pTileGrid->nWallTilesMax += 1;
            }
        }
    }

    /// <summary>RoomTile.cpp:935 (0066eea0). Allocs the zeroed tile-grid header.</summary>
    private static void AllocRoomTileGrid(D2RoomExStrc* pRoomEx, Scratch ar)
    {
        if (pRoomEx->pRoomTiles != null) return;
        pRoomEx->pRoomTiles = ar.Alloc<D2DrlgTileGridStrc>(1);
    }

    /// <summary>RoomTile.cpp:947 (0066eee0). Allocs the wall/floor/roof tile-data arrays from the
    /// counted *Max fields.</summary>
    private static void AllocTileDataArrays(D2RoomExStrc* pRoomEx, Scratch ar)
    {
        var pTileGrid = pRoomEx->pRoomTiles;
        if (pTileGrid->nFloorTilesMax != 0) pTileGrid->pFloorTiles = ar.Alloc<D2DrlgTileDataStrc>(pTileGrid->nFloorTilesMax);
        // +8 slack: SetupWarpTile Path B appends LIT stair walls the count pass never books (the
        // engine writes them past its counted array into the Fog pool's slab rounding slack).
        if (pTileGrid->nWallTilesMax != 0) pTileGrid->pWallTiles = ar.Alloc<D2DrlgTileDataStrc>(pTileGrid->nWallTilesMax + 8);
        if (pTileGrid->nRoofTilesMax != 0 && pTileGrid->pRoofTiles == null) pTileGrid->pRoofTiles = ar.Alloc<D2DrlgTileDataStrc>(pTileGrid->nRoofTilesMax);
    }

    /// <summary>RoomTile.cpp 720-724 wall block — createWallTileData + warp setup.</summary>
    private static void ProcessWallBlock(D2RoomExStrc* pRoom, int nX, int nY, int nFlags, int nOtherFlags)
    {
        var e = TileGen.GetTileLibraryEntry(pRoom, nOtherFlags, (uint)nFlags);
        TileGen.CreateWallTileData(pRoom, null, nX, nY, (uint)nFlags, e, nOtherFlags);
        // DRLGROOMTILE_SetupWarpTile 0x66e260 after every type-0x0A/0x0B wall tile except on
        // level 0x85 (Matron's Den); Path B may add the LIT stair wall.
        if ((nOtherFlags == (CELLFLAGS_0x08 | CELLFLAGS_0x02 | CELLFLAGS_0x01) || nOtherFlags == (CELLFLAGS_0x08 | CELLFLAGS_0x02)) && pRoom->pLevel->eD2LevelId != (int)AreaId.MatronsDen)
            SetupWarpTile(pRoom, nX, nY, nFlags, nOtherFlags);
    }

    private static D2DrlgTileGridStrc* FindWarpNode(D2RoomExStrc* pRoom, int nFlags)
    {
        var pWarpIds = DrlgRoom.GetWarpsIdIfExists(pRoom->pLevel->pDrlg, pRoom->pLevel->eD2LevelId);
        int nDestId = pWarpIds[((uint)nFlags >> 0x14) & 0x3f];
        for (var node = pRoom->pTileGrid; node != null; node = (D2DrlgTileGridStrc*)node->pAnimTiles)
        {
            var row = DrlgTables.LvlWarpRowAt(node->nShadows);
            if (row == null) continue;
            if (row->Id == nDestId) return node;
        }
        return null;
    }

    /// <summary>DRLGROOMTILE_SetupWarpTile 0x66e260 (disasm-decoded; the recon garbles its
    /// register/stack args). Flow:
    ///   1. warp id = warpIds[(gf&gt;&gt;0x14)&amp;0x3f]; find the room's warp node whose LvlWarp
    ///      row Id matches (the engine HALTS 0x2b1 on a miss).
    ///   2. orientation byte (gf&gt;&gt;8) 0 or 4 -&gt; CreateExitWarp 0x66e1c0 first: the UNIT_WARP
    ///      preset spawn is collision-neutral, but its FAILURE aborts SetupWarpTile entirely: no
    ///      LvlWarp setup row for the tile-type direction (0x0A-&gt;'l' 0x6c, 0x0B-&gt;'r' 0x72), or
    ///      the cell sits on the room's far-edge ring.
    ///   3. SetWarpTileDirection(node, nTileType).
    ///   4. Path B: if the (direction-adjusted) row's LitVersion != 0, re-resolve
    ///      gf' = (row.Tiles &lt;&lt; 8) | gf with a NORMAL rarity roll and create a SECOND wall
    ///      tile at the same cell, nFlags |= 8.</summary>
    private static void SetupWarpTile(D2RoomExStrc* pRoom, int nX, int nY, int nFlags, int nOtherFlags)
    {
        byte nWarpIdx = (byte)(((uint)nFlags >> 0x14) & 0x3f);
        var node = FindWarpNode(pRoom, nFlags);
        if (node == null) return; // engine: halt 0x2b1 (unreachable in shipped data)

        byte nOrientation = (byte)((uint)nFlags >> 8);
        if (nOrientation == 0 || nOrientation == 4)
        {
            byte dir = nOtherFlags != 0x0b ? (byte)0x6c : (byte)0x72;
            if (DrlgRoom.GetWarpsIdIfExistsAndSetup(pRoom->pLevel, nWarpIdx, dir) == 0) return;
            int rx = nX - pRoom->sCoords.WorldPosition.x;
            int ry = nY - pRoom->sCoords.WorldPosition.y;
            if (rx == pRoom->sCoords.WorldSize.x || ry == pRoom->sCoords.WorldSize.y) return;
        }

        SetWarpTileDirection(node, nOtherFlags);
        var row = DrlgTables.LvlWarpRowAt(node->nShadows);
        if (row == null) return;
        if (row->LitVersion == 0) return;

        uint gf = ((uint)row->Tiles << 8) | (uint)nFlags;
        var e = TileGen.GetTileLibraryEntry(pRoom, nOtherFlags, gf);
        var t = TileGen.CreateWallTileData(pRoom, null, nX, nY, gf, e, nOtherFlags);
        t->nFlags |= 8;
    }

    /// <summary>RoomTile.cpp 730-731 shadow block.</summary>
    private static void ProcessShadowBlock(D2RoomExStrc* pRoom, int nX, int nY, int nFlags)
    {
        var e = TileGen.GetTileLibraryEntry(pRoom, 0xd, (uint)nFlags);
        TileGen.CreateShadowTileData(pRoom, null, nX, nY, (uint)nFlags, e);
    }

    /// <summary>gaWarpTileOffsetX/Y @ 0x6ef554: the 4 warp floor tiles tile the 2x2 block whose
    /// far corner is the marker.</summary>
    private static readonly int[] WARP_TILE_OFF_X = { 0, 1, 0, 1 };
    private static readonly int[] WARP_TILE_OFF_Y = { 0, 0, 1, 1 };

    /// <summary>DRLGROOMTILE_SetWarpTileDirection 0x66e160 on a warp node: keep the cached LvlWarp
    /// row if its Direction is 'b' (0x62); otherwise re-run TXT_LvlWarp_Setup with the direction
    /// implied by the marker's orientation (GetWarpDirectionByte 0x66e150: orient != 0x0B -&gt; 'l'
    /// 0x6c, else 'r' 0x72).</summary>
    private static void SetWarpTileDirection(D2DrlgTileGridStrc* node, int nOrientFlags)
    {
        var row = DrlgTables.LvlWarpRowAt(node->nShadows);
        if (row == null) return;
        byte cur = (byte)row->Direction;
        if (cur == 0x62) return;
        byte wanted = nOrientFlags != 0x0b ? (byte)0x6c : (byte)0x72;
        if (cur == wanted) return;
        node->nShadows = DrlgTables.LvlWarpSetupIndex(row->Id, wanted);
    }

    /// <summary>DRLGROOMTILE_InitWarpCacheTiles 0x66e360 — collision-relevant core. Finds the
    /// room's warp node whose LvlWarp row Id matches the marker's warp id; if the
    /// (direction-adjusted) row has LitVersion != 0, creates 4 floor tiles with synthetic grid
    /// words ((sub&lt;&lt;12)|i|4)&lt;&lt;8 at the 2x2 block (nX-1..nX, nY-1..nY), each resolved via
    /// the normal rarity roll and tagged nFlags|=8.</summary>
    private static void InitWarpCacheTiles(D2RoomExStrc* pRoom, int nFlags, int nX, int nY, int nOrientFlags)
    {
        var node = FindWarpNode(pRoom, nFlags);
        if (node == null) return;

        SetWarpTileDirection(node, nOrientFlags);
        var row = DrlgTables.LvlWarpRowAt(node->nShadows);
        if (row == null) return;
        if (row->LitVersion == 0) return;

        uint nSub = ((uint)nFlags >> 8) & 0xff;
        for (uint i = 0; i < 4; i++)
        {
            uint gf = ((nSub << 0xc) | i | 4) << 8;
            var e = TileGen.GetTileLibraryEntry(pRoom, 0, gf);
            var pTileGrid = pRoom->pRoomTiles;
            int n = pTileGrid->nFloors;
            var pFloor = pTileGrid->pFloorTiles;
            pFloor[n].pNext = null;
            pTileGrid->nFloors += 1;
            TileGen.FillTileData(pRoom, &pFloor[n], nX - 1 + WARP_TILE_OFF_X[i], nY - 1 + WARP_TILE_OFF_Y[i], gf, e);
            pFloor[n].nFlags |= 8;
        }
    }

    /// <summary>RoomTile.cpp:666 (0066e9b0).</summary>
    private static void ProcessTile(int nFlags_in, D2RoomExStrc* pRoom, int nX, int nY, int nParam, int nOtherFlags)
    {
        int nFlags = nFlags_in;
        int nGridFlags;
        byte nMainIndex = (byte)((byte)((uint)nFlags >> 0x14) & 0x3f);
        if ((nOtherFlags == (CELLFLAGS_0x08 | CELLFLAGS_0x02 | CELLFLAGS_0x01) || nOtherFlags == (CELLFLAGS_0x08 | CELLFLAGS_0x02)) && 7 < nMainIndex) return;
        byte nOrientation = (byte)(uint)(nFlags >> 8);
        if (nOtherFlags == CELLFLAGS_NONE && nMainIndex == 0x1e && (nOrientation == 0 || nOrientation == 1)) nFlags |= CELLFLAGS_0x80000000;
        if ((nFlags & CELLFLAGS_0x80000000) != CELLFLAGS_NONE)
        {
            switch (nOtherFlags)
            {
                case CELLFLAGS_0x08:
                case CELLFLAGS_0x08 | CELLFLAGS_0x01:
                    {
                        // RoomTile.cpp 690-698: levels 0x6f/0x70/0x75 (Act-5 Barricade 1/2 +
                        // Barricade Snow) fall through — their orient-8/9 preset markers are real
                        // tower walls, processed as normal cells. Everywhere else:
                        // Preset::CreatesPresets — preset spawn, no base collision tile.
                        int lvl = pRoom->pLevel->eD2LevelId;
                        if (lvl < 0x6f || (lvl > 0x70 && lvl != 0x75))
                        {
                            TileGen.CreatesPresets(pRoom, (uint)nFlags, nX, nY, nOtherFlags == (CELLFLAGS_0x08 | CELLFLAGS_0x01), null);
                            return;
                        }
                        break;
                    }
                case CELLFLAGS_0x08 | CELLFLAGS_0x02:
                case CELLFLAGS_0x08 | CELLFLAGS_0x02 | CELLFLAGS_0x01:
                    if (7 < nMainIndex) return;
                    // createExitWarp spawns the UNIT_WARP preset unit only (no tiles, no rolls) —
                    // skipped. initWarpCacheTiles creates the 4 warp floor tiles (collision +
                    // rolls) — ported.
                    InitWarpCacheTiles(pRoom, nFlags, nX, nY, nOtherFlags);
                    return;
            }
        }
        if ((nFlags & CELLFLAGS_0x04) != CELLFLAGS_NONE)
        {
            // DRLGROOMTILE_UpdateOrAddTile (0x66e940). The border ring set by
            // InitGridsFromDS1File's FlagOperations(grid, OR, 0x84) lands every seam cell here: an
            // already-built near room that owns the tile wins (no tile, no roll), otherwise
            // AddTileData creates it in this room exactly as the direct path would.
            if ((nFlags & CELLFLAGS_0x02) != CELLFLAGS_NONE)
            {
                UpdateOrAddTile(pRoom, 0, nX, nY, nFlags);
                return;
            }
            if ((nFlags & CELLFLAGS_0x01) != CELLFLAGS_NONE)
            {
                UpdateOrAddTile(pRoom, nOtherFlags, nX, nY, nFlags);
                return;
            }
            if ((nFlags & CELLFLAGS_0x8000000) != 0 && (nFlags & CELLFLAGS_0x80000000) == 0)
            {
                UpdateOrAddTile(pRoom, 0xd, nX, nY, nFlags);
                return;
            }
        }
        if ((nFlags & CELLFLAGS_0x02) == CELLFLAGS_NONE)
        {
            if (nParam == 0)
            {
                if ((nFlags & CELLFLAGS_0x01) != CELLFLAGS_NONE) ProcessWallBlock(pRoom, nX, nY, nFlags, nOtherFlags);
                if ((nFlags & CELLFLAGS_0x8000000) == CELLFLAGS_NONE) return;
                ProcessShadowBlock(pRoom, nX, nY, nFlags);
                return;
            }
            uint bInside = DrlgRoom.AreXYInsideCoordinates(&pRoom->sCoords, nX, nY);
            if (bInside == 0)
            {
                if ((nFlags & CELLFLAGS_0x01) != CELLFLAGS_NONE) ProcessWallBlock(pRoom, nX, nY, nFlags, nOtherFlags);
                if ((nFlags & CELLFLAGS_0x8000000) == CELLFLAGS_NONE) return;
                ProcessShadowBlock(pRoom, nX, nY, nFlags);
                return;
            }
            nGridFlags = (nFlags & ~CELLFLAGS_0x80) | CELLFLAGS_0x80000000;
            uint nBlank = pRoom->pLevel->eD2LevelId == (int)AreaId.ArcaneSanctuary ? 0x1e00100u : 0x1e00000u;
            var eBlank = TileGen.GetTileLibraryEntry(pRoom, 0, nBlank);
            PushFloor(pRoom, nX, nY, nGridFlags, eBlank);
        }
        else
        {
            nGridFlags = nFlags;
            var e = TileGen.GetTileLibraryEntry(pRoom, 0, (uint)nFlags);
            PushFloor(pRoom, nX, nY, nGridFlags, e);
        }
        if ((nFlags & CELLFLAGS_0x01) != CELLFLAGS_NONE) ProcessWallBlock(pRoom, nX, nY, nFlags, nOtherFlags);
        if ((nFlags & CELLFLAGS_0x8000000) == CELLFLAGS_NONE) return;
        ProcessShadowBlock(pRoom, nX, nY, nFlags);
    }

    /// <summary>Publish the tiles this room created through AddTileData into the level index.
    /// Only these are linked into pRoomTiles->pMapLinks, so only these can ever be returned by
    /// FindTileAtPosition.</summary>
    private static void PublishRoomTiles(NearRooms n, List<LinkedTile> linked, nint lib, D2SeedStrc final_seed)
    {
        var owner = new MatRect(n.wx, n.wy, n.w, n.h);
        int owner_idx = n.index.AddOwner(lib, final_seed, owner);
        foreach (var t in linked) n.index.Add(t.wx, t.wy, t.n_tile_type, t.n_flags, owner, t.is_blank, owner_idx);
    }

    /// <summary>DRLGROOMTILE_UpdateOrAddTile (0x66e940) -&gt; FindTileInNearRooms / AddTileData
    /// (0x66e620). A hit in an already-built near room is updated there (nothing is emitted or
    /// rolled here).</summary>
    private static void UpdateOrAddTile(D2RoomExStrc* pRoom, int nTileType, int nX, int nY, int nFlags)
    {
        var ctx = Ctx;
        if (ctx.near is NearTileIndex near)
        {
            if (near.Find(ctx.room_wx + nX, ctx.room_wy + nY, nTileType, nFlags, ctx.room_rect))
            {
                SeamHitPreset(pRoom, near, nFlags, nX, nY);
                return;
            }
        }
        // AddTileData: warp types validate the cell is inside the room first.
        if ((nTileType == 0xb || nTileType == 10) && DrlgRoom.AreXYInsideCoordinates(&pRoom->sCoords, nX, nY) == 0) return;
        var e = TileGen.GetTileLibraryEntry(pRoom, nTileType, (uint)nFlags);
        // AddTileData ends in SetupWarpTile for the warp types, and that leaves the room's
        // NON-FLOOR map-link chain holding only the warp tile — every wall linked before it stops
        // being reachable from a neighbour's FindTileInNearRooms.
        if ((nTileType == 0xb || nTileType == 10) && ctx.near != null)
            ctx.linked.RemoveAll(t => t.n_tile_type != 0);
        // The DRAW flags the create computes carry the tile's LAYER index, and
        // FindTileAtPosition 0x66e4c0 filters every candidate on it.
        D2DrlgTileDataStrc* created;
        if (nTileType == 0) created = PushFloor(pRoom, nX, nY, nFlags, e);
        else if (nTileType == 0xd) created = TileGen.CreateShadowTileData(pRoom, null, nX, nY, (uint)nFlags, e);
        else created = TileGen.CreateWallTileData(pRoom, null, nX, nY, (uint)nFlags, e, nTileType);
        if (ctx.near != null)
            ctx.linked.Add(new LinkedTile(ctx.room_wx + nX, ctx.room_wy + nY, nTileType, created != null ? created->nFlags : 0,
                nTileType == 0 && e != null && e->main == 30 && e->sub == 0));
        if ((nTileType == 0xb || nTileType == 10) && pRoom->pLevel->eD2LevelId != (int)AreaId.MatronsDen)
            SetupWarpTile(pRoom, nX, nY, nFlags, nTileType);
    }

    /// <summary>The RoomTile.cpp floor push (direct array append + fillTileData, as the engine does
    /// inside processTile).</summary>
    private static D2DrlgTileDataStrc* PushFloor(D2RoomExStrc* pRoom, int nX, int nY, int nGridFlags, NTile* e)
    {
        var pTileGrid = pRoom->pRoomTiles;
        int nFloorCount = pTileGrid->nFloors;
        var pFloor = pTileGrid->pFloorTiles;
        pFloor[nFloorCount].pNext = null;
        pTileGrid->nFloors += 1;
        TileGen.FillTileData(pRoom, &pFloor[nFloorCount], nX, nY, (uint)nGridFlags, e);
        return &pFloor[nFloorCount];
    }

    /// <summary>RoomTile.cpp:789 (0066ec10). Walks the grid, driving processTile per cell.</summary>
    private static void InitRoomTiles(D2RoomExStrc* pRoomEx, D2DrlgGridStrc* pGrid, D2DrlgGridStrc* pOtherGrid, int nTileParam, int nKillX, int nKillY)
    {
        var ctx = Ctx;
        int nWorldPosX = pRoomEx->sCoords.WorldPosition.x;
        int nXMax = (nKillX == 0 ? 1 : 0) + pRoomEx->sCoords.WorldSize.x;
        int nYOffset = pRoomEx->sCoords.WorldPosition.y;
        int maxTile = (nKillY == 0 ? 1 : 0) + pRoomEx->sCoords.WorldSize.y;
        if (0 >= maxTile) return;
        for (int nY = 0; nY < maxTile; nY++)
        {
            for (int nColIdx = 0; nColIdx < nXMax; nColIdx++)
            {
                int nFlags = DrlgGrid.GetGridFlags(pGrid, nColIdx, nY);
                int nCellFlags = pOtherGrid == null ? CELLFLAGS_NONE : DrlgGrid.GetGridFlags(pOtherGrid, nColIdx, nY);
                ProcessTile(nFlags, pRoomEx, nColIdx + nWorldPosX, nY + nYOffset, nTileParam, nCellFlags);
            }
            if (ctx.seam_ring_x) SeamPresetCell(pRoomEx, pGrid, pOtherGrid, nXMax, nY);
        }
        if (ctx.seam_ring_y)
        {
            int nRingXMax = nXMax + (ctx.seam_ring_x ? 1 : 0);
            for (int nX = 0; nX < nRingXMax; nX++) SeamPresetCell(pRoomEx, pGrid, pOtherGrid, nX, maxTile);
        }
    }

    /// <summary>UpdateTileType (0x66e740) ends in SetWallTileFlags(ownerTile, type, visiting room):
    /// a seam tile it leaves as wall type 8/9 puts its preset on the VISITING room, at most once
    /// per tile (the 0x20 the spawn sets on the owner's tile).</summary>
    private static void SeamHitPreset(D2RoomExStrc* pRoom, NearTileIndex near, int nFlags, int nX, int nY)
    {
        if (near.hit is not NearTileIndex.Hit h) return;
        if (h.final is not int final) return;
        if (final != 8 && final != 9) return;
        D2DrlgTileDataStrc td = default;
        td.nFlags = h.entry.n_flags;
        td.nTileType = final;
        TileGen.CreatesPresets(pRoom, (uint)nFlags, nX, nY, final == 9, &td);
        h.entry.n_flags |= td.nFlags & 0x20;
    }

    /// <summary>The preset spawn of one wall-layer cell the engine's ProcessTile (0x66e9b0) visits
    /// and this port does not materialize. The cell's tile itself belongs to the neighbour
    /// (UpdateOrAddTile), so only what reaches CreatesPresets is replayed, in the engine's cell
    /// order (a tomb shrine marker rolls the room seed).</summary>
    private static void SeamPresetCell(D2RoomExStrc* pRoom, D2DrlgGridStrc* pGrid, D2DrlgGridStrc* pOtherGrid, int nX, int nY)
    {
        var ctx = Ctx;
        int nFlags = DrlgGrid.GetGridFlags(pGrid, nX, nY);
        int nOtherFlags = pOtherGrid == null ? CELLFLAGS_NONE : DrlgGrid.GetGridFlags(pOtherGrid, nX, nY);
        byte nMainIndex = (byte)((byte)((uint)nFlags >> 0x14) & 0x3f);
        if ((nOtherFlags == (CELLFLAGS_0x08 | CELLFLAGS_0x02 | CELLFLAGS_0x01) || nOtherFlags == (CELLFLAGS_0x08 | CELLFLAGS_0x02)) && 7 < nMainIndex) return;
        byte nOrientation = (byte)(uint)(nFlags >> 8);
        if (nOtherFlags == CELLFLAGS_NONE && nMainIndex == 0x1e && (nOrientation == 0 || nOrientation == 1)) nFlags |= CELLFLAGS_0x80000000;
        bool bWall89 = nOtherFlags == CELLFLAGS_0x08 || nOtherFlags == (CELLFLAGS_0x08 | CELLFLAGS_0x01);
        if ((nFlags & CELLFLAGS_0x80000000) != CELLFLAGS_NONE)
        {
            if (nOtherFlags == (CELLFLAGS_0x08 | CELLFLAGS_0x02) || nOtherFlags == (CELLFLAGS_0x08 | CELLFLAGS_0x02 | CELLFLAGS_0x01)) return;
            if (bWall89)
            {
                int lvl = pRoom->pLevel->eD2LevelId;
                if (lvl < 0x6f || (lvl > 0x70 && lvl != 0x75))
                {
                    TileGen.CreatesPresets(pRoom, (uint)nFlags, nX, nY, nOtherFlags == (CELLFLAGS_0x08 | CELLFLAGS_0x01), null);
                    return;
                }
            }
        }
        if ((nFlags & CELLFLAGS_0x01) == CELLFLAGS_NONE) return;
        if ((nFlags & CELLFLAGS_0x04) != CELLFLAGS_NONE)
        {
            if ((nFlags & CELLFLAGS_0x02) != CELLFLAGS_NONE) return;
            if (ctx.near is NearTileIndex near)
            {
                if (near.Find(ctx.room_wx + nX, ctx.room_wy + nY, nOtherFlags, nFlags, ctx.room_rect))
                {
                    SeamHitPreset(pRoom, near, nFlags, nX, nY);
                    return;
                }
            }
        }
        if (!bWall89) return;
        D2DrlgTileDataStrc td = default;
        td.nTileType = nOtherFlags;
        TileGen.CreatesPresets(pRoom, (uint)nFlags, nX, nY, nOtherFlags == (CELLFLAGS_0x08 | CELLFLAGS_0x01), &td);
    }

    // ===========================================================================
    // Grids
    // ---------------------------------------------------------------------------

    /// <summary>DrlgGrid::FlagOperations (0x67c600) as called by DRLGPRESET_InitGridsFromDS1File
    /// (0x6667d0): `FlagOperations(grid, apFlagOperations[0] = OR, 0x84)` walks the grid's BORDER
    /// ring — top row, bottom row, then the first/last column of every other row — and ORs the
    /// flag into each cell. The 0x84 operand's effect on the cell is CELLFLAGS_0x04 (measured
    /// against the engine), and that 0x04 is what routes every seam cell through ProcessTile's
    /// UpdateOrAddTile branch.</summary>
    private static void FlagBorderCells(D2DrlgGridStrc* g, int flag)
    {
        int width = g->nWidth;
        int height = g->nHeight;
        if (width == 0 || height == 0) return;
        var rows = g->pCellsRowOffsets;
        var cells = g->pCellsFlags;
        for (int x = 0; x < width; x++)
        {
            cells[rows[0] + x] |= flag;
            cells[rows[height - 1] + x] |= flag;
        }
        for (int y = 1; y < height; y++)
        {
            cells[rows[y]] |= flag;
            cells[rows[y] + width - 1] |= flag;
        }
    }

    /// <summary>DrlgGrid::AlterAllGridFlags as called by DRLGPRESET_InitGridsFromDS1File: OR a flag
    /// into EVERY cell of a grid. Used to stamp each layer's index into its own cells as
    /// `i &lt;&lt; 0x12`, which later reappears in a tile's nFlags as ((layer+1) &lt;&lt; 0xe).</summary>
    private static void FlagAllCells(D2DrlgGridStrc* g, int flag)
    {
        for (int y = 0; y < g->nHeight; y++)
            for (int x = 0; x < g->nWidth; x++)
                g->pCellsFlags[g->pCellsRowOffsets[y] + x] |= flag;
    }

    private static D2DrlgGridStrc* BuildGrid(Scratch ar, int width, int height, int* cells)
    {
        var rows = ar.Alloc<int>(height);
        for (int y = 0; y < height; y++) rows[y] = y * width;
        var g = ar.Alloc<D2DrlgGridStrc>(1);
        g->pCellsFlags = cells;
        g->pCellsRowOffsets = rows;
        g->nWidth = width;
        g->nHeight = height;
        g->bIsSubGrid = 1;
        return g;
    }

    /// <summary>A windowed sub-grid header over the whole-DS1 `cells` (row stride `full_w`): local
    /// cell (x,y) maps to DS1 cell (x+off_x, y+off_y).</summary>
    private static D2DrlgGridStrc* BuildGridWindow(Scratch ar, int* cells, int full_w, int off_x, int off_y, int sub_w, int sub_h)
    {
        var rows = ar.Alloc<int>(sub_h);
        for (int y = 0; y < sub_h; y++) rows[y] = (y + off_y) * full_w;
        var g = ar.Alloc<D2DrlgGridStrc>(1);
        g->pCellsFlags = cells + off_x;
        g->pCellsRowOffsets = rows;
        g->nWidth = sub_w;
        g->nHeight = sub_h;
        g->bIsSubGrid = 1;
        return g;
    }

    /// <summary>Collect a room's FLOOR + WALL + ROOF tile-data (post-InitRoomTiles) into a flat
    /// `CollTile` list — the input to the faithful per-room CollMap stamp. Tiles whose art did not
    /// resolve are dropped. The type-3 wall companion IS included: CreateWallTileData (0x66dc50)
    /// writes the paired type-4 tile into the same pWallTiles array and bumps nWalls, so the
    /// engine's gather stamps it like any other wall.</summary>
    private static CollTile[] CollectCollTiles(D2DrlgTileGridStrc* pTileGrid, int max_tx, int max_ty, int wall_edge)
    {
        var list = new List<CollTile>();
        static void PushArr(List<CollTile> l, D2DrlgTileDataStrc* p, int n, bool is_floor, int mtx, int mty)
        {
            if (p == null) return;
            for (int i = 0; i < n; i++)
            {
                var td = &p[i];
                // Drop tiles outside the room's tile rect, EXCEPT the wall array, whose mtx/mty
                // include the +1 far-edge col/row: a room's +1 WALL tiles stamp into the abutting
                // neighbor's CollMap via the adjacency gather. Floor/shadow +1 tiles are still
                // created (their rarity rolls keep the room seed stream engine-aligned) but never
                // contribute collision anywhere.
                if (td->nPosX < 0 || td->nPosY < 0 || td->nPosX >= mtx || td->nPosY >= mty) continue;
                var t = (NTile*)td->pTileLibraryEntry;
                if (t == null) continue;
                l.Add(new CollTile { nPosX = td->nPosX, nPosY = td->nPosY, nFlags = td->nFlags, tile = t, is_floor = is_floor });
            }
        }
        PushArr(list, pTileGrid->pFloorTiles, pTileGrid->nFloors, true, max_tx + wall_edge, max_ty + wall_edge);
        PushArr(list, pTileGrid->pWallTiles, pTileGrid->nWalls, false, max_tx + wall_edge, max_ty + wall_edge);
        PushArr(list, pTileGrid->pRoofTiles, pTileGrid->nShadows, false, max_tx, max_ty);
        return list.ToArray();
    }

    /// <summary>The engine's per-tile draw-flag -&gt; collision-flag recompute (Collision.cpp
    /// TileLibrary_SetupCollision): beyond the verbatim DT1 subtile-byte OR, each tile ORs these
    /// bits across its WHOLE 5x5 footprint, from its computed draw nFlags.</summary>
    private static byte DrawExtraColl(int nFlags)
    {
        byte extra = 0;
        if ((nFlags & 0x02) != 0) extra |= 0x10; // COLBIT_PRESET
        if ((nFlags & 0x40) != 0) extra |= 0x01; // COLBIT_WALL (blocks walk)
        if ((nFlags & 0x80) != 0) extra |= 0x04; // COLBIT_MISSILE_BARRIER
        return extra;
    }

    /// <summary>Faithful port of TileLibrary_AddCollision (0x64c4c0) + AddCollisionFlagToCoords
    /// (0x64c700): stamp one tile into a room CollMap at room-relative subtile base (relX, relY).
    /// The DT1 25-byte subtile block is OR'd with the Y (row) axis flipped — grid subtile (dc, dr)
    /// reads block byte (4-dr)*5+dc — and the tile's derived collision flags are OR'd flat across
    /// the whole 5x5 footprint.</summary>
    public static void StampCollTile(byte[] grid, int grid_w, int grid_h, int relX, int relY, in CollTile ct)
    {
        byte extra = DrawExtraColl(ct.nFlags);
        for (int dr = 0; dr < 5; dr++)
        {
            int gy = relY + dr;
            if (gy >= grid_h) break;
            for (int dc = 0; dc < 5; dc++)
            {
                int gx = relX + dc;
                if (gx >= grid_w) continue;
                grid[gy * grid_w + gx] |= (byte)(ct.tile->Subtile(dc, 4 - dr) | extra);
            }
        }
    }

    private static void SetupRoom(D2RoomExStrc* room, D2DrlgLevelStrc* level, nint lib, D2SeedStrc seed, int ws_x, int ws_y)
    {
        room->pLevel = level;
        room->apTiles[0] = lib;
        room->sSeed = seed;
        // WorldPosition stays 0 so stored nPosX/nPosY are room-local.
        room->sCoords.WorldSize.x = ws_x;
        room->sCoords.WorldSize.y = ws_y;
    }

    private static MatCtx NewCtx(NearRooms? near) =>
        near is NearRooms n
            ? new MatCtx { near = n.index, room_wx = n.wx, room_wy = n.wy, room_rect = new MatRect(n.wx, n.wy, n.w, n.h) }
            : new MatCtx();

    /// <summary>Materialize one DS1 (a room's DrlgMap window) via the full InitRoomTiles
    /// orchestration and return its placed tiles. `lib` is the room's tile library handle.</summary>
    public static CollTile[] MaterializeDs1(Ds1File d, nint lib, in Ds1RoomWindow win, NearRooms? near)
    {
        int w = (int)d.Width;
        int h = (int)d.Height;
        int full_ncells = w * h;
        // The count/InitRoomTiles loops add a +1 kill-edge, so the window spans size+1 tiles.
        int winW = win.size_x + 1;
        int winH = win.size_y + 1;
        int win_ncells = winW * winH;

        using var ar = new Scratch();

        D2DrlgStrc drlg = default;
        D2DrlgLevelStrc level = default;
        level.pDrlg = win.real_drlg != null ? win.real_drlg : &drlg;
        level.eD2LevelId = win.level_id;

        D2RoomExStrc room = default;
        SetupRoom(&room, &level, lib, win.seed, win.size_x, win.size_y);
        room.pTileGrid = win.warp_nodes;
        var pRoom = &room;

        // -- DS1 -> grids (InitGridsFromDS1File). The cell arrays cover the WHOLE DS1; the
        //    sub-grid headers window each room's (off,size) view into them.
        int nFloor = d.FloorLayers.Length, nWall = d.WallLayers.Length;
        var floor_grids = new nint[nFloor];
        for (int li = 0; li < nFloor; li++)
        {
            var cells = ar.Alloc<int>(full_ncells);
            var fl = d.FloorLayers[li];
            for (int i = 0; i < full_ncells; i++) cells[i] = (int)fl[i];
            floor_grids[li] = (nint)BuildGridWindow(ar, cells, w, win.off_x, win.off_y, winW, winH);
        }
        var wall_grids = new nint[nWall];
        var orient_grids = new nint[nWall];
        for (int li = 0; li < nWall; li++)
        {
            var wl = d.WallLayers[li];
            var wcells = ar.Alloc<int>(full_ncells);
            var ocells = ar.Alloc<int>(full_ncells);
            int n = Math.Min(wl.Wall.Length, wl.Orient.Length);
            for (int i = 0; i < full_ncells; i++)
            {
                wcells[i] = i < n ? (int)wl.Wall[i] : 0;
                ocells[i] = i < n ? Ds1File.Prop1(wl.Orient[i]) : 0; // orient grid == prop1 byte
            }
            wall_grids[li] = (nint)BuildGridWindow(ar, wcells, w, win.off_x, win.off_y, winW, winH);
            orient_grids[li] = (nint)BuildGridWindow(ar, ocells, w, win.off_x, win.off_y, winW, winH);
        }
        var shadow_cells = ar.Alloc<int>(full_ncells);
        for (int i = 0; i < full_ncells; i++) shadow_cells[i] = (int)d.Shadow[i];
        var shadow_grid = BuildGridWindow(ar, shadow_cells, w, win.off_x, win.off_y, winW, winH);

        static D2DrlgGridStrc* G(nint p) => (D2DrlgGridStrc*)p;

        // InitGridsFromDS1File, in its order: the wall-grid border ring, then each layer's index
        // into all of its own cells (wall grids 1.., every floor grid), then the floor and
        // cell-grid border rings. Orientation grids get neither.
        if (nWall != 0) FlagBorderCells(G(wall_grids[0]), CELLFLAGS_0x04);
        for (int li = 1; li < nWall; li++) FlagAllCells(G(wall_grids[li]), li << 0x12);
        for (int li = 0; li < nFloor; li++) FlagAllCells(G(floor_grids[li]), li << 0x12);
        for (int li = 0; li < nFloor; li++) FlagBorderCells(G(floor_grids[li]), CELLFLAGS_0x04);
        FlagBorderCells(shadow_grid, CELLFLAGS_0x04);

        // -- Room tile grid + count -> alloc (InitializePresetRoom order).
        AllocRoomTileGrid(pRoom, ar);
        for (int li = 0; li < nFloor; li++)
            CountTilesFromGrid(pRoom, G(floor_grids[li]), li == 0 ? win.fill_blanks : 0, win.kill_x, win.kill_y);
        for (int li = 0; li < nWall; li++)
        {
            int kx = li == 0 ? win.kill_x : win.kill1_x;
            int ky = li == 0 ? win.kill_y : win.kill1_y;
            CountWallTilesFromGrid(pRoom, G(wall_grids[li]), G(orient_grids[li]), kx, ky);
            CountTilesFromGrid(pRoom, G(wall_grids[li]), 0, kx, ky);
        }
        CountTilesFromGrid(pRoom, shadow_grid, 0, win.kill_x, win.kill_y);

        var pTileGrid = room.pRoomTiles;
        // Safety headroom: the count fns are faithful but a few special cells route to base
        // tiles; a small pad guarantees no array overflow.
        pTileGrid->nFloorTilesMax += 16;
        pTileGrid->nWallTilesMax += win_ncells + 16;
        pTileGrid->nRoofTilesMax += win_ncells + 16;
        AllocTileDataArrays(pRoom, ar);

        // -- InitRoomTiles per layer.
        var ctx = NewCtx(near);
        var saved = g_ctx;
        g_ctx = ctx;
        try
        {
            for (int li = 0; li < nFloor; li++)
                InitRoomTiles(pRoom, G(floor_grids[li]), null, li == 0 ? win.fill_blanks : 0, win.kill_x, win.kill_y);
            for (int li = 0; li < nWall; li++)
            {
                // Wall layer 0 leaves out the interior seam the engine still visits (kill_x where
                // the literal rule, kill1_x, keeps it): replay its preset spawns.
                ctx.seam_ring_x = li == 0 && win.kill_x != 0 && win.kill1_x == 0;
                ctx.seam_ring_y = li == 0 && win.kill_y != 0 && win.kill1_y == 0;
                InitRoomTiles(pRoom, G(wall_grids[li]), G(orient_grids[li]), 0,
                    li == 0 ? win.kill_x : win.kill1_x, li == 0 ? win.kill_y : win.kill1_y);
                ctx.seam_ring_x = false;
                ctx.seam_ring_y = false;
            }
            InitRoomTiles(pRoom, shadow_grid, null, 0, win.kill_x, win.kill_y);
            HandOverPresetUnits(&room, win.preset_room);

            // Publish this room's tiles for the rooms built after it (FindTileInNearRooms skips
            // the searching room, so this must happen only once the room is done).
            if (near is NearRooms nr) PublishRoomTiles(nr, ctx.linked, lib, room.sSeed);

            return CollectCollTiles(pTileGrid, win.size_x, win.size_y, 1);
        }
        finally
        {
            g_ctx = saved;
        }
    }

    // ===========================================================================
    // WILDERNESS floor-cell materialization.
    //
    // An outdoor level splits its grid into (a) PRESET cells (dwOutdoorFlags&0x200) which get an
    // allocDrlgMap+BuildArea DrlgMap — a DS1, materialized via MaterializeDs1 — and (b) FLOOR
    // cells, each a bare 8x8 RoomEx SHELL from DRLGOUTROOM_CreateOutdoorRoomEx (OutPlace.cpp:1577)
    // with NO tile data.
    //
    // The engine gives a floor room its tiles in InitGridCells (Drlg.cpp 0067d2d0):
    //   1. init the room's floor/wall/tile grids at (WorldSize+1);
    //   2. base-fill every 8x8 floor cell with 0x40002 (a plain floor tile, main=0 sub=0, via
    //      OverwriteFlag);
    //   3. Act-I only: DRLGOUTROOM_InitAct1RoomGridCells -> the wall/edge overlay;
    //   4. OR the level-type nGridFlag into cells with no terrain bits (0x3f0ff80);
    //   5. mark the grid border (FlagOperations op 0, flag 4) as cross-room transition cells;
    //   6. then the shared Count / AllocTileDataArrays / InitRoomTiles run materializes the tiles.
    // ---------------------------------------------------------------------------

    /// <summary>Level-type -&gt; floor-cell fill flag OR'd into empty cells (InitGridCells 0067d2d0,
    /// recon lines 5574-5592).</summary>
    private static int OutdoorFillFlag(int nLevelType, int level_id) => nLevelType switch
    {
        0x10 => 0x100,
        0x15 => 0x120000,
        0x16 => 0x100000,
        0x1b => 0xa00000,
        0x1c => 0x1600000,
        0x1f => level_id != 0x75 ? 0 : 0x600000,
        _ => 0,
    };

    /// <summary>gaWallNeighborOrientTable @ 0x6f2700 — 256 entries, keyed on the 8-neighbour
    /// blocked/edge bitmask, value = wall tile orientation (0 = no wall). Baked from Game.exe
    /// .rdata.</summary>
    private static readonly byte[] gaWallNeighborOrientTable =
    {
        0x00, 0x00, 0x10, 0x10, 0x00, 0x00, 0x10, 0x10, 0x0e, 0x0e, 0x06, 0x13, 0x0e, 0x0e, 0x06, 0x13,
        0x0f, 0x0f, 0x05, 0x05, 0x0f, 0x0f, 0x15, 0x15, 0x08, 0x08, 0x0a, 0x26, 0x08, 0x08, 0x28, 0x14,
        0x00, 0x00, 0x10, 0x10, 0x00, 0x00, 0x10, 0x10, 0x0e, 0x0e, 0x06, 0x13, 0x0e, 0x0e, 0x06, 0x13,
        0x0f, 0x0f, 0x05, 0x05, 0x0f, 0x0f, 0x15, 0x15, 0x08, 0x08, 0x0a, 0x26, 0x08, 0x08, 0x28, 0x14,
        0x0d, 0x0d, 0x07, 0x07, 0x0d, 0x0d, 0x0d, 0x07, 0x04, 0x04, 0x0b, 0x25, 0x04, 0x04, 0x0b, 0x2b,
        0x03, 0x03, 0x0c, 0x0c, 0x03, 0x03, 0x27, 0x27, 0x09, 0x09, 0x02, 0x2b, 0x09, 0x09, 0x2c, 0x1a,
        0x0d, 0x0d, 0x07, 0x07, 0x0d, 0x0d, 0x0d, 0x07, 0x17, 0x17, 0x29, 0x11, 0x17, 0x17, 0x29, 0x11,
        0x03, 0x03, 0x0c, 0x0c, 0x03, 0x03, 0x27, 0x27, 0x2a, 0x2a, 0x2e, 0x2a, 0x2a, 0x2a, 0x21, 0x1f,
        0x00, 0x00, 0x10, 0x10, 0x00, 0x00, 0x10, 0x10, 0x0e, 0x0e, 0x06, 0x13, 0x0e, 0x0e, 0x06, 0x13,
        0x0f, 0x0f, 0x05, 0x05, 0x0f, 0x0f, 0x15, 0x15, 0x08, 0x08, 0x0a, 0x26, 0x08, 0x08, 0x23, 0x14,
        0x00, 0x00, 0x10, 0x10, 0x00, 0x00, 0x10, 0x10, 0x0e, 0x0e, 0x06, 0x13, 0x0e, 0x0e, 0x06, 0x13,
        0x0f, 0x0f, 0x05, 0x05, 0x0f, 0x0f, 0x15, 0x15, 0x08, 0x08, 0x0a, 0x26, 0x08, 0x08, 0x28, 0x14,
        0x0d, 0x0d, 0x07, 0x07, 0x0d, 0x0d, 0x0d, 0x07, 0x04, 0x04, 0x0b, 0x25, 0x04, 0x04, 0x0b, 0x25,
        0x12, 0x12, 0x23, 0x23, 0x12, 0x12, 0x16, 0x16, 0x24, 0x24, 0x2d, 0x22, 0x24, 0x24, 0x1c, 0x1d,
        0x0d, 0x0d, 0x07, 0x07, 0x0d, 0x0d, 0x0d, 0x07, 0x17, 0x17, 0x29, 0x11, 0x17, 0x17, 0x29, 0x11,
        0x12, 0x12, 0x23, 0x23, 0x12, 0x12, 0x16, 0x16, 0x18, 0x18, 0x19, 0x20, 0x18, 0x18, 0x1e, 0x01,
    };

    /// <summary>Build the overlay for a live wilderness floor room, or null when it does not apply
    /// (non-ACT_I, no wilderness data, or no exits). ACT_I gate mirrors InitGridCells (0067d2d0).</summary>
    public static OutdoorOverlay? OutdoorOverlayFor(D2DrlgLevelStrc* pLevel, D2RoomExStrc* p)
    {
        if (Border.GetActNoFromLevelNumber(pLevel->eD2LevelId) != 0) return null; // ACT_I only
        if (pLevel->pDrlgLevelData == null) return null;
        var wild = (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;
        if (wild->nExitCount <= 0) return null;
        int n = Math.Min(6, wild->nExitCount);
        var v = new D2DrlgVertexStrc*[n];
        for (int i = 0; i < n; i++) v[i] = (D2DrlgVertexStrc*)wild->pAdjacentVertices[i];
        return new OutdoorOverlay(p->sCoords.WorldPosition, v);
    }

    /// <summary>DRLGOUTROOM_InitAct1RoomGridCells (0x680c80) body: rasterize the adjacent vertex
    /// lines into a temp edge grid (SetOutRoomEdgeFlags 0x680a70), then autotile wall orientations
    /// onto the room-local floor grid `fg` (ComputeWallOrientations 0x680b10). Neighbour-&gt;bit
    /// map: bit7 NE, bit6 E, bit5 SE, bit4 N, bit3 S, bit2 NW, bit1 W, bit0 SW.</summary>
    private static void ApplyAct1WallOverlay(Scratch ar, D2DrlgGridStrc* fg, in OutdoorOverlay ov, int ws_x, int ws_y)
    {
        // Temp edge grid: WorldSize + 3 (a 1-cell border on every side).
        int egw = ws_x + 3;
        int egh = ws_y + 3;
        var edge_cells = ar.Alloc<int>(egw * egh);
        var eg = BuildGrid(ar, egw, egh, edge_cells);

        // Room coords expanded (-1 pos, +3 size); the edge grid's cell (0,0) is world (room_pos-1).
        D2DrlgCoordsStrc sCoords = default;
        sCoords.WorldPosition.x = ov.room_pos.x - 1;
        sCoords.WorldPosition.y = ov.room_pos.y - 1;
        sCoords.WorldSize.x = ws_x + 3;
        sCoords.WorldSize.y = ws_y + 3;
        foreach (var vhead in ov.vertices)
        {
            for (var v = vhead; v != null; v = v->pNext)
                if (v->pNext != null) DrlgGrid.SetLineFlagsWithWidth(eg, v, &sCoords, 1, 0, 2);
        }

        // Output cell (ox,oy) maps to edge center (ox+1,oy+1). Grid is (WorldSize+1).
        int gw = ws_x + 1;
        int gh = ws_y + 1;
        for (int oy = 0; oy < gh; oy++)
        {
            for (int ox = 0; ox < gw; ox++)
            {
                int ex = ox + 1;
                int ey = oy + 1;
                if (DrlgGrid.GetGridFlags(eg, ex, ey) == 0) continue;
                int mask = 0;
                if (DrlgGrid.GetGridFlags(eg, ex + 1, ey - 1) != 0) mask |= 0x80; // NE
                if (DrlgGrid.GetGridFlags(eg, ex + 1, ey) != 0) mask |= 0x40; // E
                if (DrlgGrid.GetGridFlags(eg, ex + 1, ey + 1) != 0) mask |= 0x20; // SE
                if (DrlgGrid.GetGridFlags(eg, ex, ey - 1) != 0) mask |= 0x10; // N
                if (DrlgGrid.GetGridFlags(eg, ex, ey + 1) != 0) mask |= 0x08; // S
                if (DrlgGrid.GetGridFlags(eg, ex - 1, ey - 1) != 0) mask |= 0x04; // NW
                if (DrlgGrid.GetGridFlags(eg, ex - 1, ey) != 0) mask |= 0x02; // W
                if (DrlgGrid.GetGridFlags(eg, ex - 1, ey + 1) != 0) mask |= 0x01; // SW
                if (mask == 0) continue;
                int orient = gaWallNeighborOrientTable[mask];
                if (orient == 0) continue;
                DrlgGrid.AlterGridFlag(fg, ox, oy, (orient << 8) | 0x82, 3);
            }
        }
    }

    /// <summary>DRLGOUTDOOR_CheckSubTileOverlap (0x66fcf0): true if the sub-tile DS1 group can be
    /// placed at (baseX, baseY) without overlapping existing terrain. Checks the room FLOOR grid
    /// (fg) for stamped floor terrain AND the room WALL grid (wg = apWallGrids[0]) for a prior
    /// wall-bearing sub-theme placement.</summary>
    private static bool CheckSubTileOverlap(int baseX, int baseY, D2DrlgGridStrc* fg, D2DrlgGridStrc* wg, D2DrlgSubstGroupStrc* pSubstGroup, D2LvlSubTxt* pSubTxt)
    {
        int pw = pSubstGroup->tBox.nWidth;
        int ph = pSubstGroup->tBox.nHeight;
        int srcX = pSubstGroup->tBox.nPosX;
        int srcY = pSubstGroup->tBox.nPosY;
        var wall0 = Inline.At(ref pSubTxt->pWallGrid, 0);
        var tt0 = wall0;
        bool has_wall = wall0->nWidth != 0;
        for (int dy = 0; dy < ph; dy++)
        {
            for (int dx = 0; dx < pw; dx++)
            {
                // Trigger: the sub DS1 cell carries floor terrain (floor bit 2) OR — when the sub
                // has a wall grid — a wall (wall bit 1).
                uint sub_floor = (uint)DrlgGrid.GetGridFlags(&pSubTxt->pFloorGrid, srcX + dx, srcY + dy);
                bool triggered = (sub_floor & 2) != 0 ||
                    (has_wall && ((uint)DrlgGrid.GetGridFlags(wall0, srcX + dx, srcY + dy) & 1) != 0);
                if (!triggered) continue;
                // Room cell must have plain floor (bit 1 set) and no terrain bits already stamped.
                uint room_flags = (uint)DrlgGrid.GetGridFlags(fg, baseX + dx, baseY + dy);
                if ((room_flags & 0x3f0ff00) != 0 || (room_flags & 2) == 0) return false;
                // Room wall-layer check (apWallGrids[0]): reject if a PRIOR wall-bearing sub-theme
                // group already occupies this cell.
                if (((uint)DrlgGrid.GetGridFlags(wg, baseX + dx, baseY + dy) & 1) != 0) return false;
            }
        }
        _ = tt0;
        return true;
    }

    /// <summary>DRLGOUTDOOR_ApplyLvlSubTileData (0x66fad0) — writes the sub group's floor cells
    /// (bit 2) into the room floor grid (fg), and its wall cells (bit 1) into the room wall grid
    /// (wg). It also spawns a SHADOW tile per sub shadow-grid cell carrying CELLFLAGS_0x8000000,
    /// via DRLGROOMTILE_CreateShadowTileFromGrid (0x66e060) — real collision AND a rarity roll each.</summary>
    private static void ApplySubTileFloor(D2RoomExStrc* pRoom, D2DrlgGridStrc* fg, D2DrlgGridStrc* wg, D2DrlgGridStrc* og, int baseX, int baseY, D2DrlgSubstGroupStrc* pSubstGroup, D2LvlSubTxt* pSubTxt)
    {
        int pw = pSubstGroup->tBox.nWidth;
        int ph = pSubstGroup->tBox.nHeight;
        int srcX = pSubstGroup->tBox.nPosX;
        int srcY = pSubstGroup->tBox.nPosY;
        var wall0 = Inline.At(ref pSubTxt->pWallGrid, 0);
        var tt0 = Inline.At(ref pSubTxt->pTileTypeGrid, 0);
        bool has_wall = wall0->nWidth != 0;
        for (int dy = 0; dy < ph; dy++)
        {
            for (int dx = 0; dx < pw; dx++)
            {
                int flags = DrlgGrid.GetGridFlags(&pSubTxt->pFloorGrid, srcX + dx, srcY + dy);
                if ((flags & 2) != 0) DrlgGrid.AlterGridFlag(fg, baseX + dx, baseY + dy, flags | 0x80, 3);
                if (has_wall)
                {
                    int wall = DrlgGrid.GetGridFlags(wall0, srcX + dx, srcY + dy);
                    if ((wall & 1) != 0)
                    {
                        DrlgGrid.AlterGridFlag(wg, baseX + dx, baseY + dy, wall, 3);
                        // Mirror ApplyLvlSubTileData by stamping the sub's pTileTypeGrid into the
                        // room orient grid at the same cell.
                        int tt = DrlgGrid.GetGridFlags(tt0, srcX + dx, srcY + dy);
                        DrlgGrid.AlterGridFlag(og, baseX + dx, baseY + dy, tt, 3);
                    }
                }
                int sh = DrlgGrid.GetGridFlags(&pSubTxt->pShadowGrid, srcX + dx, srcY + dy);
                if ((sh & CELLFLAGS_0x8000000) != 0)
                {
                    var e = TileGen.GetTileLibraryEntry(pRoom, 0xd, (uint)sh);
                    TileGen.CreateShadowTileData(pRoom, null, baseX + dx, baseY + dy, (uint)sh, e);
                }
            }
        }
        CopyPresetUnitsFromSubTxt(pRoom, baseX, baseY, pSubstGroup, pSubTxt);
    }

    /// <summary>The room a materialization builds is a stand-in for the generated one: the preset
    /// units its tiles and sub files put on it (CreatesPresets, CopyPresetUnitsFromSubTxt) go to
    /// that room, where the engine has them. With no room to take them, the memory goes back.</summary>
    private static void HandOverPresetUnits(D2RoomExStrc* room, D2RoomExStrc* target)
    {
        var pUnit = room->pPresetUnit;
        room->pPresetUnit = null;
        while (pUnit != null)
        {
            var u = pUnit;
            pUnit = u->pPresetUnitNext;
            if (target != null)
            {
                u->pPresetUnitNext = target->pPresetUnit;
                target->pPresetUnit = u;
            }
            else
            {
                Pool.FreeServerMemory(null, u);
            }
        }
    }

    /// <summary>DRLGOUTDOOR_CopyPresetUnitsFromSubTxt (1.14d 0x66fa10), the tail of
    /// ApplyLvlSubTileData: every preset unit of the sub DS1 that lies strictly inside the placed
    /// group's box becomes a preset unit of the room, at its offset in the box plus the placement
    /// base, in room-relative subtiles. This is how an outdoor waypoint (and a shrine or well) gets
    /// onto its room.</summary>
    private static void CopyPresetUnitsFromSubTxt(D2RoomExStrc* pRoom, int baseX, int baseY, D2DrlgSubstGroupStrc* pSubstGroup, D2LvlSubTxt* pSubTxt)
    {
        var pFile = pSubTxt->pDrlgFile;
        if (pFile == null) return;
        int roomX = baseX * 5;
        int roomY = baseY * 5;
        int rectX = pSubstGroup->tBox.nPosX * 5;
        int rectY = pSubstGroup->tBox.nPosY * 5;
        int rectW = pSubstGroup->tBox.nWidth * 5;
        int rectH = pSubstGroup->tBox.nHeight * 5;
        for (var u = pFile->pPresetUnit; u != null; u = u->pPresetUnitNext)
        {
            if (u->nPosX <= rectX || u->nPosY <= rectY) continue;
            if (u->nPosX >= rectW + rectX || u->nPosY >= rectH + rectY) continue;
            var pNew = DrlgRoom.AllocPresetUnit(pRoom, null, u->eType, u->nClassId, u->nMode, u->nPosX - rectX + roomX, u->nPosY - rectY + roomY);
            pNew->nDbmCode = u->nDbmCode;
        }
    }

    /// <summary>DoNotCheckAll (1.14d 0x670170): seeded sub-tile placement — picks a random
    /// substitution group, then either tries random positions (Trials &gt;= 0) or shuffles all
    /// positions exhaustively (Trials == -1), placing the group when overlap-free. Runs
    /// Max[nSubTypeIndex] times.</summary>
    private static void DoNotCheckAll(D2RoomExStrc* pRoom, D2DrlgGridStrc* fg, D2DrlgGridStrc* wg, D2DrlgGridStrc* og, D2LvlSubTxt* pSubTxt, int nSubTypeIndex)
    {
        var pFile = pSubTxt->pDrlgFile;
        if (pFile == null) return;
        if (pFile->nSubstGroups == 0) return;
        int idx = Math.Max(0, nSubTypeIndex);
        if (idx >= 5) return;
        if (pSubTxt->Max[idx] < 1) return;

        int ws_x = pRoom->sCoords.WorldSize.x;
        int ws_y = pRoom->sCoords.WorldSize.y;

        int nMaxRemaining = pSubTxt->Max[idx];
        while (true)
        {
            // Pick a random substitution group from the DS1.
            uint nGroupIdx = DrlgRng.RandomNumberSelector(&pRoom->sSeed, (uint)pFile->nSubstGroups);
            var pSubstGroup = &pFile->pSubstGroups[nGroupIdx];

            uint nMaxX = (uint)(ws_x - pSubstGroup->tBox.nWidth);
            uint nMaxY = (uint)(ws_y - pSubstGroup->tBox.nHeight);
            // Condition: (int)(nMaxX+1) > 1 && (int)(nMaxY+1) > 1.
            if ((int)unchecked(nMaxX + 1) > 1 && (int)unchecked(nMaxY + 1) > 1)
            {
                int nTrialCount = pSubTxt->Trials[idx];
                if (nTrialCount == -1)
                {
                    // Exhaustive shuffle: fill all positions, shuffle, try each.
                    uint nTotalPositions = nMaxY * nMaxX;
                    if (nTotalPositions > 0)
                    {
                        var coordBuf = new int[nTotalPositions * 2];
                        for (uint nPosX = 0; nPosX < nTotalPositions; nPosX++)
                        {
                            coordBuf[nPosX * 2] = (int)(nPosX % nMaxX); // x
                            coordBuf[nPosX * 2 + 1] = (int)(nPosX / nMaxX); // y
                        }
                        // Shuffle: nTotalPositions random swaps (each two randomNumberSelector
                        // calls); the final swap consumes seed and matters downstream.
                        for (uint nRem = nTotalPositions; nRem > 0; nRem--)
                        {
                            uint a_idx = DrlgRng.RandomNumberSelector(&pRoom->sSeed, nTotalPositions);
                            uint b_idx = DrlgRng.RandomNumberSelector(&pRoom->sSeed, nTotalPositions);
                            int tx = coordBuf[a_idx * 2];
                            int ty = coordBuf[a_idx * 2 + 1];
                            coordBuf[a_idx * 2] = coordBuf[b_idx * 2];
                            coordBuf[a_idx * 2 + 1] = coordBuf[b_idx * 2 + 1];
                            coordBuf[b_idx * 2] = tx;
                            coordBuf[b_idx * 2 + 1] = ty;
                        }
                        // Try each shuffled position.
                        for (uint i = 0; i < nTotalPositions; i++)
                        {
                            int bx = coordBuf[i * 2] + 1;
                            int by = coordBuf[i * 2 + 1] + 1;
                            if (CheckSubTileOverlap(bx, by, fg, wg, pSubstGroup, pSubTxt))
                            {
                                ApplySubTileFloor(pRoom, fg, wg, og, bx, by, pSubstGroup, pSubTxt);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // Random trial: try nTrialCount random positions.
                    for (int nTrialIter = 0; nTrialIter < nTrialCount; nTrialIter++)
                    {
                        int bx = (int)(DrlgRng.RandomNumberSelector(&pRoom->sSeed, nMaxX) + 1);
                        int by = (int)(DrlgRng.RandomNumberSelector(&pRoom->sSeed, nMaxY) + 1);
                        if (CheckSubTileOverlap(bx, by, fg, wg, pSubstGroup, pSubTxt))
                        {
                            ApplySubTileFloor(pRoom, fg, wg, og, bx, by, pSubstGroup, pSubTxt);
                            break;
                        }
                    }
                }
            }

            nMaxRemaining -= 1;
            if (nMaxRemaining == 0) return;
        }
    }

    /// <summary>SubTypeWpShrine (1.14d 0x6707a0) — sub-theme pass: iterate the bit-mask of which
    /// consecutive LvlSub rows (by Type) to apply, load each DS1 file, and call DoNotCheckAll (all
    /// outdoor terrain subs have CheckAll=0).</summary>
    private static void ApplySubThemeTerrain(D2RoomExStrc* pRoom, D2DrlgGridStrc* fg, D2DrlgGridStrc* wg, D2DrlgGridStrc* og, int nSubTypeLookupId, int nSubTypeIndex, int nSubTypeCount)
    {
        if (nSubTypeLookupId == -1) return;
        uint dwBitMask = (uint)nSubTypeCount;
        var pLine = DrlgTables.LvlSubGetLineFromSubType(nSubTypeLookupId);
        if (pLine == null) return;
        for (; dwBitMask != 0; dwBitMask >>= 1, pLine++)
        {
            if ((dwBitMask & 1) == 0) continue;
            TileSub.InitializeDrlgFile(null, pLine);
            var pFile = pLine->pDrlgFile;
            if (pFile == null) continue;
            if (pFile->nSubstGroups == 0) continue;
            if (pLine->CheckAll == 0) DoNotCheckAll(pRoom, fg, wg, og, pLine, nSubTypeIndex);
            // CheckAll (nAct 1 or 2 path) not needed for outdoor terrain subs (all CheckAll=0).
        }
    }

    /// <summary>Materialize one outdoor FLOOR room (an 8x8 CreateOutdoorRoomEx shell). Faithful
    /// transform of InitGridCells (0067d2d0), including the ACT_I wall-orientation overlay (step 3)
    /// when `overlay` is supplied. Then the shared Count/Alloc/InitRoomTiles orchestration.</summary>
    public static CollTile[] MaterializeOutdoorFloorRoom(nint lib, int ws_x, int ws_y, int nLevelType, int level_id, int room_seed,
        OutdoorOverlay? overlay, int nSubType, int nSubTheme, int nSubThemePicked, int nWaypointCount, int nShrineCount,
        NearRooms? near, D2RoomExStrc* preset_room)
    {
        using var ar = new Scratch();

        // Grid is (WorldSize+1) per InitGridCells; the floor footprint is 8x8.
        int gw = ws_x + 1;
        int gh = ws_y + 1;
        int ncells = gw * gh;

        D2DrlgStrc drlg = default;
        D2DrlgLevelStrc level = default;
        level.pDrlg = &drlg;
        level.eD2LevelId = level_id;

        D2RoomExStrc room = default;
        // InitGridCells: sSeed.nSeedLow = nSeed, nSeedHigh = 0x29a.
        SetupRoom(&room, &level, lib, new D2SeedStrc { nSeedLow = room_seed, nSeedHigh = 0x29a }, ws_x, ws_y);
        var pRoom = &room;

        // -- Build the outdoor floor grid (InitGridCells body).
        var fg = BuildGrid(ar, gw, gh, ar.Alloc<int>(ncells));
        // Room wall grid (apWallGrids[0]): sub-theme groups whose DS1 carries wall layers stamp
        // their wall cells here. InitGridCells hardcodes nWallLayerCount=1 and allocates it.
        var wg = BuildGrid(ar, gw, gh, ar.Alloc<int>(ncells));
        // Room orient grid for the sub-theme wall cells.
        var og = BuildGrid(ar, gw, gh, ar.Alloc<int>(ncells));

        // Base fill: 8x8 floor cells = 0x40002 (OverwriteFlag / op 3).
        for (int yy = 0; yy < 8 && yy < ws_y; yy++)
            for (int xx = 0; xx < 8 && xx < ws_x; xx++)
                DrlgGrid.AlterGridFlag(fg, xx, yy, 0x40002, 3);
        // ACT_I step 3: the wall/edge orientation overlay (interior cliffs). Runs AFTER the 8x8
        // base-fill and BEFORE the level-type OR-fill (InitGridCells order).
        if (overlay is OutdoorOverlay ov) ApplyAct1WallOverlay(ar, fg, ov, ws_x, ws_y);

        // Sub-theme passes (SubTypeWpShrine, 3 calls in InitGridCells) run after
        // AllocRoomTileGrid, in this exact order: waypoint, shrine, terrain. The waypoint and
        // shrine calls place their own LvlSub DS1s AND consume the room seed via DoNotCheckAll.
        AllocRoomTileGrid(pRoom, ar);
        {
            // ApplyLvlSubTileData calls DRLGROOMTILE_GrowTileDataArray for the shadow tiles it is
            // about to spawn, so the roof array has to exist BEFORE the sub passes run.
            var pg = pRoom->pRoomTiles;
            int n = ncells + 16;
            pg->pRoofTiles = ar.Alloc<D2DrlgTileDataStrc>(n);
            pg->nRoofTilesMax = n;
        }
        var pDef = DrlgTables.LevelDefsGetLine(level_id);
        if (nWaypointCount != 0) ApplySubThemeTerrain(&room, fg, wg, og, pDef->SubWaypoint, 0, nWaypointCount);
        if (nShrineCount != 0) ApplySubThemeTerrain(&room, fg, wg, og, pDef->SubShrine, 0, nShrineCount);
        ApplySubThemeTerrain(&room, fg, wg, og, nSubType, nSubTheme, nSubThemePicked);

        // OR the level-type fill flag into cells with no terrain bits (OrFlag / op 0).
        int nGridFlag = OutdoorFillFlag(nLevelType, level_id);
        if (nGridFlag != 0)
        {
            for (int yy = 0; yy < gh; yy++)
                for (int xx = 0; xx < gw; xx++)
                    if ((DrlgGrid.GetGridFlags(fg, xx, yy) & 0x3f0ff80) == 0) DrlgGrid.AlterGridFlag(fg, xx, yy, nGridFlag, 0);
        }
        // InitGridCells' closing pair: FlagOperations(wall,0,4) then FlagOperations(floor,0,4)
        // ORs CELLFLAGS_0x04 into each grid's BORDER ring.
        FlagBorderCells(wg, CELLFLAGS_0x04);
        FlagBorderCells(fg, CELLFLAGS_0x04);

        // -- Count -> alloc -> InitRoomTiles (as DRLGROOMEX_InitializeMazeRoom).
        CountTilesFromGrid(pRoom, fg, 0, 0, 0);
        CountWallTilesFromGrid(pRoom, wg, og, 0, 0);
        CountTilesFromGrid(pRoom, wg, 0, 0, 0);

        var pTileGrid = room.pRoomTiles;
        pTileGrid->nFloorTilesMax += 16; // headroom (a few cells route to base tiles)
        pTileGrid->nWallTilesMax += ncells + 16;
        pTileGrid->nRoofTilesMax += ncells + 16;
        AllocTileDataArrays(pRoom, ar);

        var ctx = NewCtx(near);
        var saved = g_ctx;
        g_ctx = ctx;
        try
        {
            InitRoomTiles(pRoom, fg, null, 0, 0, 0);
            // Materialize the sub-theme wall grid (orient grid as pOtherGrid), as InitGridCells does.
            InitRoomTiles(pRoom, wg, og, 0, 0, 0);
            HandOverPresetUnits(&room, preset_room);

            if (near is NearRooms nr) PublishRoomTiles(nr, ctx.linked, lib, room.sSeed);

            return CollectCollTiles(pTileGrid, ws_x, ws_y, 1);
        }
        finally
        {
            g_ctx = saved;
        }
    }
}

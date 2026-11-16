// Port of libd2 packages/drlg/src/drlg/tilegen.zig (MIT, (c) 2026 jaenster).
//
// Faithful transform of the DRLGROOMTILE_* tile-builders whose bodies live in
// ghidra-reconstruct/output/D2Common/Drlg/Drlg.cpp (namespace D2Common::Drlg) plus the
// D2Common::Dungeon tile-project lookup they bottom out in (Dungeon.cpp
// TILEPROJECT_LookupTilesInAllProjects -> D2CMP FINDTILE_Lookup).
//
// These resolve a room's grid cells (main/sub/orientation) against the LEVEL's loaded DT1 tile
// library into D2DrlgTileDataStrc entries. The "tile library entry" the engine stores
// (D2TileLibraryEntryStrc*) is, for us, a native NTile* — it carries exactly the identity + 5x5
// subtile collision block the entry does, which is all the collision consumer needs.
//
// Recon addresses (1.14d win):
//   GetTileLibraryEntry    0066d820   Drlg.cpp:1366
//   SetWallTileFlags       0066db20   Drlg.cpp:1451
//   CreateWallTileData     0066dc50   Drlg.cpp:1523
//   FillTileData           0066dde0   Drlg.cpp:1601
//   CreateFloorTileData    0066def0   Drlg.cpp:1663
//   CreateShadowTileData   0066df40   Drlg.cpp:1680
//   LookupTilesInAllProjects 00604ae0 Dungeon.cpp:149
//   FINDTILE_Lookup        0060d040   FindTiles.cpp:191
//
// Match key (FINDTILE_Lookup): a tile matches when its orientation == nTileType,
// mainIndex == nMainIndex, subIndex == nSub.
//
// The debug probes of the Zig source (SEQ/SEQW/PICK dumps) are not ported.

using System.Runtime.InteropServices;
using D2Dotnet.Maps.Formats;

namespace D2Dotnet.Maps.Engine;

/// <summary>One DT1 tile in native memory (identity + rarity + 5x5 subtile block, row 0 = top).</summary>
internal unsafe struct NTile
{
    public int orientation;
    public int main;
    public int sub;
    public int rarity;
    public fixed byte flags[25];

    /// <summary>Collision flags at subtile (sx, sy), sx/sy in 0..5. Returns 0 out of range.</summary>
    public byte Subtile(int sx, int sy) => (uint)sx >= 5 || (uint)sy >= 5 ? (byte)0 : flags[sy * 5 + sx];
}

/// <summary>A parsed DT1 copied to native memory, so tile-data entries can point at its tiles.
/// Immutable; owned by the <see cref="DrlgData"/> cache.</summary>
internal sealed unsafe class NativeDt1
{
    public readonly Dt1File Src;
    public readonly NTile* Tiles;
    public readonly int Count;

    public NativeDt1(Dt1File src)
    {
        Src = src;
        Count = src.Tiles.Length;
        Tiles = (NTile*)NativeMemory.AllocZeroed((nuint)Math.Max(1, Count), (nuint)sizeof(NTile));
        for (int i = 0; i < Count; i++)
        {
            var t = src.Tiles[i];
            Tiles[i].orientation = t.Orientation;
            Tiles[i].main = t.Main;
            Tiles[i].sub = t.Sub;
            Tiles[i].rarity = t.Rarity;
            for (int k = 0; k < 25 && k < t.Flags.Length; k++) Tiles[i].flags[k] = t.Flags[k];
        }
    }

    ~NativeDt1() => NativeMemory.Free(Tiles);
}

internal static unsafe class TileGen
{
    private static NTile* MakeFill(int sub, byte flag)
    {
        var t = (NTile*)NativeMemory.AllocZeroed((nuint)sizeof(NTile));
        t->orientation = 10;
        t->main = 0;
        t->sub = sub;
        t->rarity = 1;
        for (int i = 0; i < 25; i++) t->flags[i] = flag;
        return t;
    }

    /// <summary>The engine's Act1\Outdoors\Blank.dt1 "blank fill" tiles, returned when neither the
    /// primary identity nor the type-10 fallback resolve in the loaded DT1 set:
    ///   main=30 sub=0 -> 0x05 (block_walk|wall)  — uncarved solid rock
    ///   main=30 sub=1 -> 0x01 (block_walk only)  — the Arcane-Sanctuary/void fill (0x1e00100)
    /// (process-lifetime, never freed)</summary>
    private static readonly NTile* solid_fill_tile = MakeFill(0, 0x05);
    private static readonly NTile* blank_fill_tile = MakeFill(1, 0x01);

    /// <summary>The engine's type-10 fallback entry: a real tile with an all-zero collision block.</summary>
    public static readonly NTile* zero_coll_tile = MakeFill(0, 0);

    public static NTile* SolidFillTile() => solid_fill_tile;

    // ---- tile libraries ----------------------------------------------------------------
    // The room's loaded tile library: the engine's `apTiles[32]` array of tile projects,
    // modelled here as the set of parsed DT1 files (in load order). The room's apTiles[0] holds
    // a (1-based) handle into this per-thread registry; the registry lives for one generation.

    [ThreadStatic] private static List<NativeDt1[]>? _libs;

    public static nint RegisterLib(NativeDt1[] dts)
    {
        var l = _libs ??= new List<NativeDt1[]>();
        l.Add(dts);
        return l.Count;
    }

    public static NativeDt1[] Lib(nint handle) => _libs![(int)handle - 1];

    public static void ResetLibs() => _libs?.Clear();

    private static NativeDt1[] LibFromRoom(D2RoomExStrc* pRoomEx) => Lib(pRoomEx->apTiles[0]);

    /// <summary>TILEPROJECT_LookupTilesInAllProjects (Dungeon.cpp:149) -> FINDTILE_Lookup
    /// (FindTiles.cpp:191), collapsed: walk every tile project in load order and append entries
    /// matching (orientation==nTileType, main==nMain, sub==nSub) up to nMax. Enumeration order
    /// matters for the rarity pick and the (10,0,0) fallback: the engine walks apTiles slots in
    /// file order (0x604ae0), but within one DT1 the FINDTILE hash chains are push-front
    /// (AllocHashEntry 0x60cf00 / AddTileToChain 0x60cea0 over records 0..N-1), so same-identity
    /// tiles come out in REVERSE file order — the last matching record in a file wins index 0.</summary>
    private static int LookupTilesInAllProjects(D2RoomExStrc* pRoomEx, int nTileType, int nMain, int nSub, NTile** results, int nMax)
    {
        var lib = LibFromRoom(pRoomEx);
        int n = 0;
        foreach (var d in lib)
        {
            foreach (var ti in d.Src.Lookup(nTileType, nMain, nSub))
            {
                if (n >= nMax) return n;
                results[n] = d.Tiles + ti;
                n += 1;
            }
        }
        return n;
    }

    /// <summary>DRLGROOMTILE_GetTileLibraryEntry (Drlg.cpp:1366, 1.14d 0066d820). Resolves
    /// (nTileType, nGridFlags) to a tile-library entry, consuming the room seed for the
    /// rarity-weighted pick. nGridFlags decode: main=(f&gt;&gt;0x14)&amp;0x3f, sub=(f&gt;&gt;8)&amp;0xff.</summary>
    public static NTile* GetTileLibraryEntry(D2RoomExStrc* pRoomEx, int nTileType, uint nGridFlags)
    {
        var aTileResults = stackalloc NTile*[40];
        int nMainIndex = 0;
        int nSub = 0;
        if (nGridFlags != 0)
        {
            nMainIndex = (int)((nGridFlags >> 0x14) & 0x3f);
            nSub = (int)((nGridFlags >> 8) & 0xff);
        }

        int nCount = LookupTilesInAllProjects(pRoomEx, nTileType, nMainIndex, nSub, aTileResults, 0x28);
        if (nCount == 0)
        {
            // Engine (0x66d820 line 1385): retry as the special orientation-10 (main=0, sub=0)
            // tile and return the FIRST match — no rarity roll, no seed advance.
            int nFallback = LookupTilesInAllProjects(pRoomEx, 10, 0, 0, aTileResults, 0x28);
            if (nFallback != 0) return aTileResults[0];
            // Defensive last resort for asset-stripped builds.
            if (nMainIndex == 30) return nSub == 1 ? blank_fill_tile : solid_fill_tile;
            return null; // engine: ERROR_UnrecoverableInternalError_Halt(line 0x73)
        }

        // Rarity sum over the matches (recon 1396-1409).
        uint nRaritySum = 0;
        for (int i = 0; i < nCount; i++) nRaritySum = unchecked(nRaritySum + (uint)aTileResults[i]->rarity);

        // Weighted index into [0, nRaritySum) advancing the room seed (recon 1410-1424).
        uint nRandom = 0;
        if ((int)nRaritySum >= 1)
        {
            var next = DrlgRng.SeedNext(pRoomEx->sSeed);
            pRoomEx->sSeed = next;
            if ((nRaritySum & unchecked(nRaritySum - 1)) == 0)
            {
                nRandom = unchecked(nRaritySum - 1) & (uint)next.nSeedLow;
            }
            else
            {
                // 0x66d8f4: XOR EDX,EDX; DIV EBX — the engine mods the 32-bit NEW LOW WORD only.
                // The recon's "(uint64_t)sNewSeed % nRaritySum" is a Ghidra mis-widening.
                nRandom = (uint)next.nSeedLow % nRaritySum;
            }
        }

        // Walk the rarity ladder to the selected entry (recon 1426-1442).
        int nSelectedIndex = 0;
        long nRarityEntry = (long)nRandom + 1;
        if (nRaritySum == 0) return aTileResults[0];
        while (nCount > 1 && nRarityEntry > 0)
        {
            nRarityEntry -= aTileResults[nSelectedIndex]->rarity;
            nSelectedIndex += 1;
            if (nSelectedIndex >= nCount) break;
        }
        if (nSelectedIndex == 0) return aTileResults[0];
        nSelectedIndex -= 1;
        return aTileResults[nSelectedIndex];
    }

    /// <summary>CoordsMiniMapToScreen — Transform.cpp:40 (1.14d). Isometric tile-&gt;screen.</summary>
    private static void CoordsMiniMapToScreen(int* pX, int* pY)
    {
        int x = *pX, y = *pY;
        *pX = unchecked((x - y) * 0x50);
        *pY = unchecked((y + x) * 0x50) >> 1;
    }

    private static void ApplyGridFlagBits(D2DrlgTileDataStrc* pTileData, uint nGridFlags, bool seedFlags)
    {
        if (seedFlags) pTileData->nFlags |= (int)((nGridFlags >> 0x12) & 3) * 0x4000 + 0x4000;
        if ((sbyte)(byte)nGridFlags < 0) pTileData->nFlags |= 1;
        if ((nGridFlags & 0x10000000) != 0) pTileData->nFlags |= 0x102;
        if ((nGridFlags & 0x20000) != 0) pTileData->nFlags |= 0x40;
        if ((nGridFlags & 0x10000) != 0) pTileData->nFlags |= 0x80;
        if ((nGridFlags & 8) != 0) pTileData->nFlags |= 4;
        if ((int)nGridFlags < 0) pTileData->nFlags |= 8; else pTileData->nFlags &= -9;
        if ((nGridFlags & 0x4000000) != 0) pTileData->nFlags |= 0x20c;
        if ((nGridFlags & 0x20000000) != 0) pTileData->nFlags |= 0x800;
        if ((nGridFlags & 4) != 0) pTileData->nFlags |= 0x2000;
        // GetTileCount(entry) draw bits (recon): entry tile-flags -> nFlags 4/0x800. Not modelled
        // (the tile carries subtile flags, not the entry tile-flags); collision reads the subtile
        // block, unaffected.
    }

    /// <summary>DRLGROOMTILE_FillTileData (Drlg.cpp:1601, 1.14d 0066dde0). __fastcall.</summary>
    public static void FillTileData(D2RoomExStrc* pRoomEx, D2DrlgTileDataStrc* pTileData, int nPosX, int nPosY, uint nGridFlags, NTile* pTileLibEntry)
    {
        pTileData->pTileLibraryEntry = (D2TileLibraryEntryStrc*)pTileLibEntry;
        pTileData->nTileType = 0;
        pTileData->nFlags = 0;
        pTileData->nTransitionFlags = 0;
        pTileData->nGreen = 0xff;
        pTileData->nBlue = 0xff;
        pTileData->nRed = 0xff;
        if (pRoomEx != null)
        {
            pTileData->nPosX = nPosX - pRoomEx->sCoords.WorldPosition.x;
            pTileData->nPosY = nPosY - pRoomEx->sCoords.WorldPosition.y;
            int sx = nPosX, sy = nPosY + 1;
            CoordsMiniMapToScreen(&sx, &sy);
            pTileData->nScreenX = sx;
            pTileData->nScreenY = sy + 0x28;
        }
        ApplyGridFlagBits(pTileData, nGridFlags, true);
    }

    /// <summary>DRLGPRESET_FindPresetTypeIndex 0x66d960 static tables, live-read from Game.exe
    /// .data (header triples @0x6eefc8: {levelId, firstRow, lastRow}; rows of 7 dwords @0x6ef188:
    /// {main, orientByte, subFlag, id, unitType, offX, offY}; unitType 1 = monster, 2 = object).</summary>
    private static readonly int[,] PRESET_TYPE_HDR =
    {
        { 28, 0, 3 }, { 29, 0, 3 }, { 30, 0, 3 }, { 31, 0, 3 },
        { 26, 4, 6 }, { 27, 4, 6 }, { 32, 5, 9 }, { 33, 5, 9 },
        { 34, 10, 11 }, { 35, 10, 11 }, { 36, 10, 11 }, { 37, 10, 12 },
        { 51, 13, 14 }, { 52, 15, 18 }, { 53, 15, 18 }, { 54, 15, 18 },
        { 55, 19, 20 }, { 56, 19, 20 }, { 57, 19, 20 }, { 58, 19, 20 },
        { 59, 19, 20 }, { 60, 19, 20 }, { 61, 19, 20 }, { 66, 19, 20 },
        { 67, 19, 20 }, { 68, 19, 20 }, { 69, 19, 20 }, { 70, 19, 20 },
        { 71, 19, 20 }, { 72, 19, 20 }, { 62, 21, 22 }, { 63, 21, 22 },
        { 64, 21, 22 }, { 109, 23, 24 }, { 111, 24, 33 }, { 112, 24, 33 },
        { 117, 24, 33 },
    };

    private readonly record struct PresetTypeRow(uint main, uint orient, uint sub, int id, int ut, int off_x, int off_y);

    private static readonly PresetTypeRow[] PRESET_TYPE_ROWS =
    {
        new(7, 0, 1, 14, 2, 5, 0),
        new(7, 0, 0, 13, 2, 0, 5),
        new(5, 0, 1, 16, 2, 0, 0),
        new(5, 0, 0, 15, 2, 0, 0),
        new(6, 0, 1, 27, 2, 5, -2),
        new(4, 0, 1, 24, 2, 1, 2),
        new(4, 0, 0, 23, 2, 0, 0),
        new(4, 3, 1, 25, 2, 1, 0),
        new(1, 2, 0, 62, 2, 0, 3),
        new(1, 2, 1, 63, 2, 3, 0),
        new(0, 0, 1, 16, 2, 0, 0),
        new(0, 0, 0, 64, 2, 0, 0),
        new(2, 0, 1, 47, 2, 5, 0),
        new(0, 1, 1, 291, 2, 2, 0),
        new(0, 1, 0, 290, 2, 0, 2),
        new(5, 0, 1, 293, 2, 2, 0),
        new(4, 0, 0, 292, 2, 0, 2),
        new(0, 0, 1, 295, 2, 2, 0),
        new(0, 0, 0, 294, 2, 0, 2),
        new(2, 4, 1, 92, 2, 1, 0),
        new(2, 1, 0, 91, 2, 0, 2),
        new(0, 1, 1, 229, 2, 0, 0),
        new(0, 1, 0, 230, 2, 0, 0),
        new(3, 3, 0, 449, 2, -2, 4),
        new(2, 1, 0, 435, 1, 1, 2),
        new(2, 1, 1, 435, 1, 2, 1),
        new(2, 6, 0, 435, 1, 1, 1),
        new(2, 2, 0, 433, 1, 0, 1),
        new(2, 3, 1, 432, 1, 1, 0),
        new(26, 0, 0, 434, 1, 0, 1),
        new(2, 4, 1, 524, 1, 0, 0),
        new(2, 4, 0, 525, 1, 0, 0),
        new(29, 0, 1, 60, 2, 2, 0),
        new(29, 0, 0, 60, 2, 0, 2),
    };

    /// <summary>DRLGPRESET_FindPresetTypeIndex 0x66d960: header walk in table order, then rows
    /// firstRow..lastRow matching (gf&gt;&gt;0x14&amp;0x3f, gf&gt;&gt;8&amp;0xff, subFlag).</summary>
    private static int FindPresetTypeIndex(int nLevelId, uint nGridFlags, uint nSub)
    {
        for (int h = 0; h < PRESET_TYPE_HDR.GetLength(0); h++)
        {
            if (PRESET_TYPE_HDR[h, 0] != nLevelId) continue;
            for (int i = PRESET_TYPE_HDR[h, 1]; i <= PRESET_TYPE_HDR[h, 2]; i++)
            {
                var r = PRESET_TYPE_ROWS[i];
                if (((nGridFlags >> 0x14) & 0x3f) == r.main && ((nGridFlags >> 8) & 0xff) == r.orient && nSub == r.sub) return i;
            }
        }
        return -1;
    }

    /// <summary>Preset::CreatesPresets 0x66d9e0 — a tile that marks a preset (a wall torch, a tomb
    /// shrine, a barricade tower, ...) puts that unit on the room, at the tile in room-relative
    /// subtiles plus the row's offset. Object ids 0x5b/0x5c (the tomb shrines) first roll the room
    /// seed: 0 of 3 spawns nothing. Called for type-8/9 walls (SetWallTileFlags, with the wall
    /// tile, whose nFlags 0x20 marks it spent) and for the orient-8/9 preset markers (ProcessTile,
    /// no tile); nSub = (tile type == 9).</summary>
    public static void CreatesPresets(D2RoomExStrc* pRoomEx, uint nGridFlags, int nWorldX, int nWorldY, bool bSub9, D2DrlgTileDataStrc* pTile)
    {
        if (pTile != null && (pTile->nFlags & 0x20) != 0) return;
        int lvl = pRoomEx->pLevel->eD2LevelId;
        int idx = FindPresetTypeIndex(lvl, nGridFlags, bSub9 ? 1u : 0u);
        if (idx < 0) return;
        var row = PRESET_TYPE_ROWS[idx];
        int rx = nWorldX - pRoomEx->sCoords.WorldPosition.x;
        int ry = nWorldY - pRoomEx->sCoords.WorldPosition.y;
        Drlg.CoordsRoomToWorld(&rx, &ry);
        rx += row.off_x;
        ry += row.off_y;
        if (rx < 0 || ry < 0) return;
        if (rx >= pRoomEx->sCoords.WorldSize.x * 5) return;
        if (ry >= pRoomEx->sCoords.WorldSize.y * 5) return;
        int nClassId = row.id;
        int bUseMonStats = 0;
        if (row.ut == 1)
        {
            // D2Client CheckMonStatId 0x4e6c50: an id past MonStats becomes -1.
            if (nClassId < 0 || nClassId >= GenContext.Data.Presets.MonSize) nClassId = -1;
            bUseMonStats = 1;
        }
        else if (row.ut == 2 && nClassId > 0x5a && nClassId < 0x5d && DrlgRng.GetModuloFromSeed(&pRoomEx->sSeed, 3) == 0)
        {
            if (pTile != null) pTile->nFlags |= 0x20;
            return;
        }
        DrlgRoom.AllocPresetUnit(pRoomEx, null, row.ut, nClassId, bUseMonStats, rx, ry);
        if (pTile != null) pTile->nFlags |= 0x20;
    }

    /// <summary>DRLGROOMTILE_SetWallTileFlags (Drlg.cpp:1451, 1.14d 0066db20).</summary>
    public static void SetWallTileFlags(D2DrlgTileDataStrc* pTileData, int nTileType, uint nGridFlags)
    {
        if (nTileType != 0xd) pTileData->nFlags |= (int)((nGridFlags >> 0x12) & 3) * 0x4000 + 0x4000;
        if (nTileType == 0xe) pTileData->nFlags |= 4;
        else if (nTileType == 0xb || nTileType == 10 || nTileType == 9 || nTileType == 8) pTileData->nFlags |= 2;
        ApplyGridFlagBits(pTileData, nGridFlags, false);
        // GetTileCount(entry) draw bits omitted (see ApplyGridFlagBits note).
    }

    /// <summary>DRLGROOMTILE_CreateWallTileData (Drlg.cpp:1523, 1.14d 0066dc50). __fastcall.</summary>
    public static D2DrlgTileDataStrc* CreateWallTileData(D2RoomExStrc* pRoomEx, D2DrlgTileDataStrc** ppTileHead, int nPosX, int nPosY, uint nGridFlags, NTile* pTileLibEntry, int nTileType)
    {
        var pTileGrid = pRoomEx->pRoomTiles;
        var pTileData = &pTileGrid->pWallTiles[pTileGrid->nWalls];
        if (ppTileHead != null)
        {
            pTileData->pNext = *ppTileHead;
            *ppTileHead = pTileData;
        }
        else
        {
            pTileData->pNext = null;
        }
        pTileGrid->nWalls += 1;

        pTileData->nPosX = nPosX - pRoomEx->sCoords.WorldPosition.x;
        pTileData->nPosY = nPosY - pRoomEx->sCoords.WorldPosition.y;
        int sx = nPosX, sy = nPosY + 1;
        CoordsMiniMapToScreen(&sx, &sy);
        pTileData->nScreenY = sy + 0x28;
        pTileData->nScreenX = sx;
        pTileData->pTileLibraryEntry = (D2TileLibraryEntryStrc*)pTileLibEntry;
        pTileData->nFlags = 0;
        pTileData->nTransitionFlags = 0;
        pTileData->nGreen = 0xff;
        pTileData->nBlue = 0xff;
        pTileData->nRed = 0xff;
        pTileData->nTileType = nTileType;
        // Engine SetWallTileFlags 0x66db20 runs CreatesPresets FIRST for type-8/9 walls
        // (barricade towers, tomb shrine walls, wall torches).
        if (nTileType == 8 || nTileType == 9) CreatesPresets(pRoomEx, nGridFlags, nPosX, nPosY, nTileType == 9, pTileData);
        SetWallTileFlags(pTileData, nTileType, nGridFlags);
        if (nTileType != 3) return pTileData;

        // type-3 wall spawns its type-4 shadow companion (recon 1554-1595).
        var nShadowEntry = GetTileLibraryEntry(pRoomEx, 4, nGridFlags);
        CreateWallTileData(pRoomEx, ppTileHead, nPosX, nPosY, nGridFlags, nShadowEntry, 4);
        return pTileData;
    }

    /// <summary>DRLGROOMTILE_CreateShadowTileData (Drlg.cpp:1680, 1.14d 0066df40). __fastcall.</summary>
    public static D2DrlgTileDataStrc* CreateShadowTileData(D2RoomExStrc* pRoomEx, D2DrlgTileDataStrc** ppTileHead, int nPosX, int nPosY, uint nGridFlags, NTile* pTileLibEntry)
    {
        var pTileGrid = pRoomEx->pRoomTiles;
        var pTileData = &pTileGrid->pRoofTiles[pTileGrid->nShadows];
        if (ppTileHead != null)
        {
            pTileData->pNext = *ppTileHead;
            *ppTileHead = pTileData;
        }
        else
        {
            pTileData->pNext = null;
        }
        pTileGrid->nShadows += 1;

        pTileData->nPosX = nPosX - pRoomEx->sCoords.WorldPosition.x;
        pTileData->nPosY = nPosY - pRoomEx->sCoords.WorldPosition.y;
        int sx = nPosX, sy = nPosY + 1;
        CoordsMiniMapToScreen(&sx, &sy);
        pTileData->nScreenY = sy + 0x28;
        pTileData->nGreen = 0xff;
        pTileData->nBlue = 0xff;
        pTileData->nRed = 0xff;
        pTileData->nScreenX = sx;
        pTileData->pTileLibraryEntry = (D2TileLibraryEntryStrc*)pTileLibEntry;
        pTileData->nTileType = 0xd;
        pTileData->nFlags = 0;
        pTileData->nTransitionFlags = 0;
        if ((sbyte)(byte)nGridFlags < 0) pTileData->nFlags = 1;
        if ((nGridFlags & 0x10000000) != 0) pTileData->nFlags |= 0x102;
        if ((nGridFlags & 0x20000) != 0) pTileData->nFlags |= 0x40;
        if ((nGridFlags & 0x10000) != 0) pTileData->nFlags |= 0x80;
        if ((nGridFlags & 8) != 0) pTileData->nFlags |= 4;
        if ((int)nGridFlags < 0) pTileData->nFlags |= 8; else pTileData->nFlags &= -9;
        if ((nGridFlags & 0x4000000) != 0) pTileData->nFlags |= 0x20c;
        if ((nGridFlags & 0x20000000) != 0) pTileData->nFlags |= 0x800;
        if ((nGridFlags & 4) != 0) pTileData->nFlags |= 0x2000;
        return pTileData;
    }
}

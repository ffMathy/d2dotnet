// Port of libd2 packages/drlg/src/drlg/maze/Maze.zig and drlg/enums.zig (MIT, (c) 2026 jaenster).
//
// Mechanical transform of recon/closure/Maze.cpp (D2Common::Drlg::Maze). Faithful by
// construction; field access by NAME, RNG -> DrlgRng, allocs -> Pool, tables -> DrlgTables, core
// room ops -> DrlgRoom, seed-free geometry + the seed-consuming Preset calls -> MazeDeps.
//
// pRoomExData overlay: nPresetType==2 rooms carry a D2DrlgPresetRoomStrc (DrlgGrid.h). The
// recon's raw pRoomData[0]/[1]/[3] integer payloads map to its leading int fields:
//   [0] nLevelPrest -> def   [1] nPickedFile -> variant   [3] nFlags -> flags (bit1 = HAS_MAP_DS1)

namespace D2Dotnet.Maps.Engine.Maze;

/// <summary>LvlTypes.txt "LevelType" — the maze generator's per-type dispatch key
/// (DRLGMAZE_GenerateLevel / DRLGMAZE_PickRoomPreset switch on pLevel->nLevelType).</summary>
internal static class LevelType
{
    public const int act1_caves = 3;
    public const int act1_crypt = 4;
    public const int barracks_family = 7; // grid + grow (Barracks via the level-id tail)
    public const int act1_jail = 8;
    public const int act1_catacombs = 10;
    public const int act2_sewer = 0xd;
    public const int generic_0e = 0xe; // plain BuildRoomGrid, no special
    public const int generic_0f = 0xf;
    public const int act2_tombs = 0x11;
    public const int act2_lair = 0x12;
    public const int act2_arcane = 0x13;
    public const int act3_durance = 0x16;
    public const int generic_17 = 0x17;
    public const int act3_dungeon = 0x18;
    public const int act3_sewers = 0x19;
    public const int river_family = 0x1c; // grow only (River of Flames via the level-id tail)
    public const int act5_temple = 0x20;
    public const int act5_icecaves = 0x21;
    public const int act5_worldstone = 0x22;
    public const int act5_baal = 0x23;
}

/// <summary>Special-case level ids the maze generators branch on (raw Levels.txt "Id").</summary>
internal static class MazeLevelId
{
    // DRLGMAZE_PickRoomPreset 0xf/0x17 variant overrides.
    public const int tower_lower_2 = 0x34; // type-0xf variant=2 (preset 0x169)
    public const int tower_lower_4 = 0x36; // type-0xf variant=3 (presets 0x169/0x167)
    public const int arcane_0x54 = 0x54; // type-0x17 override 0x296 -> 0x298
    public const int arcane_0x55 = 0x55; // type-0x17 override 0x295 -> 0x297
    // GenerateLevel per-level-id arms.
    public const int tomb_fixed = 0x3d; // Tombs arm: fixed preset 0x1e0
    public const int act3_sewer_special = 0x5c; // Act3 sewers 5x5-grid special block
    public const int barracks = 0x1c; // post-switch Barracks placement
    public const int river_of_flames = 0x6b; // post-switch River placement
    // Act5 ice-cave fixed-preset single levels (LevelType 0x21).
    public const int frozen_river = 114; // Ice Cave 1A, def 0x40f-roll
    public const int drifter_cavern = 116; // Ice Cave 2A, def 0x410
    public const int icy_cellar = 119; // Ice Cave 3A, def 0x411
}

internal static unsafe class MazeGen
{
    private const int DIRECTION_SOUTHEAST = MazeDeps.DIRECTION_SOUTHEAST; // 0
    private const int DIRECTION_NORTHEAST = MazeDeps.DIRECTION_NORTHEAST; // 1
    private const int DIRECTION_SOUTHWEST = MazeDeps.DIRECTION_SOUTHWEST; // 2
    private const int DIRECTION_NORTHWEST = MazeDeps.DIRECTION_NORTHWEST; // 3
    private const int DIRECTION_INVALID = MazeDeps.DIRECTION_INVALID; // -1

    private static D2DrlgPresetRoomStrc* RoomData(D2RoomExStrc* pRoomEx) => (D2DrlgPresetRoomStrc*)pRoomEx->pRoomExData;
    public static int GetDef(D2RoomExStrc* pRoomEx) => RoomData(pRoomEx)->nLevelPrest;
    public static void SetDef(D2RoomExStrc* pRoomEx, int v) => RoomData(pRoomEx)->nLevelPrest = v;
    public static int GetVariant(D2RoomExStrc* pRoomEx) => RoomData(pRoomEx)->nPickedFile;
    public static void SetVariant(D2RoomExStrc* pRoomEx, int v) => RoomData(pRoomEx)->nPickedFile = v;
    public static int GetFlags(D2RoomExStrc* pRoomEx) => RoomData(pRoomEx)->nFlags;
    public static void SetFlags(D2RoomExStrc* pRoomEx, int v) => RoomData(pRoomEx)->nFlags = v;
    public static bool HasMapDs1(D2RoomExStrc* pRoomEx) => (GetFlags(pRoomEx) & 2) != 0;
    internal static D2LvlMazeTxt* MazeTxt(D2DrlgLevelStrc* pLevel) => (D2LvlMazeTxt*)pLevel->pDrlgLevelData;

    // DRLGMAZE_PickRoomPreset (Maze.cpp:64, 1.14d 006709b0). Static orientation->preset lookups.
    private static readonly int[,] gaMazePresetLookupExt =
    {
        { 0x0, 0x0 }, { 0x0, 0x420 }, { 0x0, 0x41f }, { 0x0, 0x421 },
        { 0x0, 0x41e }, { 0x415, 0x0 }, { 0x414, 0x0 }, { 0x0, 0x0 },
        { 0x0, 0x41d }, { 0x413, 0x0 }, { 0x412, 0x0 }, { 0x0, 0x0 },
        { 0x0, 0x422 }, { 0x0, 0x0 }, { 0x0, 0x0 }, { 0x0, 0x0 },
    };
    private static readonly int[,] gaMazePresetLookup =
    {
        { 0x0, 0x0, 0x0 }, { 0x0, 0x0, 0x0 }, { 0x0, 0x0, 0x0 }, { 0x0, 0x0, 0x0 },
        { 0x0, 0x0, 0x0 }, { 0x164, 0x168, 0x293 }, { 0x163, 0x167, 0x294 }, { 0x0, 0x0, 0x0 },
        { 0x0, 0x0, 0x0 }, { 0x165, 0x169, 0x295 }, { 0x162, 0x166, 0x296 }, { 0x0, 0x0, 0x0 },
        { 0x0, 0x0, 0x0 }, { 0x0, 0x0, 0x0 }, { 0x0, 0x0, 0x0 }, { 0x0, 0x0, 0x0 },
    };

    /// <summary>DRLGMAZE_PickRoomPreset (Maze.cpp:64). Resolves the orientation-bitmask preset id
    /// for a room from its orth directions + level-type base, writes def/variant, and clears
    /// HAS_MAP_DS1. The recon's lost `_bPickSourcePreset` register param is the build-time
    /// bResetFlag=1 (CLEAR bit1) used by every call site here.</summary>
    public static D2RoomExStrc* PickRoomPreset(D2RoomExStrc* pRoomEx)
    {
        int nPresetIndex = 0;
        int nVariant = -1;
        for (var pOrth = pRoomEx->pOrth; pOrth != null; pOrth = pOrth->pNext)
        {
            switch (pOrth->neDrlgDirection)
            {
                case DIRECTION_SOUTHEAST: nPresetIndex |= 1; break;
                case DIRECTION_NORTHEAST: nPresetIndex |= 8; break;
                case DIRECTION_SOUTHWEST: nPresetIndex |= 2; break;
                case DIRECTION_NORTHWEST: nPresetIndex |= 4; break;
            }
        }

        var pMap = pRoomEx->pLevel;
        int idx = nPresetIndex & 0xf;
        // Preset BASE offsets per level type (arbitrary LvlPrest table bases, faithful to
        // DRLGMAZE_PickRoomPreset 1.14d 006709b0).
        switch (pMap->nLevelType)
        {
            case LevelType.act1_caves: nPresetIndex += 0x34; break;
            case LevelType.act1_crypt: nPresetIndex += 0x6c; break;
            case LevelType.barracks_family: nPresetIndex += 0xa7; break;
            case LevelType.act1_jail: nPresetIndex += 0xcd; break;
            case LevelType.act1_catacombs: nPresetIndex += 0x101; break;
            case LevelType.act2_sewer: nPresetIndex += 0x12d; break;
            case LevelType.generic_0e: nPresetIndex = gaMazePresetLookup[idx, 0]; break;
            case LevelType.generic_0f:
                nPresetIndex = gaMazePresetLookup[idx, 1];
                if (pMap->eD2LevelId == MazeLevelId.tower_lower_2 && nPresetIndex == 0x169) nVariant = 2;
                if (pMap->eD2LevelId == MazeLevelId.tower_lower_4 && (nPresetIndex == 0x169 || nPresetIndex == 0x167)) nVariant = 3;
                break;
            case LevelType.act2_tombs: nPresetIndex += 0x19d; break;
            case LevelType.act2_lair: nPresetIndex += 0x1e1; break;
            case LevelType.act2_arcane: nPresetIndex += 0x1fd; break;
            case LevelType.act3_durance: nPresetIndex += 0x2f1; break;
            case LevelType.generic_17:
                nPresetIndex = gaMazePresetLookup[idx, 2];
                if (pMap->eD2LevelId == MazeLevelId.arcane_0x54 && nPresetIndex == 0x296) nPresetIndex = 0x298;
                if (pMap->eD2LevelId == MazeLevelId.arcane_0x55 && nPresetIndex == 0x295)
                {
                    nPresetIndex = 0x297;
                    SetDef(pRoomEx, nPresetIndex);
                    SetVariant(pRoomEx, nVariant);
                    SetFlags(pRoomEx, GetFlags(pRoomEx) & -3);
                    return pRoomEx;
                }
                break;
            case LevelType.act3_dungeon: nPresetIndex += 0x298; break;
            case LevelType.act3_sewers: nPresetIndex += 0x2c0; break;
            case LevelType.river_family: nPresetIndex += 0x344; break;
            case LevelType.act5_temple: nPresetIndex = gaMazePresetLookupExt[idx, 0]; break;
            case LevelType.act5_icecaves:
                // Maze.cpp:214 — Var1 = DRLGMAZE_IsPresetSingleFile(pMap); if(Var1==0) +0x3ea.
                // For grid-built ice-cave maze rooms the preset is multi-file (Files>1), so
                // IsPresetSingleFile (Drlg.cpp:2474, 1.14d 00670810) is 0 here and the base
                // 0x3ea is always added.
                nPresetIndex += 0x3ea;
                break;
            case LevelType.act5_worldstone: nPresetIndex += 0x422; break;
            case LevelType.act5_baal: nPresetIndex = gaMazePresetLookupExt[idx, 1]; break;
            default: throw new DrlgException("pickRoomPreset: unknown level type");
        }

        if (nPresetIndex == 0) return pRoomEx;
        SetDef(pRoomEx, nPresetIndex);
        SetVariant(pRoomEx, nVariant);
        SetFlags(pRoomEx, GetFlags(pRoomEx) & -3); // _bPickSourcePreset==1 -> clear bit1
        return pRoomEx;
    }

    /// <summary>DRLGMAZE_FindAndReplacePreset (Maze.cpp:248). Finds the first non-locked room
    /// whose def == nPresetId, rewrites def/variant and locks (|=2) or unlocks (&amp;=-3).</summary>
    public static D2RoomExStrc* FindAndReplacePreset(D2DrlgLevelStrc* pLevel, int nPresetId, int nNewPresetId, int nNewArg1, int nEnableFlag)
    {
        var pRoomEx = pLevel->pRoomExFirst;
        while (true)
        {
            if (pRoomEx == null) return null;
            if (!HasMapDs1(pRoomEx) && GetDef(pRoomEx) == nPresetId) break;
            pRoomEx = pRoomEx->pRoomExNext;
        }
        SetDef(pRoomEx, nNewPresetId);
        SetVariant(pRoomEx, nNewArg1);
        if (nEnableFlag != 0) SetFlags(pRoomEx, GetFlags(pRoomEx) & -3);
        else SetFlags(pRoomEx, GetFlags(pRoomEx) | 2);
        return pRoomEx;
    }

    /// <summary>DRLGMAZE_PickRoomPresets (Maze.cpp:277). Merge step: connect pRoomEx to nearby
    /// non-locked rooms when the neighbour's seed roll &lt; Merge threshold.</summary>
    public static void PickRoomPresets(D2RoomExStrc* pRoomEx)
    {
        if (HasMapDs1(pRoomEx)) return;
        var pMazeTxt = MazeTxt(pRoomEx->pLevel);
        for (var pNeighbor = pRoomEx->pLevel->pRoomExFirst; pNeighbor != null; pNeighbor = pNeighbor->pRoomExNext)
        {
            if (pNeighbor == pRoomEx || HasMapDs1(pNeighbor)) continue;
            int nDeltaX = 0, nDeltaY = 0;
            uint bWithin = DrlgRoom.IsWithinDistance(&pRoomEx->sCoords, &pNeighbor->sCoords, 1, &nDeltaX, &nDeltaY);
            if (bWithin != 0 || nDeltaX == nDeltaY) continue;
            bool bConnected = false;
            for (var pOrth = pRoomEx->pOrth; pOrth != null; pOrth = pOrth->pNext)
            {
                if (pOrth->pRoomEx == pNeighbor)
                {
                    bConnected = true;
                    break;
                }
            }
            if (bConnected) continue;
            var sNewSeed = DrlgRng.SeedNext(pNeighbor->sSeed);
            pNeighbor->sSeed = sNewSeed;
            // Roll = newSeedLow % 1000 — the engine divides ONLY the 32-bit low word (1.14d
            // 0x670d16: MUL 0x10624dd3 / SHR EDX,6 / IMUL 0x3e8 / SUB ECX = newLow%1000). Ghidra
            // mis-widened this to (uint64_t)sNewSeed in Maze.cpp:305.
            int nRoll = (int)((uint)sNewSeed.nSeedLow % 1000);
            if (nRoll < pMazeTxt->Merge)
            {
                int nDirection = MazeDeps.GetDirectionFromCoordinates(&pNeighbor->sCoords, &pRoomEx->sCoords);
                if (nDirection != DIRECTION_INVALID)
                {
                    DrlgRoom.AllocNodesForBothRoomEx(pNeighbor, pRoomEx, nDirection);
                    PickRoomPreset(pNeighbor);
                }
            }
        }
    }

    /// <summary>A new maze room sized from the level's LvlMaze row.</summary>
    private static D2RoomExStrc* AllocSizedRoom(D2DrlgLevelStrc* pLevel)
    {
        var p = DrlgRoom.AllocRoomEx(pLevel, 2);
        var t = MazeTxt(p->pLevel);
        p->sCoords.WorldSize.x = t->SizeX;
        p->sCoords.WorldSize.y = t->SizeY;
        return p;
    }

    /// <summary>AllocateRoomEx (Maze.cpp:325).</summary>
    public static D2RoomExStrc* AllocateRoomEx(D2RoomExStrc* pSourceRoom, int nNeighborDir, int nDirection, int bPickPresets)
    {
        var pNew = AllocSizedRoom(pSourceRoom->pLevel);
        if (!MazeDeps.ReplaceRoomWithNewRoom(nNeighborDir, pNew, pSourceRoom))
        {
            DrlgRoom.FreeDrlgRoomEx(pNew);
            return null;
        }
        DrlgRoom.AllocNodesForBothRoomEx(pSourceRoom, pNew, nNeighborDir);
        if (bPickPresets != 0) PickRoomPresets(pNew);
        DrlgRoom.AddRoomExToLevel(pSourceRoom->pLevel, pNew);
        return pNew;
    }

    /// <summary>DRLGMAZE_AllocRoomExAndPickPreset (Maze.cpp:348). Allocates a room in eDir off
    /// pRoomExStrc and assigns it a FIXED preset. Ghidra lost the three register/stack params;
    /// recovered from the engine call sites (1.14d 0x672984 PUSH 0x14d, 0x672a89 PUSH 0x150). The
    /// new room is LOCKED with that fixed def/variant.</summary>
    public static D2RoomExStrc* AllocRoomExAndPickPreset(int eDir, D2RoomExStrc* pRoomExStrc, int nPresetIndex, int nVariant, int bPickSource)
    {
        var pNewRoom = AllocSizedRoom(pRoomExStrc->pLevel);
        if (!MazeDeps.ReplaceRoomWithNewRoom(eDir, pNewRoom, pRoomExStrc))
        {
            DrlgRoom.FreeDrlgRoomEx(pNewRoom);
            return null;
        }
        DrlgRoom.AllocNodesForBothRoomEx(pRoomExStrc, pNewRoom, eDir);
        DrlgRoom.AddRoomExToLevel(pRoomExStrc->pLevel, pNewRoom);
        if (bPickSource != 0) PickRoomPreset(pRoomExStrc);
        SetFlags(pNewRoom, GetFlags(pNewRoom) | 2);
        SetDef(pNewRoom, nPresetIndex);
        SetVariant(pNewRoom, nVariant);
        return pNewRoom;
    }

    /// <summary>ReplaceRoomWith (Maze.cpp:407). Finds the first non-locked room, grows a new room
    /// off it in nDirection, locks it with (nPresetIndex,nVariant).</summary>
    public static D2RoomExStrc* ReplaceRoomWith(int nDirection, int nPresetIndex, int nVariant, D2DrlgLevelStrc* pLevel)
    {
        var pRoomEx = pLevel->pRoomExFirst;
        D2RoomExStrc* pNewRoom = null;
        while (true)
        {
            if (pRoomEx == null) return pNewRoom;
            if (!HasMapDs1(pRoomEx))
            {
                pNewRoom = AllocSizedRoom(pRoomEx->pLevel);
                if (!MazeDeps.ReplaceRoomWithNewRoom(nDirection, pNewRoom, pRoomEx))
                {
                    DrlgRoom.FreeDrlgRoomEx(pNewRoom);
                    pNewRoom = null;
                }
                else
                {
                    DrlgRoom.AllocNodesForBothRoomEx(pRoomEx, pNewRoom, nDirection);
                    DrlgRoom.AddRoomExToLevel(pRoomEx->pLevel, pNewRoom);
                    PickRoomPreset(pRoomEx);
                    SetFlags(pNewRoom, GetFlags(pNewRoom) | 2);
                    SetDef(pNewRoom, nPresetIndex);
                    SetVariant(pNewRoom, nVariant);
                }
                if (pNewRoom != null) return pNewRoom;
            }
            pRoomEx = pRoomEx->pRoomExNext;
        }
    }

    /// <summary>One edge-pass step of BuildRoomGrid: grow a room off pFrom in nDir.</summary>
    private static D2RoomExStrc* GrowGridRoom(D2RoomExStrc* pFrom, int nDir)
    {
        var pNew = AllocSizedRoom(pFrom->pLevel);
        if (!MazeDeps.ReplaceRoomWithNewRoom(nDir, pNew, pFrom))
        {
            DrlgRoom.FreeDrlgRoomEx(pNew);
            return null;
        }
        DrlgRoom.AllocNodesForBothRoomEx(pFrom, pNew, nDir);
        PickRoomPresets(pNew);
        DrlgRoom.AddRoomExToLevel(pFrom->pLevel, pNew);
        PickRoomPreset(pFrom);
        PickRoomPreset(pNew);
        return pNew;
    }

    /// <summary>DRLGMAZE_BuildRoomGrid (Maze.cpp:448). Builds the initial nGridSize x nGridSize
    /// ring of connected rooms off pRoomExFirst (four edge passes).</summary>
    public static void BuildRoomGrid(int nGridSize, D2DrlgLevelStrc* pLevel)
    {
        var pFirstRoom = pLevel->pRoomExFirst;
        var pRoomEx = pFirstRoom;
        var pNewRoom = pFirstRoom;

        int nRemaining = nGridSize - 1;
        if (nRemaining > 0)
        {
            while (true)
            {
                pRoomEx = DrlgRoom.AllocRoomEx(pNewRoom->pLevel, 2);
                var t = MazeTxt(pRoomEx->pLevel);
                pRoomEx->sCoords.WorldSize.x = t->SizeX;
                pRoomEx->sCoords.WorldSize.y = t->SizeY;
                if (!MazeDeps.ReplaceRoomWithNewRoom(1, pRoomEx, pNewRoom))
                {
                    DrlgRoom.FreeDrlgRoomEx(pRoomEx);
                    pRoomEx = null;
                }
                else
                {
                    DrlgRoom.AllocNodesForBothRoomEx(pNewRoom, pRoomEx, 1);
                    PickRoomPresets(pRoomEx);
                    DrlgRoom.AddRoomExToLevel(pNewRoom->pLevel, pRoomEx);
                    PickRoomPreset(pNewRoom);
                    PickRoomPreset(pRoomEx);
                }
                nRemaining -= 1;
                pNewRoom = pRoomEx;
                if (nRemaining == 0) break;
            }
        }

        foreach (int dir in new[] { 0, 3 })
        {
            nRemaining = nGridSize - 1;
            if (nRemaining > 0)
            {
                while (true)
                {
                    var g = GrowGridRoom(pRoomEx, dir);
                    pRoomEx = g; // on failure the recon nulls pRoomEx
                    nRemaining -= 1;
                    if (nRemaining == 0) break;
                }
            }
        }

        int nLast = nGridSize - 2;
        if (nLast > 0)
        {
            while (true)
            {
                pRoomEx = GrowGridRoom(pRoomEx, 2);
                nLast -= 1;
                if (nLast == 0) break;
            }
        }

        DrlgRoom.AllocNodesForBothRoomEx(pRoomEx, pFirstRoom, 2);
        PickRoomPreset(pRoomEx);
        PickRoomPreset(pFirstRoom);
    }

    /// <summary>GetRandomRoomExFromLevel (Maze.cpp:559). Picks a random room; the recon inlines
    /// the pick on the 32-bit LOW word (Ghidra widened the non-pow2 modulo to 64-bit).</summary>
    public static D2RoomExStrc* GetRandomRoomExFromLevel(D2DrlgLevelStrc* pLevel)
    {
        int nCount = pLevel->nRoomExCount;
        uint nSel = 0;
        if (nCount >= 1)
        {
            var sNew = DrlgRng.SeedNext(pLevel->sSeed);
            pLevel->sSeed = sNew;
            uint low = (uint)sNew.nSeedLow;
            uint count = (uint)nCount;
            nSel = (count & unchecked(count - 1)) == 0 ? low & unchecked(count - 1) : low % count;
        }
        var pRoom = pLevel->pRoomExFirst;
        for (; nSel != 0; nSel--) pRoom = pRoom->pRoomExNext;
        return pRoom;
    }

    /// <summary>ActualLevelGeneration (Maze.cpp:590). Grows the level to its room count.</summary>
    public static void ActualLevelGeneration(D2DrlgLevelStrc* pLevel)
    {
        var pMazeTxt = MazeTxt(pLevel);
        int nDiff = pLevel->pDrlg->nDifficulty;
        int nRooms = pMazeTxt->Rooms[nDiff];
        if (pLevel->eD2LevelId == pLevel->pDrlg->nStaffTombLevel) nRooms *= 3;
        if (pLevel->eD2LevelId == pLevel->pDrlg->nBossTombLevel) nRooms *= 2;

        while (pLevel->nRoomExCount < nRooms)
        {
            var pRoomEx = GetRandomRoomExFromLevel(pLevel);
            var nSeed = DrlgRng.SeedNext(pRoomEx->sSeed);
            int nRandDirection = nSeed.nSeedLow & 3;
            pRoomEx->sSeed = nSeed;
            if (!HasMapDs1(pRoomEx))
            {
                var pRoomExNew = AllocSizedRoom(pRoomEx->pLevel);
                if (!MazeDeps.ReplaceRoomWithNewRoom(nRandDirection, pRoomExNew, pRoomEx))
                {
                    DrlgRoom.FreeDrlgRoomEx(pRoomExNew);
                }
                else
                {
                    DrlgRoom.AllocNodesForBothRoomEx(pRoomEx, pRoomExNew, nRandDirection);
                    PickRoomPresets(pRoomExNew);
                    DrlgRoom.AddRoomExToLevel(pRoomEx->pLevel, pRoomExNew);
                    PickRoomPreset(pRoomEx);
                    PickRoomPreset(pRoomExNew);
                }
            }
        }
    }

    /// <summary>DRLGMAZE_CanPlaceRoomInDirection (Maze.cpp:818). Test-places a room in eDir;
    /// returns nonzero if it fits (the test room is freed). NOTE: consumes seed via the test
    /// DRLGROOMEX_AllocRoomEx — part of the faithful seed stream.</summary>
    public static uint CanPlaceRoomInDirection(int eDir, D2RoomExStrc* pRoomEx)
    {
        if (HasMapDs1(pRoomEx)) return 0;
        var pOrth = pRoomEx->pOrth;
        while (true)
        {
            if (pOrth == null)
            {
                var pTestRoom = AllocSizedRoom(pRoomEx->pLevel);
                if (MazeDeps.ReplaceRoomWithNewRoom(eDir, pTestRoom, pRoomEx))
                {
                    DrlgRoom.AllocNodesForBothRoomEx(pRoomEx, pTestRoom, eDir);
                    DrlgRoom.AddRoomExToLevel(pRoomEx->pLevel, pTestRoom);
                    PickRoomPreset(pTestRoom);
                    DrlgRoom.FreeDrlgRoomEx(pTestRoom);
                    return 1;
                }
                DrlgRoom.FreeDrlgRoomEx(pTestRoom);
                return 0;
            }
            if (pOrth->neDrlgDirection == eDir) break;
            pOrth = pOrth->pNext;
        }
        return 0;
    }

    /// <summary>DRLGMAZE_FindTopMostRoom (Maze.cpp:861).</summary>
    public static D2RoomExStrc* FindTopMostRoom(D2DrlgLevelStrc* pLevel)
    {
        D2RoomExStrc* pTopMost = null;
        for (var pRoomEx = pLevel->pRoomExFirst; pRoomEx != null; pRoomEx = pRoomEx->pRoomExNext)
            if (pTopMost == null || pRoomEx->sCoords.WorldPosition.y < pTopMost->sCoords.WorldPosition.y)
                if (CanPlaceRoomInDirection(DIRECTION_NORTHEAST, pRoomEx) != 0) pTopMost = pRoomEx;
        return pTopMost;
    }

    /// <summary>DRLGMAZE_FindLeftMostRoom (Maze.cpp:879).</summary>
    public static D2RoomExStrc* FindLeftMostRoom(D2DrlgLevelStrc* pLevel)
    {
        D2RoomExStrc* pLeftMost = null;
        for (var pRoomEx = pLevel->pRoomExFirst; pRoomEx != null; pRoomEx = pRoomEx->pRoomExNext)
            if (pLeftMost == null || pRoomEx->sCoords.WorldPosition.x < pLeftMost->sCoords.WorldPosition.x)
                if (CanPlaceRoomInDirection(DIRECTION_SOUTHEAST, pRoomEx) != 0) pLeftMost = pRoomEx;
        return pLeftMost;
    }

    /// <summary>DRLGMAZE_FindRightMostRoom (Maze.cpp:897).</summary>
    public static D2RoomExStrc* FindRightMostRoom(D2DrlgLevelStrc* pLevel)
    {
        D2RoomExStrc* pRightMost = null;
        for (var pRoomEx = pLevel->pRoomExFirst; pRoomEx != null; pRoomEx = pRoomEx->pRoomExNext)
            if (pRightMost == null || pRightMost->sCoords.WorldPosition.x < pRoomEx->sCoords.WorldPosition.x)
                if (CanPlaceRoomInDirection(DIRECTION_SOUTHWEST, pRoomEx) != 0) pRightMost = pRoomEx;
        return pRightMost;
    }

    /// <summary>DRLGMAZE_FindBottomMostRoom (Maze.cpp:915).</summary>
    public static D2RoomExStrc* FindBottomMostRoom(D2DrlgLevelStrc* pLevel)
    {
        D2RoomExStrc* pBottomMost = null;
        for (var pRoomEx = pLevel->pRoomExFirst; pRoomEx != null; pRoomEx = pRoomEx->pRoomExNext)
            if (pBottomMost == null || pBottomMost->sCoords.WorldPosition.y < pRoomEx->sCoords.WorldPosition.y)
                if (CanPlaceRoomInDirection(DIRECTION_NORTHWEST, pRoomEx) != 0) pBottomMost = pRoomEx;
        return pBottomMost;
    }

    /// <summary>ReplaceRoom (Maze.cpp:935). Replaces a source-preset room with a dest preset, or
    /// grows one if none exists; advances *pRoll (mod 4, signed) when provided.</summary>
    public static void ReplaceRoom(in D2DrlgReplaceRoomStrc rr, D2DrlgLevelStrc* pLevel, int* pRoll)
    {
        var pRoomEx = pLevel->pRoomExFirst;
        int nDestPresetId = rr.nDestLevelPrestId;
        int nDestPickedFile = rr.nDestPickedFile;
        while (true)
        {
            if (pRoomEx == null)
            {
                ReplaceRoomWith(rr.nDirection, nDestPresetId, nDestPickedFile, pLevel);
                AdvanceRoll(pRoll);
                return;
            }
            if (!HasMapDs1(pRoomEx) && GetDef(pRoomEx) == rr.nSourceLevelPrestId)
            {
                SetFlags(pRoomEx, GetFlags(pRoomEx) | 2);
                SetDef(pRoomEx, nDestPresetId);
                SetVariant(pRoomEx, nDestPickedFile);
                AdvanceRoll(pRoll);
                return;
            }
            pRoomEx = pRoomEx->pRoomExNext;
        }
    }

    /// <summary>recon: nNext = (*pRoll + 1) &amp; 0x80000003; if(&lt;0) nNext = (nNext-1 | -4)+1;
    /// (== +1 mod 4)</summary>
    private static void AdvanceRoll(int* p)
    {
        if (p == null) return;
        int nNext = (int)((uint)(*p + 1) & 0x80000003);
        if (nNext < 0) nNext = (int)((uint)(nNext - 1) | 0xFFFFFFFC) + 1;
        *p = nNext;
    }

    internal static D2DrlgReplaceRoomStrc RR(int src, int dst, int file, int dir) =>
        new() { nSourceLevelPrestId = src, nDestLevelPrestId = dst, nDestPickedFile = file, nDirection = dir };

    /// <summary>ForAllButDenOfEvil (Maze.cpp:983). Seed-driven "+15" special-preset replacement
    /// (the ScanReplaceSpecial step that produces the out-of-range Defs).</summary>
    public static void ForAllButDenOfEvil(D2DrlgLevelStrc* pLevel)
    {
        int nFirstFileOffset;
        switch (pLevel->nLevelType)
        {
            case LevelType.act1_caves: nFirstFileOffset = 0x34; break;
            case LevelType.act1_crypt: nFirstFileOffset = 0x6c; break;
            case LevelType.barracks_family: nFirstFileOffset = 0xa7; break;
            case LevelType.act1_jail: nFirstFileOffset = 0xcd; break;
            case LevelType.act1_catacombs: nFirstFileOffset = 0x101; break;
            case LevelType.act2_sewer: nFirstFileOffset = 0x12d; break;
            case LevelType.act2_tombs: nFirstFileOffset = 0x19d; break;
            case LevelType.act3_durance: nFirstFileOffset = 0x2f1; break;
            case LevelType.act3_dungeon: nFirstFileOffset = 0x298; break;
            case LevelType.act3_sewers: nFirstFileOffset = 0x2c0; break;
            default: return;
        }
        if (pLevel->eD2LevelId == (int)AreaId.DenOfEvil) return;

        var aShuffleTable = stackalloc int[16];
        aShuffleTable[8] = 8;
        var DVar2 = DrlgRng.SeedNext(pLevel->sSeed);
        pLevel->sSeed = DVar2;
        // % 15 on the 32-bit low word only (1.14d 0x6736a5: MUL 0x88888889 / SHR EDX,3 = unsigned
        // 32-bit /15 of the new low word). Ghidra mis-widened to 64-bit.
        aShuffleTable[0xf] = (int)((uint)DVar2.nSeedLow % 0xf);
        int nTargetCount = pLevel->nRoomExCount / 5 + 1;
        for (int k = 0; k <= 0xe; k++) if (k != 8) aShuffleTable[k] = k;
        if (nTargetCount < 3) nTargetCount = 2;

        int nMaxTries = pLevel->nRoomExCount * 2;
        int nShuffleCount = 0xf;
        int nShufflePos;
        while (true)
        {
            DVar2 = DrlgRng.SeedNext(pLevel->sSeed);
            pLevel->sSeed = DVar2;
            var DVar3 = DrlgRng.SeedNext(pLevel->sSeed);
            pLevel->sSeed = DVar3;
            int nSwapTemp = nShuffleCount - 1;
            nShuffleCount = nSwapTemp;
            int nIdx2 = (int)((uint)DVar2.nSeedLow % 0xf);
            int nIdx3 = (int)((uint)DVar3.nSeedLow % 0xf);
            (aShuffleTable[nIdx2], aShuffleTable[nIdx3]) = (aShuffleTable[nIdx3], aShuffleTable[nIdx2]);
            nShufflePos = aShuffleTable[0xf];
            if (nSwapTemp == 0) break;
        }

        for (; nTargetCount != 0 && nMaxTries != 0; nMaxTries--)
        {
            int nTargetDef = aShuffleTable[nShufflePos] + nFirstFileOffset;
            aShuffleTable[0xf] = nTargetDef + 0xf;
            for (var pDVar1 = pLevel->pRoomExFirst; pDVar1 != null; pDVar1 = pDVar1->pRoomExNext)
            {
                if (!HasMapDs1(pDVar1) && GetDef(pDVar1) == nTargetDef)
                {
                    SetFlags(pDVar1, GetFlags(pDVar1) | 2);
                    nTargetCount -= 1;
                    SetDef(pDVar1, aShuffleTable[0xf]);
                    SetVariant(pDVar1, -1);
                    break;
                }
            }
            nShufflePos += 1;
            // recon: nShufflePos %= 15 via signed magic divide
            long prod = (long)nShufflePos * 0x77777777;
            int q = (int)(prod >> 0x20) - nShufflePos;
            nShufflePos += ((q >> 3) - (q >> 0x1f)) * 0xf;
        }
    }

    /// <summary>gaMazeLevelTypeCaseTable (globals_drlg.cpp:208). Maps level type -> preset case.</summary>
    private static readonly byte[] gaMazeLevelTypeCaseTable =
    {
        0xf4, 0x39, 0x67, 0x0, 0x0,
        0x1, 0xf, 0xf, 0x2, 0x3,
        0xf, 0x4, 0xf, 0xf, 0x5,
        0xf, 0xf, 0xf, 0x6, 0x7,
        0xf, 0xf, 0xf, 0x8, 0xf,
        0x9, 0xa, 0xf, 0xf, 0xb,
        0xf, 0xf, 0xf, 0xf, 0xc,
        0xd, 0xe, 0xcc, 0xcc, 0xcc,
    };

    /// <summary>case -> base preset Def (the recon's `pSelectedOffset` immediates).</summary>
    private static int? MazePresetBaseForCase(byte caseVal) => caseVal switch
    {
        0x0 => 0x34,
        0x1 => 0x6c,
        0x2 => 0xa7,
        0x3 => 0xcd,
        0x4 => 0x101,
        0x5 => 0x12d,
        0x6 => 0x19d,
        0x7 => 0x1e1,
        0x8 => 0x2f1,
        0x9 => 0x298,
        0xa => 0x2c0,
        0xb => 0x344,
        0xc => 0x3ea,
        0xd => 0x422,
        0xe => 0x41c,
        _ => null, // 0xf: no random pick
    };

    /// <summary>DRLGMAZE_SelectRandomPresetFile (Maze.cpp:1090). For nPresetFile==-1 it picks a
    /// DS1 file variant for the room: if the room's LvlPrest Def falls inside its level-type preset
    /// group [base, base+4 ints) it draws RANDOM_RandomNumberSelector(seed, Files) once per
    /// distinct Def (cached in pLevelLinkNodeFirst) — a real seed consumer.</summary>
    public static int SelectRandomPresetFile(int nPresetFile, D2DrlgLevelStrc* pLevel, D2DrlgMapStrc* pDrlgMap)
    {
        if (nPresetFile != -1)
        {
            pDrlgMap->nRandomMapFileSelector = nPresetFile; // pState[1]
            return nPresetFile;
        }
        var pLvlPrest = DrlgTables.LvlPrestGetLine(pDrlgMap->nNumber);
        int nFirstFile = pLvlPrest->Def; // *(int*)pState[2]
        int nLevelType = pLevel->nLevelType;
        if (nLevelType - 3 >= 0x21) return nLevelType - 3;
        byte caseVal = gaMazeLevelTypeCaseTable[nLevelType + 1];
        if (MazePresetBaseForCase(caseVal) is not int baseDef) return 0;
        if (baseDef >= nFirstFile) return baseDef;
        // Recon's `pSelectedOffset + 4` is int* POINTER arithmetic (+4 elems = +16 bytes), not
        // scalar +4 — Game.exe 1.14d @006738c0.
        if (nFirstFile >= baseDef + 16) return baseDef + 16;

        var pNode = pLevel->pLevelLinkNodeFirst;
        bool found = false;
        for (; pNode != null; pNode = pNode->pNext)
        {
            if (pNode->dwData0 == nFirstFile)
            {
                found = true;
                break;
            }
        }
        if (!found)
        {
            int nFileCount = pLvlPrest->Files; // ((int*)pState[2])[0x10]
            pNode = (D2DrlgLevelLinkNodeStrc*)Pool.AllocServerMemory(pLevel->pDrlg->pMemoryPool, (nuint)sizeof(D2DrlgLevelLinkNodeStrc));
            pNode->dwData0 = nFirstFile;
            pNode->dwData1 = nFileCount;
            pNode->dwData2 = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nFileCount);
            pNode->pNext = pLevel->pLevelLinkNodeFirst;
            pLevel->pLevelLinkNodeFirst = pNode;
        }
        int a = pNode->dwData2 + 1, b = pNode->dwData1;
        int r = a % b;
        int nPicked = (r != 0 && ((r < 0) != (b < 0))) ? r + b : r; // @mod
        pNode->dwData2 = nPicked;
        pDrlgMap->nRandomMapFileSelector = nPicked; // pState[1]
        return nPicked;
    }

    // GenerateWolrdstoneAndTombs (Maze.cpp:748, 1.14d 006718c0). gnMazeRoomCountX: {nX, nY}.
    private static readonly int[,] gnMazeRoomCountX = { { 0x1bf, 0x433 }, { 0x1bc, 0x435 }, { 0x1be, 0x434 }, { 0x1bd, 0x432 } };

    /// <summary>GenerateWolrdstoneAndTombs (Maze.cpp:748, 1.14d 006718c0). Builds the 3-room
    /// starter spine (rolling direction off the level seed) then writes the level-type terminal
    /// preset id (0x11 Tombs / 0x22 Worldstone) onto the root room.</summary>
    public static void GenerateWolrdstoneAndTombs(D2DrlgLevelStrc* pLevel)
    {
        var pRoomExStrc = pLevel->pRoomExFirst;
        var sNextSeed = DrlgRng.SeedNext(pLevel->sSeed);
        int nRollValue = sNextSeed.nSeedLow & 3;
        pLevel->sSeed = sNextSeed;
        int nLoopCount = 3;
        while (true)
        {
            var pNew = AllocSizedRoom(pRoomExStrc->pLevel);
            if (!MazeDeps.ReplaceRoomWithNewRoom(nRollValue, pNew, pRoomExStrc))
            {
                DrlgRoom.FreeDrlgRoomEx(pNew);
            }
            else
            {
                DrlgRoom.AllocNodesForBothRoomEx(pRoomExStrc, pNew, nRollValue);
                PickRoomPresets(pNew);
                DrlgRoom.AddRoomExToLevel(pRoomExStrc->pLevel, pNew);
                PickRoomPreset(pRoomExStrc);
                PickRoomPreset(pNew);
            }
            // nRollValue = (nRollValue + 1) mod 4 (signed &0x80000003 idiom).
            AdvanceRoll(&nRollValue);
            nLoopCount -= 1;
            if (nLoopCount == 0) break;
        }
        int idx = nRollValue;
        if (pLevel->nLevelType == LevelType.act2_tombs)
        {
            SetDef(pRoomExStrc, gnMazeRoomCountX[idx, 0]);
            SetFlags(pRoomExStrc, GetFlags(pRoomExStrc) | 2);
            SetVariant(pRoomExStrc, -1);
        }
        else if (pLevel->nLevelType == LevelType.act5_worldstone)
        {
            SetDef(pRoomExStrc, gnMazeRoomCountX[idx, 1]);
            SetFlags(pRoomExStrc, GetFlags(pRoomExStrc) | 2);
            SetVariant(pRoomExStrc, -1);
        }
    }

    /// <summary>InitAllRoomsEx (Game.exe 1.14d @ 00673a60). Allocates a DrlgMap + builds ONE area
    /// room then relinks adjacency per orth, then frees the input RoomEx. NOTE: BuildArea is
    /// called ONCE before the orth loop (the closure snapshot Maze.cpp:1210 wrongly hoists it
    /// inside the loop; the authoritative Ghidra decompile places it above).</summary>
    public static void InitAllRoomsEx(D2RoomExStrc* pRoomEx, D2DrlgLevelStrc* pLevel)
    {
        int nLvlPrestId = GetDef(pRoomEx);
        var pDrlgMap = MazeDeps.AllocDrlgMap(pLevel, nLvlPrestId, &pRoomEx->sCoords, &pLevel->sSeed);
        SelectRandomPresetFile(GetVariant(pRoomEx), pLevel, pDrlgMap);
        int bSingleRoom = (pRoomEx->sCoords.WorldSize.x < 0xd && pRoomEx->sCoords.WorldSize.y < 0xd) ? 1 : 0;
        var pRoomEx_00 = MazeDeps.BuildArea(pLevel, pDrlgMap, 0, bSingleRoom);
        for (var pOrth = pRoomEx->pOrth; pOrth != null; pOrth = pOrth->pNext)
            if (pOrth->nType == 1) DrlgRoom.AllocNodesForBothRoomEx(pRoomEx_00, pOrth->pRoomEx, pOrth->neDrlgDirection);
        DrlgRoom.FreeDrlgRoomEx(pRoomEx);
    }

    /// <summary>DRLGMAZE_GenerateLevel (Maze.cpp:1229). Full per-level-type dispatch + the
    /// seed-consuming InitAllRoomsEx (Preset pipeline) tail.</summary>
    public static int GenerateLevel(D2DrlgLevelStrc* pLevel)
    {
        if (!GenerateRoomLayout(pLevel)) return 0;
        var pSearchRoom = pLevel->pRoomExFirst;
        while (pSearchRoom != null)
        {
            var pNextRoom = pSearchRoom->pRoomExNext;
            InitAllRoomsEx(pSearchRoom, pLevel);
            pSearchRoom = pNextRoom;
        }
        return 1;
    }

    /// <summary>Act3 sewers special (Maze.cpp:1338, levelId 0x5c). The type-0x19 arm for the one
    /// level that builds a 5x5 grid and rewrites four fixed preset markers before
    /// ReplaceSewerRooms. Returns false if the 0x2c5 anchor room is never found.</summary>
    private static bool Act3SewerSpecial0x5c(D2DrlgLevelStrc* pLevel)
    {
        BuildRoomGrid(5, pLevel);
        bool found = false;
        for (var pRoomIter = pLevel->pRoomExFirst; pRoomIter != null; pRoomIter = pRoomIter->pRoomExNext)
        {
            if (!HasMapDs1(pRoomIter) && GetDef(pRoomIter) == 0x2c5)
            {
                SetFlags(pRoomIter, GetFlags(pRoomIter) | 2);
                SetDef(pRoomIter, 0x2df);
                SetVariant(pRoomIter, -1);
                found = true;
                break;
            }
        }
        if (!found) return false;
        if (!RewriteMarker(pLevel, 0x2c6, 0x2e0)) return false;
        if (!RewriteMarker(pLevel, 0x2c9, 0x2e1)) return false;
        if (!RewriteMarker(pLevel, 0x2ca, 0x2e2)) return false;
        ActualLevelGeneration(pLevel);
        MazeAct3.ReplaceSewerRooms(pLevel);
        return true;
    }

    private static bool RewriteMarker(D2DrlgLevelStrc* pLevel, int srcDef, int dstDef)
    {
        for (var p = pLevel->pRoomExFirst; p != null; p = p->pRoomExNext)
        {
            if (!HasMapDs1(p) && GetDef(p) == srcDef)
            {
                SetFlags(p, GetFlags(p) | 2);
                SetDef(p, dstDef);
                SetVariant(p, -1);
                return true;
            }
        }
        return false;
    }

    /// <summary>DRLGMAZE_ExpandRoomsWithPresets (Maze.cpp:632, 1.14d 00671320). Snapshots the room
    /// list, then grows a fixed-preset marker room off every original room (except pSkipRoom) in
    /// all 8 directions, each locked to nDefaultPresetId. Ghidra dropped the two stack params
    /// (recovered via set_custom_signature + the call sites: SetupBaalChamber @0x671430 pushes
    /// (0x344, NULL), GenerateRiverOfFlames @0x673320 pushes (0x344, pLastConnectorRoom)).</summary>
    public static void ExpandRoomsWithPresets(D2DrlgLevelStrc* pLevel, int nDefaultPresetId, D2RoomExStrc* pSkipRoom)
    {
        void* pMemPool = pLevel->pDrlg->pMemoryPool;
        int nRoomCount = pLevel->nRoomExCount;
        if (nRoomCount == 0) return;
        var pRoomPtrArray = (D2RoomExStrc**)Pool.AllocServerMemory(pMemPool, (nuint)(nRoomCount * sizeof(void*)));
        var pRoomIter = pLevel->pRoomExFirst;
        for (int i = 0; i < nRoomCount; i++)
        {
            pRoomPtrArray[i] = pRoomIter;
            pRoomIter = pRoomIter->pRoomExNext;
        }
        for (int i = 0; i < nRoomCount; i++)
        {
            if (pRoomPtrArray[i] == pSkipRoom) continue;
            for (int nDir = 0; nDir < 8; nDir++)
            {
                var pRoom = pRoomPtrArray[i];
                var pExpanded = AllocSizedRoom(pRoom->pLevel);
                if (!MazeDeps.ReplaceRoomWithNewRoom(nDir, pExpanded, pRoom))
                {
                    DrlgRoom.FreeDrlgRoomEx(pExpanded);
                }
                else
                {
                    DrlgRoom.AllocNodesForBothRoomEx(pRoom, pExpanded, nDir);
                    DrlgRoom.AddRoomExToLevel(pRoom->pLevel, pExpanded);
                    SetFlags(pExpanded, GetFlags(pExpanded) | 2);
                    SetDef(pExpanded, nDefaultPresetId);
                    SetVariant(pExpanded, -1);
                }
            }
        }
        Pool.FreeServerMemory(pMemPool, pRoomPtrArray);
    }

    // gaBaalChamberPresets (globals_drlg, 1.14d .rdata 0x6ef828; verified byte-for-byte).
    // 4 directions x 2 chambers x {preset, dir, variant}.
    private static readonly int[] gaBaalChamberPresets =
    {
        0x41e, 1, 0, 0x41d, 3, 1,
        0x41e, 1, 1, 0x41d, 3, 0,
        0x41f, 0, 1, 0x420, 2, 0,
        0x41f, 0, 0, 0x420, 2, 1,
    };

    /// <summary>SetupBaalChamber (Maze.cpp:699, 1.14d 00671430). Throne of Destruction: rolls a
    /// direction 0-3 off the root room's seed, grows two fixed-preset chambers, then expands.</summary>
    public static void SetupBaalChamber(D2DrlgLevelStrc* pLevel)
    {
        var pRoomEx = pLevel->pRoomExFirst;
        var sNewSeed = DrlgRng.SeedNext(pRoomEx->sSeed);
        int nDir = sNewSeed.nSeedLow & 3;
        pRoomEx->sSeed = sNewSeed;
        AllocRoomExAndPickPreset(gaBaalChamberPresets[nDir * 6 + 1], pRoomEx, gaBaalChamberPresets[nDir * 6 + 0], gaBaalChamberPresets[nDir * 6 + 2], 1);
        AllocRoomExAndPickPreset(gaBaalChamberPresets[nDir * 6 + 4], pRoomEx, gaBaalChamberPresets[nDir * 6 + 3], gaBaalChamberPresets[nDir * 6 + 5], 1);
        ExpandRoomsWithPresets(pLevel, 0x344, null);
    }

    /// <summary>One Arcane room placement (the repeated block in RollArcaneTypePath).</summary>
    private static D2RoomExStrc* ArcanePlaceRoom(D2RoomExStrc* pParent, int nDirection)
    {
        var pNewRoom = AllocSizedRoom(pParent->pLevel);
        if (!MazeDeps.ReplaceRoomWithNewRoom(nDirection, pNewRoom, pParent))
        {
            DrlgRoom.FreeDrlgRoomEx(pNewRoom);
            return null;
        }
        DrlgRoom.AllocNodesForBothRoomEx(pParent, pNewRoom, nDirection);
        PickRoomPresets(pNewRoom);
        DrlgRoom.AddRoomExToLevel(pParent->pLevel, pNewRoom);
        PickRoomPreset(pParent);
        PickRoomPreset(pNewRoom);
        return pNewRoom;
    }

    /// <summary>RollArcaneTypePath (Maze.cpp:1317, 1.14d 006719f0). Cleaned Ghidra decompile
    /// (session 62fbfe69): rolls an initial direction off the level seed, then for each of 4 arms
    /// builds a fixed 15-room tree off the start room (slots 8 &amp; 12 are unstored "leaf"
    /// rooms). Finally each placed room's variant (nPickedFile) is set to its facing =
    /// (arm + initialDir)%4 and the start room is tagged with facing 4.</summary>
    public static void RollArcaneTypePath(D2DrlgLevelStrc* pLevel)
    {
        var pStartRoom = pLevel->pRoomExFirst;
        var seedRoll = DrlgRng.SeedNext(pLevel->sSeed);
        int nInitialDir = seedRoll.nSeedLow & 3;
        pLevel->sSeed = seedRoll;

        // 4 arms x 15 room slots (matches the engine's 60-pointer stack array).
        var slots = stackalloc D2RoomExStrc*[60];
        for (int i = 0; i < 60; i++) slots[i] = null;

        for (int arm = 0; arm < 4; arm++)
        {
            int b = arm * 15;
            int dArm = arm & 3;
            int dArm1 = (arm + 1) & 3;
            int dArm2 = (arm + 2) & 3;
            int dArm3 = (arm + 3) & 3;
            slots[b + 0] = ArcanePlaceRoom(pStartRoom, dArm);
            slots[b + 1] = ArcanePlaceRoom(slots[b + 0], dArm);
            slots[b + 2] = ArcanePlaceRoom(slots[b + 1], dArm3);
            slots[b + 3] = ArcanePlaceRoom(slots[b + 2], dArm);
            slots[b + 4] = ArcanePlaceRoom(slots[b + 3], dArm);
            slots[b + 5] = ArcanePlaceRoom(slots[b + 4], dArm);
            slots[b + 6] = ArcanePlaceRoom(slots[b + 5], dArm);
            slots[b + 7] = ArcanePlaceRoom(slots[b + 6], dArm1);
            ArcanePlaceRoom(slots[b + 7], dArm); // leaf -> slot 8 stays null
            slots[b + 9] = ArcanePlaceRoom(slots[b + 7], dArm1);
            slots[b + 10] = ArcanePlaceRoom(slots[b + 9], dArm2);
            slots[b + 11] = ArcanePlaceRoom(slots[b + 10], dArm2);
            ArcanePlaceRoom(slots[b + 11], dArm3); // leaf -> slot 12 stays null
            slots[b + 13] = ArcanePlaceRoom(slots[b + 11], dArm2);
            slots[b + 14] = ArcanePlaceRoom(slots[b + 13], dArm2);
        }

        // Facing pass: each placed room's variant = (slot/15 + initialDir) % 4.
        for (int i = 0; i < 60; i++)
        {
            var r = slots[i];
            if (r != null) SetVariant(r, (i / 15 + nInitialDir) & 3);
        }
        SetVariant(pStartRoom, 4);
    }

    // PickSummonerLocation (Maze.cpp:1318, 1.14d 00672e50). Table values are the .rdata
    // AncientPickLocation array (faithful).
    private static readonly D2DrlgReplaceRoomStrc[] gaAncientPickLocation =
    {
        RR(PresetIds.Act2ArcaneN, PresetIds.Act2ArcaneSummonerN, -1, 3),
        RR(PresetIds.Act2ArcaneE, PresetIds.Act2ArcaneSummonerE, -1, 0),
        RR(PresetIds.Act2ArcaneS, PresetIds.Act2ArcaneSummonerS, -1, 1),
        RR(PresetIds.Act2ArcaneW, PresetIds.Act2ArcaneSummonerW, -1, 2),
    };

    /// <summary>PickSummonerLocation (Maze.cpp:1318, 1.14d 00672e50). Rolls 0-3 off the level seed
    /// and replaces the matching marker room with the Summoner/ancient placement.</summary>
    public static void PickSummonerLocation(D2DrlgLevelStrc* pLevel)
    {
        var DVar1 = DrlgRng.SeedNext(pLevel->sSeed);
        pLevel->sSeed = DVar1;
        int nRoll = DVar1.nSeedLow & 3;
        ReplaceRoom(gaAncientPickLocation[nRoll], pLevel, &nRoll);
    }

    /// <summary>The RoomEx-layer phase of DRLGMAZE_GenerateLevel (recon Maze.cpp:1256 switch) —
    /// root room + per-type dispatch + the per-level-id placement (Barracks/River or
    /// DRLGLEVEL_AdjustRoomCoordinates) + ForAllButDenOfEvil.</summary>
    public static bool GenerateRoomLayout(D2DrlgLevelStrc* pLevel)
    {
        var pRoomEx = DrlgRoom.AllocRoomEx(pLevel, 2);
        var t = MazeTxt(pRoomEx->pLevel);
        int nSizeX = t->SizeX;
        pRoomEx->sCoords.WorldSize.x = nSizeX;
        pRoomEx->sCoords.WorldSize.y = t->SizeY;
        pRoomEx->sCoords.WorldPosition.x = (pLevel->sCoordinatesAndSize.WorldSize.x - nSizeX) / 2 + pLevel->sCoordinatesAndSize.WorldPosition.x;
        pRoomEx->sCoords.WorldPosition.y = (pLevel->sCoordinatesAndSize.WorldSize.y - pRoomEx->sCoords.WorldSize.y) / 2 + pLevel->sCoordinatesAndSize.WorldPosition.y;
        DrlgRoom.AddRoomExToLevel(pLevel, pRoomEx);

        int lid = pLevel->eD2LevelId;
        switch (pLevel->nLevelType)
        {
            case LevelType.act1_caves:
                ActualLevelGeneration(pLevel);
                MazeAct1.Caves(pLevel);
                break;
            case LevelType.act1_crypt:
                ActualLevelGeneration(pLevel);
                MazeAct1.Crypt(pLevel);
                break;
            case LevelType.barracks_family: // grid + grow
                BuildRoomGrid(2, pLevel);
                ActualLevelGeneration(pLevel);
                break;
            case LevelType.river_family: // grow only
                ActualLevelGeneration(pLevel);
                break;
            case LevelType.act1_jail:
                BuildRoomGrid(2, pLevel);
                ActualLevelGeneration(pLevel);
                MazeAct1.JailRollRooms(pLevel);
                break;
            case LevelType.act1_catacombs:
                MazeAct1.ExpandCatacombsLevel(pLevel);
                ActualLevelGeneration(pLevel);
                MazeAct1.ReplaceCatacombsRooms(pLevel);
                break;
            case LevelType.act2_sewer:
                BuildRoomGrid(2, pLevel);
                ActualLevelGeneration(pLevel);
                MazeAct2Sewer.GenerateSewerLevel(pLevel);
                break;
            case LevelType.generic_0e:
            case LevelType.generic_0f:
            case LevelType.generic_17:
                BuildRoomGrid(2, pLevel); // plain grid (no special)
                break;
            case LevelType.act2_tombs:
                if (lid == MazeLevelId.tomb_fixed)
                {
                    SetFlags(pRoomEx, GetFlags(pRoomEx) | 2);
                    SetDef(pRoomEx, 0x1e0);
                    SetVariant(pRoomEx, -1);
                }
                else
                {
                    GenerateWolrdstoneAndTombs(pLevel);
                    ActualLevelGeneration(pLevel);
                    MazeAct2.TombsReplaceAreas(pLevel);
                }
                break;
            case LevelType.act2_lair:
                BuildRoomGrid(2, pLevel);
                ActualLevelGeneration(pLevel);
                MazeAct2.Lair(pLevel);
                break;
            // Arcane Sanctuary (Maze.cpp:1316). 4-arm/15-room spoke builder + Summoner marker.
            case LevelType.act2_arcane:
                RollArcaneTypePath(pLevel);
                PickSummonerLocation(pLevel);
                break;
            case LevelType.act3_durance:
                BuildRoomGrid(2, pLevel);
                ActualLevelGeneration(pLevel);
                MazeAct3.DuranceReplaceRooms(pLevel);
                break;
            case LevelType.act3_dungeon:
                BuildRoomGrid(2, pLevel);
                ActualLevelGeneration(pLevel);
                MazeAct3.DungeonRoll(pLevel);
                break;
            case LevelType.act3_sewers:
                if (lid != MazeLevelId.act3_sewer_special)
                {
                    BuildRoomGrid(2, pLevel);
                    ActualLevelGeneration(pLevel);
                    MazeAct3.ReplaceSewerRooms(pLevel);
                }
                else if (!Act3SewerSpecial0x5c(pLevel))
                {
                    return false;
                }
                break;
            case LevelType.act5_temple: // no ActualLevelGeneration
                BuildRoomGrid(2, pLevel);
                MazeAct5.TempleReplaceRooms(pLevel);
                break;
            case LevelType.act5_icecaves:
                switch (lid)
                {
                    case MazeLevelId.frozen_river:
                        {
                            uint nVariant = DrlgRng.GetModuloFromSeed(&pRoomEx->pLevel->sSeed, 2);
                            var p = pLevel->pRoomExFirst;
                            SetFlags(p, GetFlags(p) | 2);
                            SetDef(p, 0x40f - (int)nVariant);
                            SetVariant(p, -1);
                            break;
                        }
                    case MazeLevelId.drifter_cavern:
                        {
                            var p = pLevel->pRoomExFirst;
                            SetFlags(p, GetFlags(p) | 2);
                            SetDef(p, 0x410);
                            SetVariant(p, -1);
                            break;
                        }
                    case MazeLevelId.icy_cellar:
                        {
                            var p = pLevel->pRoomExFirst;
                            SetFlags(p, GetFlags(p) | 2);
                            SetDef(p, 0x411);
                            SetVariant(p, -1);
                            break;
                        }
                    default:
                        BuildRoomGrid(2, pLevel);
                        ActualLevelGeneration(pLevel);
                        MazeAct5.IceCavesReplaceRooms(pLevel);
                        break;
                }
                break;
            case LevelType.act5_worldstone:
                GenerateWolrdstoneAndTombs(pLevel);
                ActualLevelGeneration(pLevel);
                MazeAct5.WorldstonePutRed(pLevel);
                break;
            case LevelType.act5_baal:
                SetupBaalChamber(pLevel);
                break;
            default:
                return false;
        }

        // Per-level-id final placement (Maze.cpp:1484). Barracks/River do their OWN coordinate
        // translation + bbox (aligning to the Jail/Chaos sibling level); every other level falls
        // back to DRLGLEVEL_AdjustRoomCoordinates.
        if (lid == MazeLevelId.barracks) MazeAct1.GenerateBarracksLayout(pLevel);
        else if (lid == MazeLevelId.river_of_flames) MazeAct4.GenerateRiverOfFlames(pLevel);
        else MazeDeps.AdjustRoomCoordinates(pLevel);
        ForAllButDenOfEvil(pLevel);
        return true;
    }
}

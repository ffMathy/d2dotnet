// Port of libd2 packages/drlg/src/drlg/drlg.zig and drlg/Transform.zig (MIT, (c) 2026 jaenster).
//
// Mechanical transform of the TOP DRLG orchestration in recon/closure/Drlg.cpp (+ the two
// type-init helpers in Preset.cpp / Maze.cpp). Faithful by construction: field access by NAME,
// RNG -> DrlgRng, allocs -> Pool, tables -> DrlgTables, the per-level generators -> the maze /
// preset / outdoor modules.
//
// Transformed (cite recon Drlg.cpp unless noted):
//   DRLG_ApplyRoomExStateFlags                   Drlg.cpp:349
//   DRLGWARP_InitLevelWarpCoordinates            Drlg.cpp:374
//   InitLevel                                    Drlg.cpp:431  (the type dispatch)
//   DRLGLEVEL_SetLevelSizeAndAlignVsDependentLevel  Drlg.cpp:935
//   DRLGACTMISC_AllocDrlgLevel                   Drlg.cpp:797
//   GetLevelAndAlloc                             Drlg.cpp:838
//   DRLG_AllocDrlgActMisc                        Drlg.cpp:968
//   DRLGMAZE_InitializeLevel                     Maze.cpp:1221
//   DRLGLEVEL_InitializeWithPresetArea           Preset.cpp:1489
//   DRLGPRESET_InitializeRoomEx                  Preset.cpp:2077
//
// STUBBED (runtime subsystems irrelevant to DRLG room layout):
//   DRLGACTMISC_LoadTileProjects / InitializeSubA0  (DT1 load + activation queue)

using D2Dotnet.Maps.Engine.Maze;
using D2Dotnet.Maps.Engine.Outdoors;

namespace D2Dotnet.Maps.Engine;

internal static unsafe class Drlg
{
    private const byte ACT_I = 0;
    private const byte ACT_II = 1;
    private const byte ACT_III = 2;
    private const byte ACT_IV = 3;
    private const byte ACT_V = 4;

    // eD2DrlgType from LevelDefs.txt DrlgType: 1=Maze, 2=Preset, 3=Wilderness.
    public const int DRLGTYPE_MAZE = 1;
    public const int DRLGTYPE_PRESET = 2;
    public const int DRLGTYPE_WILDERNESS = 3;

    /// <summary>CoordsRoomToWorld — Transform.cpp:158 (1.14d 00643560)</summary>
    public static void CoordsRoomToWorld(int* pX, int* pY)
    {
        *pX = unchecked(*pX * 5);
        *pY = unchecked(*pY * 5);
    }

    /// <summary>Faithful DRLGLEVEL_ParseLevelData pass-2 preset overrides for Act I (1.14d
    /// 006772c0): both write a PresetArea level's nPickedFile from a placement direction,
    /// overwriting the InitializeWithPresetArea selector pick.
    ///   - Rogue Encampment (town, level 1): nPickedFile = its own placement dir, so the town
    ///     faces Blood Moor (the neighbour it was placed against).
    ///   - Outer Cloister (Courtyard, level 0x1b): jail-exit variant (only for a Black-Marsh
    ///     placement dir of 1 or 3) that also drives the Barracks layout.</summary>
    public static void ApplyAct1PresetPicks(D2DrlgStrc* pDrlg, GameTables tb, uint seed)
    {
        var pTown = GetLevelAndAlloc(pDrlg, (int)AreaId.RogueEncampment);
        if (pTown->pDrlgLevelData != null) ((D2DrlgLevelDataPresetArea*)pTown->pDrlgLevelData)->nPickedFile = ActGraph.Act1TownPick(tb, seed);
        if (ActGraph.Act1CourtyardPick(tb, seed) is int pick)
        {
            var pCourt = GetLevelAndAlloc(pDrlg, (int)AreaId.OuterCloister);
            if (pCourt->pDrlgLevelData != null) ((D2DrlgLevelDataPresetArea*)pCourt->pDrlgLevelData)->nPickedFile = pick;
        }
    }

    /// <summary>Faithful DRLGLEVEL_ParseLevelData pass-2 preset override for Act II (1.14d
    /// 006772c0, `eD2LevelId == 0x28` branch): Lut Gholein's nPickedFile is taken from
    /// aCurrentDir[node+1] (the placement direction of Rocky Waste, placed after the town).</summary>
    public static void ApplyAct2PresetPicks(D2DrlgStrc* pDrlg, GameTables tb, uint seed)
    {
        var pTown = GetLevelAndAlloc(pDrlg, (int)AreaId.LutGholein);
        if (pTown->pDrlgLevelData != null) ((D2DrlgLevelDataPresetArea*)pTown->pDrlgLevelData)->nPickedFile = ActGraph.Act2TownPick(tb, seed);
    }

    /// <summary>DRLG_ApplyRoomExStateFlags — Drlg.cpp:349 (1.14d 00642390)</summary>
    public static void ApplyRoomExStateFlags(D2DrlgLevelStrc* pLevel)
    {
        if (!(pLevel->nRoomExCount != 0 && pLevel->pRoomExStateFlags != null)) return;
        var pRoomEx = pLevel->pRoomExFirst;
        if (pRoomEx == null) return;
        int stateFlagOffset = 0;
        while (true)
        {
            int* pStateFlag = pLevel->pRoomExStateFlags + stateFlagOffset;
            stateFlagOffset += 1;
            if (*pStateFlag != 0) pRoomEx->dwOtherFlags |= 1;
            pRoomEx = pRoomEx->pRoomExNext;
            if (pRoomEx == null) break;
        }
    }

    /// <summary>DRLGWARP_InitLevelWarpCoordinates — Drlg.cpp:374 (1.14d 006423d0). Pure
    /// post-processing (no RNG): fills sWarpCoordinates from warp-flagged rooms. Locals
    /// 0-initialised to avoid the Ghidra uninitialised-read artifacts.</summary>
    public static void InitLevelWarpCoordinates(D2DrlgLevelStrc* pLevel)
    {
        var pRoomEx = pLevel->pRoomExFirst;
        bool foundWarpRoom = false;
        while (true)
        {
            if (pRoomEx == null) return;
            uint flags = pRoomEx->eRoomExFlags.Raw;
            if ((flags & 0x30000) != 0) foundWarpRoom = true;

            bool doEmit = false;
            if ((flags & 0xff0) == 0)
            {
                if (foundWarpRoom) doEmit = true;
            }
            else if (foundWarpRoom)
            {
                doEmit = true;
            }
            else
            {
                uint roomFlagMask = 0x10;
                byte warpIndex = 0;
                while (true)
                {
                    if ((flags & roomFlagMask) != 0)
                    {
                        int dest = DrlgRoom.GetWarpDestinationFromArray(pLevel, warpIndex);
                        if (dest != -1)
                        {
                            doEmit = true;
                            break;
                        }
                    }
                    roomFlagMask = unchecked(roomFlagMask * 2);
                    warpIndex = unchecked((byte)(warpIndex + 1));
                    if ((roomFlagMask & 0xff0) == 0) break;
                }
            }

            if (doEmit)
            {
                var wc = &pLevel->sWarpCoordinates;
                int idx = wc->nEntriesCount;
                if (idx >= 9) return; // engine's fixed warp-coord cache cap
                int x = pRoomEx->sCoords.WorldSize.x / 2 + pRoomEx->sCoords.WorldPosition.x;
                int y = pRoomEx->sCoords.WorldSize.y / 2 + pRoomEx->sCoords.WorldPosition.y;
                CoordsRoomToWorld(&x, &y);
                wc->anX[idx] = x;
                wc->anY[idx] = y;
                wc->nEntriesCount += 1;
                foundWarpRoom = false;
            }

            pRoomEx = pRoomEx->pRoomExNext;
        }
    }

    /// <summary>InitLevel — Drlg.cpp:431 (1.14d 006424a0). The per-level dispatch by eDrlgType.
    /// (Ghidra artifact noted in recon: the original test was `drlgType == 1`.)</summary>
    public static void InitLevel(D2DrlgLevelStrc* pLevel)
    {
        pLevel->sSeed.nSeedLow = unchecked((int)(pLevel->pDrlg->dwStartSeed + (uint)pLevel->eD2LevelId));
        pLevel->sSeed.nSeedHigh = 0x29a;
        int drlgType = pLevel->eDrlgType;
        if (drlgType == DRLGTYPE_MAZE)
        {
            MazeGen.GenerateLevel(pLevel);
        }
        else if (drlgType == DRLGTYPE_PRESET)
        {
            InitializeRoomEx(pLevel);
            ApplyRoomExStateFlags(pLevel);
            InitLevelWarpCoordinates(pLevel);
            return;
        }
        else if (drlgType == DRLGTYPE_WILDERNESS)
        {
            Outdoors.Outdoors.GenerateLevel(pLevel);
            ApplyRoomExStateFlags(pLevel);
            InitLevelWarpCoordinates(pLevel);
            return;
        }
        ApplyRoomExStateFlags(pLevel);
        InitLevelWarpCoordinates(pLevel);
    }

    /// <summary>DRLGLEVEL_SetLevelSizeAndAlignVsDependentLevel — Drlg.cpp:935 (00642d10)</summary>
    public static void SetLevelSizeAndAlignVsDependentLevel(D2DrlgStrc* pDrlg, D2DrlgLevelStrc* pLevel)
    {
        var pLevelDef = DrlgTables.LevelDefsGetLine(pLevel->eD2LevelId);
        int nDependLevelId = pLevelDef->Depend;
        int diff = pDrlg->nDifficulty;
        pLevel->sCoordinatesAndSize.WorldSize.x = pLevelDef->SizeX[diff];
        pLevel->sCoordinatesAndSize.WorldSize.y = pLevelDef->SizeY[diff];
        int nRealOffsetX = 0;
        int nRealOffsetY = 0;
        if (nDependLevelId != (int)AreaId.None)
        {
            D2DrlgLevelStrc* dep = null;
            for (var pd = pDrlg->pLevel; pd != null; pd = pd->pLevelNext)
            {
                if (pd->eD2LevelId == nDependLevelId)
                {
                    dep = pd;
                    break;
                }
            }
            if (dep == null) dep = AllocDrlgLevel(pDrlg, nDependLevelId);
            nRealOffsetX = dep->sCoordinatesAndSize.WorldPosition.x;
            nRealOffsetY = dep->sCoordinatesAndSize.WorldPosition.y;
        }
        pLevel->sCoordinatesAndSize.WorldPosition.x = pLevelDef->OffsetX + nRealOffsetX;
        pLevel->sCoordinatesAndSize.WorldPosition.y = pLevelDef->OffsetY + nRealOffsetY;
    }

    /// <summary>DRLGMAZE_InitializeLevel — Maze.cpp:1221 (006739c0). Sets pDrlgLevelData = the
    /// LvlMaze row, then aligns. We COPY the row (the shared table row must not be mutated across
    /// levels/seeds).</summary>
    private static void InitializeLevel(D2DrlgLevelStrc* pLevel)
    {
        var pLvlMazeTxt = DrlgTables.LvlMazeFindLineByLevelId(pLevel->eD2LevelId);
        var copy = (D2LvlMazeTxt*)Pool.AllocServerMemory(pLevel->pDrlg->pMemoryPool, (nuint)sizeof(D2LvlMazeTxt));
        *copy = *pLvlMazeTxt;
        pLevel->pDrlgLevelData = copy;
        SetLevelSizeAndAlignVsDependentLevel(pLevel->pDrlg, pLevel);
    }

    /// <summary>DRLGLEVEL_InitializeWithPresetArea — Preset.cpp:1489 (006674a0)</summary>
    private static void InitializeWithPresetArea(D2DrlgLevelStrc* pLevel)
    {
        var pData = (D2DrlgLevelDataPresetArea*)Pool.AllocServerMemory(pLevel->pDrlg->pMemoryPool, (nuint)sizeof(D2DrlgLevelDataPresetArea));
        var pLvlPrest = DrlgTables.LvlPrestFindLineByLevelId(pLevel->eD2LevelId);
        pLevel->pDrlgLevelData = pData;
        pData->pDrlgMap = null;
        pData->nPickedFile = 0;
        if (pLvlPrest != null && pLvlPrest->Files != 0)
        {
            uint nRandomFile = DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)pLvlPrest->Files);
            pData->nPickedFile = (int)nRandomFile;
            SetLevelSizeAndAlignVsDependentLevel(pLevel->pDrlg, pLevel);
            return;
        }
        pData->nPickedFile = -1;
        SetLevelSizeAndAlignVsDependentLevel(pLevel->pDrlg, pLevel);
    }

    /// <summary>DRLGPRESET_InitializeRoomEx — Preset.cpp:2077 (006681b0). Type-2 InitLevel arm:
    /// roll the map file, then BuildArea tiles the preset.</summary>
    private static void InitializeRoomEx(D2DrlgLevelStrc* pLevel)
    {
        var pData = (D2DrlgLevelDataPresetArea*)pLevel->pDrlgLevelData;
        var pLvlPrest = DrlgTables.LvlPrestFindLineByLevelId(pLevel->eD2LevelId);
        var pDrlgMap = Preset.AllocDrlgMap(pLevel, pLvlPrest->Def, (D2DrlgCoordStrc*)&pLevel->sCoordinatesAndSize, &pLevel->sSeed);
        pData->pDrlgMap = pDrlgMap;
        if (pData->nPickedFile == -1) pData->nPickedFile = pDrlgMap->nRandomMapFileSelector;
        else pDrlgMap->nRandomMapFileSelector = pData->nPickedFile;
        Preset.BuildArea(pLevel, pDrlgMap, 0, 0);
        // autoMap callbacks omitted (pfAutoMap/pfTownAutoMap are null in headless gen).
    }

    /// <summary>DRLGACTMISC_AllocDrlgLevel — Drlg.cpp:797 (00642ae0)</summary>
    public static D2DrlgLevelStrc* AllocDrlgLevel(D2DrlgStrc* pDrlg, int eLevel)
    {
        var pLevel = (D2DrlgLevelStrc*)Pool.AllocServerMemory(pDrlg->pMemoryPool, (nuint)sizeof(D2DrlgLevelStrc));
        pLevel->pDrlg = pDrlg;
        pLevel->eD2LevelId = eLevel;
        var pLevelDefs = DrlgTables.LevelDefsGetLine(eLevel);
        pLevel->nLevelType = pLevelDefs->LevelType;
        pLevel->eDrlgType = pLevelDefs->DrlgType;
        if ((pDrlg->dwFlags & 1) != 0) pLevel->dwFlags |= 0x10;
        pLevel->sSeed.nSeedLow = unchecked((int)(pDrlg->dwStartSeed + (uint)eLevel));
        pLevel->sSeed.nSeedHigh = 0x29a;
        switch (pLevel->eDrlgType)
        {
            case DRLGTYPE_MAZE: InitializeLevel(pLevel); break;
            case DRLGTYPE_PRESET: InitializeWithPresetArea(pLevel); break;
            case DRLGTYPE_WILDERNESS: Outdoors.Outdoors.InitializeWithWildernessLevel(pLevel); break;
        }
        pLevel->pLevelNext = pDrlg->pLevel;
        pDrlg->pLevel = pLevel;
        return pLevel;
    }

    /// <summary>GetLevelAndAlloc — Drlg.cpp:838 (00642bb0)</summary>
    public static D2DrlgLevelStrc* GetLevelAndAlloc(D2DrlgStrc* pDrlg, int eLevelId)
    {
        for (var pLevel = pDrlg->pLevel; pLevel != null; pLevel = pLevel->pLevelNext)
            if (pLevel->eD2LevelId == eLevelId) return pLevel;
        return AllocDrlgLevel(pDrlg, eLevelId);
    }

    /// <summary>DRLG_AllocDrlgActMisc — Drlg.cpp:968 (00642da0). Allocates the act's pDrlg, rolls
    /// dwStartSeed, and (Act II) the staff/boss tomb levels. The Act II tomb roll uses the 32-bit
    /// low word for the %7 (engine @0x642e.., not Ghidra's widened 64-bit divide).
    /// DRLGACTMISC_LoadTileProjects / InitializeSubA0 / AllocDrlgLevelForAct are stubbed.</summary>
    public static D2DrlgStrc* AllocDrlgActMisc(D2DrlgStrc* pDrlg, byte nActNo, uint dwInitSeed, int nTownLevelId, uint dwFlags, byte nDifficulty)
    {
        new Span<byte>(pDrlg, sizeof(D2DrlgStrc)).Clear();
        pDrlg->nActNo = nActNo;
        pDrlg->nSeed.nSeedLow = (int)dwInitSeed;
        pDrlg->nSeed.nSeedHigh = 0x29a;
        var DVar1 = DrlgRng.SeedNext(pDrlg->nSeed);
        int seedLow = DVar1.nSeedLow;
        pDrlg->dwGameLowSeed = dwInitSeed;
        pDrlg->dwFlags = dwFlags;
        pDrlg->nDifficulty = nDifficulty;
        pDrlg->nSeed = DVar1;
        pDrlg->dwStartSeed = (uint)seedLow;

        if (nActNo == ACT_II)
        {
            uint nStaffLevelOffset, nBossOffset;
            while (true)
            {
                // uSeedProduct = D2_SEED_NEXT(DVar1); nRandVal = its low word.
                var prod = DrlgRng.SeedNext(DVar1);
                uint nRandVal = (uint)prod.nSeedLow;
                nStaffLevelOffset = nRandVal % 7;
                // DVar1 = D2_SEED_NEXT_VAL(uSeedProduct) = advance once more.
                DVar1 = DrlgRng.SeedNext(prod);
                nBossOffset = (uint)DVar1.nSeedLow % 7;
                if (nStaffLevelOffset != nBossOffset) break;
            }
            pDrlg->nSeed = DVar1;
            pDrlg->nStaffTombLevel = (int)nStaffLevelOffset + 0x42;
            pDrlg->nBossTombLevel = (int)nBossOffset + 0x42;
        }
        else if (nActNo == ACT_III)
        {
            DVar1 = DrlgRng.SeedNext(DVar1);
            pDrlg->nSeed = DVar1;
            pDrlg->bJungleInterlink = DVar1.nSeedLow & 1;
        }

        if (nTownLevelId == (int)AreaId.None) return pDrlg;
        for (var pl = pDrlg->pLevel; pl != null; pl = pl->pLevelNext)
        {
            if (pl->eD2LevelId == nTownLevelId)
            {
                InitLevel(pl);
                return pDrlg;
            }
        }
        var town = AllocDrlgLevel(pDrlg, nTownLevelId);
        InitLevel(town);
        return pDrlg;
    }

    // Act-level warp graph + inter-level adjacency (orth). The wilderness border junctions need
    // pOrthData, which is built by DRLGLEVEL_AllocDrlgLevelFromLevelIdToLevelId from the DYNAMIC
    // adjacency stored in pDrlg->pWarpsInfo (NOT the static Levels.txt Vis/Warp). That dynamic
    // graph is laid down by DRLGLEVEL_ParseLevelData (Drlg.cpp:3813-3826): for each
    // placement-list entry it wires its prev/next chain neighbour as a borderless seam
    // (warpId = -1) via DRLGACT_AllocWarpsInfo + DRLGACT_SetWarpConnection. The placement RNG only
    // sets coordinates, so we replay just the warp-wiring topology from the static per-act lists
    // below, then build the orths.

    /// <summary>One placement-list entry — only the fields the warp wiring reads: the level id and
    /// the SAME-LIST node indices of its prev/next chain neighbours (-1 = none). Extracted
    /// faithfully from the ACT*_D2DrlgLevelData* initializers in Drlg.cpp (3916-4760).</summary>
    private readonly record struct LevelDataEntry(int Id, int Prev, int Next);

    private static readonly LevelDataEntry[] ACT1_LIST1 =
    {
        new((int)AreaId.StonyField, -1, -1), new((int)AreaId.ColdPlains, 0, -1),
        new((int)AreaId.BloodMoor, 1, -1), new((int)AreaId.RogueEncampment, 2, -1),
        new((int)AreaId.BurialGrounds, 1, -1), new((int)AreaId.None, -1, -1),
    };
    private static readonly LevelDataEntry[] ACT1_LIST2 =
    {
        new((int)AreaId.MooMooFarm, -1, -1), new((int)AreaId.MonasteryGate, -1, -1),
        new((int)AreaId.TamoeHighland, 1, -1), new((int)AreaId.BlackMarsh, 2, -1),
        new((int)AreaId.DarkWood, 3, -1), new((int)AreaId.None, -1, -1),
    };
    private static readonly LevelDataEntry[] ACT2_LIST1 =
    {
        new((int)AreaId.LutGholein, -1, -1), new((int)AreaId.RockyWaste, 0, -1),
        new((int)AreaId.DryHills, 1, -1), new((int)AreaId.FarOasis, 2, -1),
        new((int)AreaId.LostCity, 3, -1), new((int)AreaId.ValleyOfSnakes, 4, -1),
        new((int)AreaId.None, -1, -1),
    };
    private static readonly LevelDataEntry[] ACT2_LIST2 = { new((int)AreaId.CanyonOfTheMagi, -1, -1), new((int)AreaId.None, -1, -1) };
    private static readonly LevelDataEntry[] ACT4_LIST1 =
    {
        new((int)AreaId.PandemoniumFortress, -1, -1), new((int)AreaId.OuterSteppes, 0, -1),
        new((int)AreaId.PlainsOfDespair, 1, -1), new((int)AreaId.CityOfTheDamned, 2, -1),
        new((int)AreaId.None, -1, -1),
    };
    private static readonly LevelDataEntry[] ACT4_LIST2 = { new((int)AreaId.ChaosSanctuary, -1, -1), new((int)AreaId.None, -1, -1) };
    // Act IV (nActNo==3) still wires prev/next warps in ParseLevelData (only Act V skips).
    private static readonly LevelDataEntry[][] ALL_LISTS = { ACT1_LIST1, ACT1_LIST2, ACT2_LIST1, ACT2_LIST2, ACT4_LIST1, ACT4_LIST2 };

    // Inter-level adjacency (orth) ranges per act, from DRLGACTMISC_AllocDrlgLevelForAct
    // (Drlg.cpp:4768/4773/4783/4789). Act III's adjacency is built inside
    // OutPlace::DRLGActMisc_InitAct3; Act V uses DRLGACT_SetWarpConnectionsBetweenTwoAreas.
    private static readonly (int Start, int End)[] ACT_ORTH_RANGES =
    {
        ((int)AreaId.RogueEncampment, (int)AreaId.BurialGrounds), //  Act I
        ((int)AreaId.LutGholein, (int)AreaId.CanyonOfTheMagi), // Act II
        ((int)AreaId.PandemoniumFortress, (int)AreaId.CityOfTheDamned), // Act IV
        ((int)AreaId.FrigidHighlands, (int)AreaId.ArreatPlateau), // Act V
    };

    /// <summary>DRLGACT_AllocWarpsInfo — Drlg.cpp:648 (1.14d 006428a0). Find (by level id) or
    /// create the per-level warp-info node, initialising a fresh node's nTargetArea/nWarpId from
    /// the static Levels.txt Vis/Warp columns.</summary>
    private static D2DrlgActWarpsInfoStrc* AllocWarpsInfo(D2DrlgStrc* pDrlg, int eLevel)
    {
        for (var pe = pDrlg->pWarpsInfo; pe != null; pe = pe->pNext)
            if (pe->nLevelId == eLevel) return pe;
        var pLevelDefs = DrlgTables.LevelDefsGetLine(eLevel);
        var pInfo = (D2DrlgActWarpsInfoStrc*)Pool.AllocServerMemory(pDrlg->pMemoryPool, (nuint)sizeof(D2DrlgActWarpsInfoStrc));
        pInfo->nLevelId = eLevel;
        for (int i = 0; i < 8; i++)
        {
            pInfo->nTargetArea[i] = pLevelDefs->Vis[i];
            pInfo->nWarpId[i] = pLevelDefs->Warp[i];
        }
        pInfo->pNext = pDrlg->pWarpsInfo;
        pDrlg->pWarpsInfo = pInfo;
        return pInfo;
    }

    /// <summary>DRLGACT_SetWarpConnection — Drlg.cpp:689 (1.14d 00642920). If eLevel already
    /// occupies a nTargetArea slot, update its warp id; otherwise (when nWarpInfoIndex==-1) drop it
    /// into the first slot that is empty (nTargetArea==0) and warp-free (nWarpId==-1).</summary>
    private static void SetWarpConnection(D2DrlgActWarpsInfoStrc* pInfo, int eLevel, int nWarpId, int nWarpInfoIndex)
    {
        for (int i = 0; i < 8; i++)
        {
            if (pInfo->nTargetArea[i] == eLevel)
            {
                pInfo->nWarpId[i] = nWarpId;
                return;
            }
        }
        int idx = nWarpInfoIndex;
        if (nWarpInfoIndex == -1)
        {
            for (int k = 0; k < 8; k++)
            {
                if (pInfo->nTargetArea[k] == (int)AreaId.None && pInfo->nWarpId[k] == -1)
                {
                    idx = k;
                    break;
                }
            }
        }
        if ((uint)idx >= 8) throw new IndexOutOfRangeException("SetWarpConnection: no free warp slot");
        pInfo->nTargetArea[idx] = eLevel;
        pInfo->nWarpId[idx] = nWarpId;
    }

    /// <summary>The prev/next warp-wiring half of DRLGLEVEL_ParseLevelData (Drlg.cpp:3746-3826),
    /// stripped of the placement-RNG coordinate assignment. Act V (nActNo==4) skips this.</summary>
    private static void ParseLevelDataWarps(D2DrlgStrc* pDrlg, LevelDataEntry[] list)
    {
        if (pDrlg->nActNo == ACT_V) return;
        foreach (var entry in list)
        {
            int eCur = entry.Id;
            if (eCur == (int)AreaId.None) break;
            int ePrev = entry.Prev == -1 ? (int)AreaId.None : list[entry.Prev].Id;
            int eNext = entry.Next == -1 ? (int)AreaId.None : list[entry.Next].Id;
            GetLevelAndAlloc(pDrlg, eCur);
            if (ePrev != (int)AreaId.None)
            {
                var a = AllocWarpsInfo(pDrlg, eCur);
                var b = AllocWarpsInfo(pDrlg, ePrev);
                SetWarpConnection(a, ePrev, -1, -1);
                SetWarpConnection(b, eCur, -1, -1);
            }
            if (eNext != (int)AreaId.None)
            {
                var a = AllocWarpsInfo(pDrlg, eCur);
                var b = AllocWarpsInfo(pDrlg, eNext);
                SetWarpConnection(a, eNext, -1, -1);
                SetWarpConnection(b, eCur, -1, -1);
            }
        }
    }

    /// <summary>DRLGLEVEL_AllocDrlgLevelFromLevelIdToLevelId — Drlg.cpp:3881 (00677680). For each
    /// level id in [nLevelIdStart, eLevel] that is a wilderness level (eDrlgType==3), walk its
    /// 8-entry Vis[]/Warp[]: every Vis slot that is set AND whose parallel Warp slot is -1 is a
    /// *borderless* adjacency — a wilderness->wilderness seam. Build a level-link orth into the
    /// wilderness level's pOrthData for it. The recon's `(D2RoomExStrc*)&amp;pLevelData->pOrthData`
    /// aliases the pOrthData head onto a RoomEx whose pOrth field is at offset 0.</summary>
    public static void AllocDrlgLevelFromLevelIdToLevelId(D2DrlgStrc* pDrlg, int eLevel, int nLevelIdStart)
    {
        for (int nCur = nLevelIdStart; nCur <= eLevel; nCur++)
        {
            var pCurLevel = GetLevelAndAlloc(pDrlg, nCur);
            if (pCurLevel->eDrlgType != DRLGTYPE_WILDERNESS) continue;
            int* pVisArray = DrlgRoom.GetVisArrayFromLevelId(pDrlg, nCur);
            int* pWarpIds = DrlgRoom.GetWarpsIdIfExists(pDrlg, nCur);
            for (int i = 0; i < 8; i++)
            {
                if (pVisArray[i] != (int)AreaId.None && pWarpIds[i] == -1)
                {
                    var pData = (D2DrlgLevelDataWildernessLevel*)pCurLevel->pDrlgLevelData;
                    var pAdj = GetLevelAndAlloc(pCurLevel->pDrlg, pVisArray[i]);
                    int eDirection = MazeDeps.GetDirectionFromCoordinates(&pCurLevel->sCoordinatesAndSize, &pAdj->sCoordinatesAndSize);
                    // The two rects share no exact edge -> no seam (only for a level reached
                    // outside its act). Mirror the coordinate-adjacency builder, which likewise
                    // skips INVALID seams.
                    if (eDirection == MazeDeps.DIRECTION_INVALID) continue;
                    // (D2RoomExStrc*)&pData->pOrthData : pOrth is field 0 of D2RoomExStrc.
                    var pFakeRoomEx = (D2RoomExStrc*)&pData->pOrthData;
                    DrlgRoom.AllocDrlgOrth(pFakeRoomEx, pAdj, eDirection, pAdj->eDrlgType == DRLGTYPE_PRESET ? 1 : 0);
                }
            }
        }
    }

    /// <summary>DRLGACT_SetWarpConnectionsBetweenTwoAreas (Drlg.cpp ~4750, Act V path). Walks
    /// every pair of wilderness levels in [startId, endId] and, when their world-rects share an
    /// exact edge, builds an orth in each direction via AllocDrlgOrth.</summary>
    private static void BuildActVCoordOrths(D2DrlgStrc* pDrlg, int startId, int endId)
    {
        for (int aI = startId; aI <= endId; aI++)
        {
            var pA = GetLevelAndAlloc(pDrlg, aI);
            if (pA->eDrlgType != DRLGTYPE_WILDERNESS) continue;
            for (int bI = aI + 1; bI <= endId; bI++)
            {
                var pB = GetLevelAndAlloc(pDrlg, bI);
                if (pB->eDrlgType != DRLGTYPE_WILDERNESS) continue;
                int dirAtoB = MazeDeps.GetDirectionFromCoordinates(&pA->sCoordinatesAndSize, &pB->sCoordinatesAndSize);
                if (dirAtoB == MazeDeps.DIRECTION_INVALID) continue;
                int dirBtoA = MazeDeps.GetDirectionFromCoordinates(&pB->sCoordinatesAndSize, &pA->sCoordinatesAndSize);
                var pDataA = (D2DrlgLevelDataWildernessLevel*)pA->pDrlgLevelData;
                var pDataB = (D2DrlgLevelDataWildernessLevel*)pB->pDrlgLevelData;
                DrlgRoom.AllocDrlgOrth((D2RoomExStrc*)&pDataA->pOrthData, pB, dirAtoB, pB->eDrlgType == DRLGTYPE_PRESET ? 1 : 0);
                DrlgRoom.AllocDrlgOrth((D2RoomExStrc*)&pDataB->pOrthData, pA, dirBtoA, pA->eDrlgType == DRLGTYPE_PRESET ? 1 : 0);
            }
        }
    }

    // Act V wilderness level range for coordinate-based orth building (Frigid Highlands ..
    // Frozen Tundra). Non-wilderness levels in the range are skipped by the DRLGTYPE check.
    private const int ACT_V_WILD_START = (int)AreaId.FrigidHighlands;
    private const int ACT_V_WILD_END = (int)AreaId.FrozenTundra;

    /// <summary>Builds the inter-level wilderness adjacency (pOrthData) for every act's id span.
    /// Faithful subset of DRLGACTMISC_AllocDrlgLevelForAct: the per-act
    /// DRLGLEVEL_AllocDrlgLevelFromLevelIdToLevelId calls. Must run AFTER every level's
    /// sCoordinatesAndSize is established and BEFORE the wilderness InitLevel reads pOrthData.</summary>
    public static void BuildInterLevelOrths(D2DrlgStrc* pDrlg)
    {
        foreach (var list in ALL_LISTS) ParseLevelDataWarps(pDrlg, list);
        foreach (var r in ACT_ORTH_RANGES) AllocDrlgLevelFromLevelIdToLevelId(pDrlg, r.End, r.Start);
        BuildActVCoordOrths(pDrlg, ACT_V_WILD_START, ACT_V_WILD_END);
    }

    // DRLG_ApplyAct1WildRoadFlags — ACT1_fpLevelDataFn2_A (00677180). Sets wilderness dwFlags
    // road/transition bits from the gaWildernessLevelSeedOffsets lookup table
    // (0x6f1258-0x6f13BF, 3 entries × 5 rules × 6 ints, binary-verified).
    private static readonly int[,] RoadFlagTable =
    {
        // Entry 0
        { 0, 2, 3, 1, 0, 0x04 }, { 0, 2, 3, 2, 3, 0x04 }, { 0, 3, 17, 2, 1, 0x08 }, { 0, 3, 17, 3, 0, 0x08 }, { 0, 3, 17, 1, 1, 0x10 },
        // Entry 1
        { 0, 3, 17, 3, 3, 0x10 }, { 2, 0, 0, 0, 0, 0x08 }, { 2, 0, 0, 2, 2, 0x08 }, { 2, 0, 0, 3, 0, 0x08 }, { 2, 0, 0, 3, 2, 0x08 },
        // Entry 2
        { 2, 0, 0, 0, 1, 0x400 }, { 2, 0, 0, 1, 1, 0x400 }, { 2, 0, 0, 2, 1, 0x200 }, { 2, 0, 0, 2, 2, 0x80 }, { 2, 0, 0, 3, 2, 0x100 },
    };

    private static int InferDirectionFromCoords(D2DrlgCoordsStrc curr, D2DrlgCoordsStrc prev)
    {
        int cx = curr.WorldPosition.x + curr.WorldSize.x / 2;
        int cy = curr.WorldPosition.y + curr.WorldSize.y / 2;
        int px = prev.WorldPosition.x + prev.WorldSize.x / 2;
        int py = prev.WorldPosition.y + prev.WorldSize.y / 2;
        int dx = cx - px;
        int dy = cy - py;
        int adx = dx < 0 ? -dx : dx;
        int ady = dy < 0 ? -dy : dy;
        if (ady >= adx) return dy >= 0 ? 0 : 2;
        return dx < 0 ? 1 : 3;
    }

    private static int DirForNode(LevelDataEntry[] list, int nodeIdx, D2DrlgStrc* pDrlg)
    {
        if (nodeIdx >= list.Length) return -1;
        var entry = list[nodeIdx];
        if (entry.Id == (int)AreaId.None) return -1;
        if (entry.Prev == -1) return -1;
        int prevId = list[entry.Prev].Id;
        D2DrlgLevelStrc* pCurr = null;
        D2DrlgLevelStrc* pPrev = null;
        for (var l = pDrlg->pLevel; l != null; l = l->pLevelNext)
        {
            if (l->eD2LevelId == entry.Id) pCurr = l;
            if (l->eD2LevelId == prevId) pPrev = l;
        }
        if (pCurr == null || pPrev == null) return -1;
        return InferDirectionFromCoords(pCurr->sCoordinatesAndSize, pPrev->sCoordinatesAndSize);
    }

    /// <summary>DRLG_ApplyAct1WildRoadFlags — called at the top of InitAct1OutdoorLevel to mirror
    /// ParseLevelData's pre-gen call. Each rule checks the level id against a match/exclusion pair
    /// and a (dir0,dir1) pair derived from the placement-graph direction for the level's node and
    /// next node.</summary>
    public static void ApplyAct1WildRoadFlags(D2DrlgLevelStrc* pLevel)
    {
        if (pLevel->eDrlgType != DRLGTYPE_WILDERNESS) return;
        int lvlId = pLevel->eD2LevelId;
        var pDrlg = pLevel->pDrlg;
        var pData = (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;

        foreach (var list in new[] { ACT1_LIST1, ACT1_LIST2 })
        {
            int nodeIdx = -1;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].Id == (int)AreaId.None) break;
                if (list[i].Id == lvlId)
                {
                    nodeIdx = i;
                    break;
                }
            }
            if (nodeIdx == -1) continue;

            int dir0 = DirForNode(list, nodeIdx, pDrlg);
            int dir1 = DirForNode(list, nodeIdx + 1, pDrlg);
            for (int r = 0; r < 15; r++)
            {
                int matchId = RoadFlagTable[r, 0];
                int exc1 = RoadFlagTable[r, 1];
                int exc2 = RoadFlagTable[r, 2];
                int d0 = RoadFlagTable[r, 3];
                int d1 = RoadFlagTable[r, 4];
                int flag = RoadFlagTable[r, 5];
                if ((lvlId == matchId || matchId == 0) && lvlId != exc1 && lvlId != exc2 && dir0 == d0 && dir1 == d1)
                    pData->dwFlags |= (uint)flag;
            }
            return;
        }
    }
}

// Port of libd2 packages/drlg/src/drlg/outdoors/Outdoors.zig (MIT, (c) 2026 jaenster).
//
// Mechanical transform of the wilderness (type-3) generator from recon/closure/Outdoors.cpp
// (+ OutPlace.cpp DRLGOUTROOM_CreateOutdoorRoomEx, Drlg.cpp
// DRLGROOMEX_RollLevelSubstitutionMask / GetActNoFromLevelNumber). Faithful by construction:
// field access by NAME (the recon's RAW byte-offset grid reaches +4/+0x18/+0x2c/+0x40 are
// rewritten to the named D2DrlgLevelDataWildernessLevel fields — the 32-bit offsets do not
// survive 64-bit pointers), RNG -> low-word modulo, the preset cells -> the preset pipeline.
//
// Transformed here:
//   DRLGLEVEL_InitializeWithWildernessLevel       Outdoors.cpp:1371
//   DRLGOUTDOOR_GenerateLevel                     Outdoors.cpp:1379
//   DRLGOUTDOOR_SimplifyOutdoorPoints             Outdoors.cpp:1245
//   DRLGOUTDOOR_CreateOutdoorRoomExGrid           Outdoors.cpp:1273
//   DRLGOUTROOM_CreateOutdoorRoomEx               OutPlace.cpp:1562
//   DRLGROOMEX_RollLevelSubstitutionMask          Drlg.cpp:2422

namespace D2Dotnet.Map.Drlg.Engine.Outdoors;

internal static unsafe class Outdoors
{
    private const int ACT_I = 0;
    private const int ACT_II = 1;
    private const int ACT_III = 2;
    private const int ACT_IV = 3;
    private const int ACT_V = 4;

    /// <summary>DRLGROOMEX_RollLevelSubstitutionMask   Drlg.cpp:2422 (006406..). 64-bit artifact:
    /// recon line 2435 takes `% 100` on the FULL (uint64_t)sNewSeed; the engine modulos the
    /// 32-bit LOW word (same widening class as the maze rolls).</summary>
    public static uint RollLevelSubstitutionMask(D2RoomExStrc* pRoomEx, int nSubType, int nSubTheme)
    {
        if (!(nSubType != -1 && nSubTheme != -1)) return 0;
        var pLine = DrlgTables.LvlSubGetLineFromSubType(nSubType);
        int nCurrentType = pLine->Type;
        uint dwSubMask = 0;
        int nSubIndex = 0;
        while (nCurrentType == nSubType)
        {
            var sNewSeed = DrlgRng.SeedNext(pRoomEx->sSeed);
            pRoomEx->sSeed = sNewSeed;
            int lowMod = (int)((uint)sNewSeed.nSeedLow % 100);
            int prob = pLine->Prob[nSubTheme];
            if (lowMod < prob)
            {
                dwSubMask |= 1u << (nSubIndex & 31);
                pRoomEx->nDT1Mask |= pLine->Dt1Mask;
            }
            nSubIndex = (nSubIndex + 1) & 31; // u5 wrap
            pLine += 1;
            nCurrentType = pLine->Type;
        }
        return dwSubMask;
    }

    /// <summary>DRLGOUTROOM_CreateOutdoorRoomEx   OutPlace.cpp:1562 (0067eef0)</summary>
    public static D2RoomExStrc* CreateOutdoorRoomEx(D2DrlgLevelStrc* pLevel, int nX, int nY, int nWidth, int nHeight, int dwRoomFlags, int dwOutdoorFlags, int dwOutdoorFlagsEx, int dwDT1Mask)
    {
        var pRoomEx = DrlgRoom.AllocRoomEx(pLevel, 1);
        pRoomEx->sCoords.WorldSize.x = nWidth;
        pRoomEx->sCoords.WorldSize.y = nHeight;
        pRoomEx->sCoords.WorldPosition.x = nX;
        pRoomEx->sCoords.WorldPosition.y = nY;
        DrlgRoom.AddRoomExToLevel(pLevel, pRoomEx);
        var pOut = (D2DrlgRoomExDataMazeStrc*)pRoomEx->pRoomExData;
        pRoomEx->eRoomExFlags.OrRaw(dwRoomFlags);
        pRoomEx->eRoomExFlags.NoLos = true;
        pRoomEx->nDT1Mask = dwDT1Mask;
        pOut->dwFlags = dwOutdoorFlags;
        pOut->dwFlagsEx = dwOutdoorFlagsEx;
        var pTxt = DrlgTables.LevelDefsGetLine(pLevel->eD2LevelId);
        pOut->nSubType = pTxt->SubType;
        pOut->nSubTheme = pTxt->SubTheme;
        pOut->nSubThemePicked = (int)RollLevelSubstitutionMask(pRoomEx, pTxt->SubType, pTxt->SubTheme);
        return pRoomEx;
    }

    /// <summary>DRLGLEVEL_InitializeWithWildernessLevel   Outdoors.cpp:1371 (00642ae0 arm)</summary>
    public static void InitializeWithWildernessLevel(D2DrlgLevelStrc* pLevel)
    {
        var pData = (D2DrlgLevelDataWildernessLevel*)Pool.AllocServerMemory(pLevel->pDrlg->pMemoryPool, (nuint)sizeof(D2DrlgLevelDataWildernessLevel));
        pLevel->pDrlgLevelData = pData;
    }

    /// <summary>Toward-zero /8: (v + (v>>31)*7) >> 3.</summary>
    internal static int FloorDiv8(int v) => (v + (int)((uint)v >> 31) * 7) >> 3;

    /// <summary>DRLGOUTDOOR_SimplifyOutdoorPoints   Outdoors.cpp:1245 (00674f30). Vertex coords
    /// /= 8, then collapse consecutive duplicate vertices (OR flags, copy direction).</summary>
    public static void SimplifyOutdoorPoints(void* pMemory, D2DrlgVertexStrc** ppHead)
    {
        var p = *ppHead;
        while (true)
        {
            p->nPosX = FloorDiv8(p->nPosX);
            p->nPosY = FloorDiv8(p->nPosY);
            p = p->pNext;
            if (p == *ppHead) break;
        }
        p = *ppHead;
        while (true)
        {
            var pNext = p->pNext;
            if (p->nPosX == pNext->nPosX && p->nPosY == pNext->nPosY)
            {
                if (pNext == *ppHead) *ppHead = p;
                p->pNext = pNext->pNext;
                p->dwFlags |= pNext->dwFlags;
                p->nDirection = pNext->nDirection;
                Pool.FreeServerMemory(pMemory, pNext);
            }
            p = p->pNext;
            if (p == *ppHead) break;
        }
    }

    /// <summary>DRLGOUTDOOR_CreateOutdoorRoomExGrid   Outdoors.cpp:1273 (006750f0)</summary>
    public static void CreateOutdoorRoomExGrid(D2DrlgLevelStrc* pLevel)
    {
        var pData = (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;
        int outdoorRoomStyle = 0;
        switch (pLevel->nLevelType)
        {
            case 2: outdoorRoomStyle = 0x44103; break;
            case 0x10:
            case 0x16:
            case 0x1b:
            case 0x1c: outdoorRoomStyle = 1; break;
            case 0x15: outdoorRoomStyle = 4; break;
            case 0x1e:
            case 0x1f: outdoorRoomStyle = 0x11; break;
        }

        int worldY = pLevel->sCoordinatesAndSize.WorldPosition.y;
        if (pData->nGridCoordsHeight <= 0) return;
        for (int gridY = 0; gridY < pData->nGridCoordsHeight; gridY++)
        {
            int worldX = pLevel->sCoordinatesAndSize.WorldPosition.x;
            for (int nGridX = 0; nGridX < pData->nGridCoordsWidth; nGridX++)
            {
                int floorFlags = DrlgGrid.GetGridFlags(&pData->sGridLink, nGridX, gridY);
                int dwOutdoorFlags = DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nGridX, gridY);
                if ((dwOutdoorFlags & 0x200) == 0)
                {
                    if ((dwOutdoorFlags & 0x100) == 0)
                    {
                        int tileTypeFlags = DrlgGrid.GetGridFlags(&pData->sGridMisc, nGridX, gridY);
                        CreateOutdoorRoomEx(pLevel, worldX, worldY, 8, 8, floorFlags, dwOutdoorFlags, tileTypeFlags, outdoorRoomStyle);
                    }
                }
                else
                {
                    int tileTypeFlags = DrlgGrid.GetGridFlags(&pData->sGridPreset, nGridX, gridY);
                    if (tileTypeFlags != 0)
                    {
                        var mapCoords = new D2DrlgCoordStrc { nPosX = worldX, nPosY = worldY, nWidth = 0, nHeight = 0 };
                        var pDrlgMap = Preset.AllocDrlgMap(pLevel, tileTypeFlags, &mapCoords, &pLevel->sSeed);
                        pDrlgMap->nRandomMapFileSelector = (dwOutdoorFlags >> 0x10) & 0xf;
                        Preset.BuildArea(pLevel, pDrlgMap, floorFlags, 0);
                    }
                }
                worldX += 8;
            }
            worldY += 8;
        }
    }

    /// <summary>DRLGOUTDOOR_GenerateLevel   Outdoors.cpp:1379 (00675360). Builds the four
    /// placement grids + the boundary vertex ring, then dispatches the per-act border/preset
    /// placement, then converts the grids to RoomEx.</summary>
    public static void GenerateLevel(D2DrlgLevelStrc* pLevel)
    {
        void* pMemory = pLevel->pDrlg->pMemoryPool;
        var pData = (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;
        pData->pGridCoordsCellFlags = null;
        pData->pGridCoordsRowOffsets = null;
        int wx = pLevel->sCoordinatesAndSize.WorldSize.x;
        pData->nGridCoordsWidth = FloorDiv8(wx);
        int wy = pLevel->sCoordinatesAndSize.WorldSize.y;
        int nWidth = pData->nGridCoordsWidth;
        int nHeight = FloorDiv8(wy);
        pData->nGridCoordsHeight = nHeight;
        DrlgGrid.InitGridCells(pMemory, &pData->sGridPreset, nWidth, nHeight);
        DrlgGrid.InitGridCells(pMemory, &pData->sGridLink, nWidth, nHeight);
        DrlgGrid.InitGridCells(pMemory, &pData->sGridOutdoor, nWidth, nHeight);
        DrlgGrid.InitGridCells(pMemory, &pData->sGridMisc, nWidth, nHeight);
        DrlgVer.CreateRoomVertices(pMemory, &pData->pVertices, (D2DrlgCoordStrc*)&pLevel->sCoordinatesAndSize, 0, pData->pOrthData);
        SimplifyOutdoorPoints(pMemory, &pData->pVertices);

        int eActNo = ACT_II;
        if (pLevel->eD2LevelId != LevelIds.ForgottenSands) eActNo = Border.GetActNoFromLevelNumber(pLevel->eD2LevelId);
        switch (eActNo)
        {
            // Per-act border/preset placement (Outdoors.cpp:1402 switch).
            case ACT_I: ActInit.InitAct1OutdoorLevel(pLevel); break;
            case ACT_II: ActInit.InitAct2OutdoorLevel(pLevel); break;
            case ACT_III: OutJung.InitAct3OutdoorLevel(pLevel); break;
            case ACT_IV: ActInit.InitAct4OutdoorLevel(pLevel); break;
            case ACT_V: Act5.InitAct5OutdoorLevel(pLevel); break;
        }
        CreateOutdoorRoomExGrid(pLevel);
    }

    /// <summary>AddAct124SecondaryBorder   Outdoors.cpp:1350 (1.14d 00675290 area). Sets up the
    /// substitution-placement context (D2UnkOutdoorStrc) for an Act-1/2/4 secondary border and
    /// runs the substitution placement via TileSub.AddSecondaryBorder. Grids map to the named
    /// wilderness fields: pCoordsGrid = pLevelData+0x54 (pGridCoordsCellFlags, read as a grid),
    /// pPrimaryFloorGrid = pLevelData+0x04 (sGridPreset), pWallGrid = pLevelData+0x2c
    /// (sGridOutdoor). Consumes seed inside the substitution shuffle.</summary>
    public static void AddAct124SecondaryBorder(D2DrlgLevelStrc* pLevel, int nLvlSubId, int nLevelPrestId)
    {
        var pData = (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;
        D2UnkOutdoorStrc sUnk = default;
        sUnk.pCoordsGrid = (D2DrlgGridStrc*)&pData->pGridCoordsCellFlags;
        sUnk.pPrimaryFloorGrid = &pData->sGridPreset;
        sUnk.pWallGrid = &pData->sGridOutdoor;
        sUnk.nLevelPrestId = nLevelPrestId;
        sUnk.nPrevBorderPresetId = -1;
        sUnk.pfnIsPresetFree = (delegate*<D2DrlgLevelStrc*, int, int, int>)&OutPlace.IsGridCellPresetFree;
        sUnk.pfnTestPreset = (delegate*<D2DrlgLevelStrc*, int, int, int, int, int, int>)&OutPlace.TestOutdoorLevelPreset;
        sUnk.pfnAlterAdjacentCells = (delegate*<D2DrlgLevelStrc*, int, int, void>)&OutPlace.AlterAdjacentPresetGridCells;
        sUnk.pfnSetBlankCell = (delegate*<D2DrlgLevelStrc*, int, int, void>)&OutPlace.SetBlankGridCell;
        sUnk.pfnSpawnPreset = (delegate*<D2DrlgLevelStrc*, int, int, int, int, int, void>)&OutPlace.SpawnOutdoorLevelPresetEx;
        sUnk.pLevel = pLevel;
        sUnk.nLvlSubId = nLvlSubId;
        TileSub.AddSecondaryBorder(&sUnk);
    }

    /// <summary>AddAct124SecondaryBorderSubId213   Outdoors.cpp:1546 (1.14d 0067f630). Act-2
    /// secondary borders: LvlSub ids 2,1,3 against the shared 0x16c preset.</summary>
    public static void AddAct124SecondaryBorderSubId213(D2DrlgLevelStrc* pLevel)
    {
        AddAct124SecondaryBorder(pLevel, 2, 0x16c);
        AddAct124SecondaryBorder(pLevel, 1, 0x16c);
        AddAct124SecondaryBorder(pLevel, 3, 0x16c);
    }

    // gaShrineStyleFlags — gnOutdoorsGridWidthByLevel .rdata @0x006f061c (4 int32, recovered from
    // Ghidra 1.14d, accessed `[ECX*4+0x6f061c]` @0x0067500c). The four shrine-style grid flags
    // OR'd into sGridLink as the style cycles 0->1->2->3.
    private static readonly int[] gaShrineStyleFlags = { 0x1000, 0x2000, 0x4000, 0x8000 };

    /// <summary>SpawnAct12Shrines   Outdoors.cpp:1128 (1.14d 00674e40). Rolls a shrine style,
    /// builds the interior-cell list, Fisher-Yates-shuffles it (two RNG draws per step), then walks
    /// the shuffled cells placing up to nShrines shrines on cells clear of the 0x1b81 mask.</summary>
    public static void SpawnAct12Shrines(D2DrlgLevelStrc* pLevel, int nShrines)
    {
        var pData = (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;
        int nShrinesLeft = nShrines;
        int nSeedRoll = DrlgRng.RollRandomSeed(&pLevel->sSeed);
        int nGridWidth = pData->nGridCoordsWidth - 2;
        int nTotalCells = (pData->nGridCoordsHeight - 2) * nGridWidth;
        uint nShrineStyleIdx = (uint)nSeedRoll & 3;
        if (nTotalCells == 0) return;

        Span<int> aCellX = stackalloc int[513];
        Span<int> aCellY = stackalloc int[513];
        for (int i = 0; i < nTotalCells; i++)
        {
            aCellX[i] = i % nGridWidth;
            aCellY[i] = i / nGridWidth;
        }
        for (int nShuffleCount = nTotalCells; nShuffleCount > 0; nShuffleCount--)
        {
            int a = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nTotalCells);
            int b = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nTotalCells);
            (aCellX[a], aCellX[b]) = (aCellX[b], aCellX[a]);
            (aCellY[a], aCellY[b]) = (aCellY[b], aCellY[a]);
        }

        for (int nShuffleIdx = 0; nShuffleIdx < nTotalCells; nShuffleIdx++)
        {
            if (nShrinesLeft < 1) return;
            int nGridY = aCellY[nShuffleIdx] + 1;
            int nGridX = aCellX[nShuffleIdx] + 1;
            int flags = DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nGridX, nGridY);
            if ((flags & 0x1b81) == 0)
            {
                DrlgGrid.AlterGridFlag(&pData->sGridLink, nGridX, nGridY, gaShrineStyleFlags[nShrineStyleIdx], 0);
                DrlgGrid.AlterGridFlag(&pData->sGridOutdoor, nGridX, nGridY, 0x1000, 0);
                // recon: (idx+1) & 0x80000003 with sign-fixup == signed (idx+1) % 4.
                nShrineStyleIdx = (nShrineStyleIdx + 1) & 3;
                nShrinesLeft -= 1;
            }
        }
    }

    /// <summary>SpawnAct12Waypoint   Outdoors.cpp:959 (1.14d 006752a0). Cold Plains (level 3)
    /// places the waypoint on the first cell facing the Act-1 town vis-direction; every other
    /// Act-1/2 level Fisher-Yates-shuffles the interior cells and places at the first cell clear
    /// of 0x1b81.</summary>
    public static void SpawnAct12Waypoint(D2DrlgLevelStrc* pLevel)
    {
        var pData = (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;
        if (pLevel->eD2LevelId == LevelIds.ColdPlains)
        {
            int* pVisArray = DrlgRoom.GetVisArrayFromLevelId(pLevel->pDrlg, LevelIds.ColdPlains);
            int nVisIdx = 0;
            for (; nVisIdx < 8; nVisIdx++)
                if (pVisArray[nVisIdx] == LevelIds.BloodMoor) break;
            uint dwWaypointDirMask = 1u << (int)((uint)(nVisIdx + 4) & 0x1f);
            int gw = pData->nGridCoordsWidth;
            int gh = pData->nGridCoordsHeight;
            for (int nGridY = 0; nGridY < gh; nGridY++)
            {
                for (int nGridX = 0; nGridX < gw; nGridX++)
                {
                    uint dwLink = (uint)DrlgGrid.GetGridFlags(&pData->sGridLink, nGridX, nGridY);
                    uint dwOut = (uint)DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nGridX, nGridY);
                    if ((dwLink & dwWaypointDirMask) != 0 && (dwOut & 0x400) != 0)
                    {
                        int wx = nGridX;
                        int wy = nGridY;
                        if (wx == 0) wx = 1;
                        if (wy == 0) wy = 1;
                        if (wx == gw - 1) wx -= 1;
                        if (wy == gh - 1) wy -= 1;
                        DrlgGrid.AlterGridFlag(&pData->sGridLink, wx, wy, 0x20000, 0);
                        DrlgGrid.AlterGridFlag(&pData->sGridOutdoor, wx, wy, 0x800, 0);
                        return;
                    }
                }
            }
        }

        int nGridWidth = pData->nGridCoordsWidth - 2;
        int nTotalCells = (pData->nGridCoordsHeight - 2) * nGridWidth;
        if (nTotalCells == 0) return;

        Span<int> aCellX = stackalloc int[513];
        Span<int> aCellY = stackalloc int[513];
        for (int i = 0; i < nTotalCells; i++)
        {
            aCellX[i] = i % nGridWidth;
            aCellY[i] = i / nGridWidth;
        }
        for (int nShuffleCount = nTotalCells; nShuffleCount > 0; nShuffleCount--)
        {
            int a = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nTotalCells);
            int b = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nTotalCells);
            (aCellX[a], aCellX[b]) = (aCellX[b], aCellX[a]);
            (aCellY[a], aCellY[b]) = (aCellY[b], aCellY[a]);
        }

        for (int nIdx = 0; nIdx < nTotalCells; nIdx++)
        {
            int nGridY = aCellY[nIdx] + 1;
            int nGridX = aCellX[nIdx] + 1;
            uint flags = (uint)DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nGridX, nGridY);
            if ((flags & 0x1b81) == 0)
            {
                DrlgGrid.AlterGridFlag(&pData->sGridLink, nGridX, nGridY, 0x10000, 0);
                DrlgGrid.AlterGridFlag(&pData->sGridOutdoor, nGridX, nGridY, 0x800, 0);
                return;
            }
        }
    }
}

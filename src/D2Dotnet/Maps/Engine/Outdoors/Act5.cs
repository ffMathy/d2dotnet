// Port of libd2 packages/drlg/src/drlg/outdoors/Act5.zig (MIT, (c) 2026 jaenster).
//
// Mechanical transform of the ACT-V (siege/Bloody-Foothills) OUTDOOR placement tree. This
// whole subtree was ABSENT from the recon closure until a fresh ghidra-mcp decompile.
//
// Sources (1.14d Game.exe, session 62fbfe69):
//   OutSiege::InitAct5OutdoorLevel                          0067e600
//   OutPlace::Act5::DRLGOUTROOM_PlaceAct5RoadPath           0067dcf0
//   OutPlace::Act5::DRLGOUTROOM_PlaceAct5OuterWalls         0067def0
//   OutPlace::Act5::DRLGOUTROOM_PlaceAct5BorderPresets      0067db50
//   OutPlace::Act5::DRLGOUTROOM_PlaceAct5SpecialPresets     0067da70  (pLevel EBX)
//   OutPlace::Act5::DRLGOUTROOM_PlaceAct5SecondaryBorder    0067e0e0  (pLevel EDI)
//   OutPlace::Act5::DRLGOUTROOM_PlaceAct5SceneryPresets     0067e160
//   OutPlace::Act5::DRLGOUTPLACE_PlaceAct5BloodyFoothillsRandomPresets 0067e240 (pLevel ESI)
//   OutPlace::Act5::BloodyFoothills::DRLGOUTPLACE_PlaceAct5SiegePresets 0067e4b0 (pLevel EBX)
//   OutPlace::Act5::BloodyFoothills::DRLGOUTPLACE_PlaceAct5WallPresets  0067e560
//
// .rdata tables recovered from the binary (the recon rendered each as a single-int static stub):
//   * gaAct5SpecialPresetTable_0  @0x6f1fac  — stride-5 int rows, 3 entries, loop bound 0x6f1fe8.
//   * gnAct5OuterWallGrid{Offset,Step}ByType  @0x6f1fd8/0x6f1fdc — two interleaved int arrays,
//     12 perimeter-direction pairs {dx=offset, dy=step}.
//   * gnAct5SceneryPresetOffsetByOrientation  @0x6f2104 — stride-7 int rows, 15 entries, loop
//     bound 0x6f22a7.
//
// Faithful idioms (decompiler artifacts unwound):
//   * OuterWalls advances nXCur by `(int)nXCur->apTiles + offset*2 - 0x68` == nXCur + offset*2.
//   * `& 0xfffffffe` / `& -2` align coordinates to even cells.
//   * RNG: low-word selector, matching the engine's 32-bit modulo.
//   * PlaceAct5OuterWalls reads wall-type codes (0x371..0x37c) from sGridPreset; when that
//     prefill is absent the looked-up index is out of range; guarded to stop the perimeter walk.

namespace D2Dotnet.Maps.Engine.Outdoors;

internal static unsafe class Act5
{
    private const int OP_OR = 0;

    private static D2DrlgLevelDataWildernessLevel* Wild(D2DrlgLevelStrc* pLevel) => (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;
    private static int Div8(int v) => (v + (int)((uint)v >> 31) * 7) >> 3;
    private static int FloorMod(int a, int b)
    {
        int r = a % b;
        return (r != 0 && ((r < 0) != (b < 0))) ? r + b : r;
    }

    // gaAct5SpecialPresetTable_0 @0x6f1fac — {levelId, randVariant, sideFlag, vertPreset, horizPreset}
    private static readonly int[,] gaAct5SpecialPresetTable =
    {
        { (int)AreaId.ArreatPlateau, 0, 0, 0x391, 0x392 },
        { (int)AreaId.FrozenTundra, 0, 1, 0x3d7, 0x3d8 },
        { (int)AreaId.FrozenTundra, 0, 0, 0x3d9, 0x3da },
    };

    // gnAct5OuterWallGrid{Offset,Step}ByType — {dx (offset), dy (step)} perimeter dirs.
    private static readonly int[,] gaAct5OuterWallDir =
    {
        { -1, 0 }, { 0, -1 }, { 1, 0 }, { 0, 1 },
        { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 },
        { -1, 0 }, { -1, 0 }, { 1, 0 }, { 0, 1 },
    };

    // gnAct5SceneryPresetOffsetByOrientation @0x6f2104 — {levelId, vert, horiz, nType, count, bMandatory}
    private static readonly int[,] gaAct5SceneryPresetTable =
    {
        { (int)AreaId.FrigidHighlands, 0x3bb, 0x3bc, 0, 1, 1 },
        { (int)AreaId.ArreatPlateau, 0x3bb, 0x3bc, 0, 1, 1 },
        { (int)AreaId.FrozenTundra, 0x3bb, 0x3bc, 1, 1, 1 },
        { (int)AreaId.ArreatPlateau, 0x3b9, 0x3b9, -1, 1, 1 },
        { (int)AreaId.FrozenTundra, 0x3ba, 0x3ba, -1, 1, 1 },
        { (int)AreaId.FrigidHighlands, 0x3b0, 0x3b3, -1, 1, 0 },
        { (int)AreaId.FrigidHighlands, 0x3ae, 0x3b1, -1, 4, 0 },
        { (int)AreaId.FrigidHighlands, 0x3af, 0x3b2, -1, 4, 0 },
        { (int)AreaId.ArreatPlateau, 0x3ad, 0x3ad, -1, 1, 0 },
        { (int)AreaId.ArreatPlateau, 0x3ab, 0x3ab, -1, 1, 0 },
        { (int)AreaId.ArreatPlateau, 0x3ac, 0x3ac, -1, 5, 0 },
        { (int)AreaId.FrozenTundra, 0x3b4, 0x3b4, -1, 4, 0 },
        { (int)AreaId.FrozenTundra, 0x3b5, 0x3b5, -1, 4, 0 },
        { (int)AreaId.FrozenTundra, 0x3b6, 0x3b6, -1, 4, 0 },
        { (int)AreaId.FrozenTundra, 0x3b7, 0x3b7, -1, 3, 0 },
    };

    /// <summary>DRLGOUTROOM_PlaceAct5RoadPath   OutPlace/Act5.cpp (0067dcf0)</summary>
    private static void PlaceAct5RoadPath(D2DrlgLevelStrc* pLevel)
    {
        var pWildData = Wild(pLevel);
        int nBasePresetId = (pLevel->eD2LevelId == (int)AreaId.FrozenTundra ? 1 : 0) + 4;
        var pDrlgVertex = pWildData->pVertices;
        var pNextVertex = pWildData->pVertices->pNext;
        while (true)
        {
            int nDx, nDy, nNextDx, nNextDy;
            DrlgVer.GetCoordDiff(pDrlgVertex, &nDx, &nDy);
            DrlgVer.GetCoordDiff(pNextVertex, &nNextDx, &nNextDy);
            int nAbsDx = nDx < 0 ? -nDx : nDx;
            int nAbsDy = nDy < 0 ? -nDy : nDy;

            int nNextXAligned = pNextVertex->nPosX & -2;
            int nNextYAligned = pNextVertex->nPosY & -2;
            int nCurrentX = pDrlgVertex->nPosX & -2;
            int nCurrentY = pDrlgVertex->nPosY & -2;
            int nRoadPresetId = Border.GetRoadPresetId(nDx, nDy, nBasePresetId);
            if ((pDrlgVertex->dwFlags & 2) == 0)
            {
                while (nCurrentX != nNextXAligned || nCurrentY != nNextYAligned)
                {
                    nCurrentY += nDy * 2;
                    nCurrentX += nDx * 2;
                    OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nCurrentX, nCurrentY, nRoadPresetId, -1, 0);
                    DrlgGrid.AlterGridFlag(&pWildData->sGridOutdoor, nCurrentX, nCurrentY, 1, OP_OR);
                }
            }
            if ((pDrlgVertex->dwFlags & 1) != 0)
            {
                int nMaxCoord = pDrlgVertex->nPosX;
                if (pDrlgVertex->nPosX <= pNextVertex->nPosX) nMaxCoord = pNextVertex->nPosX;
                int nCX = (nMaxCoord + nAbsDx * -4) & -2;
                nMaxCoord = pNextVertex->nPosY;
                if (pNextVertex->nPosY < pDrlgVertex->nPosY) nMaxCoord = pDrlgVertex->nPosY;
                int nCY = (nMaxCoord + nAbsDy * -4) & -2;
                var pGrid = &pWildData->sGridOutdoor;
                DrlgGrid.AlterGridFlag(pGrid, nCX, nCY, 0x400, OP_OR);
                DrlgGrid.AlterGridFlag(pGrid, nCX + nAbsDx * 2, nCY + nAbsDy * 2, 0x400, OP_OR);
            }

            int nAdjacentPresetId = Border.GetAdjacentRoadPresetId(nDx * 2, nDy * 2, nNextDx * 2, nNextDy * 2, nBasePresetId);
            if (nAdjacentPresetId != 0)
            {
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nNextXAligned, nNextYAligned, nAdjacentPresetId, -1, 0);
                DrlgGrid.AlterGridFlag(&pWildData->sGridOutdoor, nNextXAligned, nNextYAligned, 1, OP_OR);
            }

            bool bNotAtStart = pNextVertex != pWildData->pVertices;
            pDrlgVertex = pNextVertex;
            pNextVertex = pNextVertex->pNext;
            if (!bNotAtStart) break;
        }

        if (pLevel->eD2LevelId == (int)AreaId.FrigidHighlands)
        {
            var pWildDataFinal = Wild(pLevel);
            int nMaxCoord = pWildDataFinal->nGridCoordsWidth - 2;
            int nHeight = pWildDataFinal->nGridCoordsHeight;
            DrlgGrid.AlterGridFlag(&pWildDataFinal->sGridOutdoor, nMaxCoord, nHeight - 4, 0x400, OP_OR);
            DrlgGrid.AlterGridFlag(&pWildDataFinal->sGridOutdoor, nMaxCoord, nHeight - 3, 0x400, OP_OR);
        }
    }

    /// <summary>DRLGOUTROOM_PlaceAct5OuterWalls   OutPlace/Act5.cpp (0067def0)</summary>
    private static void PlaceAct5OuterWalls(D2DrlgLevelStrc* pLevel)
    {
        var pWildData = Wild(pLevel);
        int nMaxX = pWildData->nGridCoordsWidth - 2;
        int nY = pWildData->nGridCoordsHeight - 2;
        bool bBloody = pLevel->eD2LevelId == (int)AreaId.FrozenTundra;
        int nXCur = nMaxX;
        int nYCurrent = 0;
        while (nXCur != 0 || nYCurrent != nY)
        {
            int raw = DrlgGrid.GetGridFlags(&pWildData->sGridPreset, nXCur, nYCurrent);
            int nPresetOffset = raw - (bBloody ? 0x3bd : 0x371);
            if (nPresetOffset < 0 || nPresetOffset >= 12) break;
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nXCur, nYCurrent, nPresetOffset + (bBloody ? 0x4c : 0) + 0x37d, -1, 0);
            nYCurrent += gaAct5OuterWallDir[nPresetOffset, 1] * 2;
            nXCur += gaAct5OuterWallDir[nPresetOffset, 0] * 2;
        }
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nMaxX, 0, (bBloody ? 0x4c : 0) + 0x38a, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, nY, (bBloody ? 0x4c : 0) + 0x389, -1, 0);
    }

    /// <summary>DRLGOUTROOM_PlaceAct5BorderPresets   OutPlace/Act5.cpp (0067db50)</summary>
    private static void PlaceAct5BorderPresets(D2DrlgLevelStrc* pLevel)
    {
        var pWildData = Wild(pLevel);
        int nVariant = (pLevel->eD2LevelId == (int)AreaId.BloodyFoothills ? 1 : 0) * 2 - 1;

        // Top edge (y=0): first 0x400-flagged cell -> 0x38d.
        for (int nXCur = 0; nXCur < pWildData->nGridCoordsWidth; nXCur++)
        {
            if ((DrlgGrid.GetGridFlags(&pWildData->sGridOutdoor, nXCur, 0) & 0x400) != 0)
            {
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nXCur, 0, 0x38d, nVariant, 0);
                break;
            }
        }
        // Bottom edge (y=height-2): -> 0x38c.
        {
            int nYb = pWildData->nGridCoordsHeight - 2;
            for (int nXCur = 0; nXCur < pWildData->nGridCoordsWidth; nXCur++)
            {
                if ((DrlgGrid.GetGridFlags(&pWildData->sGridOutdoor, nXCur, nYb) & 0x400) != 0)
                {
                    OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nXCur, nYb, 0x38c, nVariant, 0);
                    break;
                }
            }
        }
        // Left edge (x=0): -> 0x38e.
        for (int nYi = 0; nYi < pWildData->nGridCoordsHeight; nYi++)
        {
            if ((DrlgGrid.GetGridFlags(&pWildData->sGridOutdoor, 0, nYi) & 0x400) != 0)
            {
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, nYi, 0x38e, nVariant, 0);
                break;
            }
        }
        // Right edge (x=width-2): -> 0x38b.
        {
            int nXr = pWildData->nGridCoordsWidth - 2;
            int nYi = 0;
            if (pWildData->nGridCoordsHeight <= 0) return;
            while (true)
            {
                if ((DrlgGrid.GetGridFlags(&pWildData->sGridOutdoor, nXr, nYi) & 0x400) != 0)
                {
                    OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nXr, nYi, 0x38b, nVariant, 0);
                    return;
                }
                nYi += 1;
                if (nYi >= pWildData->nGridCoordsHeight) return;
            }
        }
    }

    /// <summary>DRLGOUTROOM_PlaceAct5SpecialPresets   OutPlace/Act5.cpp (0067da70, EBX)</summary>
    private static void PlaceAct5SpecialPresets(D2DrlgLevelStrc* pLevel)
    {
        var pWildData = Wild(pLevel);
        for (int r = 0; r < 3; r++)
        {
            if (pLevel->eD2LevelId != gaAct5SpecialPresetTable[r, 0]) continue;
            int randVariant = gaAct5SpecialPresetTable[r, 1];
            int sideFlag = gaAct5SpecialPresetTable[r, 2];
            int nX, nY, nLvlPrestId;
            if (pLevel->sCoordinatesAndSize.WorldSize.y < pLevel->sCoordinatesAndSize.WorldSize.x)
            {
                nLvlPrestId = gaAct5SpecialPresetTable[r, 4];
                if (sideFlag == 0)
                {
                    nX = 0;
                    nY = 2;
                }
                else
                {
                    nX = pWildData->nGridCoordsWidth - 2;
                    nY = 2;
                }
            }
            else
            {
                nLvlPrestId = gaAct5SpecialPresetTable[r, 3];
                nX = 2;
                nY = sideFlag == 0 ? 0 : pWildData->nGridCoordsHeight - 2;
            }
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nY, nLvlPrestId, randVariant, 0);
        }
    }

    /// <summary>DRLGOUTROOM_PlaceAct5SecondaryBorder   OutPlace/Act5.cpp (0067e0e0, EDI). Faithful
    /// setup of the secondary-border callbacks (matches the 1.14d 0067e0e0 assignments exactly)
    /// then AddSecondaryBorder, which runs the substitution-group placement (seed-consuming) over
    /// the LvlSub records of type 0xc.</summary>
    private static void PlaceAct5SecondaryBorder(D2DrlgLevelStrc* pLevel)
    {
        if (pLevel->eD2LevelId == (int)AreaId.BloodyFoothills) return;
        var pWildData = Wild(pLevel);
        D2UnkOutdoorStrc sUnk = default;
        sUnk.pCoordsGrid = (D2DrlgGridStrc*)&pWildData->pGridCoordsCellFlags;
        sUnk.pPrimaryFloorGrid = &pWildData->sGridPreset;
        sUnk.pWallGrid = &pWildData->sGridOutdoor;
        sUnk.pfnValidateBorderCell = (delegate*<D2DrlgLevelStrc*, int, int, int, int, uint, int>)&OutPlace.ValidateSecondaryBorderCell;
        sUnk.pfnAlterAdjacentCells = (delegate*<D2DrlgLevelStrc*, int, int, void>)&OutPlace.AlterAdjacentPresetGridCells;
        sUnk.pfnSetBlankCell = (delegate*<D2DrlgLevelStrc*, int, int, void>)&OutPlace.SetBlankGridCell;
        sUnk.pfnSpawnPreset = (delegate*<D2DrlgLevelStrc*, int, int, int, int, int, void>)&OutPlace.SpawnOutdoorLevelPresetEx;
        sUnk.pfnGetBorderPreset = (delegate*<D2DrlgLevelStrc*, int, int, int>)&OutPlace.GetSecondaryBorderPresetFromLvlSub;
        sUnk.nPrevBorderPresetId = -1;
        sUnk.nLvlSubId = 0xc;
        sUnk.pLevel = pLevel;
        TileSub.AddSecondaryBorder(&sUnk);
    }

    /// <summary>DRLGOUTROOM_PlaceAct5SceneryPresets   OutPlace/Act5.cpp (0067e160)</summary>
    private static void PlaceAct5SceneryPresets(D2DrlgLevelStrc* pLevel)
    {
        int nWidth = pLevel->sCoordinatesAndSize.WorldSize.x;
        int nHeight = pLevel->sCoordinatesAndSize.WorldSize.y;
        for (int r = 0; r < 15; r++)
        {
            if (pLevel->eD2LevelId != gaAct5SceneryPresetTable[r, 0]) continue;
            int nPresetId = nWidth < nHeight ? gaAct5SceneryPresetTable[r, 1] : gaAct5SceneryPresetTable[r, 2];
            int nType = gaAct5SceneryPresetTable[r, 3];
            int count = gaAct5SceneryPresetTable[r, 4];
            int mandatory = gaAct5SceneryPresetTable[r, 5];
            int bSuccess = 0;
            if (count > 0)
            {
                for (int nCount = 0; nCount < count; nCount++)
                    bSuccess = OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetId, nType, 0, 0x0f);
                if (bSuccess != 0) continue;
            }
            // engine: ERROR_UnrecoverableInternalError_Halt(0x219) for a mandatory scenery preset
            // that failed to place; degrade rather than halt, as libd2.
            if (mandatory != 0) return;
        }
    }

    /// <summary>DRLGOUTPLACE_PlaceAct5SiegePresets   BloodyFoothills.cpp (0067e4b0, EBX)</summary>
    private static void PlaceAct5SiegePresets(D2DrlgLevelStrc* pLevel)
    {
        if (pLevel == null || pLevel->eD2LevelId != (int)AreaId.FrigidHighlands) return;
        var pLevelData = Wild(pLevel);
        var pLvlPrest = DrlgTables.LvlPrestGetLine(0x370);
        int nX = pLevelData->nGridCoordsWidth - Div8(pLvlPrest->SizeX);
        int nY = pLevelData->nGridCoordsHeight - Div8(pLvlPrest->SizeY);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nY, 0x370, -1, 0);
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nY - 2, 0x380, -1, 0);
    }

    /// <summary>DRLGOUTPLACE_PlaceAct5WallPresets   BloodyFoothills.cpp (0067e560)</summary>
    private static void PlaceAct5WallPresets(D2DrlgLevelStrc* pLevel)
    {
        var pLevelData = Wild(pLevel);
        var pLvlPrest = DrlgTables.LvlPrestGetLine(0x361);
        int nSegmentWidth = Div8(pLvlPrest->SizeX);
        int nX = pLevelData->nGridCoordsWidth - nSegmentWidth;
        int nSegmentIndex = 0;
        while (-1 < nX)
        {
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, 0, nSegmentIndex + 0x361, 0, 0);
            nSegmentIndex += 1;
            nX -= nSegmentWidth;
            if (0xe < nSegmentIndex) return;
        }
    }

    /// <summary>DRLGOUTPLACE_PlaceAct5BloodyFoothillsRandomPresets   Act5.cpp (0067e240, ESI)</summary>
    private static void PlaceAct5BloodyFoothillsRandomPresets(D2DrlgLevelStrc* pLevel)
    {
        if (pLevel->eD2LevelId != (int)AreaId.FrigidHighlands) return;
        var pLevelData = Wild(pLevel);
        int nPlacedCount = 0;

        for (int nAttempts = 0; nAttempts < 0x5a; nAttempts++)
        {
            if (2 < nPlacedCount) break;
            int nGridX = (int)DrlgRng.GetModuloFromSeed(&pLevel->sSeed, (uint)(pLevelData->nGridCoordsWidth / 2));
            int nGridY = (int)DrlgRng.GetModuloFromSeed(&pLevel->sSeed, (uint)(pLevelData->nGridCoordsHeight / 2));
            int nPresetCoord = OutPlace.GetPresetCoordFromGrid(pLevel, nGridX * 2, nGridY * 2);
            if (unchecked((uint)(nPresetCoord - 0x393)) < 8)
            {
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nGridX * 2, nGridY * 2, nPresetCoord + 0x10, -1, 0);
                nPlacedCount += 1;
            }
        }

        int nStartX = pLevelData->nGridCoordsWidth / 2;
        nStartX = PickStart(pLevel, nStartX);
        int nStartY = pLevelData->nGridCoordsHeight / 2;
        nStartY = PickStart(pLevel, nStartY);

        for (int nA = 0; nA < pLevelData->nGridCoordsHeight; nA++)
        {
            if (2 < nPlacedCount) return;
            int nGW = pLevelData->nGridCoordsWidth;
            for (int nXIter = 0; nXIter < nGW; nXIter++)
            {
                if (2 < nPlacedCount) break;
                int nGX = FloorMod(nXIter + nStartX * 2, nGW);
                int nYv = FloorMod(nA + nStartY * 2, pLevelData->nGridCoordsHeight);
                int nPresetId = OutPlace.GetPresetCoordFromGrid(pLevel, nGX, nYv);
                if (unchecked((uint)(nPresetId - 0x393)) < 8)
                {
                    OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nGX, nYv, nPresetId + 0x10, -1, 0);
                    nPlacedCount += 1;
                }
                nGW = pLevelData->nGridCoordsWidth;
            }
        }
        // engine: ERROR_UnrecoverableInternalError_Halt(0x259) when fewer than 3 placed; degrade
        // rather than halt, as libd2.
    }

    /// <summary>Seed-driven start offset: low-word modulo (pow2 -> mask, else -> %).</summary>
    private static int PickStart(D2DrlgLevelStrc* pLevel, int n)
    {
        if (n < 1) return 0;
        var next = DrlgRng.SeedNext(pLevel->sSeed);
        pLevel->sSeed = next;
        uint un = (uint)n;
        uint low = (uint)next.nSeedLow;
        if ((un & (un - 1)) == 0) return (int)((un - 1) & low);
        return (int)(low % un);
    }

    /// <summary>OutSiege::InitAct5OutdoorLevel   OutSiege.cpp (0067e600)</summary>
    public static void InitAct5OutdoorLevel(D2DrlgLevelStrc* pLevel)
    {
        if (pLevel->eD2LevelId == (int)AreaId.BloodyFoothills)
        {
            PlaceAct5WallPresets(pLevel);
            return;
        }
        Border.SetOutGridLinkFlags(pLevel);
        PlaceAct5RoadPath(pLevel);
        PlaceAct5OuterWalls(pLevel);
        PlaceAct5BorderPresets(pLevel);
        PlaceAct5SpecialPresets(pLevel);
        if (pLevel->eD2LevelId == (int)AreaId.FrigidHighlands) PlaceAct5SiegePresets(pLevel);
        PlaceAct5SecondaryBorder(pLevel);
        PlaceAct5BloodyFoothillsRandomPresets(pLevel);
        PlaceAct5SceneryPresets(pLevel);
    }
}

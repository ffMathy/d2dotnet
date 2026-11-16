// Port of libd2 packages/drlg/src/drlg/outdoors/OutRoom.zig (MIT, (c) 2026 jaenster).
//
// Faithful transform of the Act-1 outdoor room/exit/corner machinery from
// recon/closure/Drlg.cpp and recon/closure/OutWild.cpp.
//
// Transformed here:
//   DRLGOUTROOM_IsGridColumnEmpty          Drlg.cpp:6007  (1.14d 0067fc70)
//   DRLGOUTROOM_SpawnVerticalBorderPresets Drlg.cpp:6117  (1.14d 0067fe90)
//   DRLGOUTROOM_PlaceRiverCrossingPreset   Drlg.cpp:6053  (1.14d 0067fd20)
//   DRLGOUTROOM_SpawnCornerFillPreset      Drlg.cpp:6219  (1.14d 006801a0)
//   DRLGOUTROOM_FillBorderCornersAndPresets Drlg.cpp:6239 (1.14d 00680200)
//   SpawnTownTransitionsAndCaves           OutWild.cpp:41 (1.14d 006803d0)
//   DRLGOUTROOM_SpawnRandomOutdoorDecorations Drlg.cpp:6346 (1.14d 006804e0)
//   SpawnRandomOutdoorDS1                  Outdoors.cpp:833 (1.14d 006745e0)
//   DRLGOUTROOM_SpawnAct1LevelPresets      Drlg.cpp:6371  (1.14d 00680580)
//   DRLGOUTROOM_BuildExitPointArray        Drlg.cpp:6614  (1.14d 00680d70)
//   DRLGOUTROOM_BuildVertexPathsWithJitter Drlg.cpp:6873  (1.14d 00681240)
//   DRLGOUTROOM_LinkOutdoorRoomExits       Drlg.cpp:6942  (1.14d 00681420)
//   DRLGOUTROOM_FindPathBetweenExits       Drlg.cpp:7184  (1.14d 006817d0)
//
// Road/transition dwFlags bits (0x4/0x8/0x10/0x80/0x100/0x200/0x400) are set by
// DRLG_ApplyAct1WildRoadFlags (Drlg.cs) from the binary table @0x6f1258.

namespace D2Dotnet.Map.Drlg.Engine.Outdoors;

internal static unsafe class OutRoom
{
    private static D2DrlgLevelDataWildernessLevel* Wild(D2DrlgLevelStrc* pLevel) => (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;

    // Variant lookup tables for SpawnVerticalBorderPresets. Interleaved pairs
    // [left_variant, right_variant] indexed by sGridPreset value. Binary-verified 1.14d: the
    // 16-entry pair table @ 0x6f2680..0x6f2700 (read as base[nVariant*2] / base[nVariant*2+1]
    // with NO index clamp). Entry 0 is never read (nVariant==0 uses the special formula below).
    private static readonly int[,] gaVerticalBorderVariants =
    {
        { 385, 2 }, // 0:  unused (special-cased)
        { 0, 6 }, // 1
        { 386, 0 }, // 2:  386 = 0x182
        { 0, 8 }, // 3
        { 2, 2 }, // 4
        { 0, 3 }, // 5
        { 1, 1 }, // 6
        { 3, 0 }, // 7
        { 0, 2 }, // 8
        { 0, 1 }, // 9
        { 1, 0 }, // 10
        { 2, 0 }, // 11
        { 2, 3 }, // 12 (0xc): river preset → right=3 (keeps 0x30000 flow)
        { 1, 3 }, // 13 (0xd): river preset → right=3
        { 3, 1 }, // 14
        { 3, 2 }, // 15
    };

    // gaOutdoorsGridWidthByLevel_0 (X offsets) @ 0x6f0614 and gaOutdoorsGridWidthByLevel_2
    // (Y offsets) @ 0x6f060c — signed i8.
    private static readonly int[] gaOutdoorsGridOffsetX = { -1, 0, 0, 1, -1, 1, 1, -1 };
    private static readonly int[] gaOutdoorsGridOffsetY = { 0, -1, 1, 0, -1, 1, -1, 1 };

    // gaOutRoomPathDirectionDelta @ 0x6f2840 (24 signed bytes). First 16 = four 4-entry
    // direction-cycle groups; [0x10..0x14] = Y step deltas; [0x14..0x18] = X step deltas
    // (cardinal moves indexed by the node's direction byte 0..3).
    private static readonly int[] gaOutRoomPathDirectionDelta =
    {
        0, 1, 2, 3, // group 0
        0, 1, 1, 1, // group 1
        3, 2, 1, 2, // group 2
        0, 3, 2, 1, // group 3
        0, 1, 0, -1, // Y deltas (offset 0x10)
        1, 0, -1, 0, // X deltas (offset 0x14)
    };

    // gPathDirectionTable @ 0x6f1518 (75 int32, 3 per 5x5-grid index). Only the first element of
    // each triple is read by DRLGPATH_GetPathDirection, so this holds the pre-extracted [idx*3]
    // value for index 0..24.
    private static readonly int[] gaPathDirectionPrimary =
    {
        5, 4, 4, 4, 3, 6, 5, 4, 3, 2, 6, 6, 6, 2, 2, 6, 7, 0, 1, 2, 7, 0, 0, 0, 1,
    };

    // gaSpiralOffsetY @ 0x6f2800, gaSpiralOffsetX @ 0x6f2810 — center-cell search.
    private static readonly int[] gaSpiralOffsetY = { 0, 1, -1, 0 };
    private static readonly int[] gaSpiralOffsetX = { -1, 0, 0, 1 };

    /// <summary>DRLGOUTROOM_IsGridColumnEmpty   Drlg.cpp:6007 (1.14d 0067fc70). True if no cell
    /// in column nX or nX+1 has sGridOutdoor bit 1 set.</summary>
    public static bool IsGridColumnEmpty(D2DrlgLevelStrc* pLevel, int nX)
    {
        var pData = Wild(pLevel);
        for (int nY = 0; nY < pData->nGridCoordsHeight; nY++)
        {
            if ((DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nX, nY) & 2) != 0) return false;
            if ((DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nX + 1, nY) & 2) != 0) return false;
        }
        return true;
    }

    /// <summary>DRLGOUTROOM_PlaceRiverCrossingPreset   Drlg.cpp:6053 (1.14d 0067fd20). nColumn is
    /// the mid-split column passed by the caller; the recon decompiles it as the phantom stack
    /// param in_stack_00000004. Disasm 0x67fdc0-0x67fe3c: empty-check at nColumn-1 (and
    /// additionally nColumn+2 when dwRoadFlags==0); the river pair is placed at nColumn /
    /// nColumn+1 where both have outdoor flow 0x30000; second variant = (dwRoadFlags!=0)+2.</summary>
    private static void PlaceRiverCrossingPreset(D2DrlgLevelStrc* pLevel, int nColumn)
    {
        var pData = Wild(pLevel);
        int nUsableRows = pData->nGridCoordsHeight - 2;
        if (nUsableRows < 1) return;

        int nStartYOffset = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nUsableRows);
        uint dwRoadFlags = pData->dwFlags & 4;
        for (int nYIter = 0; nYIter < nUsableRows; nYIter++)
        {
            int nY = (nYIter + nStartYOffset) % nUsableRows + 1;
            if (OutPlace.IsGridCellEmpty(pLevel, nColumn - 1, nY) == 0) continue;
            if (dwRoadFlags == 0 && OutPlace.IsGridCellEmpty(pLevel, nColumn + 2, nY) == 0) continue;
            if ((DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nColumn, nY) & 0xf0000) != 0x30000) continue;
            if ((DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nColumn + 1, nY) & 0xf0000) != 0x30000) continue;
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nColumn, nY, 0x1c, 1, 0);
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nColumn + 1, nY, 0x1c, (dwRoadFlags != 0 ? 1 : 0) + 2, 0);
            return;
        }
    }

    /// <summary>DRLGOUTROOM_SpawnVerticalBorderPresets   Drlg.cpp:6117 (1.14d 0067fe90). Stamps
    /// 0x1a/0x1b presets along left (nColumn) and right (nColumn+1) of the split column, using
    /// variant tables keyed on sGridPreset value.</summary>
    public static void SpawnVerticalBorderPresets(D2DrlgLevelStrc* pLevel, int nColumn)
    {
        var pData = Wild(pLevel);
        for (int nY = 0; nY < pData->nGridCoordsHeight; nY++)
        {
            int nGridFlagsLeft = DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nColumn, nY);
            int nVariantLeft = DrlgGrid.GetGridFlags(&pData->sGridPreset, nColumn, nY);
            if (nVariantLeft == 0) nVariantLeft = (nGridFlagsLeft & 0x100) != 0 ? 0 : 3;
            else if (nVariantLeft == 7 && (nGridFlagsLeft & 0xf0000) == 0x30000) nVariantLeft = 3;
            else nVariantLeft = gaVerticalBorderVariants[nVariantLeft, 0];
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nColumn, nY, 0x1a, nVariantLeft, 0);

            int nGridFlagsRight = DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nColumn + 1, nY);
            int nVariantRight = DrlgGrid.GetGridFlags(&pData->sGridPreset, nColumn + 1, nY);
            if (nVariantRight == 0) nVariantRight = (nGridFlagsRight & 0x100) != 0 ? 0 : 3;
            else if (nVariantRight == 7 && (nGridFlagsRight & 0xf0000) == 0x30000) nVariantRight = 3;
            else nVariantRight = gaVerticalBorderVariants[nVariantRight, 1];
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nColumn + 1, nY, 0x1b, nVariantRight, 0);
        }
        if ((pData->dwFlags & 0x14) != 0) PlaceRiverCrossingPreset(pLevel, nColumn);
    }

    /// <summary>DRLGOUTROOM_SpawnCornerFillPreset   Drlg.cpp:6219 (1.14d 006801a0). If
    /// sGridPreset at (nX,nY) is 0x10→place 0x19, or 0x11→place 0x18. Sets 0x40.</summary>
    private static bool SpawnCornerFillPreset(D2DrlgLevelStrc* pLevel, int nY, int nX)
    {
        var pData = Wild(pLevel);
        int nGridFlags = DrlgGrid.GetGridFlags(&pData->sGridPreset, nX, nY);
        if (nGridFlags == 0x10)
        {
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nY, 0x19, -1, 0);
            pData->dwFlags |= 0x40;
            return true;
        }
        if (nGridFlags == 0x11)
        {
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nY, 0x18, -1, 0);
            pData->dwFlags |= 0x40;
            return true;
        }
        return false;
    }

    /// <summary>DRLGOUTROOM_FillBorderCornersAndPresets   Drlg.cpp:6239 (1.14d 00680200). For
    /// non-0x27 levels: optionally spawns vertical border presets (flags &amp; 0xc), then
    /// conditionally fills a corner junction (flags &amp; 0x20, draws 1 seed) and places a
    /// transition preset (flags &amp; 0x1c, draws 1 seed).</summary>
    public static void FillBorderCornersAndPresets(D2DrlgLevelStrc* pLevel)
    {
        if (pLevel->eD2LevelId == LevelIds.MooMooFarm) return;
        var pData = Wild(pLevel);

        if ((pData->dwFlags & 0xc) != 0)
        {
            int nCol = pData->nGridCoordsWidth - 2;
            if (IsGridColumnEmpty(pLevel, nCol)) SpawnVerticalBorderPresets(pLevel, nCol);
        }

        if ((pData->dwFlags & 0x20) != 0 && (pData->dwFlags & 0x40) == 0)
        {
            var seed1 = DrlgRng.SeedNext(pLevel->sSeed);
            pLevel->sSeed = seed1;
            if ((seed1.nSeedLow & 1) == 0)
            {
                // scan row-major: outer=rows (nX), inner=cols (nY)
                for (int nX = 0; nX < pData->nGridCoordsHeight; nX++)
                {
                    bool done = false;
                    for (int nY = 0; nY < pData->nGridCoordsWidth; nY++)
                        if (SpawnCornerFillPreset(pLevel, nX, nY)) { done = true; break; }
                    if (done) break;
                }
            }
            else
            {
                // scan col-major: outer=rows (nY), inner=cols (nX)
                for (int nY = 0; nY < pData->nGridCoordsHeight; nY++)
                {
                    bool done = false;
                    for (int nX = 0; nX < pData->nGridCoordsWidth; nX++)
                        if (SpawnCornerFillPreset(pLevel, nX, nY)) { done = true; break; }
                    if (done) break;
                }
            }
        }

        if ((pData->dwFlags & 0x1c) != 0 && (pData->dwFlags & 0x40) == 0)
        {
            var seed2 = DrlgRng.SeedNext(pLevel->sSeed);
            pLevel->sSeed = seed2;
            bool bFlip = (seed2.nSeedLow & 3) / 2 == 0;
            int nX = (seed2.nSeedLow & 1) == 0 ? pData->nGridCoordsWidth - ((pData->dwFlags & 0x10) != 0 ? 4 : 5) : 3;
            int nY = bFlip ? pData->nGridCoordsHeight - 4 : 3;
            int nPresetId = pLevel->eD2LevelId == LevelIds.BloodMoor ? 0x34 : 0x33;
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nY, nPresetId, -1, 0);
            pData->dwFlags |= 0x40;
        }
    }

    /// <summary>SpawnTownTransitionsAndCaves   OutWild.cpp:41 (1.14d 006803d0). Places
    /// road/cave/town-entry presets based on dwFlags. For level 2 (Blood Moor) the town-entry
    /// preset is placed maximally distant from Rogue Encampment.</summary>
    public static void SpawnTownTransitionsAndCaves(D2DrlgLevelStrc* pLevel)
    {
        if (pLevel->eD2LevelId == LevelIds.MooMooFarm) return;
        var pData = Wild(pLevel);

        if ((pData->dwFlags & 0x10) != 0)
        {
            int nMidCol = pData->nGridCoordsWidth / 2 - 1;
            if (IsGridColumnEmpty(pLevel, nMidCol)) SpawnVerticalBorderPresets(pLevel, nMidCol);
        }
        if ((pData->dwFlags & 0x80) != 0) OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, 0, 3, 1, 0);
        if ((pData->dwFlags & 0x100) != 0) OutPlace.SpawnOutdoorLevelPresetEx(pLevel, pData->nGridCoordsWidth - 7, 0, 3, 2, 0);
        if ((pData->dwFlags & 0x200) != 0) OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, 1, 2, 1, 0);
        if ((pData->dwFlags & 0x400) != 0) OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, pData->nGridCoordsHeight - 6, 2, 1, 0);
        if ((pData->dwFlags & 0x40) != 0) return;

        if (pLevel->eD2LevelId == LevelIds.BloodMoor)
        {
            var pLinkedLevel = Drlg.GetLevelAndAlloc(pLevel->pDrlg, LevelIds.RogueEncampment);
            OutPlace.SpawnPresetFarAway(pLevel, (D2DrlgCoordStrc*)&pLinkedLevel->sCoordinatesAndSize, 0x34, -1, 1, 0xf);
        }
        else
        {
            OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x33, -1, 1, 0xf);
        }
        pData->dwFlags |= 0x40;
    }

    /// <summary>DRLGOUTROOM_SpawnRandomOutdoorDecorations   Drlg.cpp:6346 (1.14d 006804e0). Draws
    /// 1 seed. If seed &amp; 3 == 0: calls SpawnRandomOutdoorDS1 twice. Else once, and if
    /// bAllowDouble draws another seed; if that seed &amp; 1, calls again with 0x31.</summary>
    public static void SpawnRandomOutdoorDecorations(int nPresetType, D2DrlgLevelStrc* pLevel, int bAllowDouble)
    {
        var newSeed = DrlgRng.SeedNext(pLevel->sSeed);
        pLevel->sSeed = newSeed;
        if ((newSeed.nSeedLow & 3) == 0)
        {
            SpawnRandomOutdoorDS1(pLevel, nPresetType, -1);
            SpawnRandomOutdoorDS1(pLevel, nPresetType, -1);
        }
        else
        {
            SpawnRandomOutdoorDS1(pLevel, nPresetType, -1);
            if (bAllowDouble != 0)
            {
                var newSeed2 = DrlgRng.SeedNext(pLevel->sSeed);
                pLevel->sSeed = newSeed2;
                if ((newSeed2.nSeedLow & 1) != 0) SpawnRandomOutdoorDS1(pLevel, 0x31, -1);
            }
        }
    }

    /// <summary>SpawnRandomOutdoorDS1   Outdoors.cpp:833 (1.14d 006745e0). Fisher-Yates shuffle
    /// (2×nTotalCells seed draws), then scans shuffled cells for ones with sGridOutdoor bit 0x80
    /// set and tries 8 neighbor offsets from the binary tables. Falls through to
    /// SpawnOutdoorLevelPreset if none found.</summary>
    public static void SpawnRandomOutdoorDS1(D2DrlgLevelStrc* pLevel, int nLvlPrestId, int nRand)
    {
        var pData = Wild(pLevel);
        uint nGridWidthM2 = (uint)(pData->nGridCoordsWidth - 2);
        uint nTotalCells = (uint)((pData->nGridCoordsHeight - 2) * (int)nGridWidthM2);
        if (nTotalCells == 0) return;

        Span<int> cellX = stackalloc int[513];
        Span<int> cellY = stackalloc int[513];
        cellX.Clear();
        cellY.Clear();
        for (uint i = 0; i < nTotalCells; i++)
        {
            cellX[(int)i] = (int)(i % nGridWidthM2);
            cellY[(int)i] = (int)(i / nGridWidthM2);
        }

        // Fisher-Yates: nTotalCells iterations, 2 draws each
        uint remaining = nTotalCells;
        while (remaining != 0)
        {
            int a = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, nTotalCells);
            int b = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, nTotalCells);
            remaining -= 1;
            (cellX[a], cellX[b]) = (cellX[b], cellX[a]);
            (cellY[a], cellY[b]) = (cellY[b], cellY[a]);
        }

        // Scan shuffled cells: if sGridOutdoor bit 7 is set (path cell), try 8 offsets
        for (uint i = 0; i < nTotalCells; i++)
        {
            int nGridX = cellX[(int)i] + 1;
            int nGridY = cellY[(int)i] + 1;
            int flags = DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nGridX, nGridY);
            if ((sbyte)(byte)flags < 0)
            {
                for (int k = 0; k < 8; k++)
                {
                    int dx = gaOutdoorsGridOffsetX[k];
                    int dy = gaOutdoorsGridOffsetY[k];
                    if (OutPlace.TestOutdoorLevelPreset(pLevel, nGridX + dx, nGridY + dy, nLvlPrestId, 0, 0xf) != 0)
                    {
                        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nGridX + dx, nGridY + dy, nLvlPrestId, nRand, 0);
                        return;
                    }
                }
            }
        }

        // Fallback: regular Fisher-Yates placement
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nLvlPrestId, nRand, 0, 0xf);
    }

    /// <summary>DRLGOUTROOM_SpawnAct1LevelPresets   Drlg.cpp:6371 (1.14d 00680580). Level-specific
    /// preset spawns, then always 0x1d + 0x1e for all Act-1 levels.</summary>
    public static void SpawnAct1LevelPresets(D2DrlgLevelStrc* pLevel)
    {
        switch (pLevel->eD2LevelId)
        {
            case LevelIds.BloodMoor:
                SpawnRandomOutdoorDS1(pLevel, 0x2e, -1);
                SpawnRandomOutdoorDecorations(0x2f, pLevel, 0);
                break;
            case LevelIds.ColdPlains:
                SpawnRandomOutdoorDecorations(0x30, pLevel, 1);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x2c, -1, 0, 0xf);
                break;
            // Binary-verified (0x00680580): Stony Field — two DS1 roads, three presets, returns
            // before shared tail.
            case LevelIds.StonyField:
                SpawnRandomOutdoorDS1(pLevel, 0xa0, -1);
                SpawnRandomOutdoorDS1(pLevel, 0x2d, -1);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0xa2, -1, 0, 0xf);
                SpawnRandomOutdoorDecorations(0x2f, pLevel, 1);
                SpawnRandomOutdoorDecorations(0x2a, pLevel, 0);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x1f, -1, 0, 0xf);
                return;
            // Binary-verified (0x00680580): Dark Wood — three presets then two deco, falls into
            // shared tail.
            case LevelIds.DarkWood:
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0xa1, -1, 0, 0xf);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x29, -1, 0, 0xf);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x28, -1, 0, 0xf);
                SpawnRandomOutdoorDecorations(0x30, pLevel, 1);
                SpawnRandomOutdoorDecorations(0x2b, pLevel, 0);
                break;
            // Binary-verified (0x00680580): Black Marsh — three presets then two deco, falls into
            // shared tail.
            case LevelIds.BlackMarsh:
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0xa3, -1, 0, 0xf);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x26, -1, 0, 0xf);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x27, -1, 0, 0xf);
                SpawnRandomOutdoorDecorations(0x2f, pLevel, 1);
                SpawnRandomOutdoorDecorations(0x2a, pLevel, 0);
                break;
            // Binary-verified (0x00680580): Tamoe Highland — two deco then one preset, returns
            // before shared tail.
            case LevelIds.TamoeHighland:
                SpawnRandomOutdoorDecorations(0x30, pLevel, 1);
                SpawnRandomOutdoorDecorations(0x2b, pLevel, 0);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x1f, -1, 0, 0xf);
                return;
            // Binary-verified (0x00680580): Graveyard — 3×4 preset at anchor (1,1); returns before
            // shared tail.
            case LevelIds.BurialGrounds:
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 1, 1, 0x6c, -1, 0);
                return;
            // Binary-verified (0x00680580): Moo Moo Farm — five presets then shared tail.
            case LevelIds.MooMooFarm:
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x32, -1, 0, 0xf);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x2e, -1, 0, 0xf);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x1f, -1, 0, 0xf);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x26, -1, 0, 0xf);
                OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x27, -1, 0, 0xf);
                break;
        }
        OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x1d, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x1e, -1, 0, 0xf);
    }

    private static int SnapDiv8(int v) => (v + (int)((uint)(v >> 31) & 7)) >> 3;

    /// <summary>DRLGOUTROOM_SnapVertexToGrid   Drlg.cpp:6575 (1.14d 00680cc0). eType selects which
    /// axis is snapped and the constant offset added: 0/1 snap X/Y to +11, 2/3 snap X/Y to -5,
    /// 4+ copy.</summary>
    private static void SnapVertexToGrid(D2DrlgLevelStrc* pLevel, D2DrlgExitPointStrc* pDst, D2DrlgExitPointStrc* pSrc)
    {
        int nWorldX = pLevel->sCoordinatesAndSize.WorldPosition.x;
        int nWorldY = pLevel->sCoordinatesAndSize.WorldPosition.y;
        int x = pSrc->nWorldX - nWorldX;
        int y = pSrc->nWorldY - nWorldY;
        switch (pSrc->eType)
        {
            case 0: x = SnapDiv8(x) * 8 + 0xb; break;
            case 1: y = SnapDiv8(y) * 8 + 0xb; break;
            case 2: x = SnapDiv8(x) * 8 - 5; break;
            case 3: y = SnapDiv8(y) * 8 - 5; break;
        }
        pDst->nWorldX = x + nWorldX;
        pDst->nWorldY = y + nWorldY;
    }

    /// <summary>DRLGOUTROOM_BuildExitPointArray   Drlg.cpp:6614 (1.14d 00680d70). Builds
    /// aExitPoints1 from (a) the pOrthData adjacency list — town/level links to eD2LevelId 1
    /// (Rogue Encampment, per-direction offsets) or 0x1a — and then (b) ALWAYS a full grid scan
    /// for road-exit presets {4,5,6,7,0x18,0x19,0x1c,0x33,0x34}. Finally every exit is snapped
    /// into aExitPoints2 (the A* start positions).</summary>
    private static void BuildExitPointArray(D2DrlgLevelStrc* pLevel)
    {
        var pData = Wild(pLevel);
        pData->nExitCount = 0;

        // (a) orth links
        for (var orth = pData->pOrthData; orth != null; orth = orth->pNext)
        {
            if (orth->pRoomEx == null) continue;
            var pAdj = (D2DrlgLevelStrc*)orth->pRoomEx;
            int eLevelId = pAdj->eD2LevelId;
            int ax = pAdj->sCoordinatesAndSize.WorldPosition.x;
            int ay = pAdj->sCoordinatesAndSize.WorldPosition.y;
            int nIdx = pData->nExitCount;
            if (nIdx >= 6) continue;
            if (eLevelId == LevelIds.RogueEncampment)
            {
                var ex = Inline.At(ref pData->aExitPoints1, nIdx);
                ex->eType = (byte)orth->neDrlgDirection;
                switch (orth->neDrlgDirection)
                {
                    case 0: ex->nWorldX = ax + 0x3b; ex->nWorldY = ay + 0x13; break;
                    case 1: ex->nWorldX = ax + 0x1d; ex->nWorldY = ay + 0x23; break;
                    case 2: ex->nWorldX = ax + 0x4; ex->nWorldY = ay + 0x16; break;
                    case 3: ex->nWorldX = ax + 0x1d; ex->nWorldY = ay + 0x3; break;
                }
                pData->nExitCount += 1;
            }
            else if (eLevelId == LevelIds.MonasteryGate)
            {
                var ex = Inline.At(ref pData->aExitPoints1, nIdx);
                ex->nWorldX = ax + 0x1b;
                ex->nWorldY = ay + 0xd;
                ex->eType = 1;
                pData->nExitCount += 1;
            }
        }

        // (b) grid scan for road-exit presets — always runs after orth links
        int nWorldX = pLevel->sCoordinatesAndSize.WorldPosition.x;
        int nWorldY = pLevel->sCoordinatesAndSize.WorldPosition.y;
        for (int nGridX = 0; nGridX < pData->nGridCoordsWidth; nGridX++)
        {
            for (int nGridY = 0; nGridY < pData->nGridCoordsHeight; nGridY++)
            {
                int nIdx = pData->nExitCount;
                if (nIdx >= 6) break;
                int nPreset = DrlgGrid.GetGridFlags(&pData->sGridPreset, nGridX, nGridY);
                uint nOutdoorFlags = (uint)DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nGridX, nGridY);
                int nFlowDir = (int)((nOutdoorFlags >> 0x10) & 0xf);
                var ex = Inline.At(ref pData->aExitPoints1, nIdx);
                ex->nWorldX = nWorldX + nGridX * 8 + 3;
                ex->nWorldY = nWorldY + nGridY * 8 + 3;
                byte eType = 4; // default = not an exit
                switch (nPreset)
                {
                    case 4: if (nFlowDir == 3) eType = 3; break;
                    case 5: if (nFlowDir == 3) eType = 0; break;
                    case 6: if (nFlowDir == 3) eType = 1; break;
                    case 7: if (nFlowDir == 3) eType = 2; break;
                    case 0x18: eType = 1; break;
                    case 0x19: eType = 0; break;
                    case 0x1c: if (nFlowDir == 1 && nGridX == pData->nGridCoordsWidth - 2) eType = 2; break;
                    case 0x33:
                    case 0x34: eType = (byte)(nFlowDir != 0 ? 1 : 0); break;
                }
                ex->eType = eType;
                if (eType != 4) pData->nExitCount += 1;
            }
        }

        // snap every exit into aExitPoints2 (A* start positions)
        for (int i = 0; i < pData->nExitCount; i++)
            SnapVertexToGrid(pLevel, Inline.At(ref pData->aExitPoints2, i), Inline.At(ref pData->aExitPoints1, i));
    }

    /// <summary>DRLGOUTROOM_FindWarpExitInColumn   Drlg.cpp:5968 (1.14d 0067fbf0). Scans column nX
    /// (rows 1..width-2) for the cell with preset==0x1c AND a nonzero outdoor flow-dir nibble;
    /// returns its grid coords, else (-1,-1).</summary>
    private static void FindWarpExitInColumn(D2DrlgLevelStrc* pLevel, int nX, int* pOutX, int* pOutY)
    {
        var pData = Wild(pLevel);
        for (int nY = 1; nY < pData->nGridCoordsWidth - 1; nY++)
        {
            int nPreset = DrlgGrid.GetGridFlags(&pData->sGridPreset, nX, nY);
            uint nFlags = (uint)DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nX, nY);
            if (nPreset == 0x1c && ((nFlags >> 0x10) & 0xf) == 1)
            {
                *pOutX = nX;
                *pOutY = nY;
                return;
            }
        }
        *pOutX = -1;
        *pOutY = -1;
    }

    /// <summary>DRLGOUTROOM_ComputeExitTargetPositions   Drlg.cpp:6752 (1.14d 00681000). Computes
    /// each exit's target position (aExitPoints4) then snaps into aExitPoints3 (the A* goal
    /// positions).</summary>
    private static void ComputeExitTargetPositions(D2DrlgLevelStrc* pLevel)
    {
        var pData = Wild(pLevel);
        int nWorldX = pLevel->sCoordinatesAndSize.WorldPosition.x;
        int nWorldY = pLevel->sCoordinatesAndSize.WorldPosition.y;
        int nLeftX = -1;
        int nLeftY = -1;

        if ((pData->dwFlags & 0x10) != 0) FindWarpExitInColumn(pLevel, pData->nGridCoordsWidth / 2 - 1, &nLeftX, &nLeftY);

        if ((pData->dwFlags & 0x10) != 0 && nLeftX != -1)
        {
            // left-edge placement
            int nBaseX = nWorldX + 3 + nLeftX * 8;
            int nBaseY = nWorldY + 3 + nLeftY * 8;
            for (int i = 0; i < pData->nExitCount; i++)
            {
                var e4 = Inline.At(ref pData->aExitPoints4, i);
                e4->nWorldY = nBaseY;
                if (Inline.At(ref pData->aExitPoints1, i)->nWorldX > nBaseX)
                {
                    e4->nWorldX = nBaseX + 8;
                    e4->eType = 0;
                }
                else
                {
                    e4->nWorldX = nBaseX;
                    e4->eType = 2;
                }
            }
        }
        else
        {
            // averaged-center + spiral search
            for (int nExitIndex = 0; nExitIndex < pData->nExitCount; nExitIndex++)
            {
                if (nExitIndex == 0)
                {
                    int nCenterX, nCenterY;
                    if (pData->nExitCount == 1)
                    {
                        nCenterX = pData->nGridCoordsWidth / 2;
                        nCenterY = pData->nGridCoordsHeight / 2;
                    }
                    else
                    {
                        int sumX = 0, sumY = 0;
                        for (int j = 0; j < pData->nExitCount; j++)
                        {
                            sumX += Inline.At(ref pData->aExitPoints1, j)->nWorldX - nWorldX;
                            sumY += Inline.At(ref pData->aExitPoints1, j)->nWorldY - nWorldY;
                        }
                        int div = pData->nExitCount * 8;
                        nCenterX = sumX / div;
                        nCenterY = sumY / div;
                    }
                    int nFoundX = 0, nFoundY = 0;
                    bool bFound = false;
                    for (int radius = 0; radius < 8 && !bFound; radius++)
                    {
                        for (int dir = 0; dir < 4; dir++)
                        {
                            nFoundX = gaSpiralOffsetX[dir] * radius + nCenterX;
                            nFoundY = gaSpiralOffsetY[dir] * radius + nCenterY;
                            if (nFoundX >= 0 && nFoundX < pData->nGridCoordsWidth && nFoundY >= 0 && nFoundY < pData->nGridCoordsHeight)
                            {
                                if (OutPlace.IsGridCellEmpty(pLevel, nFoundX, nFoundY) != 0)
                                {
                                    bFound = true;
                                    break;
                                }
                            }
                        }
                    }
                    var e0 = Inline.At(ref pData->aExitPoints4, 0);
                    e0->nWorldX = nWorldX + nFoundX * 8 + 3;
                    e0->nWorldY = nWorldY + nFoundY * 8 + 3;
                    e0->eType = 4;
                }
                else
                {
                    var e0 = Inline.At(ref pData->aExitPoints4, 0);
                    var en = Inline.At(ref pData->aExitPoints4, nExitIndex);
                    en->nWorldX = e0->nWorldX;
                    en->nWorldY = e0->nWorldY;
                    en->eType = 4;
                }
            }
        }

        // snap every target into aExitPoints3 (A* goal positions)
        for (int i = 0; i < pData->nExitCount; i++)
            SnapVertexToGrid(pLevel, Inline.At(ref pData->aExitPoints3, i), Inline.At(ref pData->aExitPoints4, i));
    }

    // Road A* node pool. Node = 10 ints: [0]FCost [1]HMin [2]GCost [3]posX [4]posY [5]stepCount
    // [6]deltaIdx(into gaOutRoomPathDirectionDelta) [7]dirByte(0..3) [8]parentRef [9]childRef.
    // Refs are 1-based node indices (0 = null). Node 0 = init/root.
    private const int PATH_POOL_NODES = 900; // 0x384

    private sealed class PathCtx
    {
        public readonly int[] Pool = new int[PATH_POOL_NODES * 10];
        public int nPoolIndex;
        public D2DrlgLevelStrc* pLevel;
        public int targetX;
        public int targetY;

        public int Get(int idx, int field) => Pool[idx * 10 + field];
        public void Set(int idx, int field, int v) => Pool[idx * 10 + field] = v;

        private static int Delta(int i) => gaOutRoomPathDirectionDelta[i];

        /// <summary>DRLGPATH_GetPathDirection: returns primary direction toward (toX,toY).</summary>
        public static int PathDir(int fromX, int fromY, int toX, int toY) => gaPathDirectionPrimary[DirectionIndex(toX - fromX, toY - fromY)];

        /// <summary>DRLGPATH_GetDirectionIndex: maps (dx,dy) to a clamped 5x5 grid index 0..24.</summary>
        private static int DirectionIndex(int dxIn, int dyIn)
        {
            int nDx = dxIn;
            int nDy = dyIn;
            int absDx = nDx < 0 ? -nDx : nDx;
            int absDy = nDy < 0 ? -nDy : nDy;
            if (absDx < absDy * 2)
            {
                if (absDx * 2 <= absDy)
                {
                    if (nDx < 0)
                    {
                        nDx = -1;
                        if (nDy < -1) return nDx * 5 + 10;
                        if (1 >= nDy) return nDx * 5 + 0xc + nDy;
                        return nDx * 5 + 0xc + 2;
                    }
                    nDx &= 1;
                }
            }
            else if (nDy < 0) nDy = -1;
            else nDy &= 1;
            if (nDx < -1) nDx = -2;
            else if (1 < nDx) nDx = 2;
            if (nDy < -1) return nDx * 5 + 10;
            if (1 >= nDy) return nDx * 5 + 0xc + nDy;
            return nDx * 5 + 0xc + 2;
        }

        /// <summary>DRLGOUTROOM_AdvancePathDirection: cycle the current node's direction; when
        /// exhausted, backtrack up the parent chain. Returns the next node index, or -1 if the
        /// search has unwound past the root.</summary>
        private int Advance(int nodeIdx)
        {
            int idx = nodeIdx;
            if (Get(idx, 5) < 4)
            {
                Set(idx, 6, Get(idx, 6) + 1);
                int d = Delta(Get(idx, 6));
                Set(idx, 7, (d + Get(idx, 7)) & 3);
            }
            Set(idx, 5, Get(idx, 5) + 1);
            if (Get(idx, 5) != 3) return idx;
            while (idx != 0)
            {
                idx = Get(idx, 8) - 1; // parent
                Set(idx, 6, Get(idx, 6) + 1);
                int d = Delta(Get(idx, 6));
                Set(idx, 5, Get(idx, 5) + 1);
                Set(idx, 7, (d + Get(idx, 7)) & 3);
                if (Get(idx, 5) != 3) return idx;
            }
            return -1;
        }

        /// <summary>DRLGOUTROOM_ValidatePathStep: target reached / in-bounds / not a wall (0x200)
        /// / not already on the current node's ancestor chain.</summary>
        private bool Validate(int nextX, int nextY, int nodeIdx)
        {
            if (nextX == targetX && nextY == targetY) return true;
            var pData = Wild(pLevel);
            if (!(nextX >= 0 && nextX < pData->nGridCoordsWidth && nextY >= 0 && nextY < pData->nGridCoordsHeight)) return false;
            uint flags = (uint)DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nextX, nextY);
            if ((flags & 0x200) != 0) return false;
            int v = nodeIdx;
            while (true)
            {
                if (Get(v, 3) == nextX && Get(v, 4) == nextY) return false;
                int p = Get(v, 8);
                if (p == 0) return true;
                v = p - 1;
            }
        }

        /// <summary>DRLGOUTROOM_PathSearchStep: expand from the root until the target is reached or
        /// all directions within nMaxFCost are exhausted. Returns the found node index, or -1.</summary>
        public int SearchStep(int nMaxFCost)
        {
            int idx = 0; // root
            while (true)
            {
                if (Get(idx, 3) == targetX && Get(idx, 4) == targetY) return idx;
                int dir = Get(idx, 7);
                int nextX = Delta(dir + 0x14) + Get(idx, 3);
                int nextY = Delta(dir + 0x10) + Get(idx, 4);
                if (Validate(nextX, nextY, idx))
                {
                    int posX = Get(idx, 3);
                    int posY = Get(idx, 4);
                    int moveCost = (posX == nextX || posY == nextY) ? 2 : 3;
                    int parentG = Get(idx, 2);
                    int a = nextX - targetX < 0 ? -(nextX - targetX) : nextX - targetX;
                    int b = nextY - targetY < 0 ? -(nextY - targetY) : nextY - targetY;
                    int hMin = a;
                    if (b <= a)
                    {
                        hMin = b;
                        if (b < a) b = a;
                    }
                    hMin += b * 2;
                    int fCost = hMin + parentG + moveCost;
                    if (fCost <= nMaxFCost)
                    {
                        if (Get(idx, 9) == 0)
                        {
                            int slot = nPoolIndex;
                            if (slot == PATH_POOL_NODES) return -1;
                            for (int k = 0; k < 10; k++) Set(slot, k, 0);
                            nPoolIndex += 1;
                            Set(idx, 9, slot + 1);
                            Set(slot, 8, idx + 1);
                        }
                        int child = Get(idx, 9) - 1;
                        Set(child, 1, hMin);
                        Set(child, 0, fCost);
                        Set(child, 2, parentG + moveCost);
                        Set(child, 5, 0);
                        int d = PathDir(nextX, nextY, targetX, targetY);
                        int parentDirOff = Get(child, 8) - 1; // parent idx
                        int dirHalf = d / 2;
                        int group = (int)((uint)(Get(parentDirOff, 7) - dirHalf) & 3);
                        Set(child, 6, group * 4);
                        Set(child, 7, (Delta(group * 4) + dirHalf) & 3);
                        Set(child, 3, nextX);
                        Set(child, 4, nextY);
                        idx = child;
                        continue;
                    }
                }
                int adv = Advance(idx);
                if (adv < 0) return -1;
                idx = adv;
            }
        }
    }

    private static int Div8(int v) => (v + (int)((uint)(v >> 31) & 7)) >> 3;

    /// <summary>DRLGOUTROOM_FindPathBetweenExits   Drlg.cpp:7184 (1.14d 006817d0). Runs the road
    /// A* from aExitPoints2[idx] (start) to aExitPoints3[idx] (goal), growing the F-cost bound
    /// from hMin*3/2 by 5 each retry until hMin+0x23. On success, builds the vertex chain
    /// (goal→start) into pAdjacentVertices[idx]. For Manhattan distance ≤ 1 it short-circuits
    /// with a 2-vertex straight line.</summary>
    private static bool FindPathBetweenExits(D2DrlgLevelStrc* pLevel, int nExitIndex)
    {
        var pData = Wild(pLevel);
        void* nMem = pLevel->pDrlg->pMemoryPool;
        int idx = nExitIndex;
        int nWorldX = pLevel->sCoordinatesAndSize.WorldPosition.x;
        int nWorldY = pLevel->sCoordinatesAndSize.WorldPosition.y;

        int nStartGridX = Div8(Inline.At(ref pData->aExitPoints2, idx)->nWorldX - nWorldX);
        int nStartGridY = Div8(Inline.At(ref pData->aExitPoints2, idx)->nWorldY - nWorldY);
        int nTargetX = Div8(Inline.At(ref pData->aExitPoints3, idx)->nWorldX - nWorldX);
        int nTargetGridY = Div8(Inline.At(ref pData->aExitPoints3, idx)->nWorldY - nWorldY);

        int dxRaw = nStartGridX - nTargetX;
        int dyRaw = nStartGridY - nTargetGridY;
        int adx = dxRaw < 0 ? -dxRaw : dxRaw;
        int ady = dyRaw < 0 ? -dyRaw : dyRaw;

        if (ady + adx <= 1)
        {
            var pv = DrlgVer.AllocVertex(nMem, 0);
            pData->pAdjacentVertices[idx] = (nint)pv;
            pv->nPosX = nStartGridX;
            pv->nPosY = nStartGridY;
            var pv2 = DrlgVer.AllocVertex(nMem, 0);
            pv->pNext = pv2;
            pv2->nPosX = nTargetX;
            pv2->nPosY = nTargetGridY;
            return true;
        }

        // init node + cost bounds
        int hMin = adx < ady ? adx : ady; // min(|dx|,|dy|)
        int hMax = adx < ady ? ady : adx; // max(|dx|,|dy|)
        hMin += hMax * 2;
        int byInitDir = (int)((uint)(PathCtx.PathDir(nStartGridX, nStartGridY, nTargetX, nTargetGridY) / 2) & 3);

        var ctx = new PathCtx { pLevel = pLevel, targetX = nTargetX, targetY = nTargetGridY };

        int nMaxFCost = hMin / 2 + hMin; // hMin * 3/2
        int nMaxFCostLimit = nMaxFCost + 0x23;
        while (true)
        {
            ctx.nPoolIndex = 1;
            // build init/root node 0
            ctx.Set(0, 0, hMin); // FCost = HMin
            ctx.Set(0, 1, hMin); // HMin
            ctx.Set(0, 2, 0); // GCost
            ctx.Set(0, 3, nStartGridX);
            ctx.Set(0, 4, nStartGridY);
            ctx.Set(0, 5, -1); // stepCount
            ctx.Set(0, 6, 0); // deltaIdx
            ctx.Set(0, 7, byInitDir);
            ctx.Set(0, 8, 0); // parent = null
            ctx.Set(0, 9, 0); // child = null

            int found = ctx.SearchStep(nMaxFCost);
            nMaxFCost += 5;
            if (ctx.nPoolIndex > 899) return false;

            if (found >= 0)
            {
                int node = found;
                D2DrlgVertexStrc* pPrev = null;
                while (true)
                {
                    var pv = DrlgVer.AllocVertex(nMem, 0);
                    if (pData->pAdjacentVertices[idx] == 0) pData->pAdjacentVertices[idx] = (nint)pv;
                    else pPrev->pNext = pv;
                    pv->nPosX = ctx.Get(node, 3);
                    pv->nPosY = ctx.Get(node, 4);
                    pPrev = pv;
                    int p = ctx.Get(node, 8);
                    if (p == 0) break;
                    node = p - 1;
                }
                return true;
            }
            if (nMaxFCost >= nMaxFCostLimit) break;
        }
        return false;
    }

    /// <summary>Drlg_CallTable_2   Drlg.cpp:5504 (1.14d). Walks a vertex linked list; for each
    /// in-bounds (x,y) sets the given grid flag.</summary>
    private static void Drlg_CallTable_2(D2DrlgGridStrc* pGrid, D2DrlgVertexStrc* pHead, int nFlag)
    {
        var v = pHead;
        while (v != null)
        {
            int x = v->nPosX;
            int y = v->nPosY;
            v = v->pNext;
            if (x >= 0 && x < pGrid->nWidth && y >= 0 && y < pGrid->nHeight) DrlgGrid.AlterGridFlag(pGrid, x, y, nFlag, 0);
        }
    }

    // Direction tables (static in the engine, fixed values from 1.14d binary)
    private static readonly int[] gnDrlgLevelSeed = { 1, 0, -1, 0 }; // X jitter direction
    private static readonly int[] gnDrlgLevelVersion = { 0, 1, 0, -1 }; // Y jitter direction

    /// <summary>DRLGOUTROOM_BuildVertexPathsWithJitter   Drlg.cpp:6873 (1.14d 00681240). ALWAYS
    /// draws 1 seed on entry regardless of whether a path exists. If a path exists (pVertex !=
    /// null), adjusts vertex positions; draws 2 more seeds per interior vertex.</summary>
    private static void BuildVertexPathsWithJitter(D2DrlgLevelStrc* pLevel, int nPathIdx)
    {
        var pData = Wild(pLevel);
        // v0 = the A*'s first vertex, captured BEFORE any prepend (engine keeps this local
        // pointer through the prepend; the prepended head is NOT this vertex).
        var v0 = (D2DrlgVertexStrc*)pData->pAdjacentVertices[nPathIdx];

        // ALWAYS draw 1 seed on entry
        var DVar2 = DrlgRng.SeedNext(pLevel->sSeed);
        uint uVar6 = (uint)DVar2.nSeedLow & 3;
        pLevel->sSeed = DVar2;

        // If no path was found, return after the 1 draw
        if (v0 == null) return;

        // Prepend a target vertex if exit type is not "center". The prepended head keeps
        // aExitPoints4 coords; v0 (below) becomes aExitPoints3.
        if (Inline.At(ref pData->aExitPoints4, nPathIdx)->eType != 4)
        {
            var pHeadVertex = DrlgVer.AllocVertex(pLevel->pDrlg->pMemoryPool, 0);
            pHeadVertex->nPosX = Inline.At(ref pData->aExitPoints4, nPathIdx)->nWorldX;
            pHeadVertex->nPosY = Inline.At(ref pData->aExitPoints4, nPathIdx)->nWorldY;
            pHeadVertex->pNext = v0;
            pData->pAdjacentVertices[nPathIdx] = (nint)pHeadVertex;
        }

        // v0 (NOT the prepended head) gets aExitPoints3 coords; jitter starts at v1.
        v0->nPosX = Inline.At(ref pData->aExitPoints3, nPathIdx)->nWorldX;
        v0->nPosY = Inline.At(ref pData->aExitPoints3, nPathIdx)->nWorldY;

        // Walk interior vertices (those with a next), draw 2 seeds each
        var pCur = v0->pNext;
        if (pCur == null) return;

        while (pCur->pNext != null)
        {
            var DVar3 = DrlgRng.SeedNext(pLevel->sSeed);
            pLevel->sSeed = DVar3;
            int iDx = gnDrlgLevelSeed[uVar6];
            var DVar5 = DrlgRng.SeedNext(pLevel->sSeed);
            pLevel->sSeed = DVar5;
            int iDy = gnDrlgLevelVersion[uVar6];
            uVar6 = (uVar6 + 1) & 3;

            int gx = pCur->nPosX * 8;
            int gy = pCur->nPosY * 8;
            pCur->nPosX = pLevel->sCoordinatesAndSize.WorldPosition.x + gx + 3 + ((DVar3.nSeedLow & 1) + 2) * iDx;
            pCur->nPosY = pLevel->sCoordinatesAndSize.WorldPosition.y + gy + 3 + ((DVar5.nSeedLow & 1) + 2) * iDy;
            pCur = pCur->pNext;
            if (pCur == null) return;
        }

        // Set last vertex to exit point 2 coords and append exit point 1 as terminal
        pCur->nPosX = Inline.At(ref pData->aExitPoints2, nPathIdx)->nWorldX;
        pCur->nPosY = Inline.At(ref pData->aExitPoints2, nPathIdx)->nWorldY;
        var pTerminal = DrlgVer.AllocVertex(pLevel->pDrlg->pMemoryPool, 0);
        pTerminal->nPosX = Inline.At(ref pData->aExitPoints1, nPathIdx)->nWorldX;
        pTerminal->nPosY = Inline.At(ref pData->aExitPoints1, nPathIdx)->nWorldY;
        pCur->pNext = pTerminal;
    }

    /// <summary>DRLGOUTROOM_LinkOutdoorRoomExits   Drlg.cpp:6942 (1.14d 00681420). Wires inter-level
    /// exits: build the exit array + target positions, then for each exit run the road A*; on
    /// success mark the path cells (sGridOutdoor 0x80) and jitter the vertex chain.</summary>
    public static void LinkOutdoorRoomExits(D2DrlgLevelStrc* pLevel)
    {
        var pData = Wild(pLevel);
        BuildExitPointArray(pLevel);
        ComputeExitTargetPositions(pLevel);
        for (int nIndex = 0; nIndex < pData->nExitCount; nIndex++)
        {
            if (FindPathBetweenExits(pLevel, nIndex))
            {
                Drlg_CallTable_2(&pData->sGridOutdoor, (D2DrlgVertexStrc*)pData->pAdjacentVertices[nIndex], 0x80);
                BuildVertexPathsWithJitter(pLevel, nIndex);
            }
        }
    }
}

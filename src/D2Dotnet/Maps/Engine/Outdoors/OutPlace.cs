// Port of libd2 packages/drlg/src/drlg/outdoors/OutPlace.zig (MIT, (c) 2026 jaenster).
//
// Mechanical transform of the OUTDOOR preset/border PLACEMENT core from
// recon/closure/Outdoors.cpp (the spawn machinery shared by every act's
// InitAct{1..5}OutdoorLevel tree) + recon/closure/OutPlace.cpp.
//
// Faithful by construction:
//   * grid field access by NAME (recon's `(int)pDrlgLevelData + 0x04/0x2c` raw byte reaches
//     -> the named D2DrlgLevelDataWildernessLevel.sGrid{Preset,Outdoor} fields).
//   * the pointer-as-integer coordinate idiom in TestOutdoorLevelPreset
//     (`(int)nX0->apTiles - 0x68` == nX; `&nXIter->pOrth + 1` == nXIter+1) -> plain i32.
//   * RNG -> the VERIFIED RANDOM_RandomNumberSelector primitive. The recon inlines that
//     selector for the Fisher-Yates index draws; Ghidra rendered the non-pow2 branch
//     inconsistently — full64 % n at Outdoors.cpp:776 but low-word % n at :794 / :653 / :667.
//     The selector is the ground truth, so all inline draws route through it.
//   * AllocPresetFileTracker RETURNS the file index (a lost return value in the recon: the
//     function's last statement computes (dwData2+1)%dwData1, the index the caller consumes).

namespace D2Dotnet.Maps.Engine.Outdoors;

internal static unsafe class OutPlace
{
    // DrlgGrid::AlterGridFlag eOperation indices: 0=Or 1=And 2=Xor 3=Overwrite
    // 4=OverwriteIfZero 5=AndNegated
    private const int OP_OR = 0;
    private const int OP_OVERWRITE = 3;
    private const int OP_ANDNEG = 5;

    /// <summary>Toward-zero division by 8 (recon `(v + (v>>31 &amp; 7)) >> 3`).</summary>
    internal static int Div8(int v) => (v + (int)((uint)v >> 31) * 7) >> 3;

    internal static D2DrlgLevelDataWildernessLevel* Wild(D2DrlgLevelStrc* pLevel) => (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;

    // Preset::GetSizeX / GetSizeY   Preset.cpp:1261/1268
    private static int PresetSizeX(int nPresetId) => DrlgTables.LvlPrestGetLine(nPresetId)->SizeX;
    private static int PresetSizeY(int nPresetId) => DrlgTables.LvlPrestGetLine(nPresetId)->SizeY;

    /// <summary>DRLGOUTDOOR_GetPresetCoordFromGrid   Outdoors.cpp:435</summary>
    public static int GetPresetCoordFromGrid(D2DrlgLevelStrc* pLevel, int nX, int nY)
    {
        var pData = Wild(pLevel);
        int uFlags = DrlgGrid.GetGridFlags(&pData->sGridOutdoor, nX, nY);
        if ((uFlags & 0x200) == 0) return 0;
        return DrlgGrid.GetGridFlags(&pData->sGridPreset, nX, nY);
    }

    /// <summary>AlterAdjacentPresetGridCells   Outdoors.cpp:448</summary>
    public static void AlterAdjacentPresetGridCells(D2DrlgLevelStrc* pLevel, int nX, int nY)
    {
        var pData = Wild(pLevel);
        DrlgGrid.AlterGridFlag(&pData->sGridPreset, nX, nY, 0, OP_OVERWRITE);
        DrlgGrid.AlterGridFlag(&pData->sGridOutdoor, nX, nY, 0, OP_OVERWRITE);
    }

    /// <summary>SetBlankGridCell   Outdoors.cpp:456</summary>
    public static void SetBlankGridCell(D2DrlgLevelStrc* pLevel, int nX, int nY)
    {
        var pData = Wild(pLevel);
        DrlgGrid.AlterGridFlag(&pData->sGridPreset, nX, nY, 0, OP_OVERWRITE);
        DrlgGrid.AlterGridFlag(&pData->sGridOutdoor, nX, nY, 0x100, OP_OVERWRITE);
    }

    /// <summary>DRLGOUTDOOR_IsGridCellPresetFree   Outdoors.cpp:468</summary>
    public static int IsGridCellPresetFree(D2DrlgLevelStrc* pLevel, int nX, int nY)
    {
        uint uFlags = (uint)DrlgGrid.GetGridFlags(&Wild(pLevel)->sGridOutdoor, nX, nY);
        return (int)(~(uFlags >> 10) & 1);
    }

    // gaAct5BorderPresetTable   Game.exe .rdata 0x006f2038 (10 rows, 5 i32)
    // Each row: { eType, nMinIdx, nMaxIdx, nPresetNormal, nPresetHarrogath }. Looked up by
    // DRLGOUTDOOR_GetSecondaryBorderPresetFromLvlSub (0067e000).
    private static readonly int[,] gaAct5BorderPresetTable =
    {
        { 0x31, 0x01, 0x10, 0x393, 0x3db },
        { 0x31, 0x1f, 0x2e, 0x393, 0x3db },
        { 0x30, 0x01, 0x01, 0x373, 0x3bf },
        { 0x30, 0x02, 0x03, 0x371, 0x3bd },
        { 0x30, 0x04, 0x04, 0x374, 0x3c0 },
        { 0x30, 0x05, 0x05, 0x37f, 0x3cb },
        { 0x30, 0x06, 0x07, 0x37d, 0x3c9 },
        { 0x30, 0x08, 0x08, 0x380, 0x3cc },
        { 0x30, 0x1e, 0x1e, 0x000, 0x000 },
        { 0x30, 0x1f, 0x1f, -5, -5 },
    };

    /// <summary>DRLGOUTDOOR_GetSecondaryBorderPresetFromLvlSub   1.14d 0067e000 (__fastcall).
    /// Maps an Act-5 secondary-border (borderType, presetIndex) to a LvlPrest id via
    /// gaAct5BorderPresetTable, choosing the Harrogath (0x75) column when applicable.</summary>
    public static int GetSecondaryBorderPresetFromLvlSub(D2DrlgLevelStrc* pLevel, int nBorderType, int nPresetIndex)
    {
        if (nBorderType == 0x30 || nBorderType == 0x31)
        {
            for (int r = 0; r < 10; r++)
            {
                if (nBorderType == gaAct5BorderPresetTable[r, 0] && gaAct5BorderPresetTable[r, 1] <= nPresetIndex && nPresetIndex <= gaAct5BorderPresetTable[r, 2])
                {
                    if (pLevel->eD2LevelId != (int)AreaId.FrozenTundra)
                        return (gaAct5BorderPresetTable[r, 3] - gaAct5BorderPresetTable[r, 1]) + nPresetIndex;
                    return (gaAct5BorderPresetTable[r, 4] - gaAct5BorderPresetTable[r, 1]) + nPresetIndex;
                }
            }
        }
        // recon: Fog::ErrorManager::ERROR_UnrecoverableInternalError_Halt (code 0x1a5)
        throw new DrlgException("GetSecondaryBorderPresetFromLvlSub: no matching border-preset row");
    }

    /// <summary>DRLGOUTDOOR_ValidateSecondaryBorderCell   1.14d 0067e080 (__fastcall).</summary>
    public static int ValidateSecondaryBorderCell(D2DrlgLevelStrc* pLevel, int nX, int nY, int nExpectedPreset, int nUnused, uint nGridFlags)
    {
        int nResolvedPreset = GetSecondaryBorderPresetFromLvlSub(pLevel, (int)((nGridFlags >> 0x14) & 0x3f), (int)((nGridFlags >> 8) & 0xff));
        if (nResolvedPreset == -5) return 1;
        if (nResolvedPreset != nExpectedPreset) return 0;
        return IsGridCellPresetFree(pLevel, nX, nY) != 0 ? 1 : 0;
    }

    /// <summary>DRLGOUTDOOR_IsGridCellEmpty   Outdoors.cpp:475</summary>
    public static int IsGridCellEmpty(D2DrlgLevelStrc* pLevel, int nX, int nY)
    {
        int uFlags = DrlgGrid.GetGridFlags(&Wild(pLevel)->sGridOutdoor, nX, nY);
        return (uFlags & 0x1b81) == 0 ? 1 : 0;
    }

    /// <summary>TestOutdoorLevelPreset   Outdoors.cpp:482. Rectangle-fits a preset at (nX,nY):
    /// all covered sGridOutdoor cells must be in-grid and free of the 0x1b81 occupancy mask.
    /// nFlags/nOffset grow the test rect on selected edges (border-overhang allowance).</summary>
    public static int TestOutdoorLevelPreset(D2DrlgLevelStrc* pLevel, int nX, int nYIn, int nLevelPrestId, int nOffset, int nFlags)
    {
        var pData = Wild(pLevel);
        int nY = nYIn;
        int xExtent = 1; // recon nSizeY = ceil(SizeX/8)
        int yExtent = 1; // recon nSizeX = ceil(SizeY/8)
        if (nLevelPrestId != 0)
        {
            var pPrest = DrlgTables.LvlPrestGetLine(nLevelPrestId);
            xExtent = Div8(pPrest->SizeX);
            yExtent = Div8(pPrest->SizeY);
        }

        int xStart = nX;
        if (nOffset != 0)
        {
            if ((nFlags & 1) != 0)
            {
                nY -= nOffset;
                yExtent += nOffset;
            }
            if ((nFlags & 2) != 0) xExtent += nOffset;
            if ((nFlags & 4) != 0) yExtent += nOffset;
            if ((nFlags & 8) != 0)
            {
                xStart = nX - nOffset;
                xExtent += nOffset;
            }
        }

        int yEnd = nY + yExtent;
        if (nY >= yEnd) return 1;
        int xEnd = xStart + xExtent;
        for (; nY < yEnd; nY++)
        {
            for (int x = xStart; x < xEnd; x++)
            {
                if (DrlgGrid.IsPointInsideGridArea(&pData->sGridOutdoor, x, nY) == 0) return 0;
                if ((DrlgGrid.GetGridFlags(&pData->sGridOutdoor, x, nY) & 0x1b81) != 0) return 0;
            }
        }
        return 1;
    }

    /// <summary>DRLGOUTDOOR_AllocPresetFileTracker   Outdoors.cpp:550. Per-(preset)
    /// round-robin file index across one level. Returns the index the caller uses.</summary>
    public static int AllocPresetFileTracker(int nLevelPrestId, D2DrlgLevelStrc* pLevel)
    {
        void* pMemory = pLevel->pDrlg->pMemoryPool;
        var pNode = pLevel->pLevelLinkNodeFirst;
        for (; pNode != null; pNode = pNode->pNext)
            if (pNode->dwData0 == nLevelPrestId) break;

        if (pNode == null)
        {
            var pLvlPrest = DrlgTables.LvlPrestGetLine(nLevelPrestId);
            // 0x10 in the 32-bit engine; sizeof in the 64-bit transform (8-byte pNext).
            var node = (D2DrlgLevelLinkNodeStrc*)Pool.AllocServerMemory(pMemory, (nuint)sizeof(D2DrlgLevelLinkNodeStrc));
            node->dwData0 = pLvlPrest->Def;
            node->dwData1 = pLvlPrest->Files;
            node->dwData2 = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)pLvlPrest->Files);
            node->pNext = pLevel->pLevelLinkNodeFirst;
            pLevel->pLevelLinkNodeFirst = node;
            pNode = node;
        }
        pNode->dwData2 = (pNode->dwData2 + 1) % pNode->dwData1;
        return pNode->dwData2;
    }

    /// <summary>SpawnOutdoorLevelPresetEx   Outdoors.cpp:583. Stamps a preset's grid cells:
    /// marks the covered sGridOutdoor cells as preset (0x200 | fileIndex&lt;&lt;16) and writes
    /// nPresetId into the anchor sGridPreset cell.</summary>
    public static void SpawnOutdoorLevelPresetEx(D2DrlgLevelStrc* pLevel, int nX, int nY, int nPresetId, int nFileIndexIn, int bFlag)
    {
        var pData = Wild(pLevel);
        int nPresetSizeX = PresetSizeX(nPresetId);
        int nPresetSizeY = PresetSizeY(nPresetId);
        int nFileIndex = nFileIndexIn;
        if (nFileIndex == -1) nFileIndex = AllocPresetFileTracker(nPresetId, pLevel);

        int nGridEndY = Div8(nPresetSizeY) + nY;
        if (nY < nGridEndY)
        {
            int nGridEndX = Div8(nPresetSizeX) + nX;
            for (int y = nY; y < nGridEndY; y++)
            {
                for (int x = nX; x < nGridEndX; x++)
                {
                    DrlgGrid.AlterGridFlag(&pData->sGridOutdoor, x, y, 0xf0000, OP_ANDNEG);
                    DrlgGrid.AlterGridFlag(&pData->sGridOutdoor, x, y, (nFileIndex << 0x10) | 0x200, OP_OR);
                    if (bFlag != 0 && ((3 < nPresetId && nPresetId < 0x10) || unchecked((uint)(nPresetId - 0x16c)) < 0xc))
                        DrlgGrid.AlterGridFlag(&pData->sGridOutdoor, x, y, 1, OP_OR);
                    DrlgGrid.AlterGridFlag(&pData->sGridPreset, x, y, 0, OP_OVERWRITE);
                }
            }
        }
        DrlgGrid.AlterGridFlag(&pData->sGridPreset, nX, nY, nPresetId, OP_OVERWRITE);
    }

    /// <summary>SpawnPresetFarAway   Outdoors.cpp:626. Scans the interior grid for the
    /// placeable cell maximally distant (weighted metric) from pDrlgCoord's centre, then stamps
    /// the preset there.</summary>
    public static int SpawnPresetFarAway(D2DrlgLevelStrc* pLevel, D2DrlgCoordStrc* pDrlgCoord, int nLvlPrestId, int nRand, int nOffset, int nFlags)
    {
        var pData = Wild(pLevel);
        int nWorldX = pLevel->sCoordinatesAndSize.WorldPosition.x;
        int nWorldY = pLevel->sCoordinatesAndSize.WorldPosition.y;
        // recon's nGridWidth = nGridCoordsHeight, nGridHeight = nGridCoordsWidth (swapped)
        int nRangeX = pData->nGridCoordsWidth - 2; // used with X loop
        int nRangeY = pData->nGridCoordsHeight - 2; // used with Y loop

        int nRandOffsetY = nRangeX < 1 ? 0 : (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nRangeX);
        int nRandOffsetX = nRangeY < 1 ? 0 : (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nRangeY);

        int nCoordWidth = pDrlgCoord->nWidth;
        int nCoordPosX = pDrlgCoord->nPosX;
        int nCoordHeight = pDrlgCoord->nHeight;
        int nCoordPosY = pDrlgCoord->nPosY;
        int nBestDist = 0;
        int nBestX = -1;
        int nBestY = -1;
        if (-1 >= nRangeY) return 0;

        for (int nLoopY = 0; nLoopY <= nRangeY; nLoopY++)
        {
            if (-1 < nRangeX)
            {
                int nY = (nLoopY + nRandOffsetX) % nRangeY + 1;
                for (int nLoopX = 0; nLoopX <= nRangeX; nLoopX++)
                {
                    int nX = (nRandOffsetY + nLoopX) % nRangeX + 1;
                    if (TestOutdoorLevelPreset(pLevel, nX, nY, nLvlPrestId, nOffset, nFlags) != 0)
                    {
                        int nDistX = nX * 8 - (nCoordWidth / 2 + nCoordPosX) + 4 + nWorldX;
                        if (nDistX < 0) nDistX = -nDistX;
                        int nDistY = nY * 8 - (nCoordHeight / 2 + nCoordPosY) + 4 + nWorldY;
                        if (nDistY < 0) nDistY = -nDistY;
                        if (nDistY < nDistX) nDistY += nDistX * 2;
                        else nDistY = nDistX + nDistY * 2;
                        if (nBestDist < nDistY / 2)
                        {
                            nBestY = nY;
                            nBestX = nX;
                            nBestDist = nDistY / 2;
                        }
                    }
                }
            }
        }

        if (!(nBestX != -1 && nBestY != -1)) return 0;
        SpawnOutdoorLevelPresetEx(pLevel, nBestX, nBestY, nLvlPrestId, nRand, 0);
        return 1;
    }

    /// <summary>SpawnOutdoorLevelPreset   Outdoors.cpp:730. Fisher-Yates shuffles all interior
    /// cells off pLevel->sSeed, then stamps the preset at the first shuffled cell that fits.</summary>
    public static int SpawnOutdoorLevelPreset(D2DrlgLevelStrc* pLevel, int nLevelPrestId, int nRand, int nOffset, int nFlags)
    {
        var pData = Wild(pLevel);
        int nGridWidth = pData->nGridCoordsWidth - 2;
        int nTotalCells = (pData->nGridCoordsHeight - 2) * nGridWidth;
        if (nTotalCells == 0) return 0;

        Span<int> aCellX = stackalloc int[513];
        Span<int> aCellY = stackalloc int[513];
        for (int i = 0; i < nTotalCells; i++)
        {
            aCellX[i] = i % nGridWidth;
            aCellY[i] = i / nGridWidth;
        }

        // Fisher-Yates: two index draws per step, both via the verified selector.
        uint nShuffleCount = (uint)nTotalCells;
        while (nShuffleCount != 0)
        {
            int a = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nTotalCells);
            int b = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nTotalCells);
            nShuffleCount -= 1;
            (aCellX[a], aCellX[b]) = (aCellX[b], aCellX[a]);
            (aCellY[a], aCellY[b]) = (aCellY[b], aCellY[a]);
        }

        for (int i = 0; i < nTotalCells; i++)
        {
            int cx = aCellX[i];
            int cy = aCellY[i] + 1;
            if (TestOutdoorLevelPreset(pLevel, cx + 1, cy, nLevelPrestId, nOffset, nFlags) != 0)
            {
                SpawnOutdoorLevelPresetEx(pLevel, cx + 1, cy, nLevelPrestId, nRand, 0);
                return 1;
            }
        }
        return 0;
    }
}

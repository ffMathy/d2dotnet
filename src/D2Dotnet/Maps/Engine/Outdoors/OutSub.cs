// Port of libd2 packages/drlg/src/drlg/outdoors/OutSub.zig and drlg/TileSub.zig
// (MIT, (c) 2026 jaenster).
//
// Faithful transform of the DRLG outdoor SUBSTITUTION-GROUP placement (recon/closure/Drlg.cpp
// DRLGOUTDOOR_ApplySubstitutionGroup / PlaceRandomBorderSubTiles, Outdoors.cpp
// ValidateSubTilePlacement / ApplySubTileToGrid), driven by TileSub::AddSecondaryBorder once
// InitializeDrlgFile has loaded the LvlSub DrlgFile.
//
// Recovered placement signatures (Ghidra 1.14d, set_custom_signature):
// The three inner fns mistype their LvlSub param as D2DrlgLevelDataWildernessLevel* because
// [param+0x4c] reads as gridCellSize — but in D2LvlSubTxt 0x4c=GridSize, 0x50=pDrlgFile,
// 0xa4=pWallGrid[0], 0xf4=pFloorGrid, and every body access lands on those LvlSub fields, so
// the param is pLvlSub (D2LvlSubTxt*). The callbacks are __fastcall (pLevel in ECX, first index
// in EDX, remaining args on stack); the register args were dropped by the decompiler and
// recovered from the disassembly.
//   PlaceRandomBorderSubTiles  @0066f690  __stdcall RET 0xc
//   ValidateSubTilePlacement   @0066f3b0  EDI + 4 stdcall stack, RET 0x10
//   ApplySubTileToGrid         @0066f520  __stdcall RET 0x18

namespace D2Dotnet.Maps.Engine.Outdoors;

internal static unsafe class OutSub
{
    /// <summary>DRLGOUTDOOR_ApplySubstitutionGroup (Drlg.cpp:1983, 1.14d 0066f990). Consumes ONE
    /// seed draw off pLevel->sSeed to pick the start substitution group when BordType==0, then
    /// walks the groups round-robin, placing each via PlaceRandomBorderSubTiles and stopping after
    /// the first success for BordType==0.</summary>
    public static void ApplySubstitutionGroup(D2UnkOutdoorStrc* pUnkOutdoor, D2LvlSubTxt* pLvlSub)
    {
        var pFile = pLvlSub->pDrlgFile;
        if (pFile == null) return;
        int nModulo = pFile->nSubstGroups;
        if (nModulo == 0) return;
        if (pUnkOutdoor->nPrevBorderPresetId == -1) pUnkOutdoor->nPrevBorderPresetId = 0x3e;
        int nRandStartGroup = 0;
        if (pLvlSub->BordType == 0) nRandStartGroup = (int)DrlgRng.RandomNumberSelector(&pUnkOutdoor->pLevel->sSeed, (uint)nModulo);
        if (nModulo <= 0) return;

        var groups = pFile->pSubstGroups;
        int nGroupIter = 0;
        while (true)
        {
            int idx = Mod(nRandStartGroup + nGroupIter, nModulo);
            uint bPlaced = PlaceRandomBorderSubTiles(pUnkOutdoor, pLvlSub, &groups[idx]);
            if (bPlaced != 0 && pLvlSub->BordType == 0) return;
            nGroupIter += 1;
            if (!(nGroupIter < nModulo)) break;
        }
    }

    private static int Mod(int a, int b)
    {
        int r = a % b;
        return (r != 0 && ((r < 0) != (b < 0))) ? r + b : r;
    }

    /// <summary>DRLGOUTDOOR_ValidateSubTilePlacement (Outdoors.cpp:74, 1.14d 0066f3b0). For each cell
    /// of the substitution-group box, reads the LvlSub wall/floor grids and the outdoor
    /// primary-floor grid, then runs the border-validation callback. Consumes NO seed.</summary>
    private static bool ValidateSubTilePlacement(D2UnkOutdoorStrc* pOutdoorCtx, int nBaseGridX, int nBaseGridY, D2DrlgCoordStrc* pSubRect, D2LvlSubTxt* pSubLvl)
    {
        var pLevel = pOutdoorCtx->pLevel;
        int nGridStride = pSubLvl->GridSize;
        for (int dy = 0; dy < pSubRect->nHeight; dy++)
        {
            for (int dx = 0; dx < pSubRect->nWidth; dx++)
            {
                uint floorFlag = pSubLvl->pDrlgFile->nFloorLayers == 0 ? 0 : (uint)DrlgGrid.GetGridFlags(&pSubLvl->pFloorGrid, pSubRect->nPosX + dx, pSubRect->nPosY + dy);
                uint wallFlag = pSubLvl->pDrlgFile->nWallLayers == 0 ? 0 : (uint)DrlgGrid.GetGridFlags(Inline.At(ref pSubLvl->pWallGrid, 0), pSubRect->nPosX + dx, pSubRect->nPosY + dy);
                int cellY = pSubLvl->GridSize * dy + (nBaseGridY - nBaseGridY % nGridStride);
                int cellX = pSubLvl->GridSize * dx + (nBaseGridX - nBaseGridX % nGridStride);
                int nSubTileFlags = DrlgGrid.GetGridFlags(pOutdoorCtx->pPrimaryFloorGrid, cellX, cellY);

                if (pOutdoorCtx->pfnValidateBorderCell != null)
                {
                    var f = (delegate*<D2DrlgLevelStrc*, int, int, int, int, uint, int>)pOutdoorCtx->pfnValidateBorderCell;
                    if (f(pLevel, cellX, cellY, nSubTileFlags, (int)floorFlag, wallFlag) == 0) return false;
                }
                else if ((wallFlag & 1) != 0)
                {
                    int nIdx = (int)((wallFlag >> 8) & 0xff) - 1;
                    if (nIdx != pOutdoorCtx->nPrevBorderPresetId && pOutdoorCtx->nLevelPrestId + nIdx != nSubTileFlags) return false;
                    var f = (delegate*<D2DrlgLevelStrc*, int, int, int>)pOutdoorCtx->pfnIsPresetFree;
                    if (f(pLevel, cellX, cellY) == 0) return false;
                }
                else if ((floorFlag & 2) != 0)
                {
                    var f = (delegate*<D2DrlgLevelStrc*, int, int, int, int, int, int>)pOutdoorCtx->pfnTestPreset;
                    if (f(pLevel, cellX, cellY, 0, 0, 0) == 0) return false;
                }
            }
        }
        return true;
    }

    /// <summary>DRLGOUTDOOR_ApplySubTileToGrid (Outdoors.cpp:147, 1.14d 0066f520). Stamps the
    /// substitution-group cells into the outdoor grids via the border callbacks (wall -> spawn
    /// border preset, floor 0x2 -> alter adjacent, else -> blank). Consumes NO seed. nOffset
    /// shifts the box X by the chosen variant.</summary>
    private static void ApplySubTileToGrid(int nBaseX, int nBaseY, D2UnkOutdoorStrc* pOutdoorCtx, D2DrlgCoordStrc* pCoords, D2LvlSubTxt* pLvlSub, int nOffset)
    {
        var pLevel = pOutdoorCtx->pLevel;
        int nWorldX = pCoords->nPosX + nOffset;
        int nWorldY = pCoords->nPosY;
        int nGridStride = pLvlSub->GridSize;
        for (int nRow = 0; nRow < pCoords->nHeight; nRow++)
        {
            for (int nCol = 0; nCol < pCoords->nWidth; nCol++)
            {
                uint uWallFlags = pLvlSub->pDrlgFile->nWallLayers == 0 ? 0 : (uint)DrlgGrid.GetGridFlags(Inline.At(ref pLvlSub->pWallGrid, 0), nCol + nWorldX, nRow + nWorldY);
                uint uFloorFlags = pLvlSub->pDrlgFile->nFloorLayers == 0 ? 0 : (uint)DrlgGrid.GetGridFlags(&pLvlSub->pFloorGrid, nCol + nWorldX, nRow + nWorldY);
                int cellY = pLvlSub->GridSize * nRow + (nBaseY - nBaseY % nGridStride);
                int cellX = pLvlSub->GridSize * nCol + (nBaseX - nBaseX % nGridStride);

                if ((uWallFlags & 1) == 0)
                {
                    var f = (delegate*<D2DrlgLevelStrc*, int, int, void>)((uFloorFlags & 2) == 0 ? pOutdoorCtx->pfnSetBlankCell : pOutdoorCtx->pfnAlterAdjacentCells);
                    f(pLevel, cellX, cellY);
                }
                else
                {
                    uint uWall8 = (uWallFlags >> 8) & 0xff;
                    int nWallMainIndex = (int)uWall8 - 1;
                    int nPresetId;
                    if (pOutdoorCtx->pfnGetBorderPreset != null)
                    {
                        var g = (delegate*<D2DrlgLevelStrc*, int, int, int>)pOutdoorCtx->pfnGetBorderPreset;
                        nPresetId = g(pLevel, (int)((uWallFlags >> 0x14) & 0x3f), (int)uWall8);
                    }
                    else nPresetId = pOutdoorCtx->nLevelPrestId + nWallMainIndex;
                    if (nPresetId != -5 && nWallMainIndex != pOutdoorCtx->nPrevBorderPresetId)
                    {
                        var f = (delegate*<D2DrlgLevelStrc*, int, int, int, int, int, void>)pOutdoorCtx->pfnSpawnPreset;
                        f(pLevel, cellX, cellY, nPresetId, 0, 1);
                    }
                }
            }
        }
    }

    /// <summary>DRLGOUTDOOR_PlaceRandomBorderSubTiles (Drlg.cpp:1848, 1.14d 0066f690). Builds the
    /// candidate-position list for the substitution box, Fisher-Yates-shuffles it (two seed draws
    /// per step off pLevel->sSeed), then places at the first position that validates — rolling
    /// one variant draw on success. The recon `(int)(intptr_t)pRect * h` is a wrong-int*-type
    /// artifact: the real op (0066f6f1 area) is pRect[2]*GridSize / pRect[3]*GridSize.</summary>
    public static uint PlaceRandomBorderSubTiles(D2UnkOutdoorStrc* pUnkOutdoor, D2LvlSubTxt* pLvlSub, D2DrlgSubstGroupStrc* pSubstGroup)
    {
        var pRect = &pSubstGroup->tBox;
        var pLevel = pUnkOutdoor->pLevel;
        // recon Drlg.cpp:1862 `isNotBaseLvlSub = !nLvlSubId` (libd2 keys it on != 1; kept).
        bool isNotBaseLvlSub = pUnkOutdoor->nLvlSubId != 1;
        int eLvl = pLevel->eD2LevelId;
        bool bMidActLevel = eLvl >= 2 && eLvl <= 7;

        // posIterInit: base LvlSub on an interior tile (dwFlags & 0xc) shrinks the run.
        int posIterInit = 1;
        if (!isNotBaseLvlSub)
        {
            var pWild = (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;
            if ((pWild->dwFlags & 0xc) != 0) posIterInit = -1;
        }

        int posW = (pUnkOutdoor->pCoordsGrid->nWidth - pRect->nWidth * pLvlSub->GridSize) + posIterInit;
        int posH = (pUnkOutdoor->pCoordsGrid->nHeight - pRect->nHeight * pLvlSub->GridSize) + 1;
        int totalPositions = posH * posW;
        if (totalPositions == 0) return 0;

        bool strictCornerLimit = !isNotBaseLvlSub && bMidActLevel && posW <= 5 && posH <= 5;

        Span<int> posX = stackalloc int[513];
        Span<int> posY = stackalloc int[513];
        if (totalPositions > 0)
        {
            // Enumerate the (col,row) candidate grid (col = idx%posW, row = idx/posW).
            for (int posIndex = 0; posIndex < totalPositions; posIndex++)
            {
                posX[posIndex] = posIndex % posW;
                posY[posIndex] = posIndex / posW;
            }
            // Fisher-Yates: two seed draws per step, swap the two indexed pairs.
            for (int remaining = totalPositions; remaining > 0; remaining--)
            {
                int a = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)totalPositions);
                int b = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)totalPositions);
                (posX[a], posX[b]) = (posX[b], posX[a]);
                (posY[a], posY[b]) = (posY[b], posY[a]);
            }
        }

        if (totalPositions <= 0) return 0;
        for (int posIter = 0; posIter < totalPositions; posIter++)
        {
            int px = posX[posIter], py = posY[posIter];
            if (strictCornerLimit && px == 2 && py == 2) continue;
            if (ValidateSubTilePlacement(pUnkOutdoor, px, py, pRect, pLvlSub))
            {
                uint randVar = DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)pSubstGroup->nVariantCount);
                int nOffset = (pRect->nWidth + 1) * ((int)randVar + 1);
                ApplySubTileToGrid(px, py, pUnkOutdoor, pRect, pLvlSub, nOffset);
                if (pLvlSub->BordType == 0 || pLvlSub->BordType == 1) return 1;
            }
        }
        return 0;
    }
}

/// <summary>TileSub.cpp (D2Common::Drlg::TileSub — secondary-border substitution-group tiling).</summary>
internal static unsafe class TileSub
{
    // InitializeDrlgFile overlay decode (TileSub.cpp:32, 1.14d 006704e0)
    //
    // The recon reinterprets the D2LvlSubTxt record AS a D2PoolManagerStrc and derives each
    // grid by BYTE-addressing into that overlay. Decoded against the actual layouts the overlay
    // offsets land EXACTLY on the D2LvlSubTxt grid fields (Drlg.h:263):
    //   &pPools[1].pSync.LockCount       = 0x54  = &pTileTypeGrid[0]
    //   &pPools[3].pSync.RecursionCount  = 0xB8  = &pWallGrid[1]
    //   &pPools[4].pSync.SpinCount       = 0xF4  = &pFloorGrid
    //   &pPools[4].pBlocks               = 0x108 = &pShadowGrid
    // So the overlay is just named-field access; no 32-bit ABI dependence in the standalone.

    /// <summary>Core of InitializeDrlgFile: initializes the LvlSub record's per-wall-layer
    /// tile-type/wall grids + floor + shadow grids from the loaded DrlgFile's cell layers.</summary>
    public static void InitGridsFromDrlgFile(D2LvlSubTxt* pLvlSub, D2DrlgFileStrc* pDrlgFile)
    {
        int nGridWidth = pDrlgFile->nWidth + 1;
        D2DrlgGridTileDataStrc ctx = default;
        ctx.nX = 0;
        ctx.nY = 0;
        ctx.nWidth = (uint)nGridWidth;
        ctx.nHeight = (uint)(pDrlgFile->nHeight + 1);

        // recon: Fog::ErrorManager::ERROR_UnrecoverableInternalError_Halt (line 0x2b6)
        if (pDrlgFile->nSubstGroups == 0) throw new DrlgException("InitializeDrlgFile: DrlgFile has no substitution groups");

        // Wall layers: per layer init pWallGrid[i] (from pWallLayer[i]) and pTileTypeGrid[i]
        // (from pTileTypeLayer[i]).
        for (int i = 0; i < pDrlgFile->nWallLayers; i++)
        {
            DrlgGrid.InitGridFromTileData(null, Inline.At(ref pLvlSub->pWallGrid, i), (int*)pDrlgFile->pWallLayer[i], &ctx, nGridWidth);
            DrlgGrid.InitGridFromTileData(null, Inline.At(ref pLvlSub->pTileTypeGrid, i), (int*)pDrlgFile->pTileTypeLayer[i], &ctx, nGridWidth);
        }

        // AlterAllGridFlags over wall grids 1..nWallLayers-1: stamp the layer index into bits
        // 18+ (recon `nWallLayerIdx << 0x12`).
        for (int j = 1; j < pDrlgFile->nWallLayers; j++)
            DrlgGrid.AlterAllGridFlags(Inline.At(ref pLvlSub->pWallGrid, j), j << 0x12, 0);

        if (pDrlgFile->nFloorLayers != 0)
            DrlgGrid.InitGridFromTileData(null, &pLvlSub->pFloorGrid, (int*)pDrlgFile->pFloorLayer[0], &ctx, nGridWidth);

        if (pDrlgFile->pShadowLayer != null)
            DrlgGrid.InitGridFromTileData(null, &pLvlSub->pShadowGrid, (int*)pDrlgFile->pShadowLayer, &ctx, nGridWidth);
    }

    /// <summary>TileSub.cpp:32 (1.14d 006704e0). Loads the LvlSub record's DrlgFile then
    /// initializes its substitution grids. Idempotent: returns if already loaded.</summary>
    public static void InitializeDrlgFile(void* pMemory, D2LvlSubTxt* pLvlSub)
    {
        if (pLvlSub->pDrlgFile != null) return;
        Preset.AllocDrlgFile(&pLvlSub->pDrlgFile, pMemory, DrlgTables.FixedString(pLvlSub->File, 60));
        var pDrlgFile = pLvlSub->pDrlgFile;
        if (pDrlgFile == null) return;
        // Harness degrade: a zeroed file (nSubstGroups==0) means the asset is absent — skip grid
        // init; DRLGOUTDOOR_ApplySubstitutionGroup then no-ops without a seed draw. The engine
        // ERROR_Halts here (initGridsFromDrlgFile, recon line 0x2b6).
        if (pDrlgFile->nSubstGroups == 0) return;
        InitGridsFromDrlgFile(pLvlSub, pDrlgFile);
    }

    /// <summary>TileSub.cpp:91 (1.14d 00670750). Iterates consecutive LvlSub rows whose Type
    /// matches nLvlSubId. (Note: do-while reads (record+1).Type AFTER the post-increment.)</summary>
    public static void AddSecondaryBorder(D2UnkOutdoorStrc* pUnkOutdoor)
    {
        var pLvlSubTxtRecord = DrlgTables.LvlSubGetLineFromSubType(pUnkOutdoor->nLvlSubId);
        if (pLvlSubTxtRecord->Type != pUnkOutdoor->nLvlSubId) return;
        while (true)
        {
            void* pMemory = pUnkOutdoor->pLevel->pDrlg->pDS1MemPool;
            InitializeDrlgFile(pMemory, pLvlSubTxtRecord);
            OutSub.ApplySubstitutionGroup(pUnkOutdoor, pLvlSubTxtRecord);
            pLvlSubTxtRecord += 1;
            if (!(pLvlSubTxtRecord->Type == pUnkOutdoor->nLvlSubId)) break;
        }
    }
}

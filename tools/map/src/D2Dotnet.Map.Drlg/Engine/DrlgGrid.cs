// Port of libd2 packages/drlg/src/drlg/DrlgGrid.zig (MIT, (c) 2026 jaenster).
//
// Faithful transform of recon/closure/DrlgGrid.cpp (D2Common::Drlg::DrlgGrid).
// `eD2GridCellFlags` is `int` in the recon, so a grid cell pointer is `int*`.

namespace D2Dotnet.Map.Drlg.Engine;

/// <summary>DrlgGrid.h: D2DrlgGridTileDataStrc (grid-init context; Ghidra v628 /Diablo2/DRLG)</summary>
internal unsafe struct D2DrlgGridTileDataStrc
{
    public uint nX;
    public uint nY;
    public uint nWidth;
    public uint nHeight;
    public D2RoomExStrc* pRoom;
    public void* pMemory;
    public D2DrlgGridStrc* pGrid;
    public D2DrlgPresetRoomStrc* pRoomExDataPreset;
}

internal static unsafe class DrlgGrid
{
    // Flag-operation table (globals_drlg.cpp:201). Order: Or, And, Xor, Overwrite,
    // OverwriteIfZero, AndNegated.
    public static void ApplyOp(int eOperation, int* pFlag, int nFlag)
    {
        switch (eOperation)
        {
            case 0: OrFlag(pFlag, nFlag); break;
            case 1: AndFlag(pFlag, nFlag); break;
            case 2: XorFlag(pFlag, nFlag); break;
            case 3: OverwriteFlag(pFlag, nFlag); break;
            case 4: OverwriteFlagIfZero(pFlag, nFlag); break;
            case 5: AndNegatedFlag(pFlag, nFlag); break;
            default: throw new IndexOutOfRangeException("flag operation");
        }
    }

    private static int* Cell(D2DrlgGridStrc* pGrid, int nY, int nX) => pGrid->pCellsFlags + (pGrid->pCellsRowOffsets[nY] + nX);

    // Flag primitives — DrlgGrid.cpp:178-215
    public static void OverwriteFlag(int* pFlag, int nFlag) => *pFlag = nFlag;
    public static void OrFlag(int* pFlag, int nFlag) => *pFlag |= nFlag;
    public static void AndFlag(int* pFlag, int nFlag) => *pFlag &= nFlag;
    public static void XorFlag(int* pFlag, int nFlag) => *pFlag ^= nFlag;
    public static void OverwriteFlagIfZero(int* pFlag, int nFlag)
    {
        if (*pFlag != 0) return;
        *pFlag = nFlag;
    }
    public static void AndNegatedFlag(int* pFlag, int nFlag) => *pFlag &= ~nFlag;

    /// <summary>DrlgGrid.cpp:219 (1.14d 0067c480)</summary>
    public static int IsGridValid(D2DrlgGridStrc* pDrlgGrid) => (pDrlgGrid != null && pDrlgGrid->pCellsFlags != null) ? 1 : 0;

    /// <summary>DrlgGrid.cpp:229 (1.14d 0067c4a0)</summary>
    public static int IsPointInsideGridArea(D2DrlgGridStrc* pDrlgGrid, int nX, int nY) =>
        (-1 < nX && nX < pDrlgGrid->nWidth && -1 < nY && nY < pDrlgGrid->nHeight) ? 1 : 0;

    /// <summary>DrlgGrid.cpp:239 (1.14d 0067c4d0)</summary>
    public static int* GetCellAddress(D2DrlgGridStrc* pDrlgGrid, int nY, int nX) => Cell(pDrlgGrid, nY, nX);

    /// <summary>DrlgGrid.cpp:245 (1.14d 0067c4f0)</summary>
    public static void AlterGridFlag(D2DrlgGridStrc* pDrlgGrid, int nX, int nY, int nFlag, int eOperation) =>
        ApplyOp(eOperation, Cell(pDrlgGrid, nY, nX), nFlag);

    /// <summary>DrlgGrid.cpp:251 (1.14d 0067c520)</summary>
    public static void ApplyFlagOperation(D2DrlgGridStrc* pDrlgGrid, D2DrlgCoordStrc* pCoord, int nFlag, int eOperation) =>
        ApplyOp(eOperation, Cell(pDrlgGrid, pCoord->nPosY, pCoord->nPosX), nFlag);

    /// <summary>DrlgGrid.cpp:263 (1.14d 0067c570). Recon's `nX` param is typed D2RoomExStrc*
    /// but is an integer X coordinate (byte-offset artifact, line 264) — resolved to i32 X.</summary>
    public static int GetGridFlags(D2DrlgGridStrc* pGrid, int nX, int nY) => pGrid->pCellsFlags[nX + pGrid->pCellsRowOffsets[nY]];

    /// <summary>DrlgGrid.cpp:271 (1.14d 0067c590)</summary>
    public static void AlterAllGridFlags(D2DrlgGridStrc* pDrlgGrid, int nFlag, int eOperation)
    {
        int nYIdx = 0;
        if (0 >= pDrlgGrid->nHeight) return;
        int nRowWidth = pDrlgGrid->nWidth;
        while (true)
        {
            int nXIdx = 0;
            int* pCellFlag = pDrlgGrid->pCellsFlags + pDrlgGrid->pCellsRowOffsets[nYIdx];
            if (0 < nRowWidth)
            {
                while (true)
                {
                    ApplyOp(eOperation, pCellFlag, nFlag);
                    nRowWidth = pDrlgGrid->nWidth;
                    nXIdx += 1;
                    pCellFlag += 1;
                    if (!(nXIdx < nRowWidth)) break;
                }
            }
            nYIdx += 1;
            if (!(nYIdx < pDrlgGrid->nHeight)) break;
        }
    }

    /// <summary>DrlgGrid.cpp:300 (1.14d 0067c600)</summary>
    public static void FlagOperations(D2DrlgGridStrc* pGrid, int nCountIn, int nOpIndex)
    {
        int nCount = nCountIn;
        int* pTopRowCell = pGrid->pCellsFlags + pGrid->pCellsRowOffsets[0];
        int* pBottomRowCell = pGrid->pCellsFlags + pGrid->pCellsRowOffsets[pGrid->nHeight - 1];
        int nColIdx = 0;
        if (0 < pGrid->nWidth)
        {
            while (true)
            {
                ApplyOp(nCount, pTopRowCell, nOpIndex);
                pTopRowCell += 1;
                ApplyOp(nCount, pBottomRowCell, nOpIndex);
                pBottomRowCell += 1;
                nColIdx += 1;
                if (!(nColIdx < pGrid->nWidth)) break;
            }
        }

        nCount = 1;
        if (1 >= pGrid->nHeight) return;
        while (true)
        {
            int nOpIndexOrig = nCount;
            ApplyOp(nOpIndexOrig, pGrid->pCellsFlags + pGrid->pCellsRowOffsets[nCount], nOpIndex);
            ApplyOp(nOpIndexOrig, pGrid->pCellsFlags + (pGrid->pCellsRowOffsets[nCount] + pGrid->nWidth - 1), nOpIndex);
            nCount += 1;
            if (!(nCount < pGrid->nHeight)) break;
        }
    }

    /// <summary>DrlgGrid.cpp:331 (1.14d 0067c6d0)</summary>
    public static void ApplyFlagToBorderCells(D2DrlgGridStrc* pDrlgGrid, int nFlag, int eOperation)
    {
        int nIndex = 0;
        if (pDrlgGrid->nWidth == 0 && -1 < pDrlgGrid->nWidth - 1)
        {
            int* pCell = pDrlgGrid->pCellsFlags + pDrlgGrid->pCellsRowOffsets[pDrlgGrid->nHeight - 1];
            while (true)
            {
                ApplyOp(eOperation, pCell, nFlag);
                nIndex += 1;
                pCell += 1;
                if (!(nIndex < pDrlgGrid->nWidth - 1)) break;
            }
        }

        nIndex = 0;
        if (0 >= pDrlgGrid->nHeight) return;
        while (true)
        {
            ApplyOp(eOperation, pDrlgGrid->pCellsFlags + (pDrlgGrid->pCellsRowOffsets[nIndex] + pDrlgGrid->nWidth - 1), nFlag);
            nIndex += 1;
            if (!(nIndex < pDrlgGrid->nHeight)) break;
        }
    }

    /// <summary>DrlgGrid.cpp:355 (1.14d 0067c760). NOTE: the recon reads an uninitialised local
    /// `_bIncludeEndpoints` for the final endpoint write; that is the obvious decompiler alias of
    /// the `bIncludeEndpoints` parameter — resolved as such.</summary>
    public static void SetEdgeGridFlags(D2DrlgGridStrc* pDrlgGrid, D2DrlgVertexStrc* pEdge, int nFlag, int eOperation, bool bIncludeEndpoints)
    {
        int nY, nX, nEndY;
        bool bHorizontal;
        int nEndX = pEdge->nPosX;
        D2DrlgVertexStrc* pNextVertex = pEdge->pNext;
        int nNextX = pNextVertex->nPosX;
        if (nEndX == nNextX)
        {
            if (pEdge->nPosY == pNextVertex->nPosY)
            {
                ApplyOp(eOperation, Cell(pDrlgGrid, pEdge->nPosY, nEndX), nFlag);
                return;
            }
            // Vertical edge: repurpose nNextX as the next vertex's Y coordinate.
            nNextX = pNextVertex->nPosY;
            nEndY = pEdge->nPosY;
            bHorizontal = false;
            nY = nNextX;
            nX = nEndX;
            if (nNextX <= nEndY)
            {
                nY += 1;
            }
            else
            {
                nY = nEndY;
                nEndY = nNextX;
                nY += 1;
            }
        }
        else
        {
            nY = pEdge->nPosY;
            bHorizontal = true;
            nEndY = nY;
            if (nEndX < nNextX)
            {
                nX = nEndX + 1;
                nEndX = nNextX;
            }
            else
            {
                nX = nNextX + 1;
            }
        }

        while (nX != nEndX || nY != nEndY)
        {
            ApplyOp(eOperation, Cell(pDrlgGrid, nY, nX), nFlag);
            if (bHorizontal) nX += 1;
            else nY += 1;
        }

        ApplyOp(eOperation, Cell(pDrlgGrid, pEdge->nPosY, pEdge->nPosX), nFlag);
        if (!bIncludeEndpoints) return;
        ApplyOp(eOperation, Cell(pDrlgGrid, pEdge->pNext->nPosY, pEdge->pNext->nPosX), nFlag);
    }

    /// <summary>DrlgGrid.cpp:417 (1.14d 0067c8e0). Byte-offset artifacts at lines 494/519:
    /// `*(int*)((int)pCellsRowOffsets + nByteOff)` == `pCellsRowOffsets[nByteOff>>2]` (the
    /// emitter byte-offset bug) — resolved to the true i32 index.</summary>
    public static void SetLineFlagsWithWidth(D2DrlgGridStrc* pDrlgGrid, D2DrlgVertexStrc* pEdge, D2DrlgCoordsStrc* pDrlgCoords, int nFlag, int eOperation, int nLineWidth)
    {
        int nWidthIndex;
        int nY = pEdge->nPosY;
        int nX = pEdge->nPosX;
        int nDx = pEdge->pNext->nPosX - nX;
        int nDy = pEdge->pNext->nPosY - nY;
        int nSignX, nSignY;
        if (nDx < 0)
        {
            nDx = -nDx;
            nSignX = -1;
        }
        else nSignX = 1;
        if (nDy < 0)
        {
            nDy = -nDy;
            nSignY = -1;
        }
        else nSignY = 1;

        int nGridX = nX - pDrlgCoords->WorldPosition.x;
        int nError = 0;
        int nGridOffsetX = nY - pDrlgCoords->WorldPosition.y;
        int nRowOffsetY = 0;
        if (nDx < nDy)
        {
            if (0 < nLineWidth)
            {
                while (true)
                {
                    if (DrlgRoom.AreXYInsideCoordinates(pDrlgCoords, nRowOffsetY + nX, nY) != 0)
                        ApplyOp(eOperation, pDrlgGrid->pCellsFlags + (pDrlgGrid->pCellsRowOffsets[nGridOffsetX] + nRowOffsetY + nGridX), nFlag);
                    nRowOffsetY += 1;
                    if (!(nRowOffsetY < nLineWidth)) break;
                }
            }

            int nRemainingY = nDy;
            nGridOffsetX = nX;
            if (0 < nDy)
            {
                while (true)
                {
                    nY += nSignY;
                    nError += nDx;
                    if (nDy < nError)
                    {
                        nGridOffsetX += nSignX;
                        nError -= nDy;
                    }
                    nX = pDrlgCoords->WorldPosition.x;
                    nGridX = pDrlgCoords->WorldPosition.y;
                    nWidthIndex = 0;
                    if (0 < nLineWidth)
                    {
                        while (true)
                        {
                            if (DrlgRoom.AreXYInsideCoordinates(pDrlgCoords, nWidthIndex + nGridOffsetX, nY) != 0)
                                ApplyOp(eOperation, pDrlgGrid->pCellsFlags + (pDrlgGrid->pCellsRowOffsets[nY - nGridX] + nWidthIndex + (nGridOffsetX - nX)), nFlag);
                            nWidthIndex += 1;
                            if (!(nWidthIndex < nLineWidth)) break;
                        }
                    }
                    nRemainingY -= 1;
                    if (nRemainingY == 0) break;
                }
                return;
            }
        }
        else
        {
            if (0 < nLineWidth)
            {
                nGridOffsetX *= 4;
                while (true)
                {
                    if (DrlgRoom.AreXYInsideCoordinates(pDrlgCoords, nX, nRowOffsetY + nY) != 0)
                        // line 494: pCellsRowOffsets[nGridOffsetX>>2]
                        ApplyOp(eOperation, pDrlgGrid->pCellsFlags + (pDrlgGrid->pCellsRowOffsets[nGridOffsetX >> 2] + nGridX), nFlag);
                    nGridOffsetX += 4;
                    nRowOffsetY += 1;
                    if (!(nRowOffsetY < nLineWidth)) break;
                }
            }

            int nRemainingX = nDx;
            if (0 < nDx)
            {
                while (true)
                {
                    nX += nSignX;
                    nError += nDy;
                    if (nDx < nError)
                    {
                        nY += nSignY;
                        nError -= nDx;
                    }
                    nGridX = pDrlgCoords->WorldPosition.x;
                    nWidthIndex = 0;
                    if (0 < nLineWidth)
                    {
                        nRowOffsetY = (nY - pDrlgCoords->WorldPosition.y) * 4;
                        while (true)
                        {
                            if (DrlgRoom.AreXYInsideCoordinates(pDrlgCoords, nX, nWidthIndex + nY) != 0)
                                // line 519: pCellsRowOffsets[nRowOffsetY>>2]
                                ApplyOp(eOperation, pDrlgGrid->pCellsFlags + (pDrlgGrid->pCellsRowOffsets[nRowOffsetY >> 2] + (nX - nGridX)), nFlag);
                            nRowOffsetY += 4;
                            nWidthIndex += 1;
                            if (!(nWidthIndex < nLineWidth)) break;
                        }
                    }
                    nRemainingX -= 1;
                    if (nRemainingX == 0) break;
                }
            }
        }
    }

    /// <summary>DrlgGrid.cpp:535 (1.14d 0067cb80). Single allocation holds row offsets followed
    /// by the cell array (pCellsFlags = pCellsRowOffsets + nHeight).</summary>
    public static void InitGridCells(void* pMemory, D2DrlgGridStrc* pDrlgGrid, int nWidth, int nHeight)
    {
        pDrlgGrid->nWidth = nWidth;
        nuint nAllocSize = (nuint)((nWidth + 1) * nHeight * 4);
        pDrlgGrid->nHeight = nHeight;
        int* pRowOffsets = (int*)Pool.AllocServerMemory(pMemory, nAllocSize);
        pDrlgGrid->pCellsRowOffsets = pRowOffsets;
        pDrlgGrid->pCellsFlags = pDrlgGrid->pCellsRowOffsets + nHeight;
        int nRow = 0;
        if (0 < nHeight)
        {
            int nOffset = 0;
            while (true)
            {
                pDrlgGrid->pCellsRowOffsets[nRow] = nOffset;
                nOffset += nWidth;
                nRow += 1;
                if (!(nRow < nHeight)) break;
            }
        }
        pDrlgGrid->bIsSubGrid = 0;
    }

    /// <summary>DrlgGrid.cpp:558 (1.14d 0067cbf0). Recon DrlgGrid.cpp:582 shows `int nRowOffset =
    /// 0;` re-scoped inside the loop with a dead `nRowOffset += nWidth;` — a decompiler artifact
    /// that lost the loop-carried row stride. The real engine lays the grid out row-major:
    /// rowOffsets[i] = i*nWidth. Latent until cross-row GetGridFlags reads (warp-tile flags)
    /// needed the correct stride; room-gen never reads flags cross-row.</summary>
    public static void FillGrid(D2DrlgGridStrc* pDrlgGrid, int nWidth, int nHeight, int* pCellPos, int* pCellRowOffsets)
    {
        pDrlgGrid->nWidth = nWidth;
        pDrlgGrid->nHeight = nHeight;
        pDrlgGrid->pCellsFlags = pCellPos;
        new Span<byte>(pCellPos, nWidth * nHeight * 4).Clear();
        int nRowIdx = 0;
        pDrlgGrid->pCellsRowOffsets = pCellRowOffsets;
        if (nHeight < 1)
        {
            pDrlgGrid->bIsSubGrid = 0;
            return;
        }
        int nRowOffset = 0;
        while (true)
        {
            pDrlgGrid->pCellsRowOffsets[nRowIdx] = nRowOffset;
            nRowIdx += 1;
            nRowOffset += nWidth;
            if (!(nRowIdx < nHeight)) break;
        }
        pDrlgGrid->bIsSubGrid = 0;
    }

    /// <summary>DrlgGrid.cpp:582 (1.14d 0067cc60)</summary>
    public static void AllocGrid(void* pMemory, D2DrlgGridStrc* pDrlgGrid, int* pCellsFlags, int nWidth, int nHeight)
    {
        pDrlgGrid->nWidth = nWidth;
        pDrlgGrid->nHeight = nHeight;
        pDrlgGrid->pCellsFlags = pCellsFlags;
        int* pRowOffsets = (int*)Pool.AllocServerMemory(pMemory, (nuint)(nHeight * 4));
        pDrlgGrid->pCellsRowOffsets = pRowOffsets;
        int nRow = 0;
        if (0 < nHeight)
        {
            int nOffset = 0;
            while (true)
            {
                pDrlgGrid->pCellsRowOffsets[nRow] = nOffset;
                nRow += 1;
                nOffset += nWidth;
                if (!(nRow < nHeight)) break;
            }
        }
        pDrlgGrid->bIsSubGrid = 1;
    }

    /// <summary>DrlgGrid.cpp:603 (1.14d 0067ccc0)</summary>
    public static void InitGridFromTileData(void* pMemory, D2DrlgGridStrc* pGrid, int* pParentCells, D2DrlgGridTileDataStrc* pTileData, int nParentRowStride)
    {
        pGrid->nWidth = (int)pTileData->nWidth;
        pGrid->nHeight = (int)pTileData->nHeight;
        pGrid->pCellsFlags = pParentCells + ((int)pTileData->nY * nParentRowStride + (int)pTileData->nX);
        int* pRowOffsets = (int*)Pool.AllocServerMemory(pMemory, (nuint)(pTileData->nHeight * 4));
        pGrid->pCellsRowOffsets = pRowOffsets;
        int nOffset = 0;
        if (0 < (int)pTileData->nHeight)
        {
            int nRow = 0;
            while (true)
            {
                pGrid->pCellsRowOffsets[nRow] = nOffset;
                nRow += 1;
                nOffset += nParentRowStride;
                if (!(nRow < (int)pTileData->nHeight)) break;
            }
        }
        pGrid->bIsSubGrid = 1;
    }

    /// <summary>DrlgGrid.cpp:624 (1.14d 0067cd30). Byte-offset artifact at line 627:
    /// `&amp;pCellPos->nNumber + nPosY*nWidth + nPosX` indexes the D2DrlgMapStrc as an i32 array
    /// from its first field (nNumber) — resolved to that i32 base.</summary>
    public static void FillExternalCellFlags(D2DrlgGridStrc* pDrlgGrid, void* pCellPos, D2DrlgCoordStrc* pDrlgCoord, int nWidth, int* pCellFlags)
    {
        pDrlgGrid->nWidth = pDrlgCoord->nWidth;
        pDrlgGrid->nHeight = pDrlgCoord->nHeight;
        int* pMapInts = (int*)pCellPos;
        pDrlgGrid->pCellsFlags = pMapInts + (pDrlgCoord->nPosY * nWidth + pDrlgCoord->nPosX);
        pDrlgGrid->pCellsRowOffsets = pCellFlags;
        int nRowOffset = 0;
        if (0 < pDrlgCoord->nHeight)
        {
            int nRowIdx = 0;
            while (true)
            {
                pDrlgGrid->pCellsRowOffsets[nRowIdx] = nRowOffset;
                nRowIdx += 1;
                nRowOffset += nWidth;
                if (!(nRowIdx < pDrlgCoord->nHeight)) break;
            }
        }
        pDrlgGrid->bIsSubGrid = 1;
    }

    /// <summary>DrlgGrid.cpp:644 (1.14d 0067cd90)</summary>
    public static void FreeGrid(void* pMemory, D2DrlgGridStrc* pGrid)
    {
        if (pGrid->pCellsRowOffsets != null) Pool.FreeServerMemory(pMemory, pGrid->pCellsRowOffsets);
        pGrid->pCellsRowOffsets = null;
        pGrid->pCellsFlags = null;
    }

    /// <summary>DrlgGrid.cpp:655 (1.14d 0067cdc0)</summary>
    public static void ResetGrid(D2DrlgGridStrc* pDrlgGrid)
    {
        pDrlgGrid->pCellsFlags = null;
        pDrlgGrid->pCellsRowOffsets = null;
    }
}

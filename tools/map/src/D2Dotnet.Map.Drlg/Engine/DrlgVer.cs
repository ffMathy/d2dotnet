// Port of libd2 packages/drlg/src/drlg/DrlgVer.zig (MIT, (c) 2026 jaenster).
//
// Faithful transform of recon/closure/DrlgVer.cpp (D2Common::Drlg::DrlgVer — room vertex
// rings). D2DrlgOrthStrc.psCoordinatesAndSize (D2DrlgCoordsStrc) is read as D2DrlgCoordStrc,
// the same four ints, exactly as the recon does.

namespace D2Dotnet.Map.Drlg.Engine;

internal static unsafe class DrlgVer
{
    /// <summary>DrlgVer.cpp:25 (1.14d 0067cdd0)</summary>
    public static D2DrlgVertexStrc* AllocVertex(void* pMemory, byte nDirection)
    {
        var pVertex = (D2DrlgVertexStrc*)Pool.AllocServerMemory(pMemory, (nuint)sizeof(D2DrlgVertexStrc));
        pVertex->nPosX = 0;
        pVertex->nPosY = 0;
        pVertex->nDirection = 0;
        pVertex->dwFlags = 0;
        pVertex->pNext = null;
        pVertex->nDirection = nDirection;
        return pVertex;
    }

    /// <summary>DrlgVer.cpp:38 (1.14d 0067ce00)</summary>
    public static void SetVertexFlags(D2DrlgVertexStrc* pVertex, int nFlag)
    {
        pVertex->dwFlags |= 1;
        if (nFlag == 0) return;
        pVertex->dwFlags |= 2;
    }

    /// <summary>DrlgVer.cpp:49 (1.14d 0067ce20). The closure decompile (recon DrlgVer.cpp:75)
    /// lost the switch's cases 2/3 to a dropped jump table (the "dup default" artifact). Cases
    /// 0..3 recovered FAITHFULLY from Ghidra Game.exe 1.14d @0067ce20 (session 62fbfe69): the
    /// four edges are Y-top(0)/X-right(1)/Y-bottom(2)/X-left(3) with the per-edge anchor vertex
    /// + comparison sign below.</summary>
    public static D2DrlgVertexStrc* CreateVerticesFromEdges(void* pMemory, D2DrlgVertexStrc* pFirstVertex, D2DrlgCoordStrc* pDrlgCoord, byte nDirection, void* pDrlgRoomData)
    {
        int nEdgeMin, nEdgeMax, nCoordMax, nCoordMin, nAnchorPos, nEdgeVtxPos;
        D2DrlgVertexStrc* pNewVertex;
        D2DrlgVertexStrc* pEdgeVertex;
        bool bHasGap;

        var pVertex1 = pFirstVertex->pNext;
        var pThirdVertex = pVertex1->pNext;
        var pVertex2 = pThirdVertex->pNext; // 4th ring vertex (cases 2/3)

        var pOrth = (D2DrlgOrthStrc*)pDrlgRoomData;
        if (pOrth == null) return (D2DrlgVertexStrc*)&pThirdVertex->pNext;

        while (true)
        {
            // +0x10 psCoordinatesAndSize, read as D2DrlgCoordStrc; null -> pDrlgCoord
            var pEffectiveCoord = (D2DrlgCoordStrc*)pOrth->psCoordinatesAndSize;
            if (pOrth->psCoordinatesAndSize == null) pEffectiveCoord = pDrlgCoord;

            switch (pOrth->neDrlgDirection) // +0x04
            {
                case 0:
                    nCoordMax = pEffectiveCoord->nPosY;
                    nAnchorPos = pVertex1->nPosY;
                    nEdgeMin = pFirstVertex->nPosY;
                    bHasGap = true;
                    nEdgeVtxPos = -1;
                    nCoordMin = pEffectiveCoord->nHeight + nCoordMax;
                    pEdgeVertex = pFirstVertex;
                    break;
                case 1:
                    nCoordMin = pEffectiveCoord->nPosX;
                    nEdgeVtxPos = 1;
                    nCoordMax = pEffectiveCoord->nWidth + nCoordMin;
                    pNewVertex = pThirdVertex;
                    pEdgeVertex = pVertex1;
                    bHasGap = false; // LAB_0067ceb7
                    nAnchorPos = pNewVertex->nPosX;
                    nEdgeMin = pEdgeVertex->nPosX;
                    break;
                case 2:
                    nCoordMin = pEffectiveCoord->nPosY;
                    nAnchorPos = pVertex2->nPosY;
                    nEdgeMin = pThirdVertex->nPosY;
                    nEdgeVtxPos = 1;
                    bHasGap = true;
                    nCoordMax = pEffectiveCoord->nHeight + nCoordMin;
                    pEdgeVertex = pThirdVertex;
                    break;
                case 3:
                    nCoordMax = pEffectiveCoord->nPosX;
                    nEdgeVtxPos = -1;
                    nCoordMin = pEffectiveCoord->nWidth + nCoordMax;
                    pNewVertex = pFirstVertex;
                    pEdgeVertex = pVertex2;
                    bHasGap = false; // LAB_0067ceb7
                    nAnchorPos = pNewVertex->nPosX;
                    nEdgeMin = pEdgeVertex->nPosX;
                    break;
                default:
                    throw new DrlgException("DRLGVER: unrecoverable internal error (recon 0x6d)");
            }

            nEdgeMin *= nEdgeVtxPos;
            nEdgeMax = nCoordMin * nEdgeVtxPos;
            bool runLab = false;
            if ((nEdgeMax - nEdgeMin) == 0 || nEdgeMax < nEdgeMin)
            {
                if (nEdgeMin <= nCoordMax * nEdgeVtxPos) runLab = true;
            }
            else if ((nEdgeMax - nAnchorPos * nEdgeVtxPos) == 0 || nEdgeMax < nAnchorPos * nEdgeVtxPos)
            {
                pNewVertex = AllocVertex(pMemory, nDirection);
                if (bHasGap)
                {
                    pNewVertex->nPosX = pEdgeVertex->nPosX;
                    pNewVertex->nPosY = nCoordMin;
                }
                else
                {
                    pNewVertex->nPosY = pEdgeVertex->nPosY;
                    pNewVertex->nPosX = nCoordMin;
                }
                pNewVertex->pNext = pEdgeVertex->pNext;
                pEdgeVertex->pNext = pNewVertex;
                pEdgeVertex = pNewVertex;
                runLab = true; // goto LAB_0067cf79
            }

            if (runLab)
            {
                // LAB_0067cf79
                nCoordMin = pOrth->bIsDrlgTypePresetArea; // +0x08
                pEdgeVertex->dwFlags |= 1;
                if (nCoordMin != 0) pEdgeVertex->dwFlags |= 2;
                if (nCoordMax * nEdgeVtxPos < nAnchorPos * nEdgeVtxPos)
                {
                    pNewVertex = AllocVertex(pMemory, nDirection);
                    if (bHasGap)
                    {
                        pNewVertex->nPosX = pEdgeVertex->nPosX;
                        pNewVertex->nPosY = nCoordMax;
                    }
                    else
                    {
                        pNewVertex->nPosY = pEdgeVertex->nPosY;
                        pNewVertex->nPosX = nCoordMax;
                    }
                    pNewVertex->pNext = pEdgeVertex->pNext;
                    pEdgeVertex->pNext = pNewVertex;
                }
            }

            pOrth = pOrth->pNext; // +0x14
            if (pOrth == null) return null;
        }
    }

    /// <summary>DrlgVer.cpp:169 (1.14d 0067d050)</summary>
    public static void CreateRoomVertices(void* pMemory, D2DrlgVertexStrc** ppVertices, D2DrlgCoordStrc* pDrlgCoord, byte nDirection, void* pDrlgRoomData)
    {
        pDrlgCoord->nWidth -= 1;
        pDrlgCoord->nHeight -= 1;

        for (var pRoomData = (D2DrlgOrthStrc*)pDrlgRoomData; pRoomData != null; pRoomData = pRoomData->pNext)
        {
            var pCoord = (D2DrlgCoordStrc*)pRoomData->psCoordinatesAndSize; // +0x10
            pCoord->nWidth -= 1; // +0x08, resolved (*pSizePtr)--
            pCoord->nHeight -= 1; // +0x0c
        }

        var pVertex = AllocVertex(pMemory, nDirection);
        *ppVertices = pVertex;
        pVertex->nPosX = pDrlgCoord->nPosX;
        pVertex->nPosY = pDrlgCoord->nPosY + pDrlgCoord->nHeight;

        var pVertexNext = AllocVertex(pMemory, nDirection);
        pVertex->pNext = pVertexNext;
        pVertexNext->nPosX = pDrlgCoord->nPosX;
        pVertexNext->nPosY = pDrlgCoord->nPosY;

        pVertex = AllocVertex(pMemory, nDirection);
        pVertexNext->pNext = pVertex;
        pVertex->nPosX = pDrlgCoord->nPosX + pDrlgCoord->nWidth;
        pVertex->nPosY = pDrlgCoord->nPosY;

        pVertexNext = AllocVertex(pMemory, nDirection);
        pVertex->pNext = pVertexNext;
        pVertexNext->nPosX = pDrlgCoord->nPosX + pDrlgCoord->nWidth;
        pVertexNext->nPosY = pDrlgCoord->nPosY + pDrlgCoord->nHeight;
        pVertexNext->pNext = *ppVertices;

        CreateVerticesFromEdges(pMemory, *ppVertices, pDrlgCoord, nDirection, pDrlgRoomData);

        pVertex = *ppVertices;
        pVertexNext = pVertex;
        while (true)
        {
            pVertexNext->nPosX -= pDrlgCoord->nPosX;
            pVertexNext->nPosY -= pDrlgCoord->nPosY;
            pVertexNext = pVertexNext->pNext;
            if (pVertexNext == pVertex) break;
        }

        pDrlgCoord->nWidth += 1;
        pDrlgCoord->nHeight += 1;
        for (var pRestore = (D2DrlgOrthStrc*)pDrlgRoomData; pRestore != null; pRestore = pRestore->pNext)
        {
            var pCoord = (D2DrlgCoordStrc*)pRestore->psCoordinatesAndSize;
            pCoord->nWidth += 1; // resolved (*pSizePtr)++
            pCoord->nHeight += 1;
        }
    }

    /// <summary>DrlgVer.cpp:243 (1.14d 0067d1e0)</summary>
    public static void FreeVertices(void* pMemory, D2DrlgVertexStrc** ppVertices)
    {
        if (*ppVertices == null) return;
        D2DrlgVertexStrc* pNext = null;
        bool freedRing = false;
        var pCurrent = (*ppVertices)->pNext;
        if (pCurrent == null)
        {
            freedRing = true; // goto LAB_0067d22f
        }
        else
        {
            while (true)
            {
                pNext = pCurrent->pNext;
                Pool.FreeServerMemory(pMemory, pCurrent);
                pCurrent = pNext;
                if (pNext == *ppVertices) break;
            }
            Pool.FreeServerMemory(pMemory, pNext);
            if (pNext == null) freedRing = true;
        }
        if (freedRing) Pool.FreeServerMemory(pMemory, *ppVertices);
        *ppVertices = null;
    }

    /// <summary>DrlgVer.cpp:271 (1.14d 0067d280)</summary>
    public static void GetCoordDiff(D2DrlgVertexStrc* pDrlgVertex, int* pDiffX, int* pDiffY)
    {
        *pDiffX = pDrlgVertex->pNext->nPosX - pDrlgVertex->nPosX;
        *pDiffY = pDrlgVertex->pNext->nPosY - pDrlgVertex->nPosY;
        if (*pDiffX < 0) *pDiffX = -1;
        else if (0 < *pDiffX) *pDiffX = 1;
        if (*pDiffY < 0)
        {
            *pDiffY = -1;
            return;
        }
        if (0 >= *pDiffY) return;
        *pDiffY = 1;
    }
}

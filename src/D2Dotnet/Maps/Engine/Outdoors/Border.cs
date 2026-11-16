// Port of libd2 packages/drlg/src/drlg/outdoors/Border.zig (MIT, (c) 2026 jaenster).
//
// Mechanical transform of the shared Act-1/2/4/5 OUTDOOR BORDER placement core from
// recon/closure/OutPlace.cpp (+ the road .rdata tables and the DRLGOUTROOM_MarkBorderJunctions
// helper from recon/closure/Drlg.cpp).
//
// Faithful by construction:
//   * grid field access by NAME: the recon's `(int)pLevelData + 0x18 / 0x2c` raw byte reaches
//     map to D2DrlgLevelDataWildernessLevel.sGridLink / .sGridOutdoor.
//   * the pointer-as-int coordinate idiom in SetBlankBorderGridCells
//     (`(int)nScanX->apTiles + nStepX - 0x68` == nScanX + nStepX) -> plain i32 arithmetic.
//   * RNG only via OutPlace.SpawnOutdoorLevelPresetEx (-1 file index).
//   * the .rdata lookup tables are reproduced EXACTLY from Ghidra Game.exe 1.14d (session
//     62fbfe69), each cited with its address. Several are indexed by negative offsets because the
//     engine's symbol sits mid-array; the full ranges are recovered here.
//
// Transformed here (cite recon OutPlace.cpp unless noted):
//   GetActNoFromLevelNumber                  Drlg.cpp
//   DRLGOUTPLACE_GetRoadPresetId             OutPlace.cpp:161
//   DRLGOUTPLACE_GetAdjacentRoadPresetId     OutPlace.cpp:171
//   SetBlankBorderGridCells                  OutPlace.cpp:196 (1.14d 00675670)
//   DRLGOUTDOOR_GetAdjacentLevelVisMask      Outdoors.cpp:360
//   GetOutLinkVisFlag                        Outdoors.cpp:380
//   SetOutGridLinkFlags                      OutPlace.cpp:268 (1.14d 00675770)
//   PlaceAct1245OutdoorBorders               OutPlace.cpp:291 (1.14d 00675850)
//   DRLGOUTROOM_MarkBorderJunctions          Drlg.cpp:6160
//   DRLGOUTROOM_GetGridDivisionSize          OutSiege.cpp (trivial getter)

namespace D2Dotnet.Maps.Engine.Outdoors;

internal static unsafe class Border
{
    private const int OP_OR = 0;
    private const int OP_ANDNEG = 5;
    private const int ACT_I = 0;
    private const int ACT_II = 1;
    private const int ACT_III = 2;
    private const int ACT_IV = 3;
    private const int ACT_V = 4;
    private const int DIRECTION_NORTHEAST = 1;
    private const int DIRECTION_SOUTHWEST = 2;
    private const int DIRECTION_NORTHWEST = 3;

    private static D2DrlgLevelDataWildernessLevel* Wild(D2DrlgLevelStrc* pLevel) => (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;

    // gaRoadPresetDirectionLookup @0x6f0fd0 — indexed by (nDx + nDy*3) over -4..+4; the engine
    // symbol is logical index 0, so we store 9 entries and add +4.
    private const int DIR_LOOKUP_BIAS = 4;
    private static readonly int[] gaRoadPresetDirectionLookup = { -1, 1, -1, 0, -1, 2, -1, 3, -1 };
    private static int DirLookup(int nDx, int nDy) => gaRoadPresetDirectionLookup[nDx + nDy * 3 + DIR_LOOKUP_BIAS];

    // gaOutPlaceRoadPresetLookup0 @0x6f0620 (4 ints) is the head of the SAME contiguous table as
    // gaOutPlaceRoadPresetLookup1 @0x6f0630 (48 ints) — Ghidra split it. Together = 13 rows x 4
    // cols of corner/road preset ids. lookup0 is indexed [roadType + tableIndex*4] from row 0;
    // lookup1 is indexed [roadType + dir*4] from row 1 (= +4 ints).
    private static readonly int[] gaRoadCornerTable =
    {
        0x2000, 0x4000, 0x8000, 0x0, // row 0  (lookup0)
        0x0, 0x4, 0x16c, 0x31f, // row 1  (lookup1[0])
        0x10, 0x5, 0x16d, 0x320,
        0x11, 0x6, 0x16e, 0x321,
        0x0, 0x7, 0x16f, 0x322,
        0x12, 0x8, 0x170, 0x323,
        0x13, 0x9, 0x171, 0x324,
        0x16, 0xa, 0x172, 0x325,
        0x0, 0xb, 0x173, 0x326,
        0x0, 0xc, 0x174, 0x327,
        0x17, 0xd, 0x175, 0x328,
        0x0, 0xe, 0x176, 0x329,
        0x0, 0xf, 0x177, 0x32a,
    };
    private static int Lookup0(int i) => gaRoadCornerTable[i];
    private static int Lookup1(int i) => gaRoadCornerTable[i + 4];

    // gRoadPresetIdLookupTable @0x6f06f0 (24 ints) — indexed (roadType-4 + dir*2) or
    // (roadType-6 + cornerType*2), which can reach -2; the two ints before the symbol
    // (0x6f06e8 = 0x177, 0x6f06ec = 0x32a) are the tail of gaRoadCornerTable. Stored from index
    // -2 with +2 bias.
    private const int ROADID_BIAS = 2;
    private static readonly int[] gRoadPresetIdLookupTable =
    {
        0x177, 0x32a, // idx -2, -1
        0x371, 0x3bd, 0x372, 0x3be, 0x373, 0x3bf, 0x374, 0x3c0,
        0x375, 0x3c1, 0x376, 0x3c2, 0x377, 0x3c3, 0x378, 0x3c4,
        0x379, 0x3c5, 0x37a, 0x3c6, 0x37b, 0x3c7, 0x37c, 0x3c8,
    };
    private static int RoadId(int i) => gRoadPresetIdLookupTable[i + ROADID_BIAS];

    // gnOutPlaceMax @0x6f1088 — direction-pair corner table indexed -40..+40; the engine symbol
    // is logical index 0 (the decompiler captured only the forward [41]). Full 81-entry range
    // recovered from 0x6f0fe8, stored with +40 bias.
    private const int OUTPLACEMAX_BIAS = 40;
    private static readonly int[] gnOutPlaceMax =
    {
        1, 9, 9, -1, -1, 1, 8, -1, -1, 12, -1, -1, -1, -1, 12, 4, -1, -1, 5, 2,
        2, 10, -1, -1, -1, -1, 10, 1, 9, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, // idx 0 (= bias 40)
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 11, 11, 3, 12, -1, -1, -1, -1, 12, 4,
        4, 7, -1, -1, 2, 10, -1, -1, -1, -1, 10, -1, -1, 6, 3, -1, -1, 11, 11, 3,
    };
    private static int OutPlaceMax(int i) => gnOutPlaceMax[i + OUTPLACEMAX_BIAS];

    // gnOutPlaceGridHeightByAct @0x6f112c interleaved with gnOutPlaceGridWidthByAct @0x6f1130:
    // the engine reads `(&height)[nIdx*2]` and `width[nIdx*2]`, i.e. the pair (height, width) at
    // index nIdx. 6 pairs from 0x6f112c.
    private static readonly int[] gaOutPlaceGridSizeByAct =
    {
        0x175, 0x174, // pair 0
        0x174, 0x177, // pair 1
        0x0, 0x0, // pair 2
        0x175, 0x176, // pair 3
        0x176, 0x177, // pair 4
        0x0, 0x1, // pair 5
    };
    private static int GridHeight(int nIdx) => gaOutPlaceGridSizeByAct[nIdx * 2];
    private static int GridWidth(int nIdx) => gaOutPlaceGridSizeByAct[nIdx * 2 + 1];

    private static readonly int[] aActTownLevelId = { 1, 40, 75, 103, 109, int.MaxValue };

    /// <summary>GetActNoFromLevelNumber   Drlg.cpp. aActTownLevelId[5] is read at index 1..5
    /// (OOB at [5]); the engine's table has a 6th sentinel so Act-V levels fall through to
    /// ACT_V.</summary>
    public static int GetActNoFromLevelNumber(int eLevel)
    {
        for (int i = 1; i < 6; i++)
            if (eLevel < aActTownLevelId[i]) return i - ACT_II;
        return ACT_I;
    }

    /// <summary>DRLGOUTROOM_GetGridDivisionSize   OutSiege.cpp</summary>
    private static int GetGridDivisionSize(D2DrlgLevelStrc* pLevel) => (pLevel->eD2LevelId == (int)AreaId.FrozenTundra ? 1 : 0) + 4;

    /// <summary>DRLGOUTPLACE_GetRoadPresetId   OutPlace.cpp:161</summary>
    public static int GetRoadPresetId(int nDirX, int nDirY, int nRoadType)
    {
        int dir = DirLookup(nDirX, nDirY);
        if (nRoadType <= 3) return Lookup1(nRoadType + dir * 4);
        return RoadId(nRoadType - 4 + dir * 2);
    }

    /// <summary>DRLGOUTPLACE_GetAdjacentRoadPresetId   OutPlace.cpp:171</summary>
    public static int GetAdjacentRoadPresetId(int nDirXIn, int nDirY, int nPrevDirXIn, int nPrevDirY, int nRoadType)
    {
        int nDirX = nDirXIn;
        int nPrevDirX = nPrevDirXIn;
        if (nDirX < 0) nDirX += -2;
        else if (nDirX > 0) nDirX += 2;
        if (nPrevDirX < 0) nPrevDirX += -2;
        else if (nPrevDirX > 0) nPrevDirX += 2;
        int nTableIndex = OutPlaceMax(nDirX + (nPrevDirX + nPrevDirY) * 9 + nDirY);
        if (nTableIndex == -1) return 0;
        if (nRoadType <= 3) return Lookup0(nRoadType + nTableIndex * 4);
        return RoadId(nRoadType - 6 + nTableIndex * 2);
    }

    /// <summary>The road switch of PlaceAct1245OutdoorBorders; null = default (goto caseD_3:
    /// exit, no border work).</summary>
    private static int? RollRoadType(D2DrlgLevelStrc* pLevel, int nDirection) => pLevel->nLevelType switch
    {
        2 => nDirection == 0 ? 1 : 0, // !nDirection
        0x10 => 2,
        0x1b => 3,
        0x1f => GetGridDivisionSize(pLevel),
        _ => null,
    };

    private static int Iabs(int v) => v < 0 ? -v : v;
    private static int Imin(int a, int b) => a < b ? a : b;

    // SetBlankBorderGridCells   OutPlace.cpp:196 (1.14d 00675670). The recon rendered the 4x4
    // (useMaxX, useMaxY, stepX, stepY) direction table as adjacent stack locals with nDir3StepY
    // dropped; recovered from the disassembly @0x675692:
    //   dir0(0,0,1,1) dir1(1,0,-1,1) dir2(0,1,1,-1) dir3(1,1,-1,-1)
    private static readonly int[,] gaBlankBorderDirs = { { 0, 0, 1, 1 }, { 1, 0, -1, 1 }, { 0, 1, 1, -1 }, { 1, 1, -1, -1 } };

    /// <summary>SetBlankBorderGridCells: marks the outer blank-border cells of sGridOutdoor with
    /// 0x100 (so they are skipped during the grid->RoomEx conversion).</summary>
    public static void SetBlankBorderGridCells(D2DrlgLevelStrc* pLevel)
    {
        var pData = Wild(pLevel);
        var pGrid = &pData->sGridOutdoor;
        for (int di = 0; di < 4; di++)
        {
            int useMaxX = gaBlankBorderDirs[di, 0], useMaxY = gaBlankBorderDirs[di, 1];
            int stepX = gaBlankBorderDirs[di, 2], stepY = gaBlankBorderDirs[di, 3];
            int nGridX = useMaxX != 0 ? pData->nGridCoordsWidth - 1 : 0;
            int nBorderX = nGridX;
            int nY = useMaxY != 0 ? pData->nGridCoordsHeight - 1 : 0;
            int nGridFlags = DrlgGrid.GetGridFlags(pGrid, nGridX, nY);
            while ((nGridFlags & 1) == 0)
            {
                nGridFlags = DrlgGrid.GetGridFlags(pGrid, nBorderX, nY);
                int nScanX = nGridX;
                nGridX = nBorderX;
                while (true)
                {
                    nBorderX = nGridX;
                    if ((nGridFlags & 1) != 0) break;
                    DrlgGrid.AlterGridFlag(pGrid, nScanX, nY, 0x100, OP_OR);
                    nScanX = nScanX + stepX;
                    nGridFlags = DrlgGrid.GetGridFlags(pGrid, nScanX, nY);
                    nGridX = nBorderX;
                }
                nY += stepY;
                nGridFlags = DrlgGrid.GetGridFlags(pGrid, nGridX, nY);
            }
        }
    }

    /// <summary>DRLGOUTDOOR_GetAdjacentLevelVisMask   Outdoors.cpp:360. pOrthLevel is the orth's
    /// linked D2DrlgLevelStrc (nType==0); +0x1d0 = eD2LevelId.</summary>
    private static int GetAdjacentLevelVisMask(D2DrlgLevelStrc* pLevel, D2DrlgLevelStrc* pOrthLevel)
    {
        int nIndex = 0;
        for (; nIndex < 8; nIndex++)
        {
            int* pVisArray = DrlgRoom.GetVisArrayFromLevelId(pLevel->pDrlg, pLevel->eD2LevelId);
            if (pVisArray[nIndex] == pOrthLevel->eD2LevelId) break;
        }
        if (nIndex != 8) return 1 << ((nIndex + 4) & 0x1f);
        return 0;
    }

    // GetOutLinkVisFlag   Outdoors.cpp:380. gnOutdoorsGridHeightByLevel @0x6f05d8 (-4) is
    // contiguous with gnOutdoorsPresetOffsetByLevel @0x6f05dc; the engine reads
    // `(&height)[eDir*2]` and `presetOffset[eDir*2]` = the interleaved pair. Full 8-int block
    // from 0x6f05d8 (Ghidra 1.14d session 62fbfe69).
    private static readonly int[] gaOutdoorsLinkOffsets = { -4, 4, 4, -4, 12, 4, 4, 12 };

    private static int GetOutLinkVisFlag(D2DrlgLevelStrc* pLevel, D2DrlgVertexStrc* pDrlgVertex)
    {
        var pData = Wild(pLevel);
        int nPosY = pDrlgVertex->nPosY;
        int nPosX = pDrlgVertex->nPosX;
        int eEdgeDir;
        if (nPosX == 0)
        {
            eEdgeDir = nPosY == 0 ? 1 : 0;
        }
        else
        {
            int nGridWidth = pData->nGridCoordsWidth - 1;
            if (nPosY == 0) eEdgeDir = (nPosX == nGridWidth ? 1 : 0) + DIRECTION_NORTHEAST;
            else if (nPosX == nGridWidth) eEdgeDir = (nPosY == pData->nGridCoordsHeight - 1 ? 1 : 0) + DIRECTION_SOUTHWEST;
            else
            {
                if (nPosY != pData->nGridCoordsHeight - 1) return 0;
                eEdgeDir = DIRECTION_NORTHWEST;
            }
        }

        int nGridHeight = gaOutdoorsLinkOffsets[eEdgeDir * 2];
        int nOrthMatchOffset = gaOutdoorsLinkOffsets[eEdgeDir * 2 + 1];
        int nPresetOffsetX = pLevel->sCoordinatesAndSize.WorldPosition.x;
        int nWorldPosY = pLevel->sCoordinatesAndSize.WorldPosition.y;
        for (var orth = pData->pOrthData; orth != null; orth = orth->pNext)
        {
            uint bInBounds = DrlgRoom.AreXYInsideCoordinates(orth->psCoordinatesAndSize,
                nGridHeight + nPosX * 8 + nPresetOffsetX, nOrthMatchOffset + nPosY * 8 + nWorldPosY);
            if (orth->neDrlgDirection == eEdgeDir && bInBounds != 0)
            {
                if (orth->nType != 0) return 0;
                return GetAdjacentLevelVisMask(pLevel, (D2DrlgLevelStrc*)orth->pRoomEx);
            }
        }
        return 0;
    }

    /// <summary>SetOutGridLinkFlags   OutPlace.cpp:268 (1.14d 00675770). No RNG. For each vertex
    /// with dwFlags&amp;1, writes vis flags into sGridLink and a direction code into sGridOutdoor
    /// along the vertex edge.</summary>
    public static void SetOutGridLinkFlags(D2DrlgLevelStrc* pLevel)
    {
        var pData = Wild(pLevel);
        var pHead = pData->pVertices;
        if (pHead == null) return;
        var pVertex = pHead;
        var pNext = pHead->pNext;
        while (true)
        {
            if ((pVertex->dwFlags & 1) != 0)
            {
                int nLinkVisFlag = GetOutLinkVisFlag(pLevel, pVertex);
                int byDirection = pVertex->nDirection;
                DrlgGrid.SetEdgeGridFlags(&pData->sGridLink, pVertex, nLinkVisFlag, 0, true);
                DrlgGrid.SetEdgeGridFlags(&pData->sGridOutdoor, pVertex, byDirection * 2 + 1, 0, true);
            }
            bool bHasNext = pNext != pHead;
            pVertex = pNext;
            pNext = pNext->pNext;
            if (!bHasNext) break;
        }
    }

    /// <summary>DRLGOUTROOM_MarkBorderJunctions   Drlg.cpp:6160. No RNG. Marks runs of border
    /// vertices forming a convex junction by setting nDirection=1 on them (consumed by
    /// PlaceAct1245OutdoorBorders' nBorderFlags).</summary>
    public static void MarkBorderJunctions(D2DrlgLevelStrc* pLevel)
    {
        var pData = Wild(pLevel);
        var pCurrent = pData->pVertices;
        bool bComplete = false;
        // pPrevious = the vertex whose pNext is pCurrent (i.e. last in the ring).
        var pPrevious = pCurrent;
        for (var p = pCurrent->pNext; p != pCurrent; p = p->pNext) pPrevious = p;
        var pNext = pCurrent;
        while (true)
        {
            var pStart = pCurrent;
            bool cond1 = pCurrent->nPosX < pCurrent->pNext->nPosX && pCurrent->nPosY < pPrevious->nPosY && (pCurrent->dwFlags & 1) == 0 && (pPrevious->dwFlags & 1) == 0;
            bool cond2 = pCurrent->pNext->nPosY < pCurrent->nPosY && pCurrent->nPosX < pPrevious->nPosX && (pCurrent->dwFlags & 1) == 0 && (pPrevious->dwFlags & 1) == 0;
            pNext = pCurrent;
            if (cond1 || cond2)
            {
                D2DrlgVertexStrc* pLast = null;
                pNext = pCurrent;
                while (true)
                {
                    if (pNext == pStart) bComplete = true;
                    var pN = pNext->pNext;
                    if (pNext->nPosY < pN->nPosY || pN->nPosX < pNext->nPosX || (pNext->dwFlags & 1) != 0 || (pN->dwFlags & 1) != 0) break;
                    bool a = pNext->nPosX < pN->nPosX && pN->nPosY < pN->pNext->nPosY && (pN->dwFlags & 1) == 0;
                    bool b = pN->nPosY < pNext->nPosY && pN->nPosX < pN->pNext->nPosX && (pN->dwFlags & 1) == 0;
                    if (a || b) pLast = pNext;
                    pNext = pN;
                    if (pN == pCurrent) break;
                }
                if (pLast != null)
                {
                    for (; pCurrent != pLast; pCurrent = pCurrent->pNext) pCurrent->nDirection = 1;
                    pCurrent->nDirection = 1;
                    pData->dwFlags |= 0x20;
                }
            }
            pCurrent = pNext->pNext;
            if (bComplete) return;
            pPrevious = pNext;
            if (pCurrent == pData->pVertices) return;
        }
    }

    /// <summary>PlaceAct1245OutdoorBorders   OutPlace.cpp:291 (1.14d 00675850). Walks the boundary
    /// vertex ring; for each edge places the road/border presets (SpawnOutdoorLevelPresetEx -1,
    /// the RNG consumer) and at junction vertices the corner presets, then marks the blank
    /// border. Acts 1/2/4/5; Act 3 has its own.</summary>
    public static void PlaceAct1245OutdoorBorders(D2DrlgLevelStrc* pLevel)
    {
        var pData = Wild(pLevel);
        int eActNo = GetActNoFromLevelNumber(pLevel->eD2LevelId);
        var pHead = pData->pVertices;
        var pCurVertex = pHead;
        var pNextVertex = pHead->pNext;
        var pGrid = &pData->sGridOutdoor;

        while (true)
        {
            int nDx, nDy, nNextDx, nNextDy;
            DrlgVer.GetCoordDiff(pCurVertex, &nDx, &nDy);
            DrlgVer.GetCoordDiff(pNextVertex, &nNextDx, &nNextDy);
            int nAbsDx = Iabs(nDx);
            int nAbsDy = Iabs(nDy);

            int curX = pCurVertex->nPosX;
            int curY = pCurVertex->nPosY;
            int nX = pNextVertex->nPosX;
            int nY = pNextVertex->nPosY;
            int nEdgeLen = nAbsDx == 0 ? Iabs(curY - nY) : Iabs(curX - nX);

            int nDirection = pCurVertex->nDirection;
            int nBorderFlags = nDirection * 2 + 1;

            // First road switch — default exits the whole function.
            if (RollRoadType(pLevel, nDirection) is not int nRoadType) return;

            int nPresetId = nRoadType < 4 ? Lookup1(nRoadType + DirLookup(nDx, nDy) * 4) : RoadId(nRoadType - 4 + DirLookup(nDx, nDy) * 2);

            // Place the road segment along the edge.
            if ((pCurVertex->dwFlags & 2) == 0)
            {
                int runX = curX;
                int runY = curY;
                while (runX != nX || runY != nY)
                {
                    runY += nDy;
                    runX += nDx;
                    OutPlace.SpawnOutdoorLevelPresetEx(pLevel, runX, runY, nPresetId, -1, 0);
                    DrlgGrid.AlterGridFlag(pGrid, runX, runY, nBorderFlags, OP_OR);
                }
            }

            // Junction corner handling.
            if ((pCurVertex->dwFlags & 1) != 0 && (pCurVertex->dwFlags & 2) == 0)
            {
                int cx = Imin(pCurVertex->nPosX, pNextVertex->nPosX) + nEdgeLen * nAbsDx / 2;
                int cy = Imin(pCurVertex->nPosY, pNextVertex->nPosY) + nEdgeLen * nAbsDy / 2;
                switch (eActNo)
                {
                    case ACT_I:
                    case ACT_V:
                        DrlgGrid.AlterGridFlag(pGrid, cx, cy, 0xf0000, OP_ANDNEG);
                        DrlgGrid.AlterGridFlag(pGrid, cx, cy, pLevel->eD2LevelId == (int)AreaId.BurialGrounds ? 0x40400 : 0x30400, OP_OR);
                        break;
                    case ACT_II:
                        {
                            int nIdx = nDx + 2 + nDy * 2;
                            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, cx, cy, GridHeight(nIdx), -1, 0);
                            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, cx + nAbsDx, cy + nAbsDy, GridWidth(nIdx), -1, 0);
                            break;
                        }
                    case ACT_IV:
                        DrlgGrid.AlterGridFlag(pGrid, cx, cy, 0xf0000, OP_ANDNEG);
                        DrlgGrid.AlterGridFlag(pGrid, cx, cy, 0x30400, OP_OR);
                        break;
                }
            }

            // Junction border flag bit 2.
            int nDirJunction = nDirection;
            if (nDirJunction == 0) nDirJunction = pNextVertex->nDirection;
            if (nDirJunction != 0) nBorderFlags |= 2;

            // Second road switch (road type B) — default exits.
            if (RollRoadType(pLevel, nDirJunction) is not int nRoadTypeB) return;

            // Corner road preset (between this edge and the next).
            int nCornerPresetId = 0;
            bool nValid = false;
            if ((pCurVertex->dwFlags & 2) == 0)
            {
                int idxA, idxB; // idxA = the "*9" multiplied component
                if ((pNextVertex->dwFlags & 2) == 0)
                {
                    idxA = nNextDx * 2;
                    if (idxA < 0) idxA += -2;
                    else if (idxA > 0) idxA += 2;
                    idxA += nNextDy * 2;
                    idxB = nDx * 2;
                    if (idxB < 0) idxB += -2;
                    else if (idxB > 0) idxB += 2;
                }
                else
                {
                    idxB = nDx * 2;
                    if (idxB < 0) idxB += -2;
                    else if (idxB > 0) idxB += 2;
                    if (nNextDx < 0)
                    {
                        idxA = nNextDx - 2 + nNextDy;
                    }
                    else
                    {
                        idxA = nNextDx;
                        if (nNextDx > 0) idxA = nNextDx + 2;
                        idxA += nNextDy;
                    }
                }
                int lk = OutPlaceMax(idxB + idxA * 9 + nDy * 2);
                if (lk != -1)
                {
                    nCornerPresetId = nRoadTypeB < 4 ? Lookup0(nRoadTypeB + lk * 4) : RoadId(nRoadTypeB - 6 + lk * 2);
                    nValid = true;
                }
            }
            else
            {
                int idxA = nDx; // the non-*9 component here
                int idxB; // the "*9" component
                if ((pNextVertex->dwFlags & 2) == 0)
                {
                    idxB = nNextDx * 2;
                    if (nDx < 0) idxA = nDx - 2;
                    else if (nDx > 0) idxA = nDx + 2;
                    if (idxB < 0) idxB += -2;
                    else if (idxB > 0) idxB += 2;
                    idxB += nNextDy * 2;
                }
                else
                {
                    if (nDx < 0) idxA = nDx - 2;
                    else if (nDx > 0) idxA = nDx + 2;
                    if (nNextDx < 0)
                    {
                        idxB = nNextDx - 2 + nNextDy;
                    }
                    else
                    {
                        idxB = nNextDx;
                        if (nNextDx > 0) idxB = nNextDx + 2;
                        idxB += nNextDy;
                    }
                }
                int lk = OutPlaceMax(idxA + idxB * 9 + nDy);
                if (lk != -1)
                {
                    nCornerPresetId = nRoadTypeB < 4 ? Lookup0(nRoadTypeB + lk * 4) : RoadId(nRoadTypeB - 6 + lk * 2);
                    nValid = true;
                }
            }

            if (nValid)
            {
                bool doSpawn = true;
                if (nCornerPresetId == 0x13)
                {
                    if (pCurVertex->nDirection != 0) nCornerPresetId = (pNextVertex->nDirection == 0 ? 1 : 0) + 0x13;
                    else nCornerPresetId = 0x15;
                }
                else if (nCornerPresetId == 0)
                {
                    doSpawn = false;
                }
                if (doSpawn)
                {
                    OutPlace.SpawnOutdoorLevelPresetEx(pLevel, nX, nY, nCornerPresetId, -1, 0);
                    DrlgGrid.AlterGridFlag(pGrid, nX, nY, nBorderFlags, OP_OR);
                }
            }

            bool bIsLast = pNextVertex == pHead;
            pCurVertex = pNextVertex;
            pNextVertex = pNextVertex->pNext;
            if (bIsLast)
            {
                SetBlankBorderGridCells(pLevel);
                return;
            }
        }
    }
}

// Port of libd2 packages/drlg/src/drlg/DrlgRoom.zig, DrlgWarp.zig, RoomTile.zig and the
// FreeDrlgCoordList entry of DrlgLogic.zig (MIT, (c) 2026 jaenster).
//
// Faithful transform of recon/closure/DrlgRoom.cpp (D2Common::Drlg::DrlgRoom). Only the
// functions the generator reaches are ported; the recon's runtime-only entry points (the
// warp-tile nav functions that use i32 fields as 32-bit pointers, the spawn-point lookups and
// the Dungeon/D2Game cross-module calls) are left out, as libd2 leaves them panic-stubs.

namespace D2Dotnet.Maps.Engine;

/// <summary>D2RoomStrc.h — only its layout matters to the generator: ReorderNearRoomList reads
/// these fields through a pointer that actually addresses a D2RoomExStrc, exactly as the
/// transformed closure does.</summary>
internal unsafe struct D2RoomStrc
{
    public void* ppRoomList;
    public uint nClientsMax;
    public void* pRoomTiles;
    public int nIdleTicks;
    public D2RoomExStrc* pRoomEx;
    public int dwUnk0x14;
    public void* pDrlgDelete;
    public void* pUnitUpdate;
    public void* pCollisionGrid;
    public uint nRoomListCount;
    public int nAllies;
    public void* pDrlgAct;
    public int dwUnk0x30;
    public int eFlags;
    public int dwUnk0x38;
    public int dwUnk0x3C;
    public int dwUnk0x40;
    public int dwUnk0x44;
    public byte* pClients;
    public D2DrlgRoomCoordsStrc sCoords;
    public D2SeedStrc sSeed;
    public void* pUnitList;
    public int nClientsCount;
    public D2RoomStrc* pDrlgRoomNext;
}

/// <summary>Unrecoverable DRLG internal error (engine: Fog::ErrorManager halt).</summary>
internal sealed class DrlgException : Exception
{
    public DrlgException(string message) : base(message) { }
}

internal static unsafe class DrlgRoom
{
    private const int LEVEL_None = 0;
    private const int UNIT_OBJECT = 2;
    private const int UNIT_WARP = 5;
    private const int DIRECTION_SOUTHEAST = 0;
    private const int DIRECTION_NORTHEAST = 1;
    private const int DIRECTION_SOUTHWEST = 2;
    private const int DIRECTION_NORTHWEST = 3;

    internal static Exception RaiseError(int nLine) => new DrlgException($"DRLG unrecoverable internal error (recon 0x{nLine:x})");

    /// <summary>SBORROW4(a,b): signed overflow of (a - b) (decompiler flag intrinsic).</summary>
    private static bool SBORROW4(int a, int b) => ((a ^ b) & (a ^ unchecked(a - b))) < 0;

    /// <summary>OutRoom.cpp DRLGOUTROOM_AllocOutdoorRoomData (1.14d 0067d6d0). Allocates the
    /// RoomEx outdoor/maze data block (0x70 in 32-bit = D2DrlgRoomExDataMazeStrc), zeroed.</summary>
    private static void OutRoom_AllocOutdoorRoomData(D2RoomExStrc* pRoomEx)
    {
        void* p = Pool.AllocServerMemory(pRoomEx->pLevel->pDrlg->pMemoryPool, (nuint)sizeof(D2DrlgRoomExDataMazeStrc));
        pRoomEx->pRoomExData = p;
    }

    /// <summary>OutRoom.cpp DRLGOUTROOM_FreeDrlgOutdoorRoomData (1.14d 0067d610).</summary>
    private static void OutRoom_FreeDrlgOutdoorRoomData(D2RoomExStrc* pRoomEx)
    {
        var pData = (D2DrlgRoomExDataMazeStrc*)pRoomEx->pRoomExData;
        void* pMemory = pRoomEx->pLevel->pDrlg->pMemoryPool;
        if (pData == null) return;
        DrlgGrid.FreeGrid(pMemory, &pData->pOrientationGrid);
        DrlgGrid.FreeGrid(pMemory, &pData->pWallGrid);
        DrlgGrid.FreeGrid(pMemory, &pData->pFloorGrid);
        DrlgGrid.FreeGrid(pMemory, &pData->pCellGrid);
        DrlgVer.FreeVertices(pMemory, (D2DrlgVertexStrc**)&pData->pVertex);
        Pool.FreeServerMemory(pMemory, pData);
        pRoomEx->pRoomExData = null;
    }

    /// <summary>Preset.cpp:783 — alloc 0xf8 (D2DrlgPresetRoomStrc), zeroed.</summary>
    private static void Preset_AllocRoomExDataPresetArea(D2RoomExStrc* pRoomEx)
    {
        pRoomEx->pRoomExData = Pool.AllocServerMemory(pRoomEx->pLevel->pDrlg->pMemoryPool, (nuint)sizeof(D2DrlgPresetRoomStrc));
    }

    /// <summary>Preset.cpp:755. DRLGPRESET_FreePresetRoomGrids operates on a global grid
    /// free-list (empty in the standalone port) -> omitted.</summary>
    private static void Preset_FreeRoomExDataPresetArea(D2RoomExStrc* pRoomEx)
    {
        var pData = (D2DrlgPresetRoomStrc*)pRoomEx->pRoomExData;
        void* pMemory = pRoomEx->pLevel->pDrlg->pMemoryPool;
        if (pData == null) return;
        if (pData->pNavigationPoints != null) Pool.FreeServerMemory(pMemory, pData->pNavigationPoints);
        Pool.FreeServerMemory(pMemory, pData);
        pRoomEx->pRoomExData = null;
    }

    /// <summary>Preset.cpp:1572. pPath = { count; pEntries }: free entries ptr + path head.</summary>
    private static void Preset_FreeDrlgPresetUnit(void* pMemory, D2PresetUnitStrc* pPresetUnit)
    {
        void* pPath = pPresetUnit->pPath;
        if (pPath != null)
        {
            void** pEntriesPtr = (void**)((byte*)pPath + 4);
            Pool.FreeServerMemory(pMemory, *pEntriesPtr);
            Pool.FreeServerMemory(pMemory, pPath);
        }
        Pool.FreeServerMemory(pMemory, pPresetUnit);
    }

    /// <summary>DrlgRoom.cpp:116 (1.14d 0066ab70). +0x4c == pTileGrid, walked via pAnimTiles.</summary>
    public static void SetAllWarpTilesSelectValue(D2RoomExStrc* pRoomEx, uint nSelectValue)
    {
        for (var pWarpTile = pRoomEx->pTileGrid; pWarpTile != null; pWarpTile = (D2DrlgTileGridStrc*)pWarpTile->pAnimTiles)
            pWarpTile->nWalls = (int)nSelectValue;
    }

    /// <summary>DrlgRoom.cpp:360 (1.14d 0066aec0).</summary>
    public static int* GetWarpsIdIfExists(D2DrlgStrc* pDrlg, int eLevel)
    {
        var pWarpsInfo = pDrlg->pWarpsInfo;
        while (true)
        {
            if (pWarpsInfo == null)
            {
                var pLevelDefs = DrlgTables.LevelDefsGetLine(eLevel);
                return &pLevelDefs->Warp[0];
            }
            if (pWarpsInfo->nLevelId == LEVEL_None) break;
            if (eLevel == pWarpsInfo->nLevelId) return &pWarpsInfo->nWarpId[0];
            pWarpsInfo = pWarpsInfo->pNext;
        }
        throw RaiseError(0x1a2);
    }

    /// <summary>DrlgRoom.cpp:385 (1.14d 0066af50). The recon's "returns uninitialised local" was a
    /// decompiler artifact for an EAX PASSTHROUGH: TXT_LvlWarp_Setup 0x61f310 leaves the matched
    /// LvlWarp ROW pointer in EAX, and this function returns it. We return the 1-based LvlWarp row
    /// index (fits the node's i32 slot on 64-bit builds).</summary>
    public static int GetWarpsIdIfExistsAndSetup(D2DrlgLevelStrc* pLevel, byte nIdIndex, byte nDirection)
    {
        int* pWarpIdArray = GetWarpsIdIfExists(pLevel->pDrlg, pLevel->eD2LevelId);
        return DrlgTables.LvlWarpSetupIndex(pWarpIdArray[nIdIndex], nDirection);
    }

    /// <summary>DrlgWarp.cpp:21 (1.14d 0066af30)</summary>
    public static int GetWarpDestinationFromArray(D2DrlgLevelStrc* pLevel, byte nWarpIndex)
    {
        int* pWarpIdArray = GetWarpsIdIfExists(pLevel->pDrlg, pLevel->eD2LevelId);
        return pWarpIdArray[nWarpIndex];
    }

    /// <summary>DrlgRoom.cpp:552 (1.14d 0066b1f0).</summary>
    public static D2RoomExStrc* ValidateWarpFlagsAgainstVis(D2DrlgLevelStrc* pLevel)
    {
        var pRoomEx = pLevel->pRoomExFirst;
        if (pRoomEx == null) return null;
        while (true)
        {
            if (pRoomEx->eRoomExFlags.AnyWarp())
            {
                uint nRoomFlagMask = 0x10;
                uint nWarpIndex = 0;
                while (true)
                {
                    if (pRoomEx->eRoomExFlags.WarpSlot((int)nWarpIndex))
                    {
                        int* pWarpArray = null;
                        bool found = false;
                        for (var pWarpsInfo = pLevel->pDrlg->pWarpsInfo; pWarpsInfo != null; pWarpsInfo = pWarpsInfo->pNext)
                        {
                            if (pLevel->eD2LevelId == pWarpsInfo->nLevelId)
                            {
                                pWarpArray = &pWarpsInfo->nWarpId[0];
                                found = true;
                                break;
                            }
                        }
                        if (!found) pWarpArray = &DrlgTables.LevelDefsGetLine(pLevel->eD2LevelId)->Warp[0];
                        if (pWarpArray[nWarpIndex] != -1) return pRoomEx;
                    }
                    nRoomFlagMask *= 2;
                    nWarpIndex = (byte)(nWarpIndex + 1);
                    if ((nRoomFlagMask & 0xff0) == 0) break;
                }
            }
            pRoomEx = pRoomEx->pRoomExNext;
            if (pRoomEx == null) return null;
        }
    }

    // DrlgRoom.cpp:655 (1.14d 0066b3e0). Alloc size 0xec -> sizeof(D2RoomExStrc).
    // Line 660: `*(uint8_t*)&fRoomStatus = 4` after a full zero -> `fRoomStatus = 4`.
    public static D2RoomExStrc* AllocRoomEx(D2DrlgLevelStrc* pLevel, int nPresetType)
    {
        var pRoomEx = (D2RoomExStrc*)Pool.AllocServerMemory(pLevel->pDrlg->pMemoryPool, (nuint)sizeof(D2RoomExStrc));
        pRoomEx->nPresetType = nPresetType;
        pRoomEx->pLevel = pLevel;
        pRoomEx->fRoomStatus = 4; // line 660
        var sNewSeed = DrlgRng.SeedNext(pLevel->sSeed);
        D2SeedStrc* pSeedPtr = &pRoomEx->sSeed;
        pLevel->sSeed = sNewSeed;
        pSeedPtr->nSeedLow = sNewSeed.nSeedLow;
        pRoomEx->sSeed.nSeedHigh = 0x29a;
        sNewSeed = DrlgRng.SeedNext(*pSeedPtr);
        *pSeedPtr = sNewSeed;
        pRoomEx->nSeed = sNewSeed.nSeedLow;
        if ((pLevel->dwFlags & 0x10) != 0) pRoomEx->eRoomExFlags.MapReveal = true;
        // Recon AllocRoomEx flattened the original switch to `if(nPresetType){outdoor} else
        // if(==2){preset}` — making the preset arm dead. Faithful-to-engine: type 2 (maze +
        // preset rooms) takes the 0xf8 preset block; type 1 the outdoor block.
        if (nPresetType == 2)
        {
            Preset_AllocRoomExDataPresetArea(pRoomEx);
            return pRoomEx;
        }
        else if (nPresetType != 0)
        {
            OutRoom_AllocOutdoorRoomData(pRoomEx);
        }
        return pRoomEx;
    }

    /// <summary>DrlgRoom.cpp:698 (1.14d 0066b4f0).</summary>
    public static void FreeDrlgRoomTile(void* pMemory, D2RoomExStrc* pRoomEx)
    {
        var pTileGrid = pRoomEx->pTileGrid;
        while (pTileGrid != null)
        {
            var pNextTileGrid = (D2DrlgTileGridStrc*)pTileGrid->pAnimTiles;
            Pool.FreeServerMemory(pMemory, pTileGrid);
            pTileGrid = pNextTileGrid;
        }
        pRoomEx->pTileGrid = null;
    }

    /// <summary>DrlgRoom.cpp:712 (1.14d 0066b530).</summary>
    public static void FreeRoomData(void* pMemory, D2DrlgOrthStrc* pDrlgRoomData)
    {
        var p = pDrlgRoomData;
        while (p != null)
        {
            var pNext = p->pNext;
            Pool.FreeServerMemory(pMemory, p);
            p = pNext;
        }
    }

    /// <summary>DrlgRoom.cpp:723 (1.14d 0066b560). Alloc 0x18 -> sizeof(D2DrlgOrthStrc).</summary>
    public static void AllocNodesFromRoomExToRoomEx(D2RoomExStrc* pRoomEx, D2RoomExStrc* pDrlgRoomEx2, int eDrlgDir)
    {
        var pOrthIter = pRoomEx->pOrth;
        while (true)
        {
            if (pOrthIter == null)
            {
                var pNewOrth = (D2DrlgOrthStrc*)Pool.AllocServerMemory(pRoomEx->pLevel->pDrlg->pMemoryPool, (nuint)sizeof(D2DrlgOrthStrc));
                pNewOrth->pRoomEx = null;
                pNewOrth->neDrlgDirection = DIRECTION_SOUTHEAST;
                pNewOrth->bIsDrlgTypePresetArea = 0;
                pNewOrth->nType = 0;
                pNewOrth->psCoordinatesAndSize = null;
                pNewOrth->pNext = null;
                pNewOrth->pNext = pRoomEx->pOrth;
                pRoomEx->pOrth = pNewOrth;
                pNewOrth->pRoomEx = pDrlgRoomEx2;
                pNewOrth->neDrlgDirection = eDrlgDir;
                pNewOrth->nType = 1;
                pNewOrth->psCoordinatesAndSize = &pDrlgRoomEx2->sCoords;
                return;
            }
            if (pOrthIter->pRoomEx == pDrlgRoomEx2) break;
            pOrthIter = pOrthIter->pNext;
        }
    }

    /// <summary>DrlgRoom.cpp:753 (1.14d 0066b5e0).</summary>
    public static void AllocNodesForBothRoomEx(D2RoomExStrc* pRoomExA, D2RoomExStrc* pRoomExB, int eDirection)
    {
        AllocNodesFromRoomExToRoomEx(pRoomExA, pRoomExB, eDirection);
        AllocNodesFromRoomExToRoomEx(pRoomExB, pRoomExA, unchecked(eDirection - 2) & 3);
    }

    /// <summary>DrlgRoom.cpp:760 (1.14d 0066b610).</summary>
    public static void FindReplaceAndFreeSub00VsTargetRoomEx(void* pMemory, D2RoomExStrc* pRoomEx, D2RoomExStrc* pDrlgRoomExTarget)
    {
        var pOrthCurrent = pRoomEx->pOrth;
        var pOrthNext = pOrthCurrent->pNext;
        if (pOrthCurrent->nType != 0 && pOrthCurrent->pRoomEx == pDrlgRoomExTarget)
        {
            pRoomEx->pOrth = pOrthNext;
        }
        else
        {
            var pOrthPrev = pOrthCurrent;
            if (pOrthNext == null) return;
            while (true)
            {
                pOrthCurrent = pOrthNext;
                if (!(pOrthCurrent->nType == 0 || pOrthCurrent->pRoomEx != pDrlgRoomExTarget)) break;
                pOrthNext = pOrthCurrent->pNext;
                pOrthPrev = pOrthCurrent;
                if (pOrthCurrent->pNext == null) return;
            }
            pOrthPrev->pNext = pOrthCurrent->pNext;
        }
        Pool.FreeServerMemory(pMemory, pOrthCurrent);
    }

    /// <summary>DrlgRoom.cpp:800 (1.14d 0066b6a0).</summary>
    public static int CompareByDirectionAndCoord(D2DrlgOrthStrc* pOrth1, D2DrlgOrthStrc* pOrth2)
    {
        int eDir1 = pOrth1->neDrlgDirection;
        if (eDir1 < pOrth2->neDrlgDirection) return 1;
        if (pOrth2->neDrlgDirection != eDir1) return 0;
        switch (eDir1)
        {
            case DIRECTION_SOUTHEAST:
                if (pOrth1->psCoordinatesAndSize->WorldPosition.y < pOrth2->psCoordinatesAndSize->WorldPosition.y) return 1;
                break;
            case DIRECTION_NORTHEAST:
                if (pOrth2->psCoordinatesAndSize->WorldPosition.x < pOrth1->psCoordinatesAndSize->WorldPosition.x) return 1;
                break;
            case DIRECTION_SOUTHWEST:
                if (pOrth2->psCoordinatesAndSize->WorldPosition.y < pOrth1->psCoordinatesAndSize->WorldPosition.y) return 1;
                break;
            case DIRECTION_NORTHWEST:
                if (pOrth1->psCoordinatesAndSize->WorldPosition.x < pOrth2->psCoordinatesAndSize->WorldPosition.x) return 1;
                break;
        }
        return 0;
    }

    /// <summary>DrlgRoom.cpp:837 (1.14d 0066b720).</summary>
    public static void ReplaceSub00(D2RoomExStrc* pRoomEx, D2DrlgOrthStrc* pOrth)
    {
        int bShouldInsertBefore;
        var pOrthPrev = pRoomEx->pOrth;
        if (pOrthPrev == null)
        {
            pRoomEx->pOrth = pOrth;
            return;
        }
        var pOrthNext = pOrthPrev->pNext;
        if (pOrthNext == null)
        {
            bShouldInsertBefore = CompareByDirectionAndCoord(pOrth, pOrthPrev);
            if (bShouldInsertBefore != 0)
            {
                pOrth->pNext = pOrthPrev;
                pRoomEx->pOrth = pOrth;
                return;
            }
        }
        else
        {
            while (true)
            {
                var pOrthIter = pOrthNext;
                bShouldInsertBefore = CompareByDirectionAndCoord(pOrth, pOrthIter);
                pOrthNext = pOrthIter;
                if (bShouldInsertBefore != 0) break;
                pOrthNext = pOrthIter->pNext;
                pOrthPrev = pOrthIter;
                if (pOrthNext == null) break;
            }
        }
        pOrth->pNext = pOrthNext;
        pOrthPrev->pNext = pOrth;
    }

    /// <summary>DrlgRoom.cpp:874 (1.14d 0066b790). Alloc 0x18 -> sizeof(D2DrlgOrthStrc).</summary>
    public static void AllocDrlgOrth(D2RoomExStrc* pRoomEx, D2DrlgLevelStrc* pLevel, int neDrlgDirection, int bIsDrlgTypePresetArea)
    {
        var pNewOrth = (D2DrlgOrthStrc*)Pool.AllocServerMemory(pLevel->pDrlg->pMemoryPool, (nuint)sizeof(D2DrlgOrthStrc));
        pNewOrth->pNext = null;
        pNewOrth->pRoomEx = (D2RoomExStrc*)pLevel; // union: D2DrlgLevelStrc* when nType==0
        pNewOrth->neDrlgDirection = neDrlgDirection;
        pNewOrth->bIsDrlgTypePresetArea = bIsDrlgTypePresetArea;
        pNewOrth->nType = 0;
        pNewOrth->psCoordinatesAndSize = &pLevel->sCoordinatesAndSize;
        ReplaceSub00(pRoomEx, pNewOrth);
    }

    /// <summary>DrlgRoom.cpp:887 (1.14d 0066b800).</summary>
    public static uint IsWithinDistance(D2DrlgCoordsStrc* pCoords, D2DrlgCoordsStrc* pCoords2, int nSize, int* nX, int* nY)
    {
        int nX1 = pCoords->WorldPosition.x;
        int nX2 = pCoords2->WorldPosition.x;
        if (nX1 < nX2) *nX = nX2 - pCoords->WorldSize.x - nX1;
        else *nX = nX1 - pCoords2->WorldSize.x - nX2;
        nX1 = pCoords->WorldPosition.y;
        nX2 = pCoords2->WorldPosition.y;
        if (nX1 < nX2) *nY = nX2 - pCoords->WorldSize.y - nX1;
        else *nY = nX1 - pCoords2->WorldSize.y - nX2;
        if (!(*nX < nSize && *nY < nSize)) return 1;
        return 0;
    }

    /// <summary>DrlgRoom.cpp:912 (1.14d 0066b860). The recon aliases `(int*)&amp;pCoords1Copy`
    /// as the nX out-param (line 914); resolved to a discarded i32 scratch.</summary>
    public static int FreeRoomEx(D2DrlgCoordsStrc* pCoords1, D2DrlgCoordsStrc* pCoords2, int nDistance)
    {
        int nXScratch;
        int nDist = nDistance;
        return (int)IsWithinDistance(pCoords1, pCoords2, nDistance, &nXScratch, &nDist);
    }

    /// <summary>DrlgRoom.cpp:920 (1.14d 0066b880). The `(cond || (assign, cond))` comma idioms
    /// (lines 927/933) resolve to a simple if/else distance pick.</summary>
    public static int IsAdjacentOrOverlapping(D2DrlgCoordsStrc* pDrlgCoords, D2DrlgCoordsStrc* pCoordinatesAndSizeB, int nMaxDist)
    {
        int nPosA = pDrlgCoords->WorldPosition.x;
        int nPosB = pCoordinatesAndSizeB->WorldPosition.x;
        int nDistX = nPosB < nPosA ? nPosA - pCoordinatesAndSizeB->WorldSize.x - nPosB : nPosB - pDrlgCoords->WorldSize.x - nPosA;
        nPosA = pDrlgCoords->WorldPosition.y;
        nPosB = pCoordinatesAndSizeB->WorldPosition.y;
        int nDistY = nPosB < nPosA ? nPosA - pCoordinatesAndSizeB->WorldSize.y - nPosB : nPosB - pDrlgCoords->WorldSize.y - nPosA;

        bool bBorrow, bIsEqual;
        if (nMaxDist == 0)
        {
            if (0 < nDistX) return 0;
            bBorrow = false;
            bIsEqual = nDistY == 0;
        }
        else
        {
            if (nDistX == 0 && nDistY <= nMaxDist) return 1;
            if (nDistY != 0) return 0;
            bBorrow = SBORROW4(nDistX, nMaxDist);
            nDistY = unchecked(nDistX - nMaxDist);
            bIsEqual = nDistX == nMaxDist;
        }
        if (!(!bIsEqual && bBorrow == (nDistY < 0))) return 1;
        return 0;
    }

    /// <summary>DrlgRoom.cpp:964 (1.14d 0066b900).</summary>
    public static int ComputeRectanglesManhattanDistance(D2DrlgLevelStrc* pLevel, D2RoomExStrc* pRoomEx, D2RoomExStrc* pDrlgRoomEx2, int nDistance)
    {
        var pRoomExIter = pLevel->pRoomExFirst;
        while (true)
        {
            if (pRoomExIter == null) return 1;
            if (pRoomExIter != pRoomEx && pRoomExIter != pDrlgRoomEx2)
            {
                int nDistX = pRoomEx->sCoords.WorldPosition.x;
                int nDistY = pRoomExIter->sCoords.WorldPosition.x;
                if (nDistX < nDistY) nDistX = nDistY - pRoomEx->sCoords.WorldSize.x - nDistX;
                else nDistX = nDistX - pRoomExIter->sCoords.WorldSize.x - nDistY;
                nDistY = pRoomEx->sCoords.WorldPosition.y;
                int nPosY2 = pRoomExIter->sCoords.WorldPosition.y;
                if (nDistY < nPosY2) nDistY = nPosY2 - pRoomEx->sCoords.WorldSize.y - nDistY;
                else nDistY = nDistY - pRoomExIter->sCoords.WorldSize.y - nPosY2;
                if (nDistX < nDistance && nDistY < nDistance) return 0;
            }
            pRoomExIter = pRoomExIter->pRoomExNext;
        }
    }

    /// <summary>DrlgRoom.cpp:1000 (1.14d 0066b970). Line 1005 returns the previous list head as
    /// u32 (pointer-as-int artifact; callers only test truthiness).</summary>
    public static uint AddRoomExToLevel(D2DrlgLevelStrc* pLevelData, D2RoomExStrc* pRoomEx)
    {
        var pPrevFirst = pLevelData->pRoomExFirst;
        pRoomEx->pRoomExNext = pPrevFirst;
        pLevelData->nRoomExCount += 1;
        pLevelData->pRoomExFirst = pRoomEx;
        return pPrevFirst != null ? (uint)(nuint)pPrevFirst : 0;
    }

    /// <summary>DrlgRoom.cpp:1010 (1.14d 0066b980).</summary>
    public static uint AreXYInsideCoordinates(D2DrlgCoordsStrc* pDrlgCoords, int nX, int nY)
    {
        int nPosX = pDrlgCoords->WorldPosition.x;
        if (nX < nPosX) return 0;
        int nPosY = pDrlgCoords->WorldPosition.y;
        if (!(nPosY <= nY && nX < pDrlgCoords->WorldSize.x + nPosX)) return 0;
        return nY < pDrlgCoords->WorldSize.y + nPosY ? 1u : 0u;
    }

    /// <summary>DrlgRoom.cpp:1026 (1.14d 0066b9d0).</summary>
    public static uint IsPointInside(D2DrlgCoordsStrc* pDrlgCoords, int nX, int nY)
    {
        int nPosX = pDrlgCoords->WorldPosition.x;
        if (nX < nPosX) return 0;
        int nPosY = pDrlgCoords->WorldPosition.y;
        if (!(nPosY <= nY && nX <= pDrlgCoords->WorldSize.x + nPosX)) return 0;
        return nY <= pDrlgCoords->WorldSize.y + nPosY ? 1u : 0u;
    }

    /// <summary>DrlgRoom.cpp:1092 (1.14d 0066baa0).</summary>
    public static uint GetWaypointFlags(D2RoomExStrc* pRoomEx) => pRoomEx->eRoomExFlags.Raw & 0x30000;

    /// <summary>DrlgRoom.cpp:1173 (1.14d 0066bbc0). Recon param `D2RoomExStrc* pRoomEx` is the
    /// near-room COUNT; line 1177 `(uintptr_t)&amp;pRoomEx[-1].pStatusPrev + 3` == nCount - 1
    /// (32-bit sizeof artifact). The list is read through D2RoomStrc fields although it holds
    /// D2RoomExStrc pointers, exactly as the transformed recon does.</summary>
    public static void ReorderNearRoomList(int nCount, D2RoomStrc** ppRoomList)
    {
        int nListCount = nCount - 1;
        int nPassRemaining = nListCount;
        for (; 0 < nPassRemaining; nPassRemaining -= 1)
        {
            int nIndex = 0;
            if (0 < nListCount)
            {
                while (true)
                {
                    var pRoomNext = ppRoomList[nIndex + 1];
                    var pRoomCurrent = ppRoomList[nIndex];
                    if (unchecked(pRoomNext->dwUnk0x3C + pRoomNext->eFlags) <= pRoomCurrent->eFlags ||
                        unchecked(pRoomNext->dwUnk0x40 + pRoomNext->dwUnk0x38) <= pRoomCurrent->dwUnk0x38)
                    {
                        ppRoomList[nIndex] = pRoomNext;
                        ppRoomList[nIndex + 1] = pRoomCurrent;
                    }
                    nIndex += 1;
                    if (!(nIndex < nListCount)) break;
                }
            }
        }
    }

    /// <summary>DrlgRoom.cpp:1316 (1.14d 0066bda0). Pointer array realloc: `count * 4` ->
    /// `count * sizeof(ptr)` (8 in 64-bit).</summary>
    public static void ResizeArrayAndAddNewNearRoom(D2RoomExStrc* pDrlgRoomEx, void* pMemory, D2RoomExStrc* pRoomExStrc, D2RoomExStrc* pRoom)
    {
        pDrlgRoomEx->nDrlgRoomsExNearCount += 1;
        var ppNewNearRooms = (D2RoomExStrc**)Pool.ReAllocServerMemory(pMemory, pDrlgRoomEx->ppDrlgRoomsExNear, (nuint)(pDrlgRoomEx->nDrlgRoomsExNearCount * sizeof(void*)));
        pDrlgRoomEx->ppDrlgRoomsExNear = ppNewNearRooms;
        if (ppNewNearRooms == null) throw RaiseError(0x315);
        ppNewNearRooms[pDrlgRoomEx->nDrlgRoomsExNearCount - 1] = pRoom;
        ReorderNearRoomList(pDrlgRoomEx->nDrlgRoomsExNearCount, (D2RoomStrc**)pDrlgRoomEx->ppDrlgRoomsExNear);
    }

    /// <summary>DRLGROOMEX_LinkNearRoomsByVis (Drlg.cpp:1213, 1.14d 0066c2a0): for one of the
    /// room's vis slots, find the destination level's matching return-vis slot (by occurrence
    /// index when the warp id exists) and link via LinkNearRoomByDirection — which
    /// AllocRoomTile's the warp NODE onto this room's +0x4C chain. The engine calls
    /// InitLevel(pDstLevel) on demand.</summary>
    public static void LinkNearRoomsByVis(void* pMemory, D2RoomExStrc* pRoomEx, byte srcVisSlot)
    {
        if (pRoomEx == null || srcVisSlot > 7) return;
        var pSrcLevel = pRoomEx->pLevel;
        int srcLevelId = pSrcLevel->eD2LevelId;
        int* pSrcVisArray = GetVisArrayFromLevelId(pSrcLevel->pDrlg, srcLevelId);
        int dstLevelId = pSrcVisArray[srcVisSlot];
        var pDstLevel = Drlg.GetLevelAndAlloc(pSrcLevel->pDrlg, dstLevelId);
        int* pDstVisArray = GetVisArrayFromLevelId(pSrcLevel->pDrlg, dstLevelId);
        int warpDestLevel = GetWarpDestinationFromArray(pSrcLevel, srcVisSlot);
        if (pDstLevel->pRoomExFirst == null) Drlg.InitLevel(pDstLevel);

        if (warpDestLevel != -1)
        {
            // Occurrence-match: the Nth src-vis referencing dst pairs with the Nth dst-vis
            // referencing src.
            int nSrcOccurCountBeforeSlot = 0;
            for (uint i = 0; i < srcVisSlot; i++)
                if (pSrcVisArray[i] == dstLevelId) nSrcOccurCountBeforeSlot += 1;
            int nDstOccurrenceIndex = 0;
            for (uint nSlot = 0; nSlot < 8; nSlot++)
            {
                if (pDstVisArray[nSlot] == srcLevelId)
                {
                    if (nSrcOccurCountBeforeSlot == nDstOccurrenceIndex)
                    {
                        if (LinkNearRoomByDirection((byte)nSlot, pRoomEx, pMemory, srcVisSlot, warpDestLevel, pDstLevel->pRoomExFirst) != 0) return;
                        break;
                    }
                    nDstOccurrenceIndex += 1;
                }
            }
        }
        for (uint nSlot = 0; nSlot < 8; nSlot++)
        {
            if (pDstVisArray[nSlot] != srcLevelId) continue;
            if (LinkNearRoomByDirection((byte)nSlot, pRoomEx, pMemory, srcVisSlot, warpDestLevel, pDstLevel->pRoomExFirst) != 0) return;
        }
    }

    /// <summary>DrlgRoom.cpp:1336 (1.14d 0066be10). Alloc 0x18 -> sizeof(D2DrlgTileGridStrc)
    /// (warp node uses the first 0x18 bytes).</summary>
    public static void AllocRoomTile(D2RoomExStrc* pLinkedRoomEx, byte visSlot, D2RoomExStrc* pSrcRoomEx)
    {
        var pTileGrid = (D2DrlgTileGridStrc*)Pool.AllocServerMemory(pSrcRoomEx->pLevel->pDrlg->pMemoryPool, (nuint)sizeof(D2DrlgTileGridStrc));
        pTileGrid->pAnimTiles = null;
        pTileGrid->nWalls = 0;
        pTileGrid->nFloors = 0;
        pTileGrid->nShadows = 0;
        pTileGrid->pWallTiles = null;
        pTileGrid->pMapLinks = (D2DrlgTileLinkStrc*)pLinkedRoomEx;
        int nWarpId = GetWarpsIdIfExistsAndSetup(pSrcRoomEx->pLevel, visSlot, 0x62);
        pTileGrid->nShadows = nWarpId;
        pTileGrid->nWalls = 1;
        pTileGrid->pAnimTiles = (D2DrlgAnimTileGridStrc*)pSrcRoomEx->pTileGrid;
        pSrcRoomEx->pTileGrid = pTileGrid;
    }

    /// <summary>DrlgRoom.cpp:1358 (1.14d 0066be80).</summary>
    public static uint LinkNearRoomByDirection(byte dstVisSlot, D2RoomExStrc* pRoomEx, void* pMemory, byte srcVisSlot, int warpDestLevel, D2RoomExStrc* pDstRoomExIterIn)
    {
        var pDstRoomExIter = pDstRoomExIterIn;
        uint bLinkAdded = 0;
        while (true)
        {
            if (pDstRoomExIter == null) return bLinkAdded;
            if (pDstRoomExIter->eRoomExFlags.WarpSlot(dstVisSlot))
            {
                if (warpDestLevel != -1)
                {
                    ResizeArrayAndAddNewNearRoom(pRoomEx, pMemory, pDstRoomExIter, pDstRoomExIter);
                    AllocRoomTile(pDstRoomExIter, srcVisSlot, pRoomEx);
                    return 1;
                }
                int nGapX = pRoomEx->sCoords.WorldPosition.x;
                int nGapY = pDstRoomExIter->sCoords.WorldPosition.x;
                if (nGapX < nGapY) nGapX = nGapY - pRoomEx->sCoords.WorldSize.x - nGapX;
                else nGapX = nGapX - pDstRoomExIter->sCoords.WorldSize.x - nGapY;
                nGapY = pRoomEx->sCoords.WorldPosition.y;
                int nDstRoomY = pDstRoomExIter->sCoords.WorldPosition.y;
                if (nGapY < nDstRoomY) nGapY = nDstRoomY - pRoomEx->sCoords.WorldSize.y - nGapY;
                else nGapY = nGapY - pDstRoomExIter->sCoords.WorldSize.y - nDstRoomY;
                if (nGapX < 6 && nGapY < 6)
                {
                    ResizeArrayAndAddNewNearRoom(pRoomEx, pMemory, pDstRoomExIter, pDstRoomExIter);
                    bLinkAdded = 1;
                }
            }
            pDstRoomExIter = pDstRoomExIter->pRoomExNext;
        }
    }

    /// <summary>DrlgRoom.cpp:1403 (1.14d 0066bf30). Alloc 0x20 -> sizeof(D2PresetUnitStrc).</summary>
    public static D2PresetUnitStrc* AllocPresetUnit(D2RoomExStrc* pRoomEx, void* pMemory, int eUnitType, int nMonStatsId, int bUseMonStatsMaybe, int nPosX, int nPosY)
    {
        var pPresetUnit = (D2PresetUnitStrc*)Pool.AllocServerMemory(pMemory, (nuint)sizeof(D2PresetUnitStrc));
        pPresetUnit->pPresetUnitNext = null;
        pPresetUnit->pPath = null;
        pPresetUnit->nFlags = 0;
        pPresetUnit->nDbmCode = -1;
        pPresetUnit->eType = eUnitType;
        pPresetUnit->nClassId = nMonStatsId;
        pPresetUnit->nMode = bUseMonStatsMaybe;
        pPresetUnit->nPosX = nPosX;
        pPresetUnit->nPosY = nPosY;
        if (pRoomEx != null)
        {
            pPresetUnit->pPresetUnitNext = pRoomEx->pPresetUnit;
            pRoomEx->pPresetUnit = pPresetUnit;
            return pPresetUnit;
        }
        pPresetUnit->pPresetUnitNext = null;
        return pPresetUnit;
    }

    /// <summary>DrlgRoom.cpp:1425 (1.14d 0066bfa0).</summary>
    public static D2PresetUnitStrc* GetPresetUnits(D2RoomExStrc* pRoomEx)
    {
        var dwRoomExFlags = pRoomEx->eRoomExFlags;
        if (dwRoomExFlags.MapReveal) return pRoomEx->pPresetUnit;
        if (dwRoomExFlags.PresetsSpawned) return null;
        pRoomEx->eRoomExFlags.PresetsSpawned = true;
        return pRoomEx->pPresetUnit;
    }

    /// <summary>DrlgRoom.cpp:1456 (1.14d 0066c040).</summary>
    public static int* GetVisArrayFromLevelId(D2DrlgStrc* pDrlg, int eLevelId)
    {
        var pWarpsInfo = pDrlg->pWarpsInfo;
        while (true)
        {
            if (pWarpsInfo == null)
            {
                var pLevelDefs = DrlgTables.LevelDefsGetLine(eLevelId);
                return &pLevelDefs->Vis[0];
            }
            if (pWarpsInfo->nLevelId == LEVEL_None) break;
            if (eLevelId == pWarpsInfo->nLevelId) return &pWarpsInfo->nTargetArea[0];
            pWarpsInfo = pWarpsInfo->pNext;
        }
        throw RaiseError(0x410);
    }

    /// <summary>DrlgRoom.cpp:1503 (1.14d 0066c100). `goto LAB_0066c1e7` restructured by
    /// duplicating the nRoomExCount-- + break.</summary>
    public static void FreeDrlgRoomEx(D2RoomExStrc* pRoomEx)
    {
        void* pMemory = pRoomEx->pLevel->pDrlg->pMemoryPool;
        FreeDrlgRoomTile(pMemory, pRoomEx);
        if (pRoomEx->ppDrlgRoomsExNear != null)
        {
            Pool.FreeServerMemory(pMemory, pRoomEx->ppDrlgRoomsExNear);
            pRoomEx->nDrlgRoomsExNearCount = 0;
            pRoomEx->ppDrlgRoomsExNear = null;
        }
        // Faithful-to-engine (mirrors the AllocRoomEx switch): type 2 frees the preset block;
        // type 1 the outdoor block (the recon flattened this artifact too).
        if (pRoomEx->nPresetType == 2) Preset_FreeRoomExDataPresetArea(pRoomEx);
        else if (pRoomEx->nPresetType != 0) OutRoom_FreeDrlgOutdoorRoomData(pRoomEx);

        var pPresetIter = pRoomEx->pPresetUnit;
        while (pPresetIter != null)
        {
            var pPresetNext = pPresetIter->pPresetUnitNext;
            Preset_FreeDrlgPresetUnit(pMemory, pPresetIter);
            pPresetIter = pPresetNext;
        }

        var pOrthIter = pRoomEx->pOrth;
        while (true)
        {
            var pOrthCurrent = pOrthIter;
            if (pOrthCurrent == null) break;
            pOrthIter = pOrthCurrent->pNext;
            if (pOrthCurrent->nType != 0)
            {
                var pLinkedRoomEx = pOrthCurrent->pRoomEx;
                FindReplaceAndFreeSub00VsTargetRoomEx(pMemory, pRoomEx, pLinkedRoomEx);
                FindReplaceAndFreeSub00VsTargetRoomEx(pMemory, pLinkedRoomEx, pRoomEx);
            }
        }

        FreeRoomData(pMemory, pRoomEx->pOrth);
        var pLevel = pRoomEx->pLevel;
        var pRoomExSearch = pLevel->pRoomExFirst;
        if (pRoomExSearch == pRoomEx)
        {
            pLevel->pRoomExFirst = pRoomEx->pRoomExNext;
            pLevel->nRoomExCount -= 1;
        }
        else
        {
            var pRoomExIterNext = pRoomExSearch->pRoomExNext;
            while (pRoomExIterNext != null)
            {
                var pRoomExNext = pRoomExSearch->pRoomExNext;
                if (pRoomExNext == pRoomEx)
                {
                    pRoomExSearch->pRoomExNext = pRoomEx->pRoomExNext;
                    pLevel->nRoomExCount -= 1;
                    break;
                }
                pRoomExSearch = pRoomExNext;
                pRoomExIterNext = pRoomExNext->pRoomExNext;
            }
        }

        FreeRoomTilesAll(pRoomEx);
        FreeDrlgCoordList(pRoomEx);
        Pool.FreeServerMemory(pMemory, pRoomEx);
    }

    // ---- RoomTile.cpp ------------------------------------------------------------------

    /// <summary>RoomTile.cpp:1002. `&amp;pTileGrid->pWallTiles` is a field address (always
    /// non-null), so the guard reduces to pTileGrid != null.</summary>
    private static void FreeTileArrays(D2RoomExStrc* pLevelCtx, void* pPoolManagerStrc)
    {
        var pTileGrid = pLevelCtx->pRoomTiles;
        if (pTileGrid == null) return;
        if (pTileGrid->pWallTiles != null) Pool.FreeServerMemory(pPoolManagerStrc, pTileGrid->pWallTiles);
        if (pTileGrid->pFloorTiles != null) Pool.FreeServerMemory(pPoolManagerStrc, pTileGrid->pFloorTiles);
        if (pTileGrid->pRoofTiles == null) return;
        Pool.FreeServerMemory(pPoolManagerStrc, pTileGrid->pRoofTiles);
    }

    private static void FreeRoomTilesAll(D2RoomExStrc* pRoomEx)
    {
        void* pMemory = pRoomEx->pLevel->pDrlg->pMemoryPool;
        FreeTileArrays(pRoomEx, pMemory);
        var pTileGrid = pRoomEx->pRoomTiles;
        if (pTileGrid == null) return;
        var pTileLink = pTileGrid->pMapLinks;
        while (pTileLink != null)
        {
            var pNextTileLink = pTileLink->pNext; // line 1033: ->nNext == pNext
            Pool.FreeServerMemory(pMemory, pTileLink);
            pTileLink = pNextTileLink;
        }
        pTileGrid->pMapLinks = null;
        var pAnim = pTileGrid->pAnimTiles;
        while (pAnim != null)
        {
            var pNextAnim = pAnim->pNext;
            Pool.FreeServerMemory(pMemory, pAnim->ppTileData);
            Pool.FreeServerMemory(pMemory, pAnim);
            pAnim = pNextAnim;
        }
        Pool.FreeServerMemory(pMemory, pTileGrid);
        pRoomEx->pRoomTiles = null;
    }

    // ---- DrlgLogic.cpp -----------------------------------------------------------------

    /// <summary>DrlgLogic.cpp:58 (1.14d win 0066c6e0 | mac 002f331c)</summary>
    private static void FreeDrlgCoordList(D2RoomExStrc* pRoomEx)
    {
        var pDrlgCoordList = pRoomEx->pDrlgCoordList;
        void* pMemory = pRoomEx->pLevel->pDrlg->pMemoryPool;
        if (pDrlgCoordList == null) return;
        if ((pDrlgCoordList->eFlags & 2) != 0)
        {
            DrlgGrid.FreeGrid(pMemory, &pDrlgCoordList->sOrientationGrid);
            DrlgGrid.FreeGrid(pMemory, &pDrlgCoordList->sCoordIndexGrid);
        }
        var pRoomCoordList = pDrlgCoordList->pCoord;
        while (pRoomCoordList != null)
        {
            var pNextCoord = pRoomCoordList->pNext;
            Pool.FreeServerMemory(pMemory, pRoomCoordList);
            pRoomCoordList = pNextCoord;
        }
        Pool.FreeServerMemory(pMemory, pDrlgCoordList);
        pRoomEx->pDrlgCoordList = null;
    }
}

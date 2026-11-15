// Port of libd2 packages/drlg/src/drlg/maze/deps.zig (MIT, (c) 2026 jaenster).
//
// Maze dependencies that live in OTHER recon translation units but are needed by the maze
// generator. The seed-FREE geometry helpers from Drlg.cpp are transformed here faithfully (they
// belong to Drlg.cpp but the maze is the first caller); the seed-CONSUMING Preset.cpp pipeline
// (AllocDrlgMap/BuildArea) lives in Preset.cs.

namespace D2Dotnet.Map.Drlg.Engine.Maze;

internal static unsafe class MazeDeps
{
    public const int DIRECTION_SOUTHEAST = 0;
    public const int DIRECTION_NORTHEAST = 1;
    public const int DIRECTION_SOUTHWEST = 2;
    public const int DIRECTION_NORTHWEST = 3;
    public const int DIRECTION_INVALID = -1;

    /// <summary>Drlg.cpp:2492 (ReplaceRoomWithNewRoom). Positions pNewRoom adjacent to
    /// pRoomExStrc in nDirection, then verifies no overlap with the source's orth neighbours;
    /// returns the manhattan-fit result (true=placed ok). Directions 0..7; the recon's duplicated
    /// `default` blocks collapse to the overlap tail.</summary>
    public static bool ReplaceRoomWithNewRoom(int nDirection, D2RoomExStrc* pNewRoom, D2RoomExStrc* pRoomExStrc)
    {
        var pCoords = &pNewRoom->sCoords;
        var src = &pRoomExStrc->sCoords;
        int nCoordVal = 0;
        bool bSet = true;
        switch (nDirection)
        {
            case 0:
                pCoords->WorldPosition.x = src->WorldPosition.x - src->WorldSize.x;
                nCoordVal = src->WorldPosition.y;
                break;
            case 1:
                pCoords->WorldPosition.x = src->WorldPosition.x;
                nCoordVal = src->WorldPosition.y - src->WorldSize.y;
                break;
            case 2:
                pCoords->WorldPosition.x = src->WorldSize.x + src->WorldPosition.x;
                nCoordVal = src->WorldPosition.y;
                break;
            case 3:
                pCoords->WorldPosition.x = src->WorldPosition.x;
                nCoordVal = src->WorldSize.y + src->WorldPosition.y;
                break;
            case 4:
                pCoords->WorldPosition.x = src->WorldPosition.x - src->WorldSize.x;
                nCoordVal = src->WorldPosition.y - src->WorldSize.y;
                break;
            case 5:
                pCoords->WorldPosition.x = src->WorldSize.x + src->WorldPosition.x;
                nCoordVal = src->WorldPosition.y - src->WorldSize.y;
                break;
            case 6:
                pCoords->WorldPosition.x = src->WorldSize.x + src->WorldPosition.x;
                nCoordVal = src->WorldSize.y + src->WorldPosition.y;
                break;
            case 7:
                pCoords->WorldPosition.x = src->WorldPosition.x - src->WorldSize.x;
                nCoordVal = src->WorldSize.y + src->WorldPosition.y;
                break;
            default:
                bSet = false;
                break;
        }
        if (bSet) pNewRoom->sCoords.WorldPosition.y = nCoordVal;

        var pRoomData = pRoomExStrc->pOrth;
        while (true)
        {
            if (pRoomData == null)
            {
                int fManhattanDist = DrlgRoom.ComputeRectanglesManhattanDistance(pNewRoom->pLevel, pNewRoom, pRoomExStrc, 0);
                return fManhattanDist != 0;
            }
            int nc = DrlgRoom.FreeRoomEx(pCoords, pRoomData->psCoordinatesAndSize, 0);
            if (nc == 0) break;
            pRoomData = pRoomData->pNext;
        }
        return false;
    }

    /// <summary>Drlg.cpp:282 (GetDirectionFromCoordinates).</summary>
    public static int GetDirectionFromCoordinates(D2DrlgCoordsStrc* pDrlgCoords, D2DrlgCoordsStrc* pDrlgCoords2)
    {
        int nPos1 = pDrlgCoords->WorldPosition.x;
        int nPos2 = pDrlgCoords2->WorldPosition.x;
        if (nPos2 < nPos1)
        {
            if (nPos1 == pDrlgCoords2->WorldSize.x + nPos2) return DIRECTION_SOUTHEAST;
        }
        else if (nPos2 == pDrlgCoords->WorldSize.x + nPos1)
        {
            return DIRECTION_SOUTHWEST;
        }

        nPos1 = pDrlgCoords->WorldPosition.y;
        nPos2 = pDrlgCoords2->WorldPosition.y;
        if (nPos2 < nPos1)
        {
            if (nPos1 == pDrlgCoords2->WorldSize.y + nPos2) return DIRECTION_NORTHEAST;
        }
        else if (nPos2 == pDrlgCoords->WorldSize.y + nPos1)
        {
            return DIRECTION_NORTHWEST;
        }
        return DIRECTION_INVALID;
    }

    /// <summary>Drlg.cpp:460 (GetMinAndMaxCoordinatesFromLevel).</summary>
    public static void GetMinAndMaxCoordinatesFromLevel(D2DrlgLevelStrc* pLevel, int* pOutMinX, int* pOutMinY, int* pOutMaxX, int* pOutMaxY)
    {
        var pRoomEx = pLevel->pRoomExFirst;
        *pOutMinX = pRoomEx->sCoords.WorldPosition.x;
        *pOutMinY = pRoomEx->sCoords.WorldPosition.y;
        *pOutMaxX = pRoomEx->sCoords.WorldSize.x + pRoomEx->sCoords.WorldPosition.x;
        *pOutMaxY = pRoomEx->sCoords.WorldSize.y + pRoomEx->sCoords.WorldPosition.y;
        for (; pRoomEx != null; pRoomEx = pRoomEx->pRoomExNext)
        {
            int nCoord = pRoomEx->sCoords.WorldSize.x + pRoomEx->sCoords.WorldPosition.x;
            if (*pOutMaxX < nCoord) *pOutMaxX = nCoord;
            nCoord = pRoomEx->sCoords.WorldPosition.x;
            if (nCoord < *pOutMinX) *pOutMinX = nCoord;
            nCoord = pRoomEx->sCoords.WorldSize.y + pRoomEx->sCoords.WorldPosition.y;
            if (*pOutMaxY < nCoord) *pOutMaxY = nCoord;
            nCoord = pRoomEx->sCoords.WorldPosition.y;
            if (nCoord < *pOutMinY) *pOutMinY = nCoord;
        }
    }

    /// <summary>Drlg.cpp:492 (DRLGLEVEL_AdjustRoomCoordinates). Shifts every RoomEx so the level's
    /// min corner aligns with sCoordinatesAndSize. Error halts dropped (validation only).</summary>
    public static void AdjustRoomCoordinates(D2DrlgLevelStrc* pLevel)
    {
        int nMinX, nMinY, nMaxX, nMaxY;
        var pRoomEx = pLevel->pRoomExFirst;
        GetMinAndMaxCoordinatesFromLevel(pLevel, &nMinX, &nMinY, &nMaxX, &nMaxY);
        for (; pRoomEx != null; pRoomEx = pRoomEx->pRoomExNext)
        {
            pRoomEx->sCoords.WorldPosition.x += pLevel->sCoordinatesAndSize.WorldPosition.x - nMinX;
            pRoomEx->sCoords.WorldPosition.y += pLevel->sCoordinatesAndSize.WorldPosition.y - nMinY;
        }
    }

    /// <summary>Preset.cpp:1216 (DRLGPRESET_AllocDrlgMap).</summary>
    public static D2DrlgMapStrc* AllocDrlgMap(D2DrlgLevelStrc* pLevel, int nLvlPrestId, void* pDrlgCoord, D2SeedStrc* pSeed) =>
        Preset.AllocDrlgMap(pLevel, nLvlPrestId, (D2DrlgCoordStrc*)pDrlgCoord, pSeed);

    /// <summary>Preset.cpp:1964 (BuildArea).</summary>
    public static D2RoomExStrc* BuildArea(D2DrlgLevelStrc* pLevel, D2DrlgMapStrc* pDrlgMap, int nFlags, int bSingleRoom) =>
        Preset.BuildArea(pLevel, pDrlgMap, nFlags, bSingleRoom);
}

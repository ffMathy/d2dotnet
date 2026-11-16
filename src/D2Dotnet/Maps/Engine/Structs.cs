// Port of libd2 packages/drlg/src/drlg/structs.zig (MIT, (c) 2026 jaenster).
//
// Faithful transform of the D2 1.14d DRLG struct definitions: EXACT field order and names
// preserved. The `/* 0x.. */` offsets of the 32-bit engine ABI do not apply; this is a
// standalone 64-bit DRLG, so the C# compiler computes offsets (pointers are 8 bytes here,
// self-consistent). The structs live in native memory (Pool.cs) because the transformed
// closure aliases them through raw pointers exactly as the engine does.
//
// Enum typedefs (all `typedef int ...` in the headers) are plain int fields:
// eD2LevelId, eD2PresetId, eDrlgType, eDrlgLevelType, eD2UnitType, eDrlgDirection,
// eD2GridCellFlags, eCollisionFlags.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace D2Dotnet.Maps.Engine;

#pragma warning disable CS0649 // fields are written through raw pointers / native memory

/// <summary>Win32 POINT (long x, long y). 32-bit ABI long == 4 bytes.</summary>
internal struct POINT
{
    public int x;
    public int y;
}

/// <summary>Win32 RECT (long left/top/right/bottom).</summary>
internal struct RECT
{
    public int left;
    public int top;
    public int right;
    public int bottom;
}

/// <summary>D2RoomExStrc.eRoomExFlags bitfield (the canonical DRLGROOMFLAG_* bit layout).
/// Grid/room flag spaces share this u32 — nGridFlags/dwRoomFlags get OR'd in via OrRaw();
/// behaviour is faithful to the 1.14d recon (same bit values).</summary>
internal struct RoomExFlags
{
    public uint Raw;

    /// <summary>OR raw flag bits in (grid/room flag spaces share this u32).</summary>
    public void OrRaw(int bits) => Raw |= (uint)bits;

    public bool Inactive { readonly get => (Raw & 0x2) != 0; set => Set(0x2, value); }
    /// <summary>0xff0 — 8 warp/adjacency slots (bit n == slot n).</summary>
    public uint WarpSlots { readonly get => (Raw >> 4) & 0xff; set => Raw = (Raw & ~0xff0u) | ((value & 0xff) << 4); }
    /// <summary>0xf000 — sub-shrine rows 1..4.</summary>
    public uint ShrineRows { readonly get => (Raw >> 12) & 0xf; set => Raw = (Raw & ~0xf000u) | ((value & 0xf) << 12); }
    /// <summary>0x30000 — waypoint + small-waypoint.</summary>
    public uint Waypoint { readonly get => (Raw >> 16) & 0x3; set => Raw = (Raw & ~0x30000u) | ((value & 0x3) << 16); }
    public bool MapReveal { readonly get => (Raw & 0x40000) != 0; set => Set(0x40000, value); }
    /// <summary>0x80000 (don't draw line-of-sight)</summary>
    public bool NoLos { readonly get => (Raw & 0x80000) != 0; set => Set(0x80000, value); }
    public bool RoomActive { readonly get => (Raw & 0x100000) != 0; set => Set(0x100000, value); }
    public bool RoomFreed { readonly get => (Raw & 0x200000) != 0; set => Set(0x200000, value); }
    public bool Portal { readonly get => (Raw & 0x400000) != 0; set => Set(0x400000, value); }
    /// <summary>0x800000 (towns / ds1 populate=0)</summary>
    public bool NoSpawn { readonly get => (Raw & 0x800000) != 0; set => Set(0x800000, value); }
    public bool TilesLoaded { readonly get => (Raw & 0x1000000) != 0; set => Set(0x1000000, value); }
    public bool PresetsAdded { readonly get => (Raw & 0x2000000) != 0; set => Set(0x2000000, value); }
    /// <summary>0x4000000 (preset units already spawned)</summary>
    public bool PresetsSpawned { readonly get => (Raw & 0x4000000) != 0; set => Set(0x4000000, value); }
    public bool AnimatedFloor { readonly get => (Raw & 0x8000000) != 0; set => Set(0x8000000, value); }

    /// <summary>Any warp slot set (was &amp; 0xff0).</summary>
    public readonly bool AnyWarp() => (Raw & 0xff0) != 0;
    /// <summary>Warp slot `slot` set (was &amp; (1 &lt;&lt; (slot+4))).</summary>
    public readonly bool WarpSlot(int slot) => ((Raw >> 4) & (1u << (slot & 7))) != 0;
    /// <summary>Has a waypoint (was &amp; 0x30000).</summary>
    public readonly bool HasWaypoint() => (Raw & 0x30000) != 0;

    private void Set(uint bit, bool on) => Raw = on ? Raw | bit : Raw & ~bit;
}

// ---- inline arrays (the C fixed arrays of the recon structs) --------------------------

[InlineArray(2)] internal struct GridArray2 { private D2DrlgGridStrc _e; }
[InlineArray(4)] internal struct GridArray4 { private D2DrlgGridStrc _e; }
[InlineArray(4)] internal struct RoomExArray4 { private D2RoomExStrc _e; }
[InlineArray(6)] internal struct PointArray6 { private POINT _e; }
[InlineArray(6)] internal struct ExitPointArray6 { private D2DrlgExitPointStrc _e; }
[InlineArray(15)] internal struct CoordsArray15 { private D2DrlgCoordsStrc _e; }
[InlineArray(32)] internal struct SpawnPointArray32 { private D2DrlgSpawnPointStrc _e; }
/// <summary>An array of raw pointers (stored as nint: C# inline arrays cannot hold pointer types).</summary>
[InlineArray(4)] internal struct PtrArray4 { private nint _e; }
[InlineArray(2)] internal struct PtrArray2 { private nint _e; }
[InlineArray(6)] internal struct PtrArray6 { private nint _e; }
[InlineArray(8)] internal struct PtrArray8 { private nint _e; }
[InlineArray(32)] internal struct PtrArray32 { private nint _e; }
[InlineArray(128)] internal struct PtrArray128 { private nint _e; }

// ---- D2SeedStrc.h ----------------------------------------------------------------------

/// <summary>64-bit DRLG PRNG state (nSeedLow + nSeedHigh). C++ ctors/operators dropped.</summary>
internal struct D2SeedStrc
{
    public int nSeedLow;
    public int nSeedHigh;
}

// ---- Drlg/D2DrlgCoordsStrc.h -----------------------------------------------------------

internal struct D2DrlgCoordsStrc
{
    public POINT WorldPosition;
    public POINT WorldSize;
}

// ---- Drlg/Preset.h ---------------------------------------------------------------------

internal struct D2DrlgCoordStrc
{
    public int nPosX;
    public int nPosY;
    public int nWidth;
    public int nHeight;
}

internal struct D2DrlgRoomExDataPresetWorldCoordinatesBurialGrounds
{
    /// <summary>list of points where BloodRaven moves to</summary>
    public PointArray6 PointsList;
}

internal unsafe struct D2PresetUnitStrc
{
    /// <summary>Mode flag - 1 for monsters from monpreset.txt, 3 for items</summary>
    public int nMode;
    /// <summary>Unit class ID - MonStats ID for monsters, Object ID for objects</summary>
    public int nClassId;
    /// <summary>X position in world coordinates (sub-tile units)</summary>
    public int nPosX;
    public D2PresetUnitStrc* pPresetUnitNext;
    /// <summary>preset path data {int nCount; D2PresetPathEntry* pEntries}</summary>
    public void* pPath;
    /// <summary>Unit type (UNIT_MONSTER=1, UNIT_OBJECT=2, UNIT_ITEM=4, UNIT_WARP=5)</summary>
    public int eType;
    /// <summary>Y position in world coordinates (sub-tile units)</summary>
    public int nPosY;
    /// <summary>Flags bitmask (bit 0 = auto-generated object)</summary>
    public int nFlags;
    /// <summary>Non-engine side channel: the DBM/oracle "unresolved preset code" for monster
    /// units (PresetTables.DbmMonsterCode). -1 = unset. Purely informational; never read by
    /// generation. NOTE: the allocator zero-fills, so every creation site sets it.</summary>
    public int nDbmCode;
}

// ---- Drlg/D2DrlgGridStrc.h -------------------------------------------------------------

internal unsafe struct D2DrlgGridStrc
{
    /// <summary>Array of cell flags. NULL if grid not initialized.</summary>
    public int* pCellsFlags;
    /// <summary>Array of row start offsets into pCellsFlags (one per nHeight rows).</summary>
    public int* pCellsRowOffsets;
    /// <summary>Number of cells per row (X dimension).</summary>
    public int nWidth;
    /// <summary>Number of rows (Y dimension).</summary>
    public int nHeight;
    /// <summary>If nonzero, pCellsFlags is shared/embedded and not freed by FreeGrid.</summary>
    public int bIsSubGrid;
}

// ---- Drlg/D2DrlgVertexStrc.h -----------------------------------------------------------

internal unsafe struct D2DrlgVertexStrc
{
    public int nPosX;
    public int nPosY;
    public byte nDirection;
    public int dwFlags;
    public D2DrlgVertexStrc* pNext;
}

// ---- Drlg/D2DrlgOrthStrc.h -------------------------------------------------------------

internal unsafe struct D2DrlgOrthStrc
{
    /// <summary>Union: D2DrlgLevelStrc* when nType==0, D2RoomExStrc* when nType==1</summary>
    public D2RoomExStrc* pRoomEx;
    public int neDrlgDirection;
    /// <summary>BOOL: whether linked level/room is a preset area</summary>
    public int bIsDrlgTypePresetArea;
    /// <summary>0=level link (pRoomEx is D2DrlgLevelStrc*), 1=room link (D2RoomExStrc*)</summary>
    public int nType;
    /// <summary>Points to sCoordinatesAndSize of linked level or sCoords of linked room</summary>
    public D2DrlgCoordsStrc* psCoordinatesAndSize;
    public D2DrlgOrthStrc* pNext;
}

// ---- Drlg/D2DrlgReplaceRoomStrc.h ------------------------------------------------------

internal struct D2DrlgReplaceRoomStrc
{
    public int nSourceLevelPrestId;
    public int nDestLevelPrestId;
    public int nDestPickedFile;
    public int nDirection;
}

// ---- Drlg/D2DrlgRoomCoordsStrc.h -------------------------------------------------------

internal struct D2DrlgRoomCoordsStrc
{
    public int dwXStart;
    public int dwYStart;
    public int dwXSize;
    public int dwYSize;
    public RECT sTileRect;
}

// ---- Drlg/D2DrlgTileDataStrc.h ---------------------------------------------------------

internal unsafe struct D2DrlgTileDataStrc
{
    /// <summary>Screen X coordinate (from minimap->screen transform)</summary>
    public int nScreenX;
    /// <summary>Screen Y coordinate</summary>
    public int nScreenY;
    /// <summary>Tile X relative to room origin</summary>
    public int nPosX;
    /// <summary>Tile Y relative to room origin</summary>
    public int nPosY;
    /// <summary>Grid cell flags from DrlgCoordList</summary>
    public int nGridFlags;
    /// <summary>Tile draw flags bitmask</summary>
    public int nFlags;
    public D2TileLibraryEntryStrc* pTileLibraryEntry;
    /// <summary>Tile orientation/type (0=floor, 3=left wall, 4=right wall, 0xD=shadow)</summary>
    public int nTileType;
    /// <summary>Next tile in linked list (or next in array when no links)</summary>
    public D2DrlgTileDataStrc* pNext;
    /// <summary>Bit0=lightMode, bit1=fading transparency, bit2=fading to hide</summary>
    public int nTransitionFlags;
    public byte nRed;
    public byte nGreen;
    public byte nBlue;
    public byte nIntensity;
    /// <summary>GetTickCount() timestamp when fade animation ends</summary>
    public int nFadeEndTime;
}

// ---- Drlg/RoomTile.h (D2DrlgTileLinkStrc) ----------------------------------------------

internal unsafe struct D2DrlgTileLinkStrc
{
    /// <summary>0 = wall type, non-zero = floor type</summary>
    public byte bFloor;
    public byte field_0x1;
    public ushort _pad_0x02;
    public D2DrlgTileDataStrc* pTileData;
    public D2DrlgTileLinkStrc* pNext;
}

// ---- Drlg/DrlgAnim.h (D2DrlgAnimTileGridStrc) ------------------------------------------

internal unsafe struct D2DrlgAnimTileGridStrc
{
    /// <summary>Array of tile data pointers (one per animation frame)</summary>
    public D2DrlgTileDataStrc** ppTileData;
    public int nFrames;
    public int nCurrentFrame;
    /// <summary>Tile orientation/type identifier from grid</summary>
    public int pTileType;
    public D2DrlgAnimTileGridStrc* pNext;
}

// ---- Drlg/D2DrlgTileGridStrc.h ---------------------------------------------------------

internal unsafe struct D2DrlgTileGridStrc
{
    /// <summary>Linked list of tile links. In warp nodes: D2RoomExStrc* target room</summary>
    public D2DrlgTileLinkStrc* pMapLinks;
    /// <summary>Linked list of animated tile grids. In warp nodes: pNext</summary>
    public D2DrlgAnimTileGridStrc* pAnimTiles;
    /// <summary>Current wall tile write index</summary>
    public int nWalls;
    /// <summary>Current floor tile write index</summary>
    public int nFloors;
    /// <summary>Current shadow/roof write index. In warp nodes: warp destination ID</summary>
    public int nShadows;
    /// <summary>Wall tile data array (0x30 bytes per element)</summary>
    public D2DrlgTileDataStrc* pWallTiles;
    public int nWallTilesMax;
    public D2DrlgTileDataStrc* pFloorTiles;
    public int nFloorTilesMax;
    public D2DrlgTileDataStrc* pRoofTiles;
    public int nRoofTilesMax;
}

// ---- Drlg/Drlg.h -----------------------------------------------------------------------

internal unsafe struct D2DrlgRoomTilesStrc
{
    public D2DrlgTileDataStrc* pWallTiles;
    public int nWallTilesCount;
    public D2DrlgTileDataStrc* pFloorTiles;
    public int nFloorTilesCount;
    public D2DrlgTileDataStrc* pRoofTiles;
    public int nRoofTilesCount;
}

internal struct D2TileLibraryHashStrc
{
    public PtrArray128 pNodes;
}

internal unsafe struct D2DrlgLevelLinkNodeStrc
{
    public int dwData0;
    public int dwData1;
    public int dwData2;
    public D2DrlgLevelLinkNodeStrc* pNext;
}

// ---- Drlg/D2DrlgRoomExDataMazeStrc.h ---------------------------------------------------

internal unsafe struct D2DrlgRoomExDataMazeStrc
{
    /// <summary>Orientation/shadow grid for outdoor tiles</summary>
    public D2DrlgGridStrc pOrientationGrid;
    public D2DrlgGridStrc pWallGrid;
    public D2DrlgGridStrc pFloorGrid;
    public D2DrlgGridStrc pCellGrid;
    /// <summary>D2DrlgVertexStrc* vertex linked list for path/border (typed uint8_t* in .h)</summary>
    public byte* pVertex;
    /// <summary>Outdoor room flags (bit 0x80 = border)</summary>
    public int dwFlags;
    public int dwFlagsEx;
    public int field_5C;
    public int field_60;
    /// <summary>SubType from LevelDefs.txt</summary>
    public int nSubType;
    public int nSubTheme;
    /// <summary>Random subtheme variant picked (0-31)</summary>
    public int nSubThemePicked;
}

/// <summary>D2DrlgExitPointStrc — Drlg.h:88. 0x14 bytes; runtime exit/path scratch.</summary>
internal struct D2DrlgExitPointStrc
{
    public int nWorldX;
    public int nWorldY;
    public byte eType;
    public byte _pad9a;
    public byte _pad9b;
    public byte _pad9c;
    public int field_C;
    public int field_10;
}

/// <summary>D2DrlgLevelDataWildernessLevel — Drlg.h:305. pDrlgLevelData for type-3 levels
/// (alloc 0x268 in the 32-bit engine; here field order+types, compiler-computed 64-bit
/// offsets). The recon's GenerateLevel reaches the four grids by RAW byte offsets
/// (+4/+0x18/+0x2c/+0x40); the transform uses NAMED fields per the .h layout.</summary>
internal unsafe struct D2DrlgLevelDataWildernessLevel
{
    public uint dwFlags;
    /// <summary>+0x04 preset level ids / orientation</summary>
    public D2DrlgGridStrc sGridPreset;
    /// <summary>+0x18 vis/link flags</summary>
    public D2DrlgGridStrc sGridLink;
    /// <summary>+0x2c outdoor placement flags</summary>
    public D2DrlgGridStrc sGridOutdoor;
    /// <summary>+0x40 misc outdoor flags</summary>
    public D2DrlgGridStrc sGridMisc;
    public int* pGridCoordsCellFlags;
    public int* pGridCoordsRowOffsets;
    /// <summary>WorldSize.x / 8</summary>
    public int nGridCoordsWidth;
    /// <summary>WorldSize.y / 8</summary>
    public int nGridCoordsHeight;
    public D2DrlgVertexStrc* pVertices;
    public PtrArray6 pAdjacentVertices;
    public ExitPointArray6 aExitPoints1;
    public ExitPointArray6 aExitPoints2;
    public ExitPointArray6 aExitPoints3;
    public ExitPointArray6 aExitPoints4;
    public int nExitCount;
    public D2DrlgOrthStrc* pOrthData;
}

/// <summary>D2DrlgPresetRoomStrc — DrlgGrid.h. The pRoomExData block for nPresetType==2 rooms
/// (alloc 0xf8). Faithful field ORDER + types. The recon overlays integer payloads onto the
/// leading int fields (nLevelPrest/nPickedFile/nFlags) and stashes pParentDrlgMap into the
/// pMazeGrid pointer slot (recon Preset.cpp:807, raw index 0x3b).</summary>
internal unsafe struct D2DrlgPresetRoomStrc
{
    /// <summary>LvlPrest id (recon raw index [0] = Def)</summary>
    public int nLevelPrest;
    /// <summary>picked DS1 file index (recon raw index [1])</summary>
    public int nPickedFile;
    /// <summary>owning DrlgMap (recon raw index [2])</summary>
    public D2DrlgMapStrc* pMap;
    /// <summary>room flags (recon raw index [3])</summary>
    public int nFlags;
    public GridArray4 pWallGrid;
    public GridArray4 pOrientationGrid;
    public GridArray2 pFloorGrid;
    public D2DrlgGridStrc pCellGrid;
    /// <summary>recon raw index [0x3b]; runtime-overloaded to hold pParentDrlgMap</summary>
    public void* pMazeGrid;
    public void* pNavigationPoints;
    public int nNavigationPointsCount;
}

// ---- Drlg/D2DrlgFileStrc.h -------------------------------------------------------------

internal struct D2DrlgSubstGroupStrc
{
    /// <summary>Bounding box of the substitution group</summary>
    public D2DrlgCoordStrc tBox;
    /// <summary>Substitution probability/chance (DS1 version >= 13)</summary>
    public int nProbability;
    /// <summary>Number of variants; the placement variant is rolled in [0,nVariantCount)
    /// (PlaceRandomBorderSubTiles, 1.14d 0066f8be reads substGroup+0x14).</summary>
    public int nVariantCount;
}

internal unsafe struct D2DrlgFileStrc
{
    /// <summary>DS1 act field (version >= 10). Values 1 or 2 enable tag/subst layer.</summary>
    public int nAct;
    /// <summary>Raw loaded DS1 file buffer.</summary>
    public void* pDS1File;
    public int unk0x08;
    /// <summary>DS1 width in cells</summary>
    public int nWidth;
    /// <summary>DS1 height in cells</summary>
    public int nHeight;
    /// <summary>Number of wall layers (1-4)</summary>
    public int nWallLayers;
    /// <summary>Number of floor layers (1-2)</summary>
    public int nFloorLayers;
    /// <summary>Wall orientation/direction data per wall layer</summary>
    public PtrArray4 pTileTypeLayer;
    /// <summary>Wall cell/flags data per wall layer</summary>
    public PtrArray4 pWallLayer;
    /// <summary>Floor cell data per floor layer</summary>
    public PtrArray2 pFloorLayer;
    public void* pShadowLayer;
    /// <summary>Substitution group tag layer. Present when nAct is 1 or 2.</summary>
    public void* pSubstGroupTags;
    /// <summary>Number of substitution groups (version >= 12)</summary>
    public int nSubstGroups;
    public D2DrlgSubstGroupStrc* pSubstGroups;
    /// <summary>Linked list head of preset units parsed from DS1</summary>
    public D2PresetUnitStrc* pPresetUnit;
    public int unk0x58;
}

// ---- D2RoomCoordListStrc.h -------------------------------------------------------------

internal unsafe struct D2RoomCoordListStrc
{
    public D2DrlgCoordStrc pBox;
    public RECT pRect;
    public int bNode;
    public int nRoomActive;
    public int nIndex;
    public D2RoomCoordListStrc* pNext;
}

// ---- D2TileLibraryEntryStrc.h ----------------------------------------------------------

internal unsafe struct D2TileLibraryEntryStrc
{
    public int nDirection;
    public short nRoofHeight;
    public short nFlags;
    public int nHeight;
    public int nWidth;
    public int nHeightToBottom;
    public int nOrientation;
    public int nIndex;
    public int nSubIndex;
    public int nFrame_Rarity;
    public int transparentColorRGB24;
    public fixed byte dwTileFlags[4];
    public int dwBlockOffset_pBlock;
    public int nBlockSize;
    public int nBlocks;
    public void* pParent;
    public short field16_0x3c;
    public short nCacheIndex;
    public int nTextureCacheIndex;
    public int field19_0x44;
    /// <summary>Archive byte offset of tile block data</summary>
    public int dwBlockDataOffset;
    public int nBlockDataSize;
}

// ---- D2RoomExStrc.h --------------------------------------------------------------------

internal unsafe struct D2DrlgCoordListStrc
{
    /// <summary>Bit 0: simple/single coord, Bit 1: has grids allocated</summary>
    public int eFlags;
    /// <summary>Number of D2RoomCoordListStrc entries</summary>
    public int nListSize;
    /// <summary>Grid for wall/orientation indices</summary>
    public D2DrlgGridStrc sOrientationGrid;
    /// <summary>Grid mapping subtile positions to coord regions</summary>
    public D2DrlgGridStrc sCoordIndexGrid;
    /// <summary>Array of room coord list entries (size = nListSize)</summary>
    public D2RoomCoordListStrc* pCoord;
}

internal unsafe struct D2RoomExStrc
{
    /// <summary>linked list of orthogonal room connections</summary>
    public D2DrlgOrthStrc* pOrth;
    /// <summary>Initial seed value</summary>
    public int nSeed;
    /// <summary>array of near room pointers</summary>
    public D2RoomExStrc** ppDrlgRoomsExNear;
    /// <summary>Max near rooms array capacity</summary>
    public short nDrlgRoomsExNearMax;
    /// <summary>Room initialization flag</summary>
    public short bIsInit;
    /// <summary>Room type</summary>
    public int nType;
    /// <summary>Room seed (nSeedLow + nSeedHigh)</summary>
    public D2SeedStrc sSeed;
    /// <summary>Forward link in activation priority doubly-linked list</summary>
    public D2RoomExStrc* pStatusNext;
    /// <summary>Union: D2DrlgRoomExDataPresetStrc*(nPresetType==2) or
    /// D2DrlgRoomExDataMazeStrc*(nPresetType==1). void* in source.</summary>
    public void* pRoomExData;
    /// <summary>Next room in level list</summary>
    public D2RoomExStrc* pRoomExNext;
    /// <summary>D2DrlgRoomFlags bitmask</summary>
    public RoomExFlags eRoomExFlags;
    /// <summary>Number of near rooms</summary>
    public int nDrlgRoomsExNearCount;
    /// <summary>Active room, NULL when not tiled</summary>
    public void* pRoom;
    /// <summary>Room coordinates: WorldPosition.x/y, WorldSize.x/y</summary>
    public D2DrlgCoordsStrc sCoords;
    /// <summary>Room activation status/priority (0-4)</summary>
    public int fRoomStatus;
    /// <summary>1=RandomMaze(outdoor), 2=PresetArea</summary>
    public int nPresetType;
    /// <summary>Warp tile grid linked list (via pAnimTiles as next ptr)</summary>
    public D2DrlgTileGridStrc* pTileGrid;
    /// <summary>Bitmask of DT1 tile files to load</summary>
    public int nDT1Mask;
    /// <summary>Room tile data - holds walls/floors/shadows tile arrays</summary>
    public D2DrlgTileGridStrc* pRoomTiles;
    /// <summary>Parent level</summary>
    public D2DrlgLevelStrc* pLevel;
    /// <summary>Linked list head of preset units (monsters/objects/items)</summary>
    public D2PresetUnitStrc* pPresetUnit;
    /// <summary>Secondary flags field</summary>
    public int dwOtherFlags;
    /// <summary>Logical room info / coord regions</summary>
    public D2DrlgCoordListStrc* pDrlgCoordList;
    /// <summary>D2TileLibraryHashStrc*[32] - tile cache slots (void* in source)</summary>
    public PtrArray32 apTiles;
    /// <summary>Backward link in activation priority doubly-linked list</summary>
    public D2RoomExStrc* pStatusPrev;
}

// ---- D2UnkOutdoorStrc.h ----------------------------------------------------------------

internal unsafe struct D2UnkOutdoorStrc
{
    public D2DrlgLevelStrc* pLevel;
    /// <summary>Pointer into pDrlgLevelData+0x54 (bounds/coord grid)</summary>
    public D2DrlgGridStrc* pCoordsGrid;
    /// <summary>Pointer into pDrlgLevelData+0x04 (primary floor grid)</summary>
    public D2DrlgGridStrc* pPrimaryFloorGrid;
    /// <summary>Pointer into pDrlgLevelData+0x2C (wall/collision grid)</summary>
    public D2DrlgGridStrc* pWallGrid;
    /// <summary>Base preset ID for border lookups</summary>
    public int nLevelPrestId;
    /// <summary>Previous border preset index; init -1, max 0x3E</summary>
    public int nPrevBorderPresetId;
    public int nLvlSubId;
    /// <summary>Callback: test if grid cell preset slot is free</summary>
    public void* pfnIsPresetFree;
    /// <summary>Callback: test outdoor level preset (pointer32 in source)</summary>
    public void* pfnTestPreset;
    /// <summary>Callback: validate secondary border cell; NULL for Acts 1-4</summary>
    public void* pfnValidateBorderCell;
    /// <summary>Callback: get border preset from LvlSub; Act5 only</summary>
    public void* pfnGetBorderPreset;
    /// <summary>Callback: alter adjacent preset grid cells</summary>
    public void* pfnAlterAdjacentCells;
    /// <summary>Callback: clear a grid cell</summary>
    public void* pfnSetBlankCell;
    /// <summary>Callback: spawn outdoor level preset</summary>
    public void* pfnSpawnPreset;
}

// ---- Drlg/D2DrlgMapStrc.h --------------------------------------------------------------

internal unsafe struct D2DrlgMapStrc
{
    /// <summary>LvlPrest ID</summary>
    public int nNumber;
    /// <summary>Randomly selected file index from pTxtLevelPrest->Files</summary>
    public int nRandomMapFileSelector;
    public D2LvlPrestTxt* pTxtLevelPrest;
    /// <summary>Loaded DS1 file data</summary>
    public D2DrlgFileStrc* pDrlgFile;
    /// <summary>World X position</summary>
    public int nRealOffsetX;
    /// <summary>World Y position</summary>
    public int nRealOffsetY;
    /// <summary>Width in tiles</summary>
    public int nSizeX;
    /// <summary>Height in tiles</summary>
    public int nSizeY;
    /// <summary>Vertices linked list. Non-null also acts as bHasInfo for pMapGrid.</summary>
    public D2DrlgVertexStrc* pVertices;
    /// <summary>Embedded grid structure</summary>
    public D2DrlgGridStrc pMapGrid;
    /// <summary>Linked list of preset units from DrlgFile</summary>
    public D2PresetUnitStrc* pPresetUnit;
    /// <summary>BOOL - Set to 1 during AllocDrlgMap</summary>
    public int bInited;
    /// <summary>Number of pop locations</summary>
    public int nPops;
    /// <summary>Array of pop tile indices [nPops]</summary>
    public int* pPopsIndex;
    /// <summary>Array of pop sub-indices [nPops]</summary>
    public int* pPopsSubIndex;
    /// <summary>Array of pop orientations [nPops]</summary>
    public int* pPopsOrientation;
    /// <summary>Array of pop location coordinates [nPops]</summary>
    public D2DrlgCoordStrc* pPosLocation;
    /// <summary>Next DrlgMap in linked list</summary>
    public D2DrlgMapStrc* pNext;
}

// ---- Drlg/D2DrlgLevelStrc.h ------------------------------------------------------------

internal unsafe struct D2DrlgLevelWarpCoordinatesStrc
{
    public fixed int anX[9];
    public fixed int anY[9];
    public int nEntriesCount;
}

internal struct D2DrlgSpawnPointStrc
{
    public int nX;
    public int nY;
    public int nSpawnType;
}

internal unsafe struct D2DrlgLevelStrc
{
    public int eDrlgType;
    public uint dwFlags;
    public int nRoomExCount;
    public uint nActiveCount;
    public D2RoomExStrc* pRoomExFirst;
    /// <summary>Union: D2LvlMazeTxt*(maze), preset-area data, or wilderness data. void* in source.</summary>
    public void* pDrlgLevelData;
    public uint nUnknown_018;
    public D2DrlgCoordsStrc sCoordinatesAndSize;
    /// <summary>32 spawn points, 12 bytes each</summary>
    public SpawnPointArray32 aSpawnPoints;
    public D2DrlgLevelStrc* pLevelNext;
    /// <summary>Head of linked list of DrlgMap entries for this level</summary>
    public D2DrlgMapStrc* pDrlgMapFirst;
    public D2DrlgStrc* pDrlg;
    public uint nUnknown_1B8;
    public void* pAutomapEx;
    public int nLevelType;
    public D2SeedStrc sSeed;
    /// <summary>Head of linked list of level link nodes</summary>
    public D2DrlgLevelLinkNodeStrc* pLevelLinkNodeFirst;
    public int eD2LevelId;
    public int dwInactiveFrames;
    public int nSpawnPointCount;
    public int nLastRoomExCoordsIndex;
    /// <summary>Warp coordinate data: 9 X coords, 9 Y coords, and entry count</summary>
    public D2DrlgLevelWarpCoordinatesStrc sWarpCoordinates;
    public int* pRoomExStateFlags;
}

// ---- Drlg/D2DrlgActWarpsInfoStrc.h -----------------------------------------------------

internal unsafe struct D2DrlgActWarpsInfoStrc
{
    public int nLevelId;
    public fixed int nTargetArea[8];
    public fixed int nWarpId[8];
    public D2DrlgActWarpsInfoStrc* pNext;
}

// ---- Drlg/D2DrlgStrc.h -----------------------------------------------------------------

internal unsafe struct D2DrlgStrc
{
    public D2SeedStrc nSeed;
    public int nRoomDataGeneration;
    public PtrArray32 apTileProjects;
    public uint dwFlags;
    public D2DrlgActWarpsInfoStrc* pWarpsInfo;
    /// <summary>Act II: Horadric staff tomb level id</summary>
    public int nStaffTombLevel;
    public byte nRoomInitBatchCount;
    public void* pGame;
    /// <summary>4 RoomEx activation priority-queue sentinels</summary>
    public RoomExArray4 aSubA0;
    public byte nDifficulty;
    public void* pfAutoMap;
    /// <summary>Unrolled act init seed (dwInitSeed param)</summary>
    public uint dwGameLowSeed;
    public byte nActivationCountdown;
    /// <summary>iterator for room activation loop</summary>
    public D2RoomExStrc* pActivationIterator;
    public void* pDS1MemPool;
    public int nRoomDataFreeGeneration;
    public void* pAct;
    /// <summary>Rolled drlg seed; used to seed each level</summary>
    public uint dwStartSeed;
    /// <summary>Act III: jungle interlink flag = seed LSB</summary>
    public int bJungleInterlink;
    public void* pMemoryPool;
    public D2DrlgLevelStrc* pLevel;
    public byte nActNo;
    /// <summary>Act II: Duriel/boss tomb level id</summary>
    public int nBossTombLevel;
    public void* pfTownAutoMap;
}

// ---- Drlg/D2DrlgRoomExNearNodeStrc.h (Ghidra v628 /Diablo2/DRLG) -----------------------

/// <summary>Orthogonal-like room-adjacency node for near-room tracking; populated during generation.</summary>
internal unsafe struct D2DrlgRoomExNearNodeStrc
{
    public D2RoomExStrc* pRoomEx;
    public int _unk_0x04;
    public int bIsDrlgTypePresetArea;
    public int nNextCount;
    public D2DrlgCoordsStrc* psCoordinatesAndSize;
    public D2DrlgRoomExNearNodeStrc* pSub00Next;
}

// ---- Drlg/D2DrlgLevelDataPresetArea.h --------------------------------------------------

/// <summary>Preset-area arm of the pDrlgLevelData union; populated during generation.
/// The 8-byte preset-level data block: pDrlgMap + nPickedFile.</summary>
internal unsafe struct D2DrlgLevelDataPresetArea
{
    /// <summary>Pointer to the main DrlgMap for this preset level.</summary>
    public D2DrlgMapStrc* pDrlgMap;
    /// <summary>Index of the randomly picked map file variant (0-based); -1 if only one file.</summary>
    public int nPickedFile;
}

#pragma warning restore CS0649

/// <summary>Pointer helpers for the inline arrays.</summary>
internal static unsafe class Inline
{
    public static D2DrlgGridStrc* At(ref GridArray4 a, int i) => (D2DrlgGridStrc*)Unsafe.AsPointer(ref a[i]);
    public static D2DrlgGridStrc* At(ref GridArray2 a, int i) => (D2DrlgGridStrc*)Unsafe.AsPointer(ref a[i]);
    public static D2RoomExStrc* At(ref RoomExArray4 a, int i) => (D2RoomExStrc*)Unsafe.AsPointer(ref a[i]);
    public static D2DrlgExitPointStrc* At(ref ExitPointArray6 a, int i) => (D2DrlgExitPointStrc*)Unsafe.AsPointer(ref a[i]);
    public static D2DrlgCoordsStrc* At(ref CoordsArray15 a, int i) => (D2DrlgCoordsStrc*)Unsafe.AsPointer(ref a[i]);
    public static D2DrlgSpawnPointStrc* At(ref SpawnPointArray32 a, int i) => (D2DrlgSpawnPointStrc*)Unsafe.AsPointer(ref a[i]);
    public static int SizeOf<T>() where T : unmanaged => sizeof(T);
}

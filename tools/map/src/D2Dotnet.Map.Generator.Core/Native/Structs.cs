// Port of upstream map/d2_structs.h (blacha/diablo2, MIT, (c) 2021 Blayne Chard).
//
// Layout rules:
//  * Every struct is LayoutKind.Explicit with the field offsets and total size that
//    i686-w64-mingw32-g++ produces for the C header (including the #pragma pack(1) regions
//    and natural 4-byte alignment elsewhere). Several offset comments in the C header are
//    wrong (e.g. CollMap.pMapEnd is 0x24, ObjectTxt.nAutoMap is 0x1BC); the compiler is the
//    source of truth and the tests assert these values against compiler output.
//  * Pointers are stored as `uint` (a 32-bit address) instead of `T*`, so the layout is the
//    32-bit layout on every platform. That lets the AnyCPU tests verify it on x64 and lets
//    the map dumper walk structures through IGameMemory (real process memory on win-x86,
//    a fake heap in tests). Typed pointer names are kept from the C source.
//  * wchar_t is 2 bytes on Windows and is represented as ushort (not char) so that
//    Marshal.SizeOf/OffsetOf agree with sizeof().
// Field names intentionally match the C names exactly (including _1, _2 padding fields).
#pragma warning disable IDE1006 // C naming kept on purpose
#pragma warning disable CS0649
using System.Runtime.InteropServices;

namespace D2Dotnet.Map.Generator.Native;

/// <summary>Client bootstrap struct passed to D2Win (pack 1). C: <c>d2client_struct</c>.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x211)]
public unsafe struct d2client_struct
{
    [FieldOffset(0x00)] public uint dwInit;
    [FieldOffset(0x04)] public fixed byte _1[0x20D - 4];
    [FieldOffset(0x20D)] public uint fpInit;
}

[StructLayout(LayoutKind.Explicit, Size = 0x0C)]
public struct InventoryInfo
{
    [FieldOffset(0x00)] public int nLocation;
    [FieldOffset(0x04)] public int nMaxXCells;
    [FieldOffset(0x08)] public int nMaxYCells;
}

/// <summary>Collision map of a Room1.</summary>
[StructLayout(LayoutKind.Explicit, Size = 0x28)]
public struct CollMap
{
    [FieldOffset(0x00)] public uint dwPosGameX;
    [FieldOffset(0x04)] public uint dwPosGameY;
    [FieldOffset(0x08)] public uint dwSizeGameX;
    [FieldOffset(0x0C)] public uint dwSizeGameY;
    [FieldOffset(0x10)] public uint dwPosRoomX;
    [FieldOffset(0x14)] public uint dwPosRoomY;
    [FieldOffset(0x18)] public uint dwSizeRoomX;
    [FieldOffset(0x1C)] public uint dwSizeRoomY;
    /// <summary>WORD* to the first collision cell.</summary>
    [FieldOffset(0x20)] public uint pMapStart;
    /// <summary>WORD* past the last collision cell.</summary>
    [FieldOffset(0x24)] public uint pMapEnd;
}

/// <summary>levels.txt record.</summary>
[StructLayout(LayoutKind.Explicit, Size = 0x220)]
public unsafe struct LevelTxt
{
    [FieldOffset(0x000)] public uint dwLevelNo;
    [FieldOffset(0x004)] public fixed uint _1[60];
    [FieldOffset(0x0F4)] public byte _2;
    [FieldOffset(0x0F5)] public fixed byte szName[40];
    [FieldOffset(0x11D)] public fixed byte szEntranceText[40];
    [FieldOffset(0x145)] public fixed byte szLevelDesc[41];
    [FieldOffset(0x16E)] public fixed ushort wName[40];
    [FieldOffset(0x1BE)] public fixed ushort wEntranceText[40];
    [FieldOffset(0x20E)] public fixed byte nObjGroup[8];
    [FieldOffset(0x216)] public fixed byte nObjPrb[8];
}

/// <summary>1.13c RoomTile (pack 1).</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x14)]
public unsafe struct RoomTile
{
    [FieldOffset(0x00)] public uint pRoom2;
    [FieldOffset(0x04)] public uint pNext;
    [FieldOffset(0x08)] public fixed uint _2[2];
    /// <summary>DWORD* to the tile's destination number.</summary>
    [FieldOffset(0x10)] public uint nNum;
}

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x08)]
public struct QuestInfo
{
    [FieldOffset(0x00)] public uint pBuffer;
    [FieldOffset(0x04)] public uint _1;
}

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x01)]
public struct Waypoint
{
    [FieldOffset(0x00)] public byte flags;
}

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x28)]
public unsafe struct PlayerData
{
    [FieldOffset(0x00)] public fixed byte szName[0x10];
    [FieldOffset(0x10)] public uint pNormalQuest;
    [FieldOffset(0x14)] public uint pNightmareQuest;
    [FieldOffset(0x18)] public uint pHellQuest;
    [FieldOffset(0x1C)] public uint pNormalWaypoint;
    [FieldOffset(0x20)] public uint pNightmareWaypoint;
    [FieldOffset(0x24)] public uint pHellWaypoint;
}

/// <summary>1.13c PresetUnit (pack 1).</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x1C)]
public struct PresetUnit
{
    [FieldOffset(0x00)] public uint _1;
    [FieldOffset(0x04)] public uint dwTxtFileNo;
    [FieldOffset(0x08)] public uint dwPosX;
    [FieldOffset(0x0C)] public uint pPresetNext;
    [FieldOffset(0x10)] public uint _3;
    [FieldOffset(0x14)] public uint dwType;
    [FieldOffset(0x18)] public uint dwPosY;
}

/// <summary>1.13c Level (pack 1).</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x1D4)]
public unsafe struct Level
{
    [FieldOffset(0x000)] public fixed uint _1[4];
    [FieldOffset(0x010)] public uint pRoom2First;
    [FieldOffset(0x014)] public fixed uint _2[2];
    [FieldOffset(0x01C)] public uint dwPosX;
    [FieldOffset(0x020)] public uint dwPosY;
    [FieldOffset(0x024)] public uint dwSizeX;
    [FieldOffset(0x028)] public uint dwSizeY;
    [FieldOffset(0x02C)] public fixed uint _3[96];
    [FieldOffset(0x1AC)] public uint pNextLevel;
    [FieldOffset(0x1B0)] public uint _4;
    [FieldOffset(0x1B4)] public uint pMisc;
    [FieldOffset(0x1B8)] public fixed uint _5[6];
    [FieldOffset(0x1D0)] public uint dwLevelNo;
}

/// <summary>1.13c Room2 (pack 1).</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x60)]
public unsafe struct Room2
{
    [FieldOffset(0x00)] public fixed uint _1[2];
    [FieldOffset(0x08)] public uint pRoom2Near;
    [FieldOffset(0x0C)] public fixed uint _2[6];
    [FieldOffset(0x24)] public uint pRoom2Next;
    [FieldOffset(0x28)] public uint dwRoomFlags;
    [FieldOffset(0x2C)] public uint dwRoomsNear;
    [FieldOffset(0x30)] public uint pRoom1;
    [FieldOffset(0x34)] public uint dwPosX;
    [FieldOffset(0x38)] public uint dwPosY;
    [FieldOffset(0x3C)] public uint dwSizeX;
    [FieldOffset(0x40)] public uint dwSizeY;
    [FieldOffset(0x44)] public uint _3;
    [FieldOffset(0x48)] public uint dwPresetType;
    [FieldOffset(0x4C)] public uint pRoomTiles;
    [FieldOffset(0x50)] public fixed uint _4[2];
    [FieldOffset(0x58)] public uint pLevel;
    [FieldOffset(0x5C)] public uint pPreset;
}

/// <summary>1.13c Room1.</summary>
[StructLayout(LayoutKind.Explicit, Size = 0x80)]
public unsafe struct Room1
{
    [FieldOffset(0x00)] public uint pRoomsNear;
    [FieldOffset(0x04)] public fixed uint _1[3];
    [FieldOffset(0x10)] public uint pRoom2;
    [FieldOffset(0x14)] public fixed uint _2[3];
    [FieldOffset(0x20)] public uint Coll;
    [FieldOffset(0x24)] public uint dwRoomsNear;
    [FieldOffset(0x28)] public fixed uint _3[9];
    [FieldOffset(0x4C)] public uint dwPosX;
    [FieldOffset(0x50)] public uint dwPosY;
    [FieldOffset(0x54)] public uint dwSizeX;
    [FieldOffset(0x58)] public uint dwSizeY;
    [FieldOffset(0x5C)] public fixed uint _4[6];
    [FieldOffset(0x74)] public uint pUnitFirst;
    [FieldOffset(0x78)] public uint _5;
    [FieldOffset(0x7C)] public uint pRoomNext;
}

/// <summary>1.13c ActMisc.</summary>
[StructLayout(LayoutKind.Explicit, Size = 0x480)]
public unsafe struct ActMisc
{
    [FieldOffset(0x000)] public fixed uint _1[37];
    [FieldOffset(0x094)] public uint dwStaffTombLevel;
    [FieldOffset(0x098)] public fixed uint _2[245];
    [FieldOffset(0x46C)] public uint pAct;
    [FieldOffset(0x470)] public fixed uint _3[3];
    [FieldOffset(0x47C)] public uint pLevelFirst;
}

/// <summary>1.13c Act.</summary>
[StructLayout(LayoutKind.Explicit, Size = 0x4C)]
public unsafe struct Act
{
    [FieldOffset(0x00)] public fixed uint _1[3];
    [FieldOffset(0x0C)] public uint dwMapSeed;
    [FieldOffset(0x10)] public uint pRoom1;
    [FieldOffset(0x14)] public uint dwAct;
    [FieldOffset(0x18)] public fixed uint _2[12];
    [FieldOffset(0x48)] public uint pMisc;
}

[StructLayout(LayoutKind.Explicit, Size = 0x68)]
public unsafe struct Path
{
    [FieldOffset(0x00)] public ushort xOffset;
    [FieldOffset(0x02)] public ushort xPos;
    [FieldOffset(0x04)] public ushort yOffset;
    [FieldOffset(0x06)] public ushort yPos;
    [FieldOffset(0x08)] public fixed uint _1[2];
    [FieldOffset(0x10)] public ushort xTarget;
    [FieldOffset(0x12)] public ushort yTarget;
    [FieldOffset(0x14)] public fixed uint _2[2];
    [FieldOffset(0x1C)] public uint pRoom1;
    [FieldOffset(0x20)] public uint pRoomUnk;
    [FieldOffset(0x24)] public fixed uint _3[3];
    [FieldOffset(0x30)] public uint pUnit;
    [FieldOffset(0x34)] public uint dwFlags;
    [FieldOffset(0x38)] public uint _4;
    [FieldOffset(0x3C)] public uint dwPathType;
    [FieldOffset(0x40)] public uint dwPrevPathType;
    [FieldOffset(0x44)] public uint dwUnitSize;
    [FieldOffset(0x48)] public fixed uint _5[4];
    [FieldOffset(0x58)] public uint pTargetUnit;
    [FieldOffset(0x5C)] public uint dwTargetType;
    [FieldOffset(0x60)] public uint dwTargetId;
    [FieldOffset(0x64)] public byte bDirection;
}

[StructLayout(LayoutKind.Explicit, Size = 0x14)]
public unsafe struct ItemPath
{
    [FieldOffset(0x00)] public fixed uint _1[3];
    [FieldOffset(0x0C)] public uint dwPosX;
    [FieldOffset(0x10)] public uint dwPosY;
}

[StructLayout(LayoutKind.Explicit, Size = 0x08)]
public struct Stat
{
    [FieldOffset(0x00)] public ushort wSubIndex;
    [FieldOffset(0x02)] public ushort wStatIndex;
    [FieldOffset(0x04)] public uint dwStatValue;
}

[StructLayout(LayoutKind.Explicit, Size = 0x40)]
public unsafe struct StatList
{
    [FieldOffset(0x00)] public fixed uint _1[9];
    [FieldOffset(0x24)] public uint pStat;
    [FieldOffset(0x28)] public ushort wStatCount1;
    [FieldOffset(0x2A)] public ushort wStatCount2;
    [FieldOffset(0x2C)] public fixed uint _2[2];
    [FieldOffset(0x34)] public uint _3;
    [FieldOffset(0x38)] public uint _4;
    [FieldOffset(0x3C)] public uint pNext;
}

[StructLayout(LayoutKind.Explicit, Size = 0x2C)]
public unsafe struct Inventory1
{
    [FieldOffset(0x00)] public uint dwSignature;
    [FieldOffset(0x04)] public uint bGame1C;
    [FieldOffset(0x08)] public uint pOwner;
    [FieldOffset(0x0C)] public uint pFirstItem;
    [FieldOffset(0x10)] public uint pLastItem;
    [FieldOffset(0x14)] public fixed uint _1[2];
    [FieldOffset(0x1C)] public uint dwLeftItemUid;
    [FieldOffset(0x20)] public uint pCursorItem;
    [FieldOffset(0x24)] public uint dwOwnerId;
    [FieldOffset(0x28)] public uint dwItemCount;
}

[StructLayout(LayoutKind.Explicit, Size = 0x34)]
public unsafe struct Light
{
    [FieldOffset(0x00)] public fixed uint _1[3];
    [FieldOffset(0x0C)] public uint dwType;
    [FieldOffset(0x10)] public fixed uint _2[7];
    [FieldOffset(0x2C)] public uint dwStaticValid;
    [FieldOffset(0x30)] public uint pnStaticMap;
}

[StructLayout(LayoutKind.Explicit, Size = 0x02)]
public struct SkillInfo
{
    [FieldOffset(0x00)] public ushort wSkillId;
}

[StructLayout(LayoutKind.Explicit, Size = 0x38)]
public unsafe struct Skill
{
    [FieldOffset(0x00)] public uint pSkillInfo;
    [FieldOffset(0x04)] public uint pNextSkill;
    [FieldOffset(0x08)] public fixed uint _1[8];
    [FieldOffset(0x28)] public uint dwSkillLevel;
    [FieldOffset(0x2C)] public fixed uint _2[2];
    [FieldOffset(0x34)] public uint dwFlags;
}

[StructLayout(LayoutKind.Explicit, Size = 0x10)]
public struct Info
{
    [FieldOffset(0x00)] public uint pGame1C;
    [FieldOffset(0x04)] public uint pFirstSkill;
    [FieldOffset(0x08)] public uint pLeftSkill;
    [FieldOffset(0x0C)] public uint pRightSkill;
}

[StructLayout(LayoutKind.Explicit, Size = 0x88)]
public unsafe struct ItemData
{
    [FieldOffset(0x00)] public uint dwQuality;
    [FieldOffset(0x04)] public fixed uint _1[2];
    [FieldOffset(0x0C)] public uint dwItemFlags;
    [FieldOffset(0x10)] public fixed uint _2[2];
    [FieldOffset(0x18)] public uint dwFlags;
    [FieldOffset(0x1C)] public fixed uint _3[3];
    [FieldOffset(0x28)] public uint dwQuality2;
    [FieldOffset(0x2C)] public uint dwItemLevel;
    [FieldOffset(0x30)] public fixed uint _4[2];
    [FieldOffset(0x38)] public ushort wPrefix;
    [FieldOffset(0x3A)] public fixed ushort _5[2];
    [FieldOffset(0x3E)] public ushort wSuffix;
    [FieldOffset(0x40)] public uint _6;
    [FieldOffset(0x44)] public byte BodyLocation;
    [FieldOffset(0x45)] public byte ItemLocation;
    [FieldOffset(0x46)] public byte _7;
    /// <summary>The C comment says 0x47, but WORD alignment puts it at 0x48.</summary>
    [FieldOffset(0x48)] public ushort _8;
    [FieldOffset(0x4C)] public fixed uint _9[4];
    [FieldOffset(0x5C)] public uint pOwnerInventory;
    [FieldOffset(0x60)] public uint _10;
    [FieldOffset(0x64)] public uint pNextInvItem;
    [FieldOffset(0x68)] public byte _11;
    [FieldOffset(0x69)] public byte NodePage;
    [FieldOffset(0x6A)] public ushort _12;
    [FieldOffset(0x6C)] public fixed uint _13[6];
    [FieldOffset(0x84)] public uint pOwner;
}

[StructLayout(LayoutKind.Explicit, Size = 0x64)]
public unsafe struct MonsterData
{
    [FieldOffset(0x00)] public fixed byte _1[22];
    /// <summary>Anonymous bit-field byte at 0x16: fUnk:1, fNormal:1, fChamp:1, fBoss:1, fMinion:1 (LSB first).</summary>
    [FieldOffset(0x16)] public byte Flags;
    [FieldOffset(0x17)] public fixed byte _2[5];
    [FieldOffset(0x1C)] public fixed byte anEnchants[9];
    [FieldOffset(0x26)] public ushort wUniqueNo;
    [FieldOffset(0x28)] public uint _5;
    [FieldOffset(0x2C)] public fixed ushort wName[28];

    public readonly bool fUnk => (Flags & 0x01) != 0;
    public readonly bool fNormal => (Flags & 0x02) != 0;
    public readonly bool fChamp => (Flags & 0x04) != 0;
    public readonly bool fBoss => (Flags & 0x08) != 0;
    public readonly bool fMinion => (Flags & 0x10) != 0;
}

[StructLayout(LayoutKind.Explicit, Size = 0x308)]
public unsafe struct MonsterTxt
{
    [FieldOffset(0x000)] public fixed byte _1[0x6];
    [FieldOffset(0x006)] public ushort nLocaleTxtNo;
    [FieldOffset(0x008)] public ushort flag;
    [FieldOffset(0x00A)] public ushort _1a;
    // union { DWORD flag1; struct { BYTE flag1a, flag1b, flag1c[2]; }; }
    [FieldOffset(0x00C)] public uint flag1;
    [FieldOffset(0x00C)] public byte flag1a;
    [FieldOffset(0x00D)] public byte flag1b;
    [FieldOffset(0x00E)] public fixed byte flag1c[2];
    [FieldOffset(0x010)] public fixed byte _2[0x22];
    [FieldOffset(0x032)] public ushort velocity;
    [FieldOffset(0x034)] public fixed byte _2a[0x52];
    /// <summary>WORD tcs[3][4], row-major.</summary>
    [FieldOffset(0x086)] public fixed ushort tcs[3 * 4];
    [FieldOffset(0x09E)] public fixed byte _2b[0x52];
    [FieldOffset(0x0F0)] public fixed ushort szDescriptor[0x3C];
    [FieldOffset(0x168)] public fixed byte _3[0x1A0];
}

/// <summary>objects.txt record.</summary>
[StructLayout(LayoutKind.Explicit, Size = 0x1C0)]
public unsafe struct ObjectTxt
{
    [FieldOffset(0x000)] public fixed byte szName[0x40];
    [FieldOffset(0x040)] public fixed ushort wszName[0x40];
    [FieldOffset(0x0C0)] public fixed byte _1[4];
    [FieldOffset(0x0C4)] public byte nSelectable0;
    [FieldOffset(0x0C5)] public fixed byte _2[0x87];
    [FieldOffset(0x14C)] public byte nOrientation;
    [FieldOffset(0x14D)] public fixed byte _2b[0x19];
    [FieldOffset(0x166)] public byte nSubClass;
    [FieldOffset(0x167)] public fixed byte _3[0x11];
    [FieldOffset(0x178)] public byte nParm0;
    [FieldOffset(0x179)] public fixed byte _4[0x39];
    [FieldOffset(0x1B2)] public byte nPopulateFn;
    [FieldOffset(0x1B3)] public byte nOperateFn;
    [FieldOffset(0x1B4)] public fixed byte _5[8];
    /// <summary>The C comment says 0x1BB, but DWORD alignment puts it at 0x1BC.</summary>
    [FieldOffset(0x1BC)] public uint nAutoMap;
}

[StructLayout(LayoutKind.Explicit, Size = 0x38)]
public unsafe struct ObjectData
{
    [FieldOffset(0x00)] public uint pTxt;
    [FieldOffset(0x04)] public uint Type;
    [FieldOffset(0x08)] public fixed uint _1[8];
    [FieldOffset(0x28)] public fixed byte szOwner[0x10];
}

[StructLayout(LayoutKind.Explicit, Size = 0x14)]
public unsafe struct ObjectPath
{
    [FieldOffset(0x00)] public uint pRoom1;
    [FieldOffset(0x04)] public fixed uint _1[2];
    [FieldOffset(0x0C)] public uint dwPosX;
    [FieldOffset(0x10)] public uint dwPosY;
}

[StructLayout(LayoutKind.Explicit, Size = 0xEC)]
public unsafe struct UnitAny
{
    [FieldOffset(0x00)] public uint dwType;
    [FieldOffset(0x04)] public uint dwTxtFileNo;
    [FieldOffset(0x08)] public uint _1;
    [FieldOffset(0x0C)] public uint dwUnitId;
    [FieldOffset(0x10)] public uint dwMode;
    // union at 0x14
    [FieldOffset(0x14)] public uint pPlayerData;
    [FieldOffset(0x14)] public uint pItemData;
    [FieldOffset(0x14)] public uint pMonsterData;
    [FieldOffset(0x14)] public uint pObjectData;
    [FieldOffset(0x18)] public uint dwAct;
    [FieldOffset(0x1C)] public uint pAct;
    [FieldOffset(0x20)] public fixed uint dwSeed[2];
    [FieldOffset(0x28)] public uint _2;
    // union at 0x2C
    [FieldOffset(0x2C)] public uint pPath;
    [FieldOffset(0x2C)] public uint pItemPath;
    [FieldOffset(0x2C)] public uint pObjectPath;
    [FieldOffset(0x30)] public fixed uint _3[5];
    [FieldOffset(0x44)] public uint dwGfxFrame;
    [FieldOffset(0x48)] public uint dwFrameRemain;
    [FieldOffset(0x4C)] public ushort wFrameRate;
    [FieldOffset(0x4E)] public ushort _4;
    [FieldOffset(0x50)] public uint pGfxUnk;
    [FieldOffset(0x54)] public uint pGfxInfo;
    [FieldOffset(0x58)] public uint _5;
    [FieldOffset(0x5C)] public uint pStats;
    [FieldOffset(0x60)] public uint pInventory;
    [FieldOffset(0x64)] public uint ptLight;
    [FieldOffset(0x68)] public fixed uint _6[9];
    [FieldOffset(0x8C)] public ushort wX;
    [FieldOffset(0x8E)] public ushort wY;
    [FieldOffset(0x90)] public uint _7;
    [FieldOffset(0x94)] public uint dwOwnerType;
    [FieldOffset(0x98)] public uint dwOwnerId;
    [FieldOffset(0x9C)] public fixed uint _8[2];
    [FieldOffset(0xA4)] public uint pOMsg;
    [FieldOffset(0xA8)] public uint pInfo;
    [FieldOffset(0xAC)] public fixed uint _9[6];
    [FieldOffset(0xC4)] public uint dwFlags;
    [FieldOffset(0xC8)] public uint dwFlags2;
    [FieldOffset(0xCC)] public fixed uint _10[5];
    [FieldOffset(0xE0)] public uint pChangedNext;
    [FieldOffset(0xE4)] public uint pRoomNext;
    [FieldOffset(0xE8)] public uint pListNext;
}

/// <summary>Pack 1.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x27)]
public unsafe struct NPCMenu
{
    [FieldOffset(0x00)] public uint dwNPCClassId;
    [FieldOffset(0x04)] public uint dwEntryAmount;
    [FieldOffset(0x08)] public ushort wEntryId1;
    [FieldOffset(0x0A)] public ushort wEntryId2;
    [FieldOffset(0x0C)] public ushort wEntryId3;
    [FieldOffset(0x0E)] public ushort wEntryId4;
    [FieldOffset(0x10)] public ushort _1;
    [FieldOffset(0x12)] public uint dwEntryFunc1;
    [FieldOffset(0x16)] public uint dwEntryFunc2;
    [FieldOffset(0x1A)] public uint dwEntryFunc3;
    [FieldOffset(0x1E)] public uint dwEntryFunc4;
    [FieldOffset(0x22)] public fixed byte _2[5];
}

/// <summary>Pack 1.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0xF8)]
public unsafe struct OverheadMsg
{
    [FieldOffset(0x00)] public uint _1;
    [FieldOffset(0x04)] public uint dwTrigger;
    [FieldOffset(0x08)] public fixed uint _2[2];
    [FieldOffset(0x10)] public fixed byte Msg[232];
}

[StructLayout(LayoutKind.Explicit, Size = 0x104)]
public unsafe struct D2MSG
{
    /// <summary>HWND (32-bit handle).</summary>
    [FieldOffset(0x00)] public uint myHWND;
    [FieldOffset(0x04)] public fixed byte lpBuf[256];
}

[StructLayout(LayoutKind.Explicit, Size = 0x18)]
public struct InventoryLayout
{
    [FieldOffset(0x00)] public byte SlotWidth;
    [FieldOffset(0x01)] public byte SlotHeight;
    [FieldOffset(0x02)] public byte unk1;
    [FieldOffset(0x03)] public byte unk2;
    [FieldOffset(0x04)] public uint Left;
    [FieldOffset(0x08)] public uint Right;
    [FieldOffset(0x0C)] public uint Top;
    [FieldOffset(0x10)] public uint Bottom;
    [FieldOffset(0x14)] public byte SlotPixelWidth;
    [FieldOffset(0x15)] public byte SlotPixelHeight;
}

/// <summary>Empty C++ struct (sizeof == 1).</summary>
[StructLayout(LayoutKind.Explicit, Size = 0x01)]
public struct MpqTable
{
    [FieldOffset(0x00)] private readonly byte _empty;
}

[StructLayout(LayoutKind.Explicit, Size = 0x1C)]
public struct sgptDataTable
{
    [FieldOffset(0x00)] public uint pPlayerClass;
    [FieldOffset(0x04)] public uint dwPlayerClassRecords;
    [FieldOffset(0x08)] public uint pBodyLocs;
    [FieldOffset(0x0C)] public uint dwBodyLocsRecords;
    [FieldOffset(0x10)] public uint pStorePage;
    [FieldOffset(0x14)] public uint dwStorePageRecords;
    [FieldOffset(0x18)] public uint pElemTypes;
}

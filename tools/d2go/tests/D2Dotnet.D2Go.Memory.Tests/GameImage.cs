using D2Dotnet.D2Go.Data;
using D2Dotnet.D2Go.Data.Stat;

namespace D2Dotnet.D2Go.Memory.Tests;

/// <summary>A stat list entry: layer u16, id u16, value u32.</summary>
internal readonly record struct RawStat(StatId Id, uint Value, ushort Layer = 0);

/// <summary>A player unit to lay out in a <see cref="GameImage"/>.</summary>
internal sealed record PlayerSpec
{
    public uint UnitId { get; init; } = 1;
    public string Name { get; init; } = "";
    public uint Area { get; init; }
    public ushort X { get; init; }
    public ushort Y { get; init; }
    public bool IsMain { get; init; }
    public bool HasInventory { get; init; } = true;
    public byte CorpseFlag { get; init; }
    public RawStat[] Stats { get; init; } = [];
    public RawStat[] BaseStats { get; init; } = [];
    public uint[] StateWords { get; init; } = new uint[6];
    public ulong StashOrder { get; init; }
    public uint Class { get; init; }
    public int Bucket { get; init; }
}

/// <summary>
/// Builds the memory image of a game, laid out exactly as d2go reads it: a module whose code holds the offset patterns
/// (resolving to the offsets below), and the structures they lead to, allocated in a <see cref="FakeProcessMemory"/>.
/// Structure offsets are written from the Go source (pkg/memory/*.go).
/// </summary>
internal sealed class GameImage
{
    public const ulong Base = 0x7FF6_1000_0000;
    public const uint CodeSize = 0x2000;

    // Where each pattern is in the module code, and the offsets it resolves to.
    public const uint GameDataAt = 0x100, UnitTableAt = 0x200, UIAt = 0x300, HoverAt = 0x400, ExpansionAt = 0x500, RosterAt = 0x600, PanelAt = 0x700;
    public const ulong GameDataOffset = 0x0222_3323;
    public const ulong UnitTableOffset = 0x01E0_0000;
    public const ulong UIOffset = 0x0210_0000;
    public const ulong HoverOffset = 0x0220_0000;
    public const ulong ExpansionOffset = 0x0230_0000;
    public const ulong RosterOffset = 0x0240_0601;
    public const ulong PanelOffset = 0x0250_0000;

    private readonly Dictionary<(ulong Table, int Bucket), ulong> _tails = [];

    public GameImage(bool withPatterns = true)
    {
        Memory = new FakeProcessMemory(unchecked((nuint)Base), CodeSize);
        Memory.Map(Base, CodeSize);
        Memory.Map(Base + UnitTableOffset, 5 * 1024);
        if (withPatterns)
            WritePatterns();

        // Expansion character pointer.
        ExpansionChar = Memory.Alloc(0x100);
        Memory.WriteU64(Base + ExpansionOffset, ExpansionChar);
    }

    public FakeProcessMemory Memory { get; }

    public ulong ExpansionChar { get; }

    public static Offset ExpectedOffset => new(
        (nuint)GameDataOffset, (nuint)UnitTableOffset, (nuint)UIOffset, (nuint)HoverOffset, (nuint)ExpansionOffset,
        (nuint)RosterOffset, (nuint)PanelOffset);

    public GameReader Reader(GameTables? tables = null) => new(new GameProcess(Memory), tables ?? TestTables.Tables);

    /// <summary>Writes the seven patterns of offset.go with operands that resolve to the offsets above.</summary>
    public void WritePatterns()
    {
        // GameData: offset = (pattern - base) - 0x121 + u32 at pattern+3.
        WritePattern(Offsets.GameData, GameDataAt);
        Memory.WriteU32(Base + GameDataAt + 3, (uint)(GameDataOffset - GameDataAt + 0x121));
        // UnitTable: u32 at pattern+7.
        WritePattern(Offsets.UnitTable, UnitTableAt);
        Memory.WriteU32(Base + UnitTableAt + 7, (uint)UnitTableOffset);
        // UI: (pattern - base) + 10 + u32 at pattern+6.
        WritePattern(Offsets.UI, UIAt);
        Memory.WriteU32(Base + UIAt + 6, (uint)(UIOffset - UIAt - 10));
        // Hover: u32 at pattern+3, minus 1.
        WritePattern(Offsets.Hover, HoverAt);
        Memory.WriteU32(Base + HoverAt + 3, (uint)(HoverOffset + 1));
        // Expansion: (pattern - base) + 7 + u32 at pattern+3.
        WritePattern(Offsets.Expansion, ExpansionAt);
        Memory.WriteU32(Base + ExpansionAt + 3, (uint)(ExpansionOffset - ExpansionAt - 7));
        // Roster: (pattern - base) + 1 + u32 at pattern-3, whose top byte is the pattern's first byte (0x02).
        Memory.WriteU32(Base + RosterAt - 3, (uint)(RosterOffset - RosterAt - 1));
        WritePattern(Offsets.Roster, RosterAt);
        Assert.Equal(0x02u, (uint)((RosterOffset - RosterAt - 1) >> 24));
        // PanelManagerContainer: (pattern - base) + u32 at pattern+3 (read from the image) + 7.
        WritePattern(Offsets.PanelManagerContainer, PanelAt);
        Memory.WriteU32(Base + PanelAt + 3, (uint)(PanelOffset - PanelAt - 7));
    }

    private void WritePattern(OffsetPattern pattern, uint at)
    {
        for (var i = 0; i < pattern.Bytes.Length; i++)
        {
            if (pattern.Mask[i] != '?')
                Memory.WriteU8(Base + at + (ulong)i, pattern.Bytes[i]);
        }
    }

    // ---- unit tables ----

    public const ulong PlayerTable = 0, MonsterTable = 1024, ObjectTable = 2 * 1024, ItemTable = 4 * 1024;

    /// <summary>Appends a unit to a bucket's list (linked at +0x150).</summary>
    public void Link(ulong table, int bucket, ulong unit)
    {
        if (_tails.TryGetValue((table, bucket), out var tail))
            Memory.WriteU64(tail + 0x150, unit);
        else
            Memory.WriteU64(Base + UnitTableOffset + table + (ulong)(bucket * 8), unit);
        _tails[(table, bucket)] = unit;
    }

    /// <summary>A stat list header (pointer, count) at <paramref name="header"/> and its 8-byte entries.</summary>
    public void WriteStatList(ulong header, IReadOnlyList<RawStat> stats)
    {
        if (stats.Count == 0)
            return;
        var array = Memory.Alloc((ulong)(stats.Count * 8));
        for (var i = 0; i < stats.Count; i++)
        {
            var e = array + (ulong)(i * 8);
            Memory.WriteU16(e, stats[i].Layer);
            Memory.WriteU16(e + 2, (ushort)stats[i].Id);
            Memory.WriteU32(e + 4, stats[i].Value);
        }

        Memory.WriteU64(header, array);
        Memory.WriteU64(header + 8, (ulong)stats.Count);
    }

    /// <summary>A path whose (u16) coordinates are at <paramref name="xAt"/> and <paramref name="xAt"/> + 4.</summary>
    public ulong Path(ushort x, ushort y, ulong xAt)
    {
        var path = Memory.Alloc(0x100);
        Memory.WriteU16(path + xAt, x);
        Memory.WriteU16(path + xAt + 4, y);
        return path;
    }

    /// <summary>Lays out a player unit (player.go) and links it into its bucket.</summary>
    public ulong AddPlayer(PlayerSpec p)
    {
        var unit = Memory.Alloc(0x200);
        Memory.WriteU32(unit + 0x08, p.UnitId);

        var name = Memory.Alloc(0x40);
        Memory.WriteCString(name, p.Name);
        Memory.WriteU64(unit + 0x10, name);

        var path = Path(p.X, p.Y, 0x02);
        var room1 = Memory.Alloc(0x40);
        var room2 = Memory.Alloc(0x100);
        var level = Memory.Alloc(0x200);
        Memory.WriteU64(path + 0x20, room1);
        Memory.WriteU64(room1 + 0x18, room2);
        Memory.WriteU64(room2 + 0x90, level);
        Memory.WriteU32(level + 0x1F8, p.Area);
        Memory.WriteU64(unit + 0x38, path);

        if (p.HasInventory)
        {
            var inventory = Memory.Alloc(0x100);
            // Both flags: d2go picks +0x70 for expansion characters, +0x30 otherwise.
            Memory.WriteU16(inventory + 0x30, p.IsMain ? (ushort)1 : (ushort)0);
            Memory.WriteU16(inventory + 0x70, p.IsMain ? (ushort)1 : (ushort)0);
            Memory.WriteU64(unit + 0x90, inventory);
        }

        var statsEx = Memory.Alloc(0xB00);
        WriteStatList(statsEx + 0x30, p.BaseStats);
        WriteStatList(statsEx + 0x88, p.Stats);
        for (var i = 0; i < 6; i++)
            Memory.WriteU32(statsEx + 0xAD0 + (ulong)(i * 4), p.StateWords[i]);
        Memory.WriteU64(unit + 0x88, statsEx);

        Memory.WriteU64(unit + 0xD8, p.StashOrder);
        Memory.WriteU32(unit + 0x174, p.Class);
        Memory.WriteU8(unit + 0x1A6, p.CorpseFlag);

        Link(PlayerTable, p.Bucket, unit);
        return unit;
    }

    /// <summary>Lays out a monster unit (monsters.go) and links it into its bucket.</summary>
    public ulong AddMonster(uint txtFileNo, uint unitId, ushort x, ushort y, byte typeFlag = 0, bool corpse = false,
        IReadOnlyList<RawStat>? stats = null, int bucket = 0)
    {
        var unit = Memory.Alloc(0x200);
        Memory.WriteU32(unit + 0x00, 1);
        Memory.WriteU32(unit + 0x04, txtFileNo);
        Memory.WriteU32(unit + 0x08, unitId);
        var unitData = Memory.Alloc(0x100);
        Memory.WriteU8(unitData + 0x1A, typeFlag);
        Memory.WriteU64(unit + 0x10, unitData);
        Memory.WriteU64(unit + 0x38, Path(x, y, 0x02));
        var statsEx = Memory.Alloc(0x100);
        // Monster stats: pointer at +0x30, count at +0x38, entries read from pointer + 2 as (id u16, value u32).
        WriteStatList(statsEx + 0x30, stats ?? []);
        Memory.WriteU64(unit + 0x88, statsEx);
        Memory.WriteU8(unit + 0x1A6, corpse ? (byte)1 : (byte)0);
        Link(MonsterTable, bucket, unit);
        return unit;
    }

    /// <summary>
    /// Lays out an object unit (object.go). <paramref name="nonShrineLayout"/> aligns the unit data to 64 KiB so that,
    /// with a unit id below 65536 and mode 0, the 8 bytes at +0x0A are 0 and d2go reads the interact type.
    /// </summary>
    public ulong AddObject(uint txtFileNo, uint unitId, ushort x, ushort y, uint mode = 0, byte typeByte = 0,
        string owner = "", uint unitType = 2, bool nonShrineLayout = false, int bucket = 0)
    {
        var unit = Memory.Alloc(0x200);
        Memory.WriteU32(unit + 0x00, unitType);
        Memory.WriteU32(unit + 0x04, txtFileNo);
        Memory.WriteU32(unit + 0x08, unitId);
        Memory.WriteU32(unit + 0x0C, mode);
        var unitData = Memory.Alloc(0x100, nonShrineLayout ? 0x10000UL : 0x10);
        if (!nonShrineLayout && (unitData & 0xFFFF) == 0)
            unitData = Memory.Alloc(0x100, 0x10) | 0x10;
        Memory.Map(unitData, 0x100);
        Memory.WriteU8(unitData + 0x08, typeByte);
        Memory.Write(unitData + 0x34, System.Text.Encoding.UTF8.GetBytes(owner));
        Memory.WriteU64(unit + 0x10, unitData);
        Memory.WriteU64(unit + 0x38, Path(x, y, 0x10));
        Link(ObjectTable, bucket, unit);
        return unit;
    }

    /// <summary>The item stats structure (item.go getItemStats) for an item.</summary>
    public ulong ItemStats(IReadOnlyList<RawStat>? baseStats = null, IReadOnlyList<RawStat>? fullStats = null, uint flags = 0, ulong lastStatsList = 0)
    {
        var statsEx = Memory.Alloc(0x100);
        WriteStatList(statsEx + 0x30, baseStats ?? []);
        WriteStatList(statsEx + 0x88, fullStats ?? []);
        Memory.WriteU32(statsEx + 0x1C, flags);
        Memory.WriteU64(statsEx + 0x70, lastStatsList);
        return statsEx;
    }

    /// <summary>A modifier stat list node: flags at +0x1C (u64), base stats at +0x30, previous list at +0x48.</summary>
    public ulong StatListNode(ulong flags, IReadOnlyList<RawStat> stats, ulong previous = 0)
    {
        var node = Memory.Alloc(0x100);
        Memory.WriteU64(node + 0x1C, flags);
        WriteStatList(node + 0x30, stats);
        Memory.WriteU64(node + 0x48, previous);
        return node;
    }

    /// <summary>Lays out an item unit (item.go) and links it into its bucket.</summary>
    public ulong AddItem(uint txtFileNo, uint unitId, uint location, uint owner, byte invPage = 0, uint quality = 2,
        uint flags = 0, ushort x = 0, ushort y = 0, ulong statsEx = 0, uint unitType = 4, int bucket = 0)
    {
        var unit = Memory.Alloc(0x200);
        Memory.WriteU32(unit + 0x00, unitType);
        Memory.WriteU32(unit + 0x04, txtFileNo);
        Memory.WriteU32(unit + 0x08, unitId);
        Memory.WriteU32(unit + 0x0C, location);
        var unitData = Memory.Alloc(0x100);
        Memory.WriteU32(unitData + 0x00, quality);
        Memory.WriteU32(unitData + 0x0C, owner);
        Memory.WriteU32(unitData + 0x18, flags);
        Memory.WriteU8(unitData + 0x55, invPage);
        Memory.WriteU64(unit + 0x10, unitData);
        Memory.WriteU64(unit + 0x38, Path(x, y, 0x10));
        Memory.WriteU64(unit + 0x88, statsEx == 0 ? ItemStats() : statsEx);
        Link(ItemTable, bucket, unit);
        return unit;
    }

    // ---- fixed structures ----

    /// <summary>The hovered unit (game_reader.go hoveredData).</summary>
    public void SetHover(ushort isHovered, uint unitType, uint unitId)
    {
        Memory.WriteU16(Base + HoverOffset, isHovered);
        Memory.WriteU32(Base + HoverOffset + 4, unitType);
        Memory.WriteU32(Base + HoverOffset + 8, unitId);
    }

    /// <summary>A byte of the UI buffer, which starts at base + UI - 0xA.</summary>
    public void SetUIByte(int index, byte value) => Memory.WriteU8(Base + UIOffset - 0xA + (ulong)index, value);

    /// <summary>The 85-byte quest buffer behind two pointers at base + 0x22E2978; d2go drops its first 3 bytes.</summary>
    public ulong SetQuestBytes(ReadOnlySpan<byte> afterFirstThree)
    {
        var buffer = Memory.Alloc(85);
        Memory.Write(buffer + 3, afterFirstThree);
        var q1 = Memory.Alloc(8);
        Memory.WriteU64(q1, buffer);
        Memory.WriteU64(Base + 0x22E2978, q1);
        return buffer;
    }

    /// <summary>A party list (roster.go): structs linked at +0x148, the first being the main player's.</summary>
    public void SetRoster(params (string Name, uint Area, uint X, uint Y)[] members)
    {
        var first = Memory.Alloc(0x200);
        var prev = first;
        foreach (var (name, area, x, y) in members)
        {
            var s = Memory.Alloc(0x200);
            Memory.Write(s, System.Text.Encoding.UTF8.GetBytes(name));
            Memory.WriteU32(s + 0x5C, area);
            Memory.WriteU32(s + 0x60, x);
            Memory.WriteU32(s + 0x64, y);
            Memory.WriteU64(prev + 0x148, s);
            prev = s;
        }

        Memory.WriteU64(Base + RosterOffset, first);
    }

    /// <summary>
    /// A widget container (process.go ReadWidgetContainer): name pointer at +0x08, name length at +0x10, active at
    /// +0x50 and visible at +0x51; the name struct holds the child list at +0x38 and its size at +0x40.
    /// </summary>
    public ulong Widget(string name, bool active, bool visible, uint nameLength = 0, ulong childList = 0, uint childCount = 0)
    {
        var container = Memory.Alloc(0x100);
        var nameStruct = Memory.Alloc(0x100);
        Memory.WriteCString(nameStruct, name);
        Memory.WriteU64(container + 0x08, nameStruct);
        Memory.WriteU32(container + 0x10, nameLength);
        Memory.WriteU8(container + 0x50, active ? (byte)1 : (byte)0);
        Memory.WriteU8(container + 0x51, visible ? (byte)1 : (byte)0);
        Memory.WriteU64(nameStruct + 0x38, childList);
        Memory.WriteU32(nameStruct + 0x40, childCount);
        return container;
    }

    /// <summary>The panel manager and its child widgets.</summary>
    public void SetPanels(params ulong[] children)
    {
        var list = Memory.Alloc((ulong)Math.Max(children.Length * 8, 8));
        for (var i = 0; i < children.Length; i++)
            Memory.WriteU64(list + (ulong)(i * 8), children[i]);
        var panel = Widget("PanelManager", true, true, childList: list, childCount: (uint)children.Length);
        Memory.WriteU64(Base + PanelOffset, panel);
    }

    public static Position Pos(int x, int y) => new(x, y);
}

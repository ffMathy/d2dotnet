using D2Dotnet.Map.Binary;

namespace D2Dotnet.Map.Bintools.Readers;

/// <summary>A monstats.bin record (upstream <c>MonsterNode</c>).</summary>
public sealed record MonsterNode(
    ushort Id,
    ushort BaseId,
    ushort BaseNextId,
    // Name index, lookup inside of lang index
    ushort NameLangId,
    // Description lookup inside of lang index
    ushort DescriptionLangId,
    ushort Unk1,
    uint Flags,
    uint Code);

/// <summary>A MonStats2.bin record (upstream <c>Monster2Parser</c>).</summary>
public sealed record Monster2Node(
    uint Id,
    ushort Unk1,
    ushort Unk1b,
    byte SizeX,
    byte SizeY,
    // This state is used to read the NpcAssign packet
    byte[] State,
    byte[] Unk100);

/// <summary>Reader for data/global/excel/monstats.bin (upstream <c>MonsterReader</c>).</summary>
public static class MonsterReader
{
    /// <summary>Size of a single monster record.</summary>
    public const int RecordSize = 424;

    public static IReadOnlyList<MonsterNode> Parse(byte[] bytes) => Parse(new BinaryCursor(bytes));

    public static IReadOnlyList<MonsterNode> Parse(BinaryCursor bp)
    {
        var count = bp.Lu32();
        var monsters = new List<MonsterNode>((int)Math.Min(count, 4096));
        for (var i = 0; i < count; i++)
        {
            var node = new MonsterNode(
                Id: bp.Lu16(),
                BaseId: bp.Lu16(),
                BaseNextId: bp.Lu16(),
                NameLangId: bp.Lu16(),
                DescriptionLangId: bp.Lu16(),
                Unk1: bp.Lu16(),
                Flags: bp.Lu32(),
                Code: bp.Lu32());
            bp.Skip(RecordSize - 20);
            monsters.Add(node);
        }
        return monsters;
    }
}

/// <summary>Reader for data/global/excel/MonStats2.bin (upstream <c>MonsterReader2</c>).</summary>
public static class MonsterReader2
{
    private const int Unk100Size = 0x134 - 10 - 11 - 16;

    public static IReadOnlyList<Monster2Node> Parse(byte[] bytes) => Parse(new BinaryCursor(bytes));

    public static IReadOnlyList<Monster2Node> Parse(BinaryCursor bp)
    {
        var count = bp.Lu32();
        var monsters = new List<Monster2Node>((int)Math.Min(count, 4096));
        for (var i = 0; i < count; i++)
        {
            var id = bp.Lu32();
            var unk1 = bp.Lu16();
            var unk1b = bp.Lu16();
            var sizeX = bp.U8();
            var sizeY = bp.U8();
            bp.Skip(11);
            var state = bp.Bytes(16);
            var unk100 = bp.Bytes(Unk100Size);
            monsters.Add(new Monster2Node(id, unk1, unk1b, sizeX, sizeY, state, unk100));
        }
        return monsters;
    }
}

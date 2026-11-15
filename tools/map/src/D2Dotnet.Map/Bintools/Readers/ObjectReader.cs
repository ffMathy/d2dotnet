using D2Dotnet.Map.Binary;

namespace D2Dotnet.Map.Bintools.Readers;

/// <summary>An objects.bin record (upstream <c>ObjectNode</c>).</summary>
public sealed record ObjectNode(
    string Name,
    string NameW,
    string Token,
    byte SpawnMax,
    byte TrapProb,
    uint X,
    uint Y,
    byte IsDoor,
    byte IsVisibilityBlocked,
    byte Orientation,
    uint XOffset,
    uint YOffset,
    byte XSpace,
    byte YSpace,
    byte Red,
    byte Green,
    byte Blue,
    byte SubClass);

/// <summary>Reader for data/global/excel/objects.bin (upstream <c>ObjectReader</c>).</summary>
/// <remarks>Field offsets and skips are copied verbatim from upstream.</remarks>
public static class ObjectReader
{
    /// <summary>Size of a single object record.</summary>
    public const int RecordSize = 448;

    private const int Skip3 = 0x13a - 0xd8;
    private const int Skip4 = 0x148 - 0x13d;
    private const int Skip5 = 0x162 - 0x150;
    // name(64) nameW(64) token(3) spawnMax(1) skip1(8) trapProb(1) skip2(3) x(4) y(4)
    // skip3 isDoor isVisibilityBlocked orientation(3) skip4 xOffset yOffset(8) skip5 xSpace ySpace red green blue subClass(6)
    private const int FieldsSize = 64 + 64 + 3 + 1 + 8 + 1 + 3 + 4 + 4 + Skip3 + 3 + Skip4 + 8 + Skip5 + 6;
    private const int SkipToEnd = RecordSize - FieldsSize;

    public static IReadOnlyList<ObjectNode> Parse(byte[] bytes) => Parse(new BinaryCursor(bytes));

    public static IReadOnlyList<ObjectNode> Parse(BinaryCursor bp)
    {
        var count = bp.Lu32();
        var objects = new List<ObjectNode>((int)Math.Min(count, 4096));
        for (var i = 0; i < count; i++) objects.Add(ParseObject(bp));
        return objects;
    }

    private static ObjectNode ParseObject(BinaryCursor bp)
    {
        var name = bp.FixedString(64);
        var nameW = bp.FixedString(64);
        var token = bp.FixedString(3);
        var spawnMax = bp.U8();
        bp.Skip(8);
        var trapProb = bp.U8();
        bp.Skip(3);
        var x = bp.Lu32();
        var y = bp.Lu32();

        bp.Skip(Skip3);

        var isDoor = bp.U8();
        var isVisibilityBlocked = bp.U8();
        var orientation = bp.U8();

        bp.Skip(Skip4);
        var xOffset = bp.Lu32();
        var yOffset = bp.Lu32();

        bp.Skip(Skip5);
        var xSpace = bp.U8();
        var ySpace = bp.U8();

        var red = bp.U8();
        var green = bp.U8();
        var blue = bp.U8();
        var subClass = bp.U8();

        bp.Skip(SkipToEnd);

        return new ObjectNode(name, nameW, token, spawnMax, trapProb, x, y, isDoor, isVisibilityBlocked, orientation,
            xOffset, yOffset, xSpace, ySpace, red, green, blue, subClass);
    }
}

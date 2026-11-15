using D2Dotnet.Map.Binary;

namespace D2Dotnet.Map.Bintools.Readers;

/// <summary>A levels.bin record (upstream <c>LevelNode</c>).</summary>
public sealed record LevelNode(
    ushort Id,
    byte Pallette,
    byte Act,
    bool IsTeleportEnabled,
    bool IsRaining,
    byte Mud,
    byte NoPer,
    byte IsInside,
    string Name,
    string Warp);

/// <summary>Reader for data/global/excel/levels.bin (upstream <c>LevelReader</c>).</summary>
public static class LevelReader
{
    /// <summary>Size of a single level record.</summary>
    public const int RecordSize = 544;

    // id(2) pallette act isTeleportEnabled isRaining mud noPer isInside (7) + skip2(0xf5 - 9) + name(40) + warp(40)
    private const int Skip2 = 0xf5 - 9;
    private const int FieldsSize = 2 + 7 + Skip2 + 40 + 40;
    private const int SkipToEnd = RecordSize - FieldsSize;

    public static IReadOnlyList<LevelNode> Parse(byte[] bytes) => Parse(new BinaryCursor(bytes));

    public static IReadOnlyList<LevelNode> Parse(BinaryCursor bp)
    {
        var count = bp.Lu32();
        var levels = new List<LevelNode>((int)Math.Min(count, 4096));
        for (var i = 0; i < count; i++) levels.Add(ParseLevel(bp));
        return levels;
    }

    private static LevelNode ParseLevel(BinaryCursor bp)
    {
        var id = bp.Lu16();
        var pallette = bp.U8();
        var act = bp.U8();
        var isTeleportEnabled = bp.U8() == 1;
        var isRaining = bp.U8() == 1;
        var mud = bp.U8();
        var noPer = bp.U8();
        var isInside = bp.U8();
        bp.Skip(Skip2);
        var name = bp.FixedString(40);
        var warp = bp.FixedString(40);
        // entryFile: bp.string(40),
        bp.Skip(SkipToEnd);
        return new LevelNode(id, pallette, act, isTeleportEnabled, isRaining, mud, noPer, isInside, name, warp);
    }
}

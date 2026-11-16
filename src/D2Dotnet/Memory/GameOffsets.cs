// Patterns and offset decoding from d2go pkg/memory/offset.go and addresses.go (github.com/hectorgimenez/d2go, MIT,
// (c) 2023 Héctor Giménez).

using System.Buffers.Binary;

namespace D2Dotnet.Memory;

/// <summary>
/// Where the game keeps its structures, relative to the module base: found by scanning the game code for byte
/// patterns when a <see cref="GameReader"/> is created.
/// </summary>
/// <param name="GameData">The game data structure.</param>
/// <param name="UnitTable">The unit hash tables (players, monsters, objects, missiles, items; 1024 bytes each).</param>
/// <param name="UI">The user interface flags (the automap flag; the panel flags start 0xA bytes before).</param>
/// <param name="Hover">The hovered unit.</param>
/// <param name="Expansion">The pointer to the expansion character flag.</param>
/// <param name="Roster">The pointer to the party list.</param>
/// <param name="PanelManager">The pointer to the panel manager widget.</param>
public readonly record struct GameOffsets(
    nuint GameData,
    nuint UnitTable,
    nuint UI,
    nuint Hover,
    nuint Expansion,
    nuint Roster,
    nuint PanelManager)
{
    /// <summary>A code pattern: its bytes, and a mask where <c>?</c> matches any byte.</summary>
    internal sealed record Pattern(string Name, byte[] Bytes, string Mask);

    // offset.go, in the order it scans them.
    internal static readonly Pattern GameDataPattern = new("GameData", [0x44, 0x88, 0x25, 0x00, 0x00, 0x00, 0x00, 0x66, 0x44, 0x89, 0x25, 0x00, 0x00, 0x00, 0x00], "xxx????xxxx????");
    internal static readonly Pattern UnitTablePattern = new("UnitTable", [0x48, 0x03, 0xC7, 0x49, 0x8B, 0x8C, 0xC6], "xxxxxxx");
    internal static readonly Pattern UIPattern = new("UI", [0x40, 0x84, 0xed, 0x0f, 0x94, 0x05], "xxxxxx");
    internal static readonly Pattern HoverPattern = new("Hover", [0xc6, 0x84, 0xc2, 0x00, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8b, 0x74], "xxx?????xxx");
    internal static readonly Pattern ExpansionPattern = new("Expansion", [0x48, 0x8B, 0x05, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8B, 0xD9, 0xF3, 0x0F, 0x10, 0x50, 0x00], "xxx????xxxxxxx?");
    internal static readonly Pattern RosterPattern = new("Roster", [0x02, 0x45, 0x33, 0xD2, 0x4D, 0x8B], "xxxxxx");
    internal static readonly Pattern PanelManagerPattern = new("PanelManagerContainer", [0x48, 0x89, 0x05, 0x00, 0x00, 0x00, 0x00, 0x48, 0x85, 0xDB, 0x74, 0x1E], "xxx????xxxxx");

    /// <summary>The patterns, in scan order.</summary>
    internal static IReadOnlyList<Pattern> Patterns { get; } =
        [GameDataPattern, UnitTablePattern, UIPattern, HoverPattern, ExpansionPattern, RosterPattern, PanelManagerPattern];

    // addresses.go: the terror zone areas, relative to the module base.
    internal const nuint TerrorZonesOffline = 0x22D6D44;
    internal const nuint TerrorZonesOnline = 0x29BD4D8;

    /// <summary>
    /// Scans a module image for the patterns and decodes the offsets. <c>rip</c>-relative displacements are signed;
    /// the unit table and hover operands are module-relative addresses.
    /// </summary>
    /// <exception cref="InvalidDataException">A pattern is not in the image: the game build is not the one these patterns are for.</exception>
    public static GameOffsets Find(ReadOnlySpan<byte> image)
    {
        var missing = new List<string>();
        var gameData = At(image, GameDataPattern, missing);
        var unitTable = At(image, UnitTablePattern, missing);
        var ui = At(image, UIPattern, missing);
        var hover = At(image, HoverPattern, missing);
        var expansion = At(image, ExpansionPattern, missing);
        var roster = At(image, RosterPattern, missing, operandStart: -3);
        var panel = At(image, PanelManagerPattern, missing);
        if (missing.Count > 0)
            throw new InvalidDataException($"Code patterns not found in the game module: {string.Join(", ", missing)}. This game build is not supported.");

        static nuint Offset(long value) => unchecked((nuint)value);

        return new GameOffsets(
            // GameData: pattern - 0x121 + displacement at +3.
            GameData: Offset((long)gameData - 0x121 + Displacement(image, gameData + 3)),
            // UnitTable: the module-relative address at +7.
            UnitTable: Operand(image, unitTable + 7),
            // UI: the end of the instruction (+10) plus the displacement at +6.
            UI: Offset((long)ui + 10 + Displacement(image, ui + 6)),
            // Hover: the module-relative address at +3, minus 1.
            Hover: Offset((long)Operand(image, hover + 3) - 1),
            // Expansion: the end of the instruction (+7) plus the displacement at +3.
            Expansion: Offset((long)expansion + 7 + Displacement(image, expansion + 3)),
            // Roster: the instruction ends at +1, its displacement is the 4 bytes before (the pattern's first byte is its top byte).
            Roster: Offset((long)roster + 1 + Displacement(image, roster - 3)),
            // PanelManagerContainer: the end of the instruction (+7) plus the displacement at +3.
            PanelManager: Offset((long)panel + Displacement(image, panel + 3) + 7));
    }

    // Where a pattern is; adds its name to `missing` when it is not there (or its operand starts before the image).
    private static int At(ReadOnlySpan<byte> image, Pattern pattern, List<string> missing, int operandStart = 0)
    {
        var i = GameProcess.FindPattern(image, pattern.Bytes, pattern.Mask);
        if (i < 0 || i + operandStart < 0)
            missing.Add(pattern.Name);
        return i;
    }

    private static int Displacement(ReadOnlySpan<byte> image, int at) => BinaryPrimitives.ReadInt32LittleEndian(image.Slice(at, 4));

    private static uint Operand(ReadOnlySpan<byte> image, int at) => BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at, 4));
}

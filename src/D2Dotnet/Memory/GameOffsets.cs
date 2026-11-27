// Patterns and offset decoding from d2go pkg/memory/offset.go and addresses.go (github.com/hectorgimenez/d2go, MIT,
// (c) 2023 Héctor Giménez).

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace D2Dotnet.Memory;

/// <summary>
/// Where the game keeps its structures, relative to the module base: found by scanning the game code for byte
/// patterns when a <see cref="GameReader"/> is created.
/// </summary>
/// <param name="GameData">The game data structure; null when its pattern is not found (no reader uses it).</param>
/// <param name="UnitTable">The unit hash tables (players, monsters, objects, missiles, items; 1024 bytes each).</param>
/// <param name="UI">The user interface flags (the automap flag; the panel flags start 0xA bytes before).</param>
/// <param name="Hover">The hovered unit.</param>
/// <param name="Expansion">The pointer to the expansion character flag; null when its pattern is not found.</param>
/// <param name="Roster">The pointer to the party list; null when its pattern is not found.</param>
/// <param name="PanelManager">The pointer to the panel manager widget.</param>
public readonly record struct GameOffsets(
    nuint? GameData,
    nuint UnitTable,
    nuint UI,
    nuint Hover,
    nuint? Expansion,
    nuint? Roster,
    nuint PanelManager)
{
    /// <summary>
    /// The names of the optional offsets whose pattern was not found: <c>GameData</c>, <c>Expansion</c> and
    /// <c>Roster</c>. The reader works without them, with less: see <see cref="GameReader"/>.
    /// </summary>
    public IReadOnlyList<string> Missing =>
        [.. new (string Name, nuint? Value)[] { ("GameData", GameData), ("Expansion", Expansion), ("Roster", Roster) }
            .Where(o => o.Value is null).Select(o => o.Name)];

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

    private static readonly string[] Names = ["GameData", "UnitTable", "UI", "Hover", "Expansion", "Roster", "PanelManager"];

    /// <summary>
    /// The offsets as text, to keep a build's offsets between runs (see <see cref="Parse"/>): one <c>Name=0x...</c>
    /// line per offset, in the order of the constructor, with an empty value for a missing optional offset.
    /// </summary>
    public string ToText()
    {
        nuint?[] values = [GameData, UnitTable, UI, Hover, Expansion, Roster, PanelManager];
        var text = new StringBuilder();
        for (var i = 0; i < Names.Length; i++)
            text.Append(Names[i]).Append('=').Append(values[i] is { } v ? string.Create(CultureInfo.InvariantCulture, $"0x{v:X}") : "").Append('\n');
        return text.ToString();
    }

    /// <summary>Offsets from the text <see cref="ToText"/> writes. Blank lines are ignored; names are case-sensitive.</summary>
    /// <exception cref="FormatException">A line is not <c>Name=value</c>, a name is unknown or repeated, a value is not
    /// hexadecimal with a <c>0x</c> prefix, or a required offset (unit table, UI, hover, panel manager) is missing.</exception>
    public static GameOffsets Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var values = new Dictionary<string, nuint?>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
                throw new FormatException($"'{line}' is not Name=value.");
            var name = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (Array.IndexOf(Names, name) < 0)
                throw new FormatException($"'{name}' is not an offset.");
            if (!values.TryAdd(name, value.Length == 0 ? null : Hex(name, value)))
                throw new FormatException($"'{name}' is given twice.");
        }

        nuint? Optional(string name) => values.GetValueOrDefault(name);
        nuint Required(string name) => Optional(name) ?? throw new FormatException($"The required offset {name} is missing.");

        return new GameOffsets(
            Optional("GameData"), Required("UnitTable"), Required("UI"), Required("Hover"),
            Optional("Expansion"), Optional("Roster"), Required("PanelManager"));

        static nuint Hex(string name, string value) =>
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ulong.TryParse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v)
                ? (nuint)v
                : throw new FormatException($"{name} has '{value}', which is not a 0x hexadecimal number.");
    }

    /// <summary>
    /// Scans a module image for the patterns and decodes the offsets. <c>rip</c>-relative displacements are signed;
    /// the unit table and hover operands are module-relative addresses. The game data, expansion and roster patterns
    /// are optional: when one is not found its offset is null (see <see cref="Missing"/>).
    /// </summary>
    /// <exception cref="InvalidDataException">A required pattern (unit table, UI, hover, panel manager) is not in the image: the game build is not the one these patterns are for.</exception>
    public static GameOffsets Find(ReadOnlySpan<byte> image)
    {
        var missing = new List<string>();
        var optional = new List<string>();
        var gameData = At(image, GameDataPattern, optional);
        var unitTable = At(image, UnitTablePattern, missing);
        var ui = At(image, UIPattern, missing);
        var hover = At(image, HoverPattern, missing);
        var expansion = At(image, ExpansionPattern, optional);
        var roster = At(image, RosterPattern, optional, operandStart: -3);
        var panel = At(image, PanelManagerPattern, missing);
        if (missing.Count > 0)
            throw new InvalidDataException($"Code patterns not found in the game module: {string.Join(", ", missing)}. This game build is not supported.");

        static nuint Offset(long value) => unchecked((nuint)value);

        return new GameOffsets(
            // GameData: pattern - 0x121 + displacement at +3.
            GameData: gameData < 0 ? null : Offset((long)gameData - 0x121 + Displacement(image, gameData + 3)),
            // UnitTable: the module-relative address at +7.
            UnitTable: Operand(image, unitTable + 7),
            // UI: the end of the instruction (+10) plus the displacement at +6.
            UI: Offset((long)ui + 10 + Displacement(image, ui + 6)),
            // Hover: the module-relative address at +3, minus 1.
            Hover: Offset((long)Operand(image, hover + 3) - 1),
            // Expansion: the end of the instruction (+7) plus the displacement at +3.
            Expansion: expansion < 0 ? null : Offset((long)expansion + 7 + Displacement(image, expansion + 3)),
            // Roster: the instruction ends at +1, its displacement is the 4 bytes before (the pattern's first byte is its top byte).
            Roster: roster < 0 ? null : Offset((long)roster + 1 + Displacement(image, roster - 3)),
            // PanelManagerContainer: the end of the instruction (+7) plus the displacement at +3.
            PanelManager: Offset((long)panel + Displacement(image, panel + 3) + 7));
    }

    // Where a pattern is; adds its name to `missing` when it is not there (or its operand starts before the image).
    private static int At(ReadOnlySpan<byte> image, Pattern pattern, List<string> missing, int operandStart = 0)
    {
        var i = GameProcess.FindPattern(image, pattern.Bytes, pattern.Mask);
        if (i < 0 || i + operandStart < 0)
        {
            missing.Add(pattern.Name);
            return -1;
        }

        return i;
    }

    private static int Displacement(ReadOnlySpan<byte> image, int at) => BinaryPrimitives.ReadInt32LittleEndian(image.Slice(at, 4));

    private static uint Operand(ReadOnlySpan<byte> image, int at) => BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(at, 4));
}

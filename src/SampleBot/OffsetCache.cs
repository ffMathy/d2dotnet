using System.Globalization;
using D2Dotnet.Memory;

/// <summary>
/// Keeps the offsets a scan found, one file per game build. The running game keeps part of its code unreadable, and which
/// part changes while it runs, so a later scan can miss a pattern an earlier one found.
/// </summary>
internal static class OffsetCache
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Dotnet", "offsets");

    /// <summary>The cached offsets of the running build, or a fresh scan (saved for next time) when there are none.</summary>
    public static GameOffsets LoadOrScan(GameProcess process)
    {
        var build = process.ReadBuildTimestamp();
        var file = Path.Combine(Folder, $"{build:X8}.txt");
        if (build != 0 && File.Exists(file))
            return Parse(File.ReadAllLines(file));

        var offsets = GameOffsets.Find(process.ReadModule());
        if (build != 0)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(file, Format(offsets));
        }

        return offsets;
    }

    private static IEnumerable<string> Format(GameOffsets o)
    {
        yield return $"GameData={Hex(o.GameData)}";
        yield return $"UnitTable={Hex(o.UnitTable)}";
        yield return $"UI={Hex(o.UI)}";
        yield return $"Hover={Hex(o.Hover)}";
        yield return $"Expansion={Hex(o.Expansion)}";
        yield return $"Roster={Hex(o.Roster)}";
        yield return $"PanelManager={Hex(o.PanelManager)}";

        static string Hex(nuint? value) => value is { } v ? $"0x{v:X}" : "";
    }

    private static GameOffsets Parse(string[] lines)
    {
        var values = lines.Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        nuint? Optional(string name) =>
            values.TryGetValue(name, out var v) && v.StartsWith("0x", StringComparison.Ordinal)
                ? nuint.Parse(v.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : null;
        nuint Required(string name) => Optional(name) ?? throw new InvalidDataException($"The offset cache has no {name}.");

        return new GameOffsets(
            Optional("GameData"), Required("UnitTable"), Required("UI"), Required("Hover"),
            Optional("Expansion"), Optional("Roster"), Required("PanelManager"));
    }
}

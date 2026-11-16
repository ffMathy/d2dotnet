namespace D2Dotnet.Map.GameData.Casc;

/// <summary>
/// The install's <c>.build.info</c>: a <c>|</c>-separated table whose header names columns as
/// <c>Name!TYPE:size</c> (for example <c>Build Key!HEX:16</c>, <c>Active!DEC:1</c>, <c>Product!STRING:0</c>).
/// </summary>
internal sealed class BuildInfo
{
    private BuildInfo(IReadOnlyList<IReadOnlyDictionary<string, string>> rows) => Rows = rows;

    /// <summary>Rows keyed by column name without the <c>!TYPE:size</c> suffix, case-insensitive.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; }

    public static BuildInfo Parse(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();
        if (lines.Count == 0)
            throw new CascFormatException(".build.info is empty.");

        var columns = lines[0].Split('|').Select(c => c.Split('!')[0].Trim()).ToArray();
        if (!columns.Contains("Build Key", StringComparer.OrdinalIgnoreCase))
            throw new CascFormatException(".build.info has no 'Build Key' column.");

        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            var values = line.Split('|');
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Length; i++)
                row[columns[i]] = i < values.Length ? values[i].Trim() : "";
            rows.Add(row);
        }
        return new BuildInfo(rows);
    }

    /// <summary>
    /// The row to open, like CascLib: the first active one (<c>Active</c> = 1, or no Active column),
    /// optionally restricted to a product code such as <c>osi</c> (D2R).
    /// </summary>
    public IReadOnlyDictionary<string, string> SelectRow(string? product)
    {
        foreach (var row in Rows)
        {
            if (row.TryGetValue("Active", out var active) && active != "1")
                continue;
            if (product is not null && row.TryGetValue("Product", out var p) && !string.Equals(p, product, StringComparison.OrdinalIgnoreCase))
                continue;
            if (row.TryGetValue("Build Key", out var key) && key.Length == 32)
                return row;
        }
        throw new CascFormatException(product is null
            ? ".build.info has no active build with a build key."
            : $".build.info has no active build for product '{product}'.");
    }
}

/// <summary>
/// A CASC text config (build config, CDN config): <c>key = value [value...]</c> lines, <c>#</c> comments.
/// </summary>
internal sealed class ConfigFile
{
    private readonly Dictionary<string, string[]> _values;

    private ConfigFile(Dictionary<string, string[]> values) => _values = values;

    public IEnumerable<string> Keys => _values.Keys;

    public static ConfigFile Parse(string text)
    {
        var values = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            values[line[..eq].Trim()] = line[(eq + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        return new ConfigFile(values);
    }

    public string[]? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    /// <summary>
    /// A manifest reference such as <c>vfs-root = ckey [ekey]</c> with <c>vfs-root-size = csize [esize]</c>,
    /// or null when the key is missing.
    /// </summary>
    public ConfigFileRef? GetFileRef(string key)
    {
        if (Get(key) is not { Length: > 0 } hashes)
            return null;
        var sizes = Get(key + "-size");
        return new ConfigFileRef(
            key,
            Hex.Parse(hashes[0]),
            hashes.Length > 1 ? Hex.Parse(hashes[1]) : null,
            sizes is { Length: > 0 } && long.TryParse(sizes[0], out var c) ? c : null);
    }
}

/// <summary>A file named by the build config: content key, optional encoding key, optional content size.</summary>
internal sealed record ConfigFileRef(string Name, byte[] CKey, byte[]? EKey, long? ContentSize);

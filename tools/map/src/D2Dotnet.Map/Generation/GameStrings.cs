using System.Text.Json;
using D2Dotnet.Map.Data;
using D2Dotnet.Map.GameData;

namespace D2Dotnet.Map.Generation;

/// <summary>
/// The string tables of Diablo II: Resurrected (<c>data/local/lng/strings/*.json</c>), which replace the classic
/// <c>.tbl</c> files. Each file is a JSON array of objects with a <c>Key</c> and one field per language
/// (<c>enUS</c>, <c>deDE</c>, ...).
/// </summary>
/// <remarks>
/// A key resolves to the value in the selected language, or to the <c>enUS</c> value when that one is blank. Within a
/// file the first entry with a usable value wins; across files the caller's preferred file is searched first, then
/// every file in path order. Thread-safe once built.
/// </remarks>
public sealed class GameStrings
{
    /// <summary>Folder of the D2R string tables.</summary>
    public const string Directory = "data/local/lng/strings/";

    /// <summary>Language used when none is configured, and the fallback for blank values.</summary>
    public const string DefaultLanguage = "enUS";

    /// <summary>The language fields of the D2R string tables.</summary>
    public static IReadOnlyList<string> Languages { get; } =
        ["enUS", "zhTW", "deDE", "esES", "frFR", "itIT", "koKR", "plPL", "esMX", "jaJP", "ptBR", "ruRU", "zhCN"];

    /// <summary>Classic (D2 1.x <c>data/local/lng/XXX</c>) language codes and their D2R field.</summary>
    private static readonly Dictionary<string, string> ClassicLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENG"] = "enUS",
        ["DEU"] = "deDE",
        ["ESP"] = "esES",
        ["FRA"] = "frFR",
        ["ITA"] = "itIT",
        ["KOR"] = "koKR",
        ["POL"] = "plPL",
        ["JPN"] = "jaJP",
        ["POR"] = "ptBR",
        ["RUS"] = "ruRU",
        ["CHI"] = "zhTW",
        ["SIN"] = "zhCN",
    };

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly List<(string File, Dictionary<string, string> Values)> _files;

    private GameStrings(string language, List<(string File, Dictionary<string, string> Values)> files)
    {
        Language = language;
        _files = files;
    }

    /// <summary>An empty table: every lookup misses.</summary>
    public static GameStrings Empty { get; } = new(DefaultLanguage, []);

    /// <summary>The D2R language field values are read from.</summary>
    public string Language { get; }

    /// <summary>The files read, by normalized game path, in search order.</summary>
    public IEnumerable<string> Files => _files.Select(f => f.File);

    /// <summary>Number of keys over all files.</summary>
    public int Count => _files.Sum(f => f.Values.Count);

    /// <summary>
    /// Map a configured language to a D2R field: a D2R field in any letter case (<c>enus</c> → <c>enUS</c>), a
    /// classic code (<c>ENG</c> → <c>enUS</c>), or anything else as given (a mod may add fields).
    /// </summary>
    /// <exception cref="ArgumentException">The language is blank.</exception>
    public static string ResolveLanguage(string? language)
    {
        var lang = language?.Trim();
        if (string.IsNullOrEmpty(lang)) throw new ArgumentException("A language is required", nameof(language));
        var d2r = Languages.FirstOrDefault(l => string.Equals(l, lang, StringComparison.OrdinalIgnoreCase));
        if (d2r != null) return d2r;
        return ClassicLanguages.TryGetValue(lang, out var mapped) ? mapped : lang;
    }

    /// <summary>Read every <c>*.json</c> under <see cref="Directory"/>; no files gives an empty table.</summary>
    /// <exception cref="JsonException">A string table is not valid JSON.</exception>
    public static GameStrings Load(IGameFileProvider files, string language)
    {
        ArgumentNullException.ThrowIfNull(files);
        var tables = files.EnumerateFiles(Directory)
            .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(p => (p, files.ReadFile(p)))
            .Where(f => f.Item2 != null)
            .Select(f => (f.p, f.Item2!));
        return Parse(tables, language);
    }

    /// <summary>Build a table from string table files (path, UTF-8 JSON bytes).</summary>
    /// <exception cref="JsonException">A string table is not valid JSON.</exception>
    public static GameStrings Parse(IEnumerable<(string Path, byte[] Json)> files, string language)
    {
        var lang = ResolveLanguage(language);
        var parsed = new List<(string File, Dictionary<string, string> Values)>();
        foreach (var (path, json) in files.OrderBy(f => GamePath.Normalize(f.Path), StringComparer.Ordinal))
            parsed.Add((GamePath.Normalize(path), ParseFile(json, lang)));
        return new GameStrings(lang, parsed);
    }

    private static Dictionary<string, string> ParseFile(byte[] json, string language)
    {
        ReadOnlyMemory<byte> bytes = json;
        if (json.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) bytes = bytes[3..];

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(bytes, ParseOptions);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("A string table must be a JSON array");

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (!entry.TryGetProperty("Key", out var keyProp) || keyProp.ValueKind != JsonValueKind.String) continue;
            var key = keyProp.GetString();
            if (string.IsNullOrEmpty(key) || values.ContainsKey(key)) continue;
            var value = Text(entry, language) ?? Text(entry, DefaultLanguage);
            if (value != null) values[key] = value;
        }
        return values;
    }

    private static string? Text(JsonElement entry, string field)
    {
        if (!entry.TryGetProperty(field, out var p) || p.ValueKind != JsonValueKind.String) return null;
        var s = p.GetString();
        return s == null || JsNumber.TrimJs(s).Length == 0 ? null : s;
    }

    /// <summary>
    /// The text of <paramref name="key"/>, searching <paramref name="preferredFile"/> (a file name such as
    /// <c>levels.json</c>) first; null when no file has it.
    /// </summary>
    public string? Get(string? key, string? preferredFile = null)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (preferredFile != null)
        {
            foreach (var (file, values) in _files)
                if (file.EndsWith("/" + preferredFile, StringComparison.OrdinalIgnoreCase) && values.TryGetValue(key, out var v)) return v;
        }
        foreach (var (_, values) in _files)
            if (values.TryGetValue(key, out var v)) return v;
        return null;
    }
}

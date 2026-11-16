using System.Text.Json;
using D2Dotnet.Files;

namespace D2Dotnet.Data;

/// <summary>
/// The string tables of Diablo II: Resurrected (<c>data/local/lng/strings/*.json</c>, which replace the classic
/// <c>.tbl</c> files), in one language. Each file is a JSON array of objects with a <c>Key</c> and one field per
/// language (<c>enUS</c>, <c>deDE</c>, ...). Immutable and thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// A key resolves to its text in the selected language, or to the <c>enUS</c> text when that one is blank. Within a
/// file the first entry of a key with usable text wins; across files a preferred file (when the caller names one) is
/// searched first, then every file in path order. Byte order marks, comments and trailing commas are accepted.
/// </para>
/// <para>
/// The id lookups (<see cref="GetLevelName"/>, <see cref="GetMonsterName"/>, ...) need the excel tables, so they work on
/// the instance of <see cref="GameDatabase.Localization"/>; on one made by <see cref="Load"/> or
/// <see cref="Parse"/>
/// they throw. They translate the table's string key, fall back to the key itself when no string table has it (D2R's
/// keys are mostly the English text), trim the result, and give null when the key is blank or the id unknown.
/// </para>
/// </remarks>
public sealed class Localization
{
    /// <summary>Folder of the D2R string tables, relative to the game's data root.</summary>
    public const string Directory = "data/local/lng/strings/";

    /// <summary>The language used when none is configured, and the fallback for blank text.</summary>
    public const string DefaultLanguage = "enUS";

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

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

    private readonly (string File, Dictionary<string, string> Values)[] _files;
    private readonly GameDatabase? _database;

    private Localization(string language, (string File, Dictionary<string, string> Values)[] files, GameDatabase? database)
    {
        Language = language;
        _files = files;
        _database = database;
    }

    /// <summary>An empty table: every key lookup misses.</summary>
    public static Localization Empty { get; } = new(DefaultLanguage, [], null);

    /// <summary>The language fields of the D2R string tables.</summary>
    public static IReadOnlyList<string> Languages { get; } =
        ["enUS", "zhTW", "deDE", "esES", "frFR", "itIT", "koKR", "plPL", "esMX", "jaJP", "ptBR", "ruRU", "zhCN"];

    /// <summary>The D2R language field text is read from.</summary>
    public string Language { get; }

    /// <summary>The files read, by normalized game path, in search order.</summary>
    public IEnumerable<string> Files => _files.Select(f => f.File);

    /// <summary>The number of keys over all files.</summary>
    public int Count => _files.Sum(f => f.Values.Count);

    /// <summary>The text of a key, or null when no file has it (see <see cref="Get"/>).</summary>
    public string? this[string key] => Get(key);

    /// <summary>The display name of a level, or null (see <see cref="GetLevelName"/>).</summary>
    public string? this[AreaId id] => GetLevelName(id);

    /// <summary>The display name of a monster class, or null (see <see cref="GetMonsterName"/>).</summary>
    public string? this[MonsterId id] => GetMonsterName(id);

    /// <summary>The display name of an object class, or null (see <see cref="GetObjectName"/>).</summary>
    public string? this[ObjectId id] => GetObjectName(id);

    /// <summary>The display name of a base item, or null (see <see cref="GetItemName"/>).</summary>
    public string? this[ItemId id] => GetItemName(id);

    /// <summary>
    /// Map a configured language to a D2R field: a D2R field in any letter case (<c>enus</c> is <c>enUS</c>), a classic
    /// code (<c>ENG</c> is <c>enUS</c>), or anything else as given (a mod may add fields).
    /// </summary>
    /// <exception cref="ArgumentException">The language is blank.</exception>
    public static string ResolveLanguage(string? language)
    {
        var lang = language?.Trim();
        if (string.IsNullOrEmpty(lang))
            throw new ArgumentException("A language is required", nameof(language));
        var d2r = Languages.FirstOrDefault(l => string.Equals(l, lang, StringComparison.OrdinalIgnoreCase));
        if (d2r != null)
            return d2r;
        return ClassicLanguages.TryGetValue(lang, out var mapped) ? mapped : lang;
    }

    /// <summary>Read every <c>*.json</c> under <see cref="Directory"/>; no files gives an empty table.</summary>
    /// <exception cref="JsonException">A string table is not valid JSON, or not an array.</exception>
    /// <exception cref="ArgumentException">The language is blank.</exception>
    public static Localization Load(IGameFileProvider files, string language) => Create(files, language, null);

    internal static Localization Create(IGameFileProvider files, string language, GameDatabase? database)
    {
        ArgumentNullException.ThrowIfNull(files);
        var tables = files.EnumerateFiles(Directory)
            .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(p => (Path: p, Json: files.ReadFile(p)))
            .Where(f => f.Json != null)
            .Select(f => (f.Path, f.Json!));
        return Create(tables, language, database);
    }

    /// <summary>Build a table from string table files: (path, UTF-8 JSON bytes) pairs.</summary>
    /// <exception cref="JsonException">A string table is not valid JSON, or not an array.</exception>
    /// <exception cref="ArgumentException">The language is blank.</exception>
    public static Localization Parse(IEnumerable<(string Path, byte[] Json)> files, string language) => Create(files, language, null);

    private static Localization Create(IEnumerable<(string Path, byte[] Json)> files, string language, GameDatabase? database)
    {
        ArgumentNullException.ThrowIfNull(files);
        var lang = ResolveLanguage(language);
        var parsed = files
            .Select(f => (File: GamePath.Normalize(f.Path), f.Json))
            .OrderBy(f => f.File, StringComparer.Ordinal)
            .Select(f => (f.File, ParseFile(f.Json, lang)))
            .ToArray();
        return new Localization(lang, parsed, database);
    }

    private static Dictionary<string, string> ParseFile(byte[] json, string language)
    {
        ReadOnlyMemory<byte> bytes = json;
        if (json.AsSpan().StartsWith("\uFEFF"u8))
            bytes = bytes[3..];

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(bytes, ParseOptions);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("A string table must be a JSON array");

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            if (!entry.TryGetProperty("Key", out var keyProperty) || keyProperty.ValueKind != JsonValueKind.String)
                continue;
            var key = keyProperty.GetString();
            if (string.IsNullOrEmpty(key) || values.ContainsKey(key))
                continue;
            var value = Text(entry, language) ?? Text(entry, DefaultLanguage);
            if (value != null)
                values[key] = value;
        }

        return values;
    }

    private static string? Text(JsonElement entry, string field)
    {
        if (!entry.TryGetProperty(field, out var property) || property.ValueKind != JsonValueKind.String)
            return null;
        var text = property.GetString();
        return text == null || TrimWhiteSpace(text).Length == 0 ? null : text;
    }

    /// <summary>
    /// The text of <paramref name="key"/>, searching <paramref name="preferredFile"/> (a file name such as
    /// <c>levels.json</c>) first; null when no file has it.
    /// </summary>
    public string? Get(string? key, string? preferredFile = null)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        if (preferredFile != null)
        {
            foreach (var (file, values) in _files)
            {
                if (file.EndsWith("/" + preferredFile, StringComparison.OrdinalIgnoreCase) && values.TryGetValue(key, out var preferred))
                    return preferred;
            }
        }

        foreach (var (_, values) in _files)
        {
            if (values.TryGetValue(key, out var value))
                return value;
        }

        return null;
    }

    /// <summary>
    /// The text of a string key the way the id lookups use it: the key is trimmed, its text (searching
    /// <paramref name="preferredFile"/> first) or else the key itself is returned trimmed; null for a blank key.
    /// </summary>
    public string? Translate(string? key, string? preferredFile = null)
    {
        if (key == null)
            return null;
        var trimmed = TrimWhiteSpace(key);
        if (trimmed.Length == 0)
            return null;
        return TrimWhiteSpace(Get(trimmed, preferredFile) ?? trimmed);
    }

    /// <summary>The display name of a level (levels.txt <c>LevelName</c>, searched in <c>levels.json</c> first), or null.</summary>
    /// <exception cref="InvalidOperationException">This instance is not attached to a <see cref="GameDatabase"/>.</exception>
    public string? GetLevelName(AreaId id) => Translate(Database.Levels.Find(id)?.NameKey, "levels.json");

    /// <summary>The display name of a monster class (monstats.txt <c>NameStr</c>, searched in <c>monsters.json</c> first), or null.</summary>
    /// <exception cref="InvalidOperationException">This instance is not attached to a <see cref="GameDatabase"/>.</exception>
    public string? GetMonsterName(MonsterId id) => Translate(Database.Monsters.Find(id)?.NameKey, "monsters.json");

    /// <summary>The display name of a super unique (superuniques.txt <c>Name</c>, searched in <c>monsters.json</c> first), or null.</summary>
    /// <param name="index">The super unique's <see cref="SuperUniqueInfo.Index"/>.</param>
    /// <exception cref="InvalidOperationException">This instance is not attached to a <see cref="GameDatabase"/>.</exception>
    public string? GetSuperUniqueName(int index) => Translate(Database.SuperUniques.Find(index)?.NameKey, "monsters.json");

    /// <summary>The display name of an object class (objects.txt <c>Name</c>, searched in <c>objects.json</c> first), or null.</summary>
    /// <exception cref="InvalidOperationException">This instance is not attached to a <see cref="GameDatabase"/>.</exception>
    public string? GetObjectName(ObjectId id) => Translate(Database.Objects.Find(id)?.NameKey, "objects.json");

    /// <summary>
    /// The display name of a base item (its <c>namestr</c> key, searched in <c>item-names.json</c> first, or its
    /// <c>name</c> when the table has no <c>namestr</c>), or null.
    /// </summary>
    /// <exception cref="InvalidOperationException">This instance is not attached to a <see cref="GameDatabase"/>.</exception>
    public string? GetItemName(ItemId id)
    {
        var item = Database.Items.Find(id);
        if (item == null)
            return null;
        return Translate(item.NameKey.Length > 0 ? item.NameKey : item.Name, "item-names.json");
    }

    private GameDatabase Database => _database ?? throw new InvalidOperationException(
        "Id lookups need the excel tables: use GameDatabase.Localization instead of a Localization loaded on its own.");

    /// <summary>JavaScript's <c>String.prototype.trim</c> (the map server's original rule): Unicode spaces and line terminators, and U+FEFF.</summary>
    private static string TrimWhiteSpace(string s)
    {
        var start = 0;
        var end = s.Length;
        while (start < end && IsWhiteSpace(s[start]))
            start++;
        while (end > start && IsWhiteSpace(s[end - 1]))
            end--;
        return start == 0 && end == s.Length ? s : s[start..end];
    }

    private static bool IsWhiteSpace(char c) =>
        c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680' or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or '\uFEFF'
        || c is >= '\u2000' and <= '\u200A';
}

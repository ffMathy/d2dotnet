using D2Dotnet.Files;
using D2Dotnet.Map.Data;
using D2Dotnet.Map.Drlg.Data;

namespace D2Dotnet.Map.Generation;

/// <summary>Name and object data of a level object: objects.txt <c>Name</c> (translated), and its operate function.</summary>
/// <param name="Name">The display name.</param>
/// <param name="OperateFn">objects.txt <c>OperateFn</c>.</param>
/// <param name="Selectable">objects.txt <c>Selectable0</c> is set (upstream only reports <c>op</c> for those).</param>
public sealed record GameObjectInfo(string Name, int OperateFn, bool Selectable);

/// <summary>What the server knows about an npc class id.</summary>
/// <param name="Name">The display name, or null.</param>
/// <param name="SuperId">The SuperUniques.txt index when the npc is a super unique.</param>
public readonly record struct NpcInfo(string? Name, int? SuperId);

/// <summary>
/// The game data used to name levels, npcs, super uniques and objects (upstream <c>Diablo2MpqData</c>, which read
/// <c>levels.bin</c>, <c>monstats.bin</c>, <c>objects.bin</c> and the <c>.tbl</c> string tables out of the classic
/// MPQs). Read from the D2R excel tables (<c>Levels</c>, <c>MonStats</c>, <c>SuperUniques</c>, <c>Objects</c>) and the
/// D2R string tables (<see cref="GameStrings"/>).
/// </summary>
/// <remarks>
/// Each name is the string table text of the table's string key (Levels <c>LevelName</c>, MonStats <c>NameStr</c>,
/// SuperUniques <c>Name</c>, Objects <c>Name</c>), or the key itself when no string table has it; trimmed.
/// Npc ids follow <see cref="Drlg.PresetUnit.ClassId"/>: a MonStats game row (the <c>Expansion</c> divider row not
/// counted), <see cref="MonsterCount"/> + a SuperUniques row for a super unique, and anything above that for a
/// MonPlace entry (which has no name).
/// </remarks>
public sealed class MapGameData
{
    private readonly Dictionary<int, string> _levels = [];
    private readonly List<string> _monsters = [];
    private readonly List<string> _superUniques = [];
    private readonly Dictionary<int, GameObjectInfo> _objects = [];

    /// <param name="levels">Levels.txt (<c>Id</c>, <c>LevelName</c>).</param>
    /// <param name="monStats">MonStats.txt (<c>Id</c>, <c>NameStr</c>).</param>
    /// <param name="superUniques">SuperUniques.txt (<c>Superunique</c>, <c>Name</c>).</param>
    /// <param name="objects">Objects.txt (<c>Name</c>, optional <c>Id</c>, <c>Selectable0</c>, <c>OperateFn</c>).</param>
    /// <param name="strings">The string tables.</param>
    public MapGameData(TxtTable levels, TxtTable monStats, TxtTable superUniques, TxtTable objects, GameStrings strings)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(monStats);
        ArgumentNullException.ThrowIfNull(superUniques);
        ArgumentNullException.ThrowIfNull(objects);
        Strings = strings ?? throw new ArgumentNullException(nameof(strings));

        for (var row = 0; row < levels.RowCount; row++)
        {
            if (!TryInt(levels.Str(row, "Id"), out var id)) continue;
            var name = Translate(levels.Str(row, "LevelName"), "levels.json");
            if (name != null) _levels.TryAdd(id, name);
        }

        // Game rows, as the engine (and the DRLG port) count them: blank and "Expansion" divider rows are left out
        foreach (var row in GameRows(monStats, "Id")) _monsters.Add(Translate(monStats.Str(row, "NameStr"), "monsters.json") ?? "");
        foreach (var row in GameRows(superUniques, "Superunique")) _superUniques.Add(Translate(superUniques.Str(row, "Name"), "monsters.json") ?? "");

        var hasId = objects.HasColumn("Id");
        for (var row = 0; row < objects.RowCount; row++)
        {
            var id = row;
            if (hasId)
            {
                if (!TryInt(objects.Str(row, "Id"), out id)) continue;
            }
            var name = Translate(objects.Str(row, "Name"), "objects.json");
            if (name == null) continue;
            _objects.TryAdd(id, new GameObjectInfo(name, (int)objects.Int(row, "OperateFn"), objects.Int(row, "Selectable0") != 0));
        }
    }

    /// <summary>Excel tables read by <see cref="Load"/>.</summary>
    public static IReadOnlyList<string> Tables { get; } = ["Levels", "MonStats", "SuperUniques", "Objects"];

    /// <summary>The string tables names are translated with.</summary>
    public GameStrings Strings { get; }

    /// <summary>Number of MonStats game rows (upstream <c>Diablo2MpqMonsters.Size</c>).</summary>
    public int MonsterCount => _monsters.Count;

    /// <summary>Number of SuperUniques game rows.</summary>
    public int SuperUniqueCount => _superUniques.Count;

    /// <summary>Load the tables through a DRLG data source (which caches them) and the string tables from the game files.</summary>
    /// <exception cref="InvalidDataException">An excel table is missing.</exception>
    public static MapGameData Load(IDrlgDataSource tables, IGameFileProvider files, string language)
    {
        TxtTable Need(string name) => tables.GetTable(name) ?? throw new InvalidDataException($"Missing excel table data/global/excel/{name.ToLowerInvariant()}.txt");
        return new MapGameData(Need("Levels"), Need("MonStats"), Need("SuperUniques"), Need("Objects"), GameStrings.Load(files, language));
    }

    /// <summary>Display name of a level, or null for an unknown level.</summary>
    public string? LevelName(int levelId) => _levels.GetValueOrDefault(levelId);

    /// <summary>Objects.txt data of an object class, or null when the table has no such row.</summary>
    public GameObjectInfo? Object(int objectId) => _objects.GetValueOrDefault(objectId);

    /// <summary>Name and super unique id of an npc class id (upstream <c>FixMap</c> for npcs).</summary>
    public NpcInfo Npc(int classId)
    {
        if (classId < 0) return default;
        if (classId < _monsters.Count) return new NpcInfo(NullIfEmpty(_monsters[classId]), null);
        var superId = classId - _monsters.Count;
        if (superId < _superUniques.Count) return new NpcInfo(NullIfEmpty(_superUniques[superId]), superId);
        return default; // a MonPlace entry
    }

    private static bool TryInt(string s, out int value) =>
        int.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out value);

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private string? Translate(string key, string preferredFile)
    {
        var k = JsNumber.TrimJs(key);
        if (k.Length == 0) return null;
        return JsNumber.TrimJs(Strings.Get(k, preferredFile) ?? k);
    }

    private static IEnumerable<int> GameRows(TxtTable table, string column)
    {
        for (var row = 0; row < table.RowCount; row++)
        {
            var key = table.Str(row, column);
            if (key.Length == 0 || key == "Expansion") continue;
            yield return row;
        }
    }
}

using D2Dotnet.Data;
using D2Dotnet.Files;
using D2Dotnet.Maps.Formats;

namespace D2Dotnet.Maps.Engine;

/// <summary>The production data source: the excel tables of a <see cref="GameDatabase"/> and the raw
/// <c>.ds1</c>/<c>.dt1</c> files of its game files.</summary>
/// <remarks>
/// <para>Files read:</para>
/// <list type="bullet">
/// <item><c>data/global/excel/{levels,lvlprest,lvltypes,lvlmaze,lvlsub,lvlwarp,monstats,superuniques,monplace,monpreset}.txt</c></item>
/// <item><c>data/global/excel/objpreset.txt</c> (or <c>obj.txt</c>) for the DS1 object id table,
/// unless one is passed to the constructor</item>
/// <item><c>data/global/tiles/**.ds1</c> and <c>data/global/tiles/**.dt1</c>, as the tables name them</item>
/// </list>
/// <para>Every excel column is looked up by its header name, so the extra columns D2R added are
/// ignored. Thread-safe.</para>
/// </remarks>
internal sealed class GameDataSource : IMapDataSource
{
    /// <summary>Where the tiles are, relative to the game's data root.</summary>
    public const string TilesDirectory = "data/global/tiles/";

    private readonly GameDatabase _data;
    private readonly int[]? _presetObjectTable;
    private readonly Lazy<int[]> _resolvedObjectTable;

    /// <param name="data">The game data (its tables, and its files for the tiles).</param>
    /// <param name="presetObjectTable">Optional 750-entry DS1 object id table (act*150 + id -&gt;
    /// objects.txt row). When null it is built from <c>objpreset.txt</c>/<c>obj.txt</c>.</param>
    public GameDataSource(GameDatabase data, IReadOnlyList<int>? presetObjectTable = null)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        if (presetObjectTable != null && presetObjectTable.Count < 750)
            throw new ArgumentException("The preset object table needs 750 entries (5 acts of 150).", nameof(presetObjectTable));
        _presetObjectTable = presetObjectTable?.ToArray();
        _resolvedObjectTable = new Lazy<int[]>(ResolveObjectTable, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public ExcelTable? GetTable(string name) => _data.GetTable(name);

    public Ds1File? GetDs1(string tilesRelativePath)
    {
        var bytes = _data.Files.ReadFile(TilesDirectory + GamePath.Normalize(tilesRelativePath));
        return bytes == null ? null : Ds1File.Parse(bytes);
    }

    public Dt1File? GetDt1(string tilesRelativePath)
    {
        var bytes = _data.Files.ReadFile(TilesDirectory + GamePath.Normalize(tilesRelativePath));
        return bytes == null ? null : Dt1File.Parse(bytes);
    }

    public int[] GetPresetObjectTable() => _presetObjectTable ?? _resolvedObjectTable.Value;

    private int[] ResolveObjectTable()
    {
        foreach (var name in new[] { "objpreset", "obj" })
        {
            var table = GetTable(name);
            if (table == null) continue;
            var built = BuildPresetObjectTable(table, GetTable("objects"));
            if (built != null) return built;
        }
        throw new InvalidDataException(
            "No DS1 object table found: expected data/global/excel/objpreset.txt (or obj.txt) with Act / Id / object columns. " +
            "Pass the 750-entry table to the MapGenerator constructor instead.");
    }

    /// <summary>Build the act*150+id table from an objpreset/obj.txt-style table: one row per
    /// (act, DS1 object id) naming the objects.txt row it places, by number or (D2R's objpreset.txt) by the
    /// <c>Class</c> of the objects.txt row, which <paramref name="objects"/> resolves to its <c>*ID</c>.</summary>
    internal static int[]? BuildPresetObjectTable(ExcelTable t, ExcelTable? objects = null)
    {
        var byClass = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (objects != null && objects.FindColumn("Class") is var classCol and >= 0 && objects.FindColumn("*ID", "Id") is var rowIdCol and >= 0)
        {
            for (int r = 0; r < objects.RowCount; r++)
            {
                var name = objects.GetString(r, classCol);
                if (name.Length > 0 && !objects.IsEmpty(r, rowIdCol))
                    byClass.TryAdd(name, objects.GetInt64(r, rowIdCol));
            }
        }

        var actCol = t.FindColumn("Act");
        var idCol = t.FindColumn("Index", "Id", "Preset", "PresetId");
        var objCol = t.FindColumn("ObjectClass", "Object Class", "ObjectId", "Object");
        if (actCol < 0 || idCol < 0 || objCol < 0) return null;
        var rows = new List<(long Act, long Id, long Obj)>();
        long minAct = long.MaxValue;
        for (int r = 0; r < t.RowCount; r++)
        {
            if (t.IsEmpty(r, actCol) || t.IsEmpty(r, idCol)) continue;
            long act = t.GetInt64(r, actCol);
            var cell = t.GetString(r, objCol).Trim();
            long obj = long.TryParse(cell, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? number
                : byClass.TryGetValue(cell, out var row) ? row : 0;
            rows.Add((act, t.GetInt64(r, idCol), obj));
            if (act < minAct) minAct = act;
        }
        if (rows.Count == 0) return null;
        long actBase = minAct >= 1 ? 1 : 0; // Act 1..5 or 0..4
        var table = new int[750];
        foreach (var (act, id, obj) in rows)
        {
            long a = act - actBase;
            if (a < 0 || a > 4 || id < 0 || id >= 150) continue;
            table[a * 150 + id] = (int)obj;
        }
        return table;
    }
}

using System.Collections.Concurrent;
using D2Dotnet.Map.Drlg.Formats;
using D2Dotnet.Map.GameData;

namespace D2Dotnet.Map.Drlg.Data;

/// <summary>Production data source: reads the raw game files (excel .txt, .ds1, .dt1) of a
/// Diablo II: Resurrected install through an <see cref="IGameFileProvider"/>.</summary>
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
public sealed class GameFilesDataSource : IDrlgDataSource
{
    public const string ExcelDir = "data/global/excel/";
    public const string TilesDir = "data/global/tiles/";

    private readonly IGameFileProvider _files;
    private readonly int[]? _presetObjectTable;
    private readonly ConcurrentDictionary<string, TxtTable?> _tables = new(StringComparer.OrdinalIgnoreCase);
    private int[]? _resolvedObjectTable;

    /// <param name="files">The game files.</param>
    /// <param name="presetObjectTable">Optional 750-entry DS1 object id table (act*150 + id ->
    /// objects.txt row). When null it is built from <c>objpreset.txt</c>/<c>obj.txt</c>.</param>
    public GameFilesDataSource(IGameFileProvider files, int[]? presetObjectTable = null)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        if (presetObjectTable != null && presetObjectTable.Length < 750) throw new ArgumentException("needs 750 entries", nameof(presetObjectTable));
        _presetObjectTable = presetObjectTable;
    }

    public TxtTable? GetTable(string name) => _tables.GetOrAdd(name, n =>
    {
        var bytes = _files.ReadFile(ExcelDir + n.ToLowerInvariant() + ".txt");
        return bytes == null ? null : TxtTable.Parse(bytes);
    });

    public Ds1File? GetDs1(string tilesRelativePath)
    {
        var bytes = _files.ReadFile(TilesDir + GamePath.Normalize(tilesRelativePath));
        return bytes == null ? null : Ds1File.Parse(bytes);
    }

    public Dt1File? GetDt1(string tilesRelativePath)
    {
        var bytes = _files.ReadFile(TilesDir + GamePath.Normalize(tilesRelativePath));
        return bytes == null ? null : Dt1File.Parse(bytes);
    }

    public int[] GetPresetObjectTable()
    {
        if (_presetObjectTable != null) return _presetObjectTable;
        var t = _resolvedObjectTable;
        if (t != null) return t;
        foreach (var name in new[] { "objpreset", "obj" })
        {
            var table = GetTable(name);
            if (table == null) continue;
            var built = BuildPresetObjectTable(table);
            if (built != null) return _resolvedObjectTable = built;
        }
        throw new InvalidDataException(
            "No DS1 object table found: expected data/global/excel/objpreset.txt (or obj.txt) with Act / Id / object columns. " +
            "Pass the 750-entry table to the GameFilesDataSource constructor instead.");
    }

    private static string? FindCol(TxtTable t, params string[] names)
    {
        foreach (var n in names)
            if (t.HasColumn(n)) return n;
        return null;
    }

    /// <summary>Build the act*150+id table from an objpreset/obj.txt-style table: one row per
    /// (act, DS1 object id) naming the objects.txt row it places.</summary>
    internal static int[]? BuildPresetObjectTable(TxtTable t)
    {
        var actCol = FindCol(t, "Act", "act", "ACT");
        var idCol = FindCol(t, "Index", "Id", "ID", "id", "index", "Preset", "PresetId");
        var objCol = FindCol(t, "ObjectClass", "Object Class", "objectclass", "ObjectId", "objectID", "ObjectID", "Object", "object");
        if (actCol == null || idCol == null || objCol == null) return null;
        var rows = new List<(long Act, long Id, long Obj)>();
        long minAct = long.MaxValue;
        for (int r = 0; r < t.RowCount; r++)
        {
            if (t.Str(r, actCol).Length == 0 || t.Str(r, idCol).Length == 0) continue;
            long act = t.Int(r, actCol);
            rows.Add((act, t.Int(r, idCol), t.Int(r, objCol)));
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

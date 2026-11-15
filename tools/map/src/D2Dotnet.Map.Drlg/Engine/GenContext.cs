using System.Collections.Concurrent;
using D2Dotnet.Map.Drlg.Data;
using D2Dotnet.Map.Drlg.Formats;

namespace D2Dotnet.Map.Drlg.Engine;

/// <summary>Everything a generation reads, built once from an <see cref="IDrlgDataSource"/>:
/// the parsed tables plus process-lifetime DS1/DT1 parse caches (DS1/DT1 parsing consumes no
/// seed, so a shared parse only affects identity, never output). Thread-safe.</summary>
internal sealed class DrlgData
{
    private readonly IDrlgDataSource _source;
    private readonly ConcurrentDictionary<string, Ds1File?> _ds1 = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Dt1File?> _dt1 = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, NativeDt1?> _ndt1 = new(StringComparer.Ordinal);

    public GameTables Tables { get; }
    public PresetTables Presets { get; }

    public DrlgData(IDrlgDataSource source)
    {
        _source = source;
        TxtTable Need(string name) => source.GetTable(name) ?? throw new InvalidDataException($"The data source has no '{name}' excel table.");
        Tables = new GameTables(Need(ExcelTables.Levels), Need(ExcelTables.LvlPrest), Need(ExcelTables.LvlTypes),
            Need(ExcelTables.LvlMaze), Need(ExcelTables.LvlSub), Need(ExcelTables.LvlWarp));
        Presets = new PresetTables(Need(ExcelTables.MonStats), Need(ExcelTables.SuperUniques), Need(ExcelTables.MonPlace),
            Need(ExcelTables.MonPreset), source.GetPresetObjectTable());
    }

    /// <summary>Normalise a tiles-relative path the way the data source keys it.</summary>
    public static string Key(string rel) => rel.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

    public Ds1File? GetDs1(string rel) => _ds1.GetOrAdd(Key(rel), k => _source.GetDs1(k));

    public Dt1File? GetDt1(string rel) => _dt1.GetOrAdd(Key(rel), k => _source.GetDt1(k));

    /// <summary>The DT1 copied to native memory (tile-data entries point into it).</summary>
    public NativeDt1? GetNativeDt1(string rel) => _ndt1.GetOrAdd(Key(rel), k => GetDt1(k) is Dt1File d ? new NativeDt1(d) : null);
}

/// <summary>The data of the generation running on this thread.</summary>
internal static class GenContext
{
    [ThreadStatic] private static DrlgData? _current;

    public static DrlgData Data => _current ?? throw new InvalidOperationException("No DRLG generation is active on this thread.");

    public static void Bind(DrlgData? data) => _current = data;
}

using D2Dotnet.Map.Drlg.Formats;

namespace D2Dotnet.Map.Drlg.Data;

/// <summary>Everything the map generator reads from the game: the excel tables and the parsed
/// DS1 (preset map) and DT1 (tile library) files.</summary>
/// <remarks>
/// <para>Implementations must be safe to call from several threads at once; the generator caches
/// what it gets, so each file is usually asked for once per <see cref="DrlgGenerator"/>.</para>
/// <para>Tile paths are relative to <c>data/global/tiles/</c>, lower case, with forward slashes,
/// exactly as LvlPrest/LvlSub/LvlTypes name them after normalisation (for example
/// <c>act1/town/townn1.ds1</c> or <c>act1/town/floor.dt1</c>). Returning null for a missing file
/// is allowed: the engine then sees an empty file, as libd2 does for a file it lacks.</para>
/// </remarks>
public interface IDrlgDataSource
{
    /// <summary>An excel table by name, without extension (see <see cref="ExcelTables"/>), or null
    /// when the source does not have it.</summary>
    TxtTable? GetTable(string name);

    /// <summary>A parsed .ds1 by tiles-relative path, or null when it does not exist.</summary>
    Ds1File? GetDs1(string tilesRelativePath);

    /// <summary>A parsed .dt1 by tiles-relative path, or null when it does not exist.</summary>
    Dt1File? GetDt1(string tilesRelativePath);

    /// <summary>gpsPresetObjectTable (1.14d .data @0x748ad8): for each act 0..4, the 150 objects.txt
    /// row ids a DS1 object id below 0x96 resolves to (index act*150 + id). 750 entries.</summary>
    int[] GetPresetObjectTable();
}

/// <summary>The excel tables the generator reads.</summary>
public static class ExcelTables
{
    public const string Levels = "Levels";
    public const string LvlPrest = "LvlPrest";
    public const string LvlTypes = "LvlTypes";
    public const string LvlMaze = "LvlMaze";
    public const string LvlSub = "LvlSub";
    public const string LvlWarp = "LvlWarp";
    public const string MonStats = "MonStats";
    public const string SuperUniques = "SuperUniques";
    public const string MonPlace = "MonPlace";
    public const string MonPreset = "MonPreset";

    /// <summary>Every table <see cref="DrlgGenerator"/> needs.</summary>
    public static IReadOnlyList<string> Required { get; } = new[]
    {
        Levels, LvlPrest, LvlTypes, LvlMaze, LvlSub, LvlWarp, MonStats, SuperUniques, MonPlace, MonPreset,
    };
}

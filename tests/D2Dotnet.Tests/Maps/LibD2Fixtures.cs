using D2Dotnet.Maps;

namespace D2Dotnet.Tests.Maps;

/// <summary>The libd2 checkout the test project's build downloads into <c>.cache/libd2/</c> (see
/// <see cref="Fixtures.LibD2"/>), and a generator over its baked data. Never skips: a missing
/// checkout fails the calling test.</summary>
internal static class LibD2Fixtures
{
    /// <summary>The extracted libd2 source tree (pinned commit).</summary>
    public static string Root => Fixtures.LibD2;

    public static string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    public static string ExcelDir => PathOf("packages", "data", "src", "excel");
    public static string Ds1Blob => PathOf("packages", "drlg", "src", "blobs", "ds1_blob.bin");
    public static string Dt1Blob => PathOf("packages", "formats", "src", "blobs", "dt1_blob.bin");
    public static string PresetObjectTable => PathOf("packages", "drlg", "src", "excel", "PresetObjectTable.bin");

    /// <summary>Raw .ds1/.dt1 fixtures of libd2 (formats + drlg map fixtures), deduped by name.</summary>
    public static IReadOnlyList<string> RawMapFixtures(string ext)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in new[] { PathOf("packages", "formats", "src", "maps"), PathOf("packages", "drlg", "src", "maps") })
            foreach (var f in Directory.GetFiles(d, "*" + ext))
                seen.TryAdd(Path.GetFileName(f), f);
        return seen.Values.OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToArray();
    }

    private static readonly Lazy<LibD2BakedDataSource> _source = new(() => new LibD2BakedDataSource(Root));
    private static readonly Lazy<MapGenerator> _generator = new(() => new MapGenerator(_source.Value));

    /// <summary>One shared baked data source (thread-safe).</summary>
    public static LibD2BakedDataSource Source => _source.Value;

    /// <summary>One shared generator over <see cref="Source"/> (thread-safe).</summary>
    public static MapGenerator Generator => _generator.Value;

    /// <summary>The <c>LevelName</c> of a level in libd2's levels.txt (what libd2 reports as a level's name).</summary>
    public static string LevelName(AreaId id)
    {
        var levels = Source.GetTable("Levels")!;
        return levels.FindRow("Id", (int)id) is int row ? levels.GetString(row, "LevelName") : "";
    }
}

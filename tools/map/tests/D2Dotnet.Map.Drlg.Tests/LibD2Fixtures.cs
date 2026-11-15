using System.Reflection;

namespace D2Dotnet.Map.Drlg.Tests;

/// <summary>Locates the libd2 checkout the test project's build downloaded into
/// <c>.cache/libd2/</c>. Never skips: a missing checkout fails the calling test.</summary>
internal static class LibD2Fixtures
{
    private static string? Meta(string key) =>
        typeof(LibD2Fixtures).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

    /// <summary>The extracted libd2 source tree (pinned commit).</summary>
    public static string Root
    {
        get
        {
            var dir = Meta("LibD2SourceDir") ?? "";
            if (dir.Length == 0 || !Directory.Exists(Path.Combine(dir, "packages")))
                Assert.Fail($"libd2 fixtures are missing at '{dir}'. The test project's build downloads them from " +
                            $"{Meta("LibD2Url")} into <repo>/.cache/libd2/; that download or its extraction failed. " +
                            "Restore network access (or place the codeload tarball libd2-<sha>.tar.gz in .cache/libd2/) and rebuild.");
            return dir;
        }
    }

    public static string PathOf(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

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
    private static readonly Lazy<DrlgGenerator> _generator = new(() => new DrlgGenerator(_source.Value));

    /// <summary>One shared baked data source (thread-safe).</summary>
    public static LibD2BakedDataSource Source => _source.Value;

    /// <summary>One shared generator over <see cref="Source"/> (thread-safe).</summary>
    public static DrlgGenerator Generator => _generator.Value;
}

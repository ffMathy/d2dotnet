using System.Reflection;
using System.Text;
using D2Dotnet.Map.GameData;

namespace D2Dotnet.D2Go.Data.Tests;

/// <summary>Locates the d2go checkout the test project's build downloaded into <c>.cache/d2go/</c>.
/// Never skips: a missing checkout fails the calling test.</summary>
internal static class D2GoFixtures
{
    private static string? Meta(string key) =>
        typeof(D2GoFixtures).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

    /// <summary>The extracted d2go source tree (pinned commit).</summary>
    public static string Root
    {
        get
        {
            var dir = Meta("D2GoSourceDir") ?? "";
            if (dir.Length == 0 || !File.Exists(Path.Combine(dir, "pkg", "data", "data.go")))
                Assert.Fail($"d2go fixtures are missing at '{dir}'. The test project's build downloads them from " +
                            $"{Meta("D2GoUrl")} into <repo>/.cache/d2go/; that download or its extraction failed. " +
                            "Restore network access (or place the codeload tarball d2go-<sha>.tar.gz in .cache/d2go/) and rebuild.");
            return dir;
        }
    }

    /// <summary>The excel tables d2go generates its Go tables from.</summary>
    public static string TxtDirectory => Path.Combine(Root, "cmd", "txttocode", "txt");

    /// <summary>A Go source file of the checkout, e.g. <c>pkg/data/item/items.go</c>.</summary>
    public static string GoFile(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath), Encoding.UTF8);

    private static readonly Lazy<GameTables> _tables = new(() => GameTables.Load(new TxtFolderProvider(TxtDirectory)));

    /// <summary>The tables loaded from d2go's txt files through <see cref="GameTables.Load"/> (shared, immutable).</summary>
    public static GameTables Tables => _tables.Value;
}

/// <summary>Serves <c>data/global/excel/&lt;name&gt;.txt</c> from a flat folder of txt files.</summary>
internal sealed class TxtFolderProvider(string folder) : IGameFileProvider
{
    private string? Resolve(string path)
    {
        var normalized = GamePath.Normalize(path);
        if (!normalized.StartsWith(GameTables.ExcelDirectory, StringComparison.Ordinal))
            return null;
        var name = normalized[GameTables.ExcelDirectory.Length..];
        return Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
    }

    public bool Exists(string path) => Resolve(path) is not null;

    public byte[]? ReadFile(string path) => Resolve(path) is { } file ? File.ReadAllBytes(file) : null;

    public IEnumerable<string> EnumerateFiles(string directory) =>
        Directory.EnumerateFiles(folder).Select(f => GameTables.ExcelDirectory + Path.GetFileName(f).ToLowerInvariant()).Order(StringComparer.Ordinal);
}

/// <summary>An in-memory provider for synthetic tables.</summary>
internal sealed class MemoryProvider : IGameFileProvider
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public MemoryProvider Add(string path, string content)
    {
        _files[GamePath.Normalize(path)] = Encoding.UTF8.GetBytes(content);
        return this;
    }

    public MemoryProvider AddBytes(string path, byte[] content)
    {
        _files[GamePath.Normalize(path)] = content;
        return this;
    }

    public bool Exists(string path) => _files.ContainsKey(GamePath.Normalize(path));

    public byte[]? ReadFile(string path) => _files.GetValueOrDefault(GamePath.Normalize(path));

    public IEnumerable<string> EnumerateFiles(string directory) => _files.Keys.Order(StringComparer.Ordinal);
}

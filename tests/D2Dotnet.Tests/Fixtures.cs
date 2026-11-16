using System.Reflection;
using System.Text;
using D2Dotnet.Files;

namespace D2Dotnet.Tests;

/// <summary>
/// The oracle sources the test project's build downloads into <c>.cache/</c> (see <c>tests/Fixtures.targets</c>): libd2
/// and d2go at their pinned commits. Never skips: a missing checkout fails the calling test.
/// </summary>
internal static class Fixtures
{
    private static string? Meta(string key) =>
        typeof(Fixtures).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

    private static string Checkout(string name, string dirKey, string urlKey, string marker)
    {
        var dir = Meta(dirKey) ?? "";
        if (dir.Length == 0 || !Path.Exists(Path.Combine(dir, marker)))
            Assert.Fail($"{name} fixtures are missing at '{dir}'. The test project's build downloads them from " +
                        $"{Meta(urlKey)} into <repo>/.cache/; that download or its extraction failed. " +
                        $"Restore network access (or place the codeload tarball in .cache/{name}/) and rebuild.");
        return dir;
    }

    /// <summary>The extracted libd2 source tree.</summary>
    public static string LibD2 => Checkout("libd2", "LibD2SourceDir", "LibD2Url", "packages");

    /// <summary>libd2's excel tables (classic 1.14d data, Windows-1252 text).</summary>
    public static string LibD2ExcelDirectory => Path.Combine(LibD2, "packages", "data", "src", "excel");

    /// <summary>The extracted d2go source tree.</summary>
    public static string D2Go => Checkout("d2go", "D2GoSourceDir", "D2GoUrl", Path.Combine("pkg", "data", "data.go"));

    /// <summary>The (D2R) excel tables d2go generates its Go tables from.</summary>
    public static string D2GoTxtDirectory => Path.Combine(D2Go, "cmd", "txttocode", "txt");

    /// <summary>A Go source file of the d2go checkout, e.g. <c>pkg/data/item/items.go</c>.</summary>
    public static string D2GoFile(string relativePath) => File.ReadAllText(Path.Combine(D2Go, relativePath), Encoding.UTF8);

    private static readonly Lazy<MemoryFiles> _d2goFiles = new(() => MemoryFiles.FromExcelFolder(D2GoTxtDirectory));
    private static readonly Lazy<MemoryFiles> _libd2Files = new(() => MemoryFiles.FromExcelFolder(LibD2ExcelDirectory));

    /// <summary>d2go's txt files served as <c>data/global/excel/*.txt</c>.</summary>
    public static MemoryFiles D2GoFiles => _d2goFiles.Value;

    /// <summary>libd2's excel tables served as <c>data/global/excel/*.txt</c>.</summary>
    public static MemoryFiles LibD2Files => _libd2Files.Value;
}

/// <summary>An in-memory <see cref="IGameFileProvider"/>.</summary>
internal sealed class MemoryFiles : IGameFileProvider
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    /// <summary>Every <c>*.txt</c> of a flat folder as <c>data/global/excel/&lt;lowercase name&gt;</c>.</summary>
    public static MemoryFiles FromExcelFolder(string folder)
    {
        var files = new MemoryFiles();
        foreach (var file in Directory.EnumerateFiles(folder, "*.txt"))
            files.Add("data/global/excel/" + Path.GetFileName(file), File.ReadAllBytes(file));
        return files;
    }

    public MemoryFiles Add(string path, string content) => Add(path, Encoding.UTF8.GetBytes(content));

    public MemoryFiles Add(string path, byte[] content)
    {
        _files[GamePath.Normalize(path)] = content;
        return this;
    }

    public MemoryFiles Remove(string path)
    {
        _files.Remove(GamePath.Normalize(path));
        return this;
    }

    /// <summary>A copy, to change without touching the original.</summary>
    public MemoryFiles Clone()
    {
        var copy = new MemoryFiles();
        foreach (var (path, bytes) in _files)
            copy._files[path] = bytes;
        return copy;
    }

    public bool Exists(string path) => _files.ContainsKey(GamePath.Normalize(path));

    public byte[]? ReadFile(string path) => _files.GetValueOrDefault(GamePath.Normalize(path));

    public IEnumerable<string> EnumerateFiles(string directory)
    {
        var prefix = GamePath.Normalize(directory).TrimEnd('/');
        var all = _files.Keys.Order(StringComparer.Ordinal);
        return prefix.Length == 0 ? all : all.Where(k => k.StartsWith(prefix + "/", StringComparison.Ordinal));
    }
}

namespace D2Dotnet.Files;

/// <summary>
/// Reads game files from a folder of extracted files (CascView export, or a <c>-direct -txt</c> mod folder).
/// </summary>
/// <remarks>
/// <para>
/// The root may be any of these, detected on construction (folder names are matched ignoring case):
/// </para>
/// <list type="bullet">
/// <item>a folder that contains <c>data/</c> (with <c>global/</c> or <c>local/</c> inside): paths map directly below it.
/// This is the layout of a D2R install used with <c>-direct -txt</c>, and of <c>mods/&lt;name&gt;/&lt;name&gt;.mpq/</c>;</item>
/// <item>a CascView export, where the <c>data:</c> mount becomes one more folder: <c>&lt;root&gt;/data/data/global/...</c>;</item>
/// <item>the <c>data</c> folder itself (it contains <c>global/</c> or <c>local/</c>): <c>data/global/x</c> maps to <c>&lt;root&gt;/global/x</c>.</item>
/// </list>
/// <para>
/// Lookups are case-insensitive on every OS. To make that work on case-sensitive file systems the
/// provider lists the folder once, on first need; files added afterwards are found by
/// <see cref="Exists"/>/<see cref="ReadFile"/> only when their exact path matches.
/// </para>
/// </remarks>
public sealed class DirectoryFileProvider : IGameFileProvider
{
    private readonly Lazy<Dictionary<string, string>> _files;

    /// <param name="root">The folder to read; see the remarks for the accepted layouts.</param>
    /// <exception cref="DirectoryNotFoundException"><paramref name="root"/> does not exist.</exception>
    public DirectoryFileProvider(string root)
    {
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Game data folder '{root}' does not exist.");
        (BaseDirectory, PathPrefix) = Detect(full);
        _files = new Lazy<Dictionary<string, string>>(Scan, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The folder that game paths are resolved against, after layout detection.</summary>
    public string BaseDirectory { get; }

    /// <summary>
    /// The part of a game path the base folder stands for: empty, or <c>data/</c> when the root is the <c>data</c> folder itself.
    /// </summary>
    public string PathPrefix { get; }

    /// <inheritdoc/>
    public bool Exists(string path) => Resolve(path) is not null;

    /// <inheritdoc/>
    public byte[]? ReadFile(string path) => Resolve(path) is { } file ? File.ReadAllBytes(file) : null;

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateFiles(string directory)
    {
        var prefix = GamePath.Normalize(directory).TrimEnd('/');
        var all = _files.Value.Keys.Order(StringComparer.Ordinal);
        if (prefix.Length == 0 || prefix == ".")
            return all;
        prefix += "/";
        return all.Where(p => p.StartsWith(prefix, StringComparison.Ordinal));
    }

    private string? Resolve(string path)
    {
        var normalized = GamePath.Normalize(path);
        if (normalized.Length == 0 || normalized.EndsWith('/') || !normalized.StartsWith(PathPrefix, StringComparison.Ordinal))
            return null;
        if (normalized.Split('/').Any(part => part is ".." or "."))
            return null; // stay inside the base folder

        var direct = Path.Combine(BaseDirectory, path.Replace('\\', '/').TrimStart('/')[PathPrefix.Length..]);
        if (File.Exists(direct))
            return direct;
        return _files.Value.TryGetValue(normalized, out var file) ? file : null;
    }

    private Dictionary<string, string> Scan()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (var file in Directory.EnumerateFiles(BaseDirectory, "*", options))
            files.TryAdd(PathPrefix + GamePath.Normalize(Path.GetRelativePath(BaseDirectory, file)), file);
        return files;
    }

    private static (string BaseDirectory, string Prefix) Detect(string root)
    {
        static bool IsDataFolder(string dir) =>
            Child(dir, "global") is not null || Child(dir, "local") is not null;

        if (Child(root, "data") is { } data)
        {
            if (IsDataFolder(data))
                return (root, "");
            if (Child(data, "data") is { } nested && IsDataFolder(nested))
                return (data, ""); // CascView export: <root>/data/data/global/...
        }
        if (IsDataFolder(root))
            return (root, "data/");
        return (root, "");
    }

    private static string? Child(string parent, string name) =>
        Directory.EnumerateDirectories(parent)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
}

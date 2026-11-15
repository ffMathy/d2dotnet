using D2Dotnet.Map.GameData.Casc;

namespace D2Dotnet.Map.GameData;

/// <summary>
/// Reads game files straight from the CASC storage of an installed Diablo II: Resurrected.
/// </summary>
/// <remarks>
/// <para>
/// Open it on the install folder, the one that contains <c>.build.info</c> and <c>Data/</c>.
/// The storage's virtual file system is loaded once, on open; file data is read and decoded on demand.
/// Instances are safe to use from several threads.
/// </para>
/// <para>
/// Path mapping. D2R's TVFS root mounts sub-manifests, and CascLib (and so CascView) names their
/// files <c>mount:path</c>, for example <c>data:data/global/excel/levels.txt</c>. This provider drops the
/// mount prefix, so that file is <c>data/global/excel/levels.txt</c>, matching the classic layout and
/// <see cref="DirectoryFileProvider"/>. When two mounts contain the same path, the <c>data</c> mount
/// wins, then the first one in manifest order. Files outside any mount keep their name. The raw names
/// are available through <see cref="VfsNames"/> and <see cref="ReadVfsFile"/>.
/// </para>
/// <para>
/// Only files whose data is present in the local index are listed: a partial install (for example a
/// language that was not downloaded) leaves files in the manifest that cannot be read.
/// </para>
/// <para>
/// <see cref="ReadFile"/> throws <see cref="CascFormatException"/> when the storage is corrupt, and
/// <see cref="CascEncryptedException"/> for files encrypted with a key this reader does not have.
/// </para>
/// </remarks>
public sealed class CascFileProvider : IGameFileProvider, IDisposable
{
    /// <summary>The mount whose files win when several mounts contain the same path.</summary>
    public const string PrimaryMount = "data";

    private readonly CascStorage _storage;
    private readonly Dictionary<string, TvfsFile> _byPath;
    private readonly Dictionary<string, TvfsFile> _byVfsName;
    private readonly string[] _sortedPaths;

    private CascFileProvider(CascStorage storage)
    {
        _storage = storage;
        _byPath = new Dictionary<string, TvfsFile>(StringComparer.Ordinal);
        _byVfsName = new Dictionary<string, TvfsFile>(StringComparer.OrdinalIgnoreCase);

        var local = storage.Files.Where(f => storage.IsLocal(f.File)).ToList();
        foreach (var entry in local)
            _byVfsName.TryAdd(NormalizeVfsName(entry.Name), entry.File);

        // Primary mount first, then the rest in manifest order; the first file for a path wins.
        foreach (var entry in local.OrderBy(f => MountOf(f.Name) == PrimaryMount ? 0 : 1))
            _byPath.TryAdd(ToGamePath(entry.Name), entry.File);

        _sortedPaths = _byPath.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Open the CASC storage of the install in <paramref name="installDirectory"/>.</summary>
    /// <param name="installDirectory">The folder containing <c>.build.info</c>.</param>
    /// <param name="product">
    /// Product code to pick from <c>.build.info</c> (D2R is <c>osi</c>); null takes the first active build.
    /// </param>
    /// <exception cref="FileNotFoundException">There is no <c>.build.info</c>, or a config or index is missing.</exception>
    /// <exception cref="CascFormatException">The storage is malformed or not a TVFS storage.</exception>
    public static CascFileProvider Open(string installDirectory, string? product = null)
    {
        var storage = CascStorage.Open(installDirectory, product);
        try
        {
            return new CascFileProvider(storage);
        }
        catch
        {
            storage.Dispose();
            throw;
        }
    }

    /// <summary>The product code of the opened build (<c>osi</c> for D2R), if <c>.build.info</c> names one.</summary>
    public string? Product => _storage.BuildRow.TryGetValue("Product", out var p) && p.Length > 0 ? p : null;

    /// <summary>The game version of the opened build, if <c>.build.info</c> names one.</summary>
    public string? Version => _storage.BuildRow.TryGetValue("Version", out var v) && v.Length > 0 ? v : null;

    /// <summary>
    /// CascLib-style names of all locally present files, such as <c>data:data/global/excel/levels.txt</c>
    /// (lowercase, forward slashes).
    /// </summary>
    public IReadOnlyCollection<string> VfsNames => _byVfsName.Keys;

    /// <summary>Number of files reachable through <see cref="IGameFileProvider"/> paths.</summary>
    public int Count => _byPath.Count;

    public bool Exists(string path) => _byPath.ContainsKey(GamePath.Normalize(path));

    public byte[]? ReadFile(string path) =>
        _byPath.TryGetValue(GamePath.Normalize(path), out var file) ? _storage.Read(file) : null;

    /// <summary>Read a file by its CascLib-style name (<c>data:data/global/excel/levels.txt</c>), or null when it does not exist.</summary>
    public byte[]? ReadVfsFile(string vfsName) =>
        _byVfsName.TryGetValue(NormalizeVfsName(vfsName), out var file) ? _storage.Read(file) : null;

    public IEnumerable<string> EnumerateFiles(string directory)
    {
        var prefix = GamePath.Normalize(directory).TrimEnd('/');
        if (prefix.Length == 0 || prefix == ".")
            return _sortedPaths;
        prefix += "/";
        // _sortedPaths is ordinal-sorted, so everything under the prefix is one contiguous run.
        var start = Array.BinarySearch(_sortedPaths, prefix, StringComparer.Ordinal);
        if (start < 0)
            start = ~start;
        return Run(start, prefix);
    }

    private IEnumerable<string> Run(int start, string prefix)
    {
        for (var i = start; i < _sortedPaths.Length && _sortedPaths[i].StartsWith(prefix, StringComparison.Ordinal); i++)
            yield return _sortedPaths[i];
    }

    /// <summary>Map a CascLib-style VFS name to an <see cref="IGameFileProvider"/> path by dropping the <c>mount:</c> prefix.</summary>
    public static string ToGamePath(string vfsName)
    {
        var colon = vfsName.LastIndexOf(':');
        return GamePath.Normalize(colon < 0 ? vfsName : vfsName[(colon + 1)..]);
    }

    private static string MountOf(string vfsName)
    {
        var colon = vfsName.IndexOf(':');
        return colon < 0 ? "" : vfsName[..colon].ToLowerInvariant();
    }

    private static string NormalizeVfsName(string name) => GamePath.Normalize(name);

    public void Dispose() => _storage.Dispose();

    public override string ToString() => _storage.ToString();
}

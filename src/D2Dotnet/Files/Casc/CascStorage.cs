using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace D2Dotnet.Files.Casc;

/// <summary>A file of the storage's virtual file system, with its CascLib-style name (e.g. <c>data:data/global/excel/levels.txt</c>).</summary>
internal sealed record VfsEntry(string Name, TvfsFile File);

/// <summary>
/// A local (installed) CASC storage: <c>.build.info</c> → build config → local index + data archives →
/// ENCODING and the TVFS root, with mounted sub-manifests flattened into <c>mount:path</c> names.
/// </summary>
internal sealed partial class CascStorage : IDisposable
{
    /// <summary>Size of the record header in front of each BLTE stream in a <c>data.###</c> archive.</summary>
    public const int RecordHeaderSize = 0x1E;

    private const int MaxMountDepth = 8;

    private readonly string _dataDirectory;
    private readonly Dictionary<int, SafeFileHandle> _archives = new();
    private readonly Lock _archiveLock = new();
    private readonly Lazy<EncodingTable> _encoding;
    private bool _disposed;

    private CascStorage(string installDirectory, string dataDirectory, IReadOnlyDictionary<string, string> buildRow,
        ConfigFile buildConfig, LocalIndex index)
    {
        InstallDirectory = installDirectory;
        _dataDirectory = dataDirectory;
        BuildRow = buildRow;
        BuildConfig = buildConfig;
        Index = index;
        _encoding = new Lazy<EncodingTable>(LoadEncoding, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string InstallDirectory { get; }

    /// <summary>The selected row of <c>.build.info</c>.</summary>
    public IReadOnlyDictionary<string, string> BuildRow { get; }

    public ConfigFile BuildConfig { get; }

    public LocalIndex Index { get; }

    /// <summary>The ENCODING table, loaded on first use.</summary>
    public EncodingTable Encoding => _encoding.Value;

    /// <summary>Every file of the virtual file system, including ones whose data is not installed locally.</summary>
    public IReadOnlyList<VfsEntry> Files { get; private set; } = [];

    public static CascStorage Open(string installDirectory, string? product = null)
    {
        var buildInfoPath = Path.Combine(installDirectory, ".build.info");
        if (!File.Exists(buildInfoPath))
            throw new FileNotFoundException($"'{installDirectory}' is not a CASC install: no .build.info.", buildInfoPath);

        var row = BuildInfo.Parse(SharedFile.ReadAllText(buildInfoPath)).SelectRow(product);
        var dataRoot = FindChild(installDirectory, "Data")
            ?? throw new DirectoryNotFoundException($"'{installDirectory}' has a .build.info but no Data folder.");
        var configDir = FindChild(dataRoot, "config")
            ?? throw new DirectoryNotFoundException($"'{dataRoot}' has no config folder.");
        var dataDir = FindChild(dataRoot, "data")
            ?? throw new DirectoryNotFoundException($"'{dataRoot}' has no data folder.");

        var buildKey = row["Build Key"].ToLowerInvariant();
        var configPath = FindConfig(configDir, buildKey)
            ?? throw new FileNotFoundException($"Build config {buildKey} is missing from '{configDir}'.");
        var buildConfig = ConfigFile.Parse(SharedFile.ReadAllText(configPath));

        var storage = new CascStorage(installDirectory, dataDir, row, buildConfig, LocalIndex.Load(dataDir));
        try
        {
            storage.Files = storage.LoadVfs();
            return storage;
        }
        catch
        {
            storage.Dispose();
            throw;
        }
    }

    /// <summary>True when every span of the file is present in the local index.</summary>
    public bool IsLocal(TvfsFile file) => file.Spans.All(s => Index.TryGet(s.EKey, out _));

    /// <summary>Read a VFS file: decode each span and concatenate them.</summary>
    public byte[] Read(TvfsFile file)
    {
        if (file.Spans.Count == 1)
            return ReadSpan(file.Path, file.Spans[0]);
        using var output = new MemoryStream((int)Math.Min(file.ContentSize, Array.MaxLength));
        foreach (var span in file.Spans)
            output.Write(ReadSpan(file.Path, span));
        return output.ToArray();
    }

    private byte[] ReadSpan(string name, TvfsSpan span)
    {
        var data = ReadEKey(span.EKey, name);
        if (data.Length == span.ContentSize)
            return data;
        if ((long)span.FileOffset + span.ContentSize <= data.Length)
            return data.AsSpan((int)span.FileOffset, (int)span.ContentSize).ToArray();
        throw new CascFormatException($"{name}: decoded {data.Length} bytes, but the manifest expects {span.ContentSize} at offset {span.FileOffset}.");
    }

    /// <summary>Read and decode the file with this encoding key (at least 9 bytes are used).</summary>
    public byte[] ReadEKey(ReadOnlySpan<byte> ekey, string description)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Index.TryGet(ekey, out var entry))
            throw new FileNotFoundException($"{description}: EKey {Hex.Of(ekey[..EKey9.Length])} is not in the local index (not installed?).");
        var record = ReadRecord(entry, description);
        try
        {
            return Blte.Decode(record.AsSpan(RecordHeaderSize));
        }
        catch (CascFormatException e) when (e is not CascEncryptedException)
        {
            throw new CascFormatException($"{description}: {e.Message}", e);
        }
    }

    /// <summary>Read the raw record (30-byte header + BLTE) an index entry points at.</summary>
    private byte[] ReadRecord(IndexEntry entry, string description)
    {
        if (entry.Size < RecordHeaderSize + 8)
            throw new CascFormatException($"{description}: index entry size {entry.Size} is too small for a BLTE record.");
        var handle = Archive(entry.Archive);
        var buffer = new byte[entry.Size];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = RandomAccess.Read(handle, buffer.AsSpan(read), entry.Offset + read);
            if (n == 0)
                throw new CascFormatException($"{description}: data.{entry.Archive:D3} is truncated at offset {entry.Offset + read}.");
            read += n;
        }
        // CascLib only trusts the BLTE signature here: the EKey/size/checksum fields of the
        // 30-byte header have been seen zeroed or garbage in shipped storages.
        if (BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(RecordHeaderSize)) != Blte.Magic)
            throw new CascFormatException($"{description}: no BLTE data at data.{entry.Archive:D3} offset {entry.Offset}.");
        return buffer;
    }

    private SafeFileHandle Archive(int number)
    {
        lock (_archiveLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_archives.TryGetValue(number, out var handle))
                return handle;
            var name = $"data.{number:D3}";
            var path = FindChild(_dataDirectory, name, directory: false)
                ?? throw new FileNotFoundException($"Data archive {name} is missing from '{_dataDirectory}'.");
            // Read-only, sharing writes so a running Battle.net agent is not disturbed.
            handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _archives[number] = handle;
            return handle;
        }
    }

    private EncodingTable LoadEncoding()
    {
        var reference = BuildConfig.GetFileRef("encoding")
            ?? throw new CascFormatException("Build config has no 'encoding' entry.");
        if (reference.EKey is null)
            throw new CascFormatException("Build config 'encoding' entry has no encoding key.");
        var data = ReadEKey(reference.EKey, "ENCODING");
        VerifyContentKey(data, reference.CKey, "ENCODING");
        return EncodingTable.Parse(data);
    }

    /// <summary>Read a manifest named by the build config, resolving its EKey through ENCODING when the config gives only the CKey.</summary>
    private byte[] ReadConfigFile(ConfigFileRef reference)
    {
        var ekey = reference.EKey;
        if (ekey is null)
        {
            if (!Encoding.TryGet(reference.CKey, out var entry) || entry.EKeys.Count == 0)
                throw new CascFormatException($"'{reference.Name}' ({Hex.Of(reference.CKey)}) is not in ENCODING.");
            ekey = entry.EKeys[0];
        }
        var data = ReadEKey(ekey, reference.Name);
        VerifyContentKey(data, reference.CKey, reference.Name);
        return data;
    }

    private static void VerifyContentKey(ReadOnlySpan<byte> data, ReadOnlySpan<byte> ckey, string name)
    {
        Span<byte> md5 = stackalloc byte[16];
        MD5.HashData(data, md5);
        if (!md5.SequenceEqual(ckey))
            throw new CascFormatException($"{name}: content MD5 {Hex.Of(md5)} does not match its content key {Hex.Of(ckey)}.");
    }

    [GeneratedRegex("^vfs-[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex VfsMountKey();

    private List<VfsEntry> LoadVfs()
    {
        var rootRef = BuildConfig.GetFileRef("vfs-root") ?? BuildConfig.GetFileRef("root")
            ?? throw new CascFormatException("Build config has neither 'vfs-root' nor 'root'.");
        var rootData = ReadConfigFile(rootRef);
        if (!TvfsManifest.HasSignature(rootData))
            throw new CascFormatException($"The root manifest '{rootRef.Name}' is not TVFS; D2R storages use a TVFS root.");

        // vfs-1 .. vfs-N: the manifests that may be mounted as sub-directories of the root.
        var mounts = new Dictionary<EKey9, ConfigFileRef>();
        foreach (var key in BuildConfig.Keys.Where(k => VfsMountKey().IsMatch(k)))
        {
            var reference = BuildConfig.GetFileRef(key)!;
            byte[]? ekey = reference.EKey;
            if (ekey is null && Encoding.TryGet(reference.CKey, out var entry) && entry.EKeys.Count > 0)
                ekey = entry.EKeys[0];
            if (ekey is not null)
                mounts.TryAdd(EKey9.From(ekey), reference);
        }

        var files = new List<VfsEntry>();
        Flatten(TvfsManifest.Parse(rootData), "", mounts, files, depth: 0);
        return files;
    }

    private void Flatten(TvfsManifest manifest, string prefix, Dictionary<EKey9, ConfigFileRef> mounts, List<VfsEntry> into, int depth)
    {
        foreach (var file in manifest.Files)
        {
            var name = prefix + file.Path;
            if (file.Spans.Count == 1 && mounts.TryGetValue(EKey9.From(file.Spans[0].EKey), out var mount))
            {
                if (depth >= MaxMountDepth)
                    throw new CascFormatException($"TVFS mounts nest deeper than {MaxMountDepth} levels at '{name}'.");
                if (!Index.TryGet(file.Spans[0].EKey, out _))
                    continue; // a mounted manifest that is not installed locally: none of its files can be read
                var sub = ReadConfigFile(mount);
                if (TvfsManifest.HasSignature(sub))
                {
                    // CascLib names files of a mounted manifest "<mount>:<path>".
                    Flatten(TvfsManifest.Parse(sub), name + ":", mounts, into, depth + 1);
                    continue;
                }
            }
            into.Add(new VfsEntry(name, file));
        }
    }

    /// <summary>Find a child file or directory by name, exactly first and then ignoring case.</summary>
    internal static string? FindChild(string parent, string name, bool directory = true)
    {
        var exact = Path.Combine(parent, name);
        if (directory ? Directory.Exists(exact) : File.Exists(exact))
            return exact;
        if (!Directory.Exists(parent))
            return null;
        var candidates = directory ? Directory.EnumerateDirectories(parent) : Directory.EnumerateFiles(parent);
        return candidates.FirstOrDefault(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindConfig(string configDir, string key)
    {
        var a = FindChild(configDir, key[..2]);
        var b = a is null ? null : FindChild(a, key[2..4]);
        return b is null ? null : FindChild(b, key, directory: false);
    }

    public void Dispose()
    {
        lock (_archiveLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var handle in _archives.Values)
                handle.Dispose();
            _archives.Clear();
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder("CASC ");
        if (BuildRow.TryGetValue("Product", out var product) && product.Length > 0)
            sb.Append(product).Append(' ');
        if (BuildRow.TryGetValue("Version", out var version) && version.Length > 0)
            sb.Append(version).Append(' ');
        return sb.Append("at ").Append(InstallDirectory).ToString();
    }
}

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using D2Dotnet.Files;
using D2Dotnet.Files.Casc;

namespace D2Dotnet.Tests.Files.Writer;

/// <summary>Where a written file's record ended up.</summary>
/// <param name="ArchivePath">The <c>data.###</c> file.</param>
/// <param name="RecordOffset">Offset of the 30-byte record header.</param>
/// <param name="BlteLength">Length of the BLTE stream after the record header.</param>
/// <param name="BlteHeaderLength">Length of the BLTE header (8, or 12 + 24 * frames).</param>
public sealed record WrittenRecord(string ArchivePath, long RecordOffset, int BlteLength, int BlteHeaderLength)
{
    /// <summary>Offset of the first frame's data in the archive.</summary>
    public long FirstFrameOffset => RecordOffset + CascStorage.RecordHeaderSize + BlteHeaderLength;
}

/// <summary>The result of <see cref="CascStorageWriter.Write"/>.</summary>
public sealed class WrittenStorage
{
    public required string InstallDirectory { get; init; }
    public required string DataDirectory { get; init; }
    public required string BuildConfigPath { get; init; }
    public required IReadOnlyDictionary<string, WrittenRecord> Records { get; init; }
    public required IReadOnlyList<string> IndexFiles { get; init; }
    public required byte[] RootCKey { get; init; }
}

/// <summary>
/// Builds a small but structurally real local CASC storage in a folder, modelled on a Battle.net
/// install of D2R: <c>.build.info</c>, <c>Data/config/xx/yy/&lt;key&gt;</c> build and CDN configs,
/// version-7 <c>Data/data/*.idx</c> buckets with Jenkins checksums, <c>data.###</c> archives with
/// 30-byte record headers, an ENCODING file, and a TVFS root (<c>vfs-root</c>) that mounts a
/// sub-manifest (<c>vfs-1</c>) as <c>data</c>, so CascLib-style names read <c>data:data/global/...</c>.
/// </summary>
public sealed class CascStorageWriter
{
    private readonly List<(string Mount, string Path, byte[][] Parts, BlteLayout Layout, bool Installed)> _files = [];

    public string Product { get; set; } = "osi";
    public string Version { get; set; } = "1.6.80273";

    /// <summary>When false, <c>vfs-root</c> and <c>vfs-N</c> carry only the CKey, so the reader must resolve them through ENCODING.</summary>
    public bool ManifestEKeysInBuildConfig { get; set; } = true;

    /// <summary>Records go to the next <c>data.###</c> once an archive grows past this many bytes.</summary>
    public long ArchiveSizeLimit { get; set; } = 4096;

    /// <summary>Also write an older, bogus version of every index bucket, which the reader must ignore.</summary>
    public bool WriteStaleIndexVersions { get; set; } = true;

    /// <summary>Write an inactive row for another product before the real one in <c>.build.info</c>.</summary>
    public bool WriteInactiveBuildInfoRow { get; set; } = true;

    /// <summary>Add a file under a TVFS mount (<c>data</c> by default), or at the root when <paramref name="mount"/> is null.</summary>
    public CascStorageWriter Add(string path, byte[] content, BlteLayout? layout = null, string? mount = "data")
    {
        _files.Add((mount ?? "", path, [content], layout ?? BlteLayout.HeaderlessZ, true));
        return this;
    }

    public CascStorageWriter Add(string path, string content, BlteLayout? layout = null, string? mount = "data") =>
        Add(path, Encoding.UTF8.GetBytes(content), layout, mount);

    /// <summary>Add a file stored as several spans, each its own encoded file.</summary>
    public CascStorageWriter AddMultiSpan(string path, byte[][] parts, BlteLayout? layout = null, string mount = "data")
    {
        _files.Add((mount, path, parts, layout ?? BlteLayout.HeaderlessZ, true));
        return this;
    }

    /// <summary>Add a file that is in the manifest but whose data is not installed (absent from the index and archives).</summary>
    public CascStorageWriter AddNotInstalled(string path, string content, string mount = "data")
    {
        _files.Add((mount, path, [Encoding.UTF8.GetBytes(content)], BlteLayout.HeaderlessZ, false));
        return this;
    }

    private sealed record Blob(string Name, byte[] CKey, byte[] EKey, byte[] Blte, long ContentSize, string ESpec, bool Installed);

    public WrittenStorage Write(string installDirectory)
    {
        var blobs = new List<Blob>();
        Blob Encode(string name, byte[] content, BlteLayout layout, bool installed = true)
        {
            var (blte, ekey) = BlteWriter.Encode(content, layout);
            var blob = new Blob(name, MD5.HashData(content), ekey, blte, content.Length, layout.ESpec, installed);
            blobs.Add(blob);
            return blob;
        }

        // 1. Content files, grouped by mount.
        var mounts = new Dictionary<string, List<TvfsFileSpec>>(StringComparer.Ordinal);
        foreach (var (mount, path, parts, layout, installed) in _files)
        {
            var spans = new List<TvfsSpanSpec>();
            for (var i = 0; i < parts.Length; i++)
            {
                var name = (mount.Length > 0 ? mount + ":" : "") + path + (parts.Length > 1 ? $"#span{i}" : "");
                var blob = Encode(name, parts[i], layout, installed);
                spans.Add(new TvfsSpanSpec(blob.EKey, 0, (uint)parts[i].Length, (uint)blob.Blte.Length));
            }
            if (!mounts.TryGetValue(mount, out var list))
                mounts[mount] = list = [];
            list.Add(new TvfsFileSpec(path, spans));
        }

        // 2. One sub-manifest per mount (vfs-1..N), referenced from the root manifest by EKey.
        var rootEntries = mounts.TryGetValue("", out var rootFiles) ? rootFiles.ToList() : [];
        var vfsBlobs = new List<Blob>();
        foreach (var (mount, files) in mounts.Where(m => m.Key.Length > 0).OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            var manifest = Encode($"vfs-{vfsBlobs.Count + 1}", TvfsWriter.Write(files), BlteLayout.Single('Z'));
            vfsBlobs.Add(manifest);
            rootEntries.Add(new TvfsFileSpec(mount, [new TvfsSpanSpec(manifest.EKey, 0, (uint)manifest.ContentSize, (uint)manifest.Blte.Length)]));
        }
        var root = Encode("vfs-root", TvfsWriter.Write(rootEntries), BlteLayout.Single('Z'));

        // 3. ENCODING: every blob's CKey -> EKey (but not ENCODING itself).
        var encodingContent = EncodingWriter.Write(blobs.Select(b => (b.CKey, b.EKey, b.ContentSize, b.ESpec, (long)b.Blte.Length)));
        var encoding = Encode("encoding", encodingContent, BlteLayout.Chunked(encodingContent.Length, 1024, 'Z', 'N'));

        // 4. Archives and index.
        var dataRoot = Path.Combine(installDirectory, "Data");
        var dataDir = Path.Combine(dataRoot, "data");
        var configDir = Path.Combine(dataRoot, "config");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(configDir);
        var records = new Dictionary<string, WrittenRecord>(StringComparer.Ordinal);
        var indexEntries = new List<(byte[] EKey, int Archive, long Offset, uint Size)>();
        var archive = 0;
        FileStream? stream = null;
        try
        {
            foreach (var blob in blobs.Where(b => b.Installed))
            {
                if (stream is null || stream.Length > ArchiveSizeLimit)
                {
                    stream?.Dispose();
                    archive = stream is null ? 0 : archive + 1;
                    stream = File.Create(Path.Combine(dataDir, $"data.{archive:D3}"));
                }
                var offset = stream.Length;
                stream.Write(RecordHeader(blob.EKey, blob.Blte.Length, offset));
                stream.Write(blob.Blte);
                var size = (uint)(CascStorage.RecordHeaderSize + blob.Blte.Length);
                indexEntries.Add((blob.EKey, archive, offset, size));
                var headerLength = (int)BinaryPrimitives.ReadUInt32BigEndian(blob.Blte.AsSpan(4));
                records[blob.Name] = new WrittenRecord(stream.Name, offset, blob.Blte.Length, headerLength == 0 ? 8 : headerLength);
            }
        }
        finally
        {
            stream?.Dispose();
        }
        var indexFiles = WriteIndex(dataDir, indexEntries);

        // 5. Configs.
        string Hash(ConfigFileRefSpec s) => ManifestEKeysInBuildConfig ? $"{Hex(s.CKey)} {Hex(s.EKey)}" : Hex(s.CKey);
        var build = new StringBuilder()
            .Append("# Build Configuration\n\n")
            .Append($"root = {Hex(root.CKey)}\n")
            .Append($"encoding = {Hex(encoding.CKey)} {Hex(encoding.EKey)}\n")
            .Append($"encoding-size = {encoding.ContentSize} {encoding.Blte.Length}\n")
            .Append($"build-name = {Version}\n")
            .Append($"build-uid = {Product}\n")
            .Append("build-product = D2R\n")
            .Append($"vfs-root = {Hash(new(root.CKey, root.EKey))}\n")
            .Append($"vfs-root-size = {root.ContentSize}{(ManifestEKeysInBuildConfig ? $" {root.Blte.Length}" : "")}\n");
        for (var i = 0; i < vfsBlobs.Count; i++)
        {
            var v = vfsBlobs[i];
            build.Append($"vfs-{i + 1} = {Hash(new(v.CKey, v.EKey))}\n")
                .Append($"vfs-{i + 1}-size = {v.ContentSize}{(ManifestEKeysInBuildConfig ? $" {v.Blte.Length}" : "")}\n");
        }
        var buildKey = WriteConfig(configDir, build.ToString());
        var cdnKey = WriteConfig(configDir, "# CDN Configuration\n\narchives = \narchive-group = \n");

        // 6. .build.info.
        var info = new StringBuilder("Branch!STRING:0|Active!DEC:1|Build Key!HEX:16|CDN Key!HEX:16|Install Key!HEX:16|IM Size!DEC:4|CDN Path!STRING:0|CDN Hosts!STRING:0|CDN Servers!STRING:0|Tags!STRING:0|Armadillo!STRING:0|Last Activated!STRING:0|Version!STRING:0|KeyRing!HEX:16|Product!STRING:0\n");
        if (WriteInactiveBuildInfoRow)
            info.Append($"us|0|{new string('0', 32)}|{cdnKey}|||||||||0.0.0||other\n");
        // Branch, Active, Build Key, CDN Key, Install Key, IM Size, CDN Path, CDN Hosts, CDN Servers, Tags, Armadillo, Last Activated, Version, KeyRing, Product
        info.AppendJoin('|', "us", "1", buildKey, cdnKey, "", "", "tpr/test", "cdn.example.invalid", "http://cdn.example.invalid/", "Windows x86_64 US? enUS speech?:enUS text?", "", "", Version, "", Product).Append('\n');
        File.WriteAllText(Path.Combine(installDirectory, ".build.info"), info.ToString().Replace("\n", "\r\n"));

        return new WrittenStorage
        {
            InstallDirectory = installDirectory,
            DataDirectory = dataDir,
            BuildConfigPath = Path.Combine(configDir, buildKey[..2], buildKey[2..4], buildKey),
            Records = records,
            IndexFiles = indexFiles,
            RootCKey = root.CKey,
        };
    }

    private sealed record ConfigFileRefSpec(byte[] CKey, byte[] EKey);

    private static string Hex(byte[] b) => Convert.ToHexStringLower(b);

    private static string WriteConfig(string configDir, string text)
    {
        var key = Hex(MD5.HashData(Encoding.UTF8.GetBytes(text)));
        var dir = Path.Combine(configDir, key[..2], key[2..4]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, key), text);
        return key;
    }

    // Record header, CascLib BLTE_ENCODED_HEADER: ekey[16] byte-reversed, u32 LE size (header + BLTE),
    // u8 flags, u8 0, u32 LE jenkins(first 0x16 bytes, 0x3D6BE971), u32 LE checksum (VerifyHeaderSpan).
    private static readonly uint[] OffsetTable =
    [
        0x049396B8, 0x72A82A9B, 0xEE626CCA, 0x9917754F, 0x15DE40B1, 0xF5A8A9B6, 0x421EAC7E, 0xA9D55C9A,
        0x317FD40C, 0x04FAF80D, 0x3D6BE971, 0x52933CFD, 0x27F64B7D, 0xC6F5C11B, 0xD5757E3A, 0x6C388745,
    ];

    internal static byte[] RecordHeader(byte[] ekey, int blteLength, long recordOffset)
    {
        var h = new byte[CascStorage.RecordHeaderSize];
        for (var i = 0; i < 16; i++)
            h[i] = ekey[15 - i];
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x10), (uint)(CascStorage.RecordHeaderSize + blteLength));
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x16), Jenkins96.HashLittle(h.AsSpan(0, 0x16), 0x3D6BE971));

        var encodedOffset = (uint)(recordOffset + CascStorage.RecordHeaderSize);
        encodedOffset = OffsetTable[encodedOffset & 0x0F] ^ encodedOffset;
        Span<byte> offsetBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(offsetBytes, encodedOffset);
        Span<byte> hashed = stackalloc byte[4];
        int p;
        for (p = 0; p < 0x1A; p++)
            hashed[p & 3] ^= h[p];
        for (var j = 0; j < 4; j++, p++)
            h[0x1A + j] = (byte)(hashed[p & 3] ^ offsetBytes[p & 3]);
        return h;
    }

    private List<string> WriteIndex(string dataDir, List<(byte[] EKey, int Archive, long Offset, uint Size)> entries)
    {
        var written = new List<string>();
        foreach (var bucket in entries.GroupBy(e => LocalIndex.BucketOf(e.EKey)).OrderBy(g => g.Key))
        {
            var sorted = bucket.OrderBy(e => Convert.ToHexString(e.EKey, 0, 9), StringComparer.Ordinal).ToList();
            var block = new byte[sorted.Count * 18];
            for (var i = 0; i < sorted.Count; i++)
            {
                var e = block.AsSpan(i * 18, 18);
                sorted[i].EKey.AsSpan(0, 9).CopyTo(e);
                var storage = ((ulong)sorted[i].Archive << 30) | (ulong)sorted[i].Offset;
                for (var b = 0; b < 5; b++)
                    e[9 + b] = (byte)(storage >> (8 * (4 - b)));
                BinaryPrimitives.WriteUInt32LittleEndian(e[14..], sorted[i].Size);
            }
            var file = IndexFile(bucket.Key, block);
            var path = Path.Combine(dataDir, $"{bucket.Key:x2}{2:x8}.idx");
            File.WriteAllBytes(path, file);
            written.Add(path);

            if (WriteStaleIndexVersions)
            {
                // An older version that points every key at garbage: must not be used.
                var stale = (byte[])block.Clone();
                for (var i = 0; i < sorted.Count; i++)
                    BinaryPrimitives.WriteUInt32LittleEndian(stale.AsSpan(i * 18 + 14), 0xFFFFFF);
                File.WriteAllBytes(Path.Combine(dataDir, $"{bucket.Key:x2}{1:x8}.idx"), IndexFile(bucket.Key, stale));
            }
        }
        return written;
    }

    /// <summary>A version-7 <c>.idx</c> file for one bucket around an 18-byte-entry block.</summary>
    internal static byte[] IndexFile(int bucket, byte[] block, byte segmentBits = 30)
    {
        var file = new byte[0x28 + block.Length];
        var header = file.AsSpan(8, 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header, 7);
        header[2] = (byte)bucket;
        header[3] = 0;
        header[4] = 4;  // span size bytes
        header[5] = 5;  // span offset bytes
        header[6] = 9;  // key bytes
        header[7] = segmentBits;
        BinaryPrimitives.WriteUInt64LittleEndian(header[8..], 1UL << segmentBits);
        BinaryPrimitives.WriteUInt32LittleEndian(file, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), Jenkins96.HashLittle(header, 0));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x20), (uint)block.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x24), LocalIndex.EntryBlockHash(block));
        block.CopyTo(file, 0x28);
        return file;
    }
}

/// <summary>Test-side ENCODING writer: 4 KB CKey and EKey pages, each listed with its first key and MD5.</summary>
public static class EncodingWriter
{
    public const int PageSize = 4096;

    public static byte[] Write(IEnumerable<(byte[] CKey, byte[] EKey, long ContentSize, string ESpec, long EncodedSize)> files)
    {
        var list = files.ToList();
        var especs = list.Select(f => f.ESpec).Distinct().ToList();
        var especBlock = Encoding.ASCII.GetBytes(string.Concat(especs.Select(s => s + "\0")));

        var ckeyEntries = list
            .GroupBy(f => Convert.ToHexString(f.CKey))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                var ekeys = g.Select(f => f.EKey).ToList();
                var e = new byte[6 + 16 + 16 * ekeys.Count];
                e[0] = (byte)ekeys.Count;
                WriteUInt40(e.AsSpan(1), (ulong)first.ContentSize);
                first.CKey.CopyTo(e, 6);
                for (var i = 0; i < ekeys.Count; i++)
                    ekeys[i].CopyTo(e, 22 + 16 * i);
                return (Key: first.CKey, Entry: e);
            })
            .ToList();

        var ekeyEntries = list
            .OrderBy(f => Convert.ToHexString(f.EKey), StringComparer.Ordinal)
            .Select(f =>
            {
                var e = new byte[16 + 4 + 5];
                f.EKey.CopyTo(e, 0);
                BinaryPrimitives.WriteUInt32BigEndian(e.AsSpan(16), (uint)especs.IndexOf(f.ESpec));
                WriteUInt40(e.AsSpan(20), (ulong)f.EncodedSize);
                return (Key: f.EKey, Entry: e);
            })
            .ToList();

        var (ckeyToc, ckeyPages) = Pages(ckeyEntries);
        var (ekeyToc, ekeyPages) = Pages(ekeyEntries);

        var header = new byte[22];
        header[0] = (byte)'E';
        header[1] = (byte)'N';
        header[2] = 1;
        header[3] = 16;
        header[4] = 16;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(5), PageSize / 1024);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(7), PageSize / 1024);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(9), (uint)(ckeyPages.Length / PageSize));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(13), (uint)(ekeyPages.Length / PageSize));
        header[17] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(18), (uint)especBlock.Length);

        return [.. header, .. especBlock, .. ckeyToc, .. ckeyPages, .. ekeyToc, .. ekeyPages];
    }

    private static (byte[] Toc, byte[] Pages) Pages(List<(byte[] Key, byte[] Entry)> entries)
    {
        var toc = new MemoryStream();
        var pages = new MemoryStream();
        var i = 0;
        while (i < entries.Count)
        {
            var page = new byte[PageSize];
            var pos = 0;
            var firstKey = entries[i].Key;
            while (i < entries.Count && pos + entries[i].Entry.Length <= PageSize)
            {
                entries[i].Entry.CopyTo(page, pos);
                pos += entries[i].Entry.Length;
                i++;
            }
            toc.Write(firstKey);
            toc.Write(MD5.HashData(page));
            pages.Write(page);
        }
        return (toc.ToArray(), pages.ToArray());
    }

    private static void WriteUInt40(Span<byte> b, ulong v)
    {
        for (var i = 0; i < 5; i++)
            b[i] = (byte)(v >> (8 * (4 - i)));
    }
}

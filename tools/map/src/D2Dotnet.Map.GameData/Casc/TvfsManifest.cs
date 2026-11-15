using System.Buffers.Binary;
using System.Text;

namespace D2Dotnet.Map.GameData.Casc;

/// <summary>One span of a TVFS file: a byte range of the encoded file named by <see cref="EKey"/>.</summary>
internal sealed record TvfsSpan(byte[] EKey, uint FileOffset, uint ContentSize, uint EncodedSize);

/// <summary>A file in a TVFS manifest. Its content is the concatenation of its spans.</summary>
internal sealed record TvfsFile(string Path, IReadOnlyList<TvfsSpan> Spans)
{
    public long ContentSize => Spans.Sum(s => (long)s.ContentSize);
}

/// <summary>
/// Parser for TVFS ("TACT virtual file system"), the root manifest of D2R and other recent TACT products.
/// </summary>
/// <remarks>
/// Ported from CascLib's <c>CascRootFile_TVFS.cpp</c>, cross-checked with TACTLib's <c>VFSManifestReader</c>.
/// Header, all big-endian after the signature:
/// <code>
/// char[4] "TVFS"  u8 formatVersion (1)  u8 headerSize (0x26, 0x2E with EST)  u8 ekeySize (9)  u8 patchKeySize (9)
/// u32 flags (1 include CKey, 2 write support, 4 patch support, 8 lowercase manifest)
/// u32 pathTableOffset  u32 pathTableSize  u32 vfsTableOffset  u32 vfsTableSize
/// u32 cftTableOffset   u32 cftTableSize   u16 maxDepth  [u32 estTableOffset  u32 estTableSize]
/// </code>
/// Path table: a prefix tree of name fragments. Each node is
/// <c>[0x00 = separator before] [u8 length, name] [0x00 = separator after] [0xFF, u32 nodeValue]</c>;
/// a value with the top bit set is a folder whose children follow (the low 31 bits are their size
/// including the value itself); otherwise it is an offset into the VFS table. The whole table is
/// usually wrapped in a root folder <c>0xFF u32</c>.
/// VFS table entry: <c>u8 spanCount (1..224)</c>, then per span <c>u32 fileOffset, u32 contentSize,
/// cftOffset</c> (1-4 bytes, sized by the CFT table size). CFT entry: <c>ekey[ekeySize], u32 encodedSize, ...</c>.
/// A file whose EKey is one of the build config's <c>vfs-N</c> manifests is a mounted sub-directory;
/// that is resolved by the caller (<see cref="CascStorage"/>), which names its files <c>mount:path</c>.
/// </remarks>
internal sealed class TvfsManifest
{
    public const uint Magic = 0x54564653; // "TVFS" read big-endian

    private const uint FolderNode = 0x80000000;
    private const uint FolderSizeMask = 0x7FFFFFFF;

    private TvfsManifest(byte ekeySize, uint flags, IReadOnlyList<TvfsFile> files)
    {
        EKeySize = ekeySize;
        Flags = flags;
        Files = files;
    }

    public byte EKeySize { get; }

    public uint Flags { get; }

    /// <summary>All files, in path table order, with their full paths inside this manifest.</summary>
    public IReadOnlyList<TvfsFile> Files { get; }

    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(data) == Magic;

    public static TvfsManifest Parse(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data) || data.Length < 8)
            throw new CascFormatException("Missing TVFS signature.");
        var version = data[4];
        var headerSize = data[5];
        var ekeySize = data[6];
        var patchKeySize = data[7];
        if (version != 1)
            throw new CascFormatException($"Unsupported TVFS format version {version}.");
        if (ekeySize is not (9 or 16) || patchKeySize != ekeySize)
            throw new CascFormatException($"Unsupported TVFS key sizes {ekeySize}/{patchKeySize}.");
        if (headerSize < 0x26 || headerSize > data.Length)
            throw new CascFormatException($"Bad TVFS header size {headerSize}.");

        var h = data[..headerSize];
        var flags = BinaryPrimitives.ReadUInt32BigEndian(h[8..]);
        var pathOffset = BinaryPrimitives.ReadUInt32BigEndian(h[12..]);
        var pathSize = BinaryPrimitives.ReadUInt32BigEndian(h[16..]);
        var vfsOffset = BinaryPrimitives.ReadUInt32BigEndian(h[20..]);
        var vfsSize = BinaryPrimitives.ReadUInt32BigEndian(h[24..]);
        var cftOffset = BinaryPrimitives.ReadUInt32BigEndian(h[28..]);
        var cftSize = BinaryPrimitives.ReadUInt32BigEndian(h[32..]);

        var pathTable = Table(data, pathOffset, pathSize, "path");
        var reader = new Reader(
            Table(data, vfsOffset, vfsSize, "VFS").ToArray(),
            Table(data, cftOffset, cftSize, "container file").ToArray(),
            ekeySize,
            OffsetFieldSize(cftSize));

        var files = new List<TvfsFile>();
        var p = 0;
        var end = pathTable.Length;
        // Usually the whole table is a root folder: 0xFF, u32 (0x80000000 | size including the u32).
        if (pathTable.Length >= 5 && pathTable[0] == 0xFF)
        {
            var root = BinaryPrimitives.ReadUInt32BigEndian(pathTable[1..]);
            if ((root & FolderNode) == 0 || 1 + (root & FolderSizeMask) > pathTable.Length)
                throw new CascFormatException("TVFS root folder node is malformed.");
            end = 1 + (int)(root & FolderSizeMask);
            p = 5;
        }
        ParseFolder(pathTable, p, end, new StringBuilder(), reader, files);
        return new TvfsManifest(ekeySize, flags, files);
    }

    /// <summary>Size of the CFT offset field in a VFS span: 1 byte up to 0xFF, 2 up to 0xFFFF, 3 up to 0xFFFFFF, else 4.</summary>
    public static int OffsetFieldSize(uint tableSize) => tableSize switch
    {
        > 0xFFFFFF => 4,
        > 0xFFFF => 3,
        > 0xFF => 2,
        _ => 1,
    };

    private static ReadOnlySpan<byte> Table(ReadOnlySpan<byte> data, uint offset, uint size, string name)
    {
        if ((long)offset + size > data.Length)
            throw new CascFormatException($"TVFS {name} table ({offset}+{size}) runs past the end of the manifest ({data.Length} bytes).");
        return data.Slice((int)offset, (int)size);
    }

    private static void ParseFolder(ReadOnlySpan<byte> table, int pos, int end, StringBuilder path, Reader reader, List<TvfsFile> files)
    {
        var saved = path.Length;
        while (pos < end)
        {
            // [0x00] -> separator before the name
            if (table[pos] == 0)
            {
                path.Append('/');
                pos++;
            }

            // [length, name] unless the value marker follows
            if (pos < end && table[pos] != 0xFF)
            {
                int length = table[pos++];
                if (pos + length > end)
                    throw new CascFormatException("TVFS path table entry runs past its folder.");
                path.Append(Encoding.UTF8.GetString(table.Slice(pos, length)));
                pos += length;
            }

            // [0x00] -> separator after the name
            var separatorAfter = false;
            if (pos < end && table[pos] == 0)
            {
                separatorAfter = true;
                pos++;
            }

            if (pos < end && table[pos] != 0xFF)
            {
                // A fragment without a value: CascLib treats it as followed by a separator,
                // and the next entry continues the same path.
                path.Append('/');
                continue;
            }
            if (separatorAfter)
                path.Append('/');
            if (pos >= end)
                break;

            if (pos + 5 > end)
                throw new CascFormatException("TVFS path table node value is truncated.");
            var value = BinaryPrimitives.ReadUInt32BigEndian(table[(pos + 1)..]);
            pos += 5;

            if ((value & FolderNode) != 0)
            {
                var size = (int)(value & FolderSizeMask);
                if (size < 4 || pos - 4 + size > end)
                    throw new CascFormatException("TVFS folder node size runs past its parent.");
                var folderEnd = pos - 4 + size;
                ParseFolder(table, pos, folderEnd, path, reader, files);
                pos = folderEnd;
            }
            else if (reader.ReadSpans(value) is { } spans)
            {
                files.Add(new TvfsFile(path.ToString(), spans));
            }

            path.Length = saved;
        }
        path.Length = saved;
    }

    private sealed class Reader(byte[] vfs, byte[] cft, int ekeySize, int cftOffsetSize)
    {
        /// <summary>Spans of the VFS entry at <paramref name="offset"/>, or null for deleted/other entries (span count 0 or above 224).</summary>
        public TvfsSpan[]? ReadSpans(uint offset)
        {
            if (offset >= vfs.Length)
                throw new CascFormatException($"TVFS VFS offset {offset} is outside the VFS table.");
            int count = vfs[offset];
            if (count is 0 or > 224)
                return null; // 225-254 = "other" file, 255 = deleted file
            var spanLength = 8 + cftOffsetSize;
            var pos = (int)offset + 1;
            if (pos + (long)count * spanLength > vfs.Length)
                throw new CascFormatException($"TVFS VFS entry at {offset} runs past the VFS table.");

            var spans = new TvfsSpan[count];
            for (var i = 0; i < count; i++, pos += spanLength)
            {
                var span = vfs.AsSpan(pos, spanLength);
                var fileOffset = BinaryPrimitives.ReadUInt32BigEndian(span);
                var contentSize = BinaryPrimitives.ReadUInt32BigEndian(span[4..]);
                var cftPos = BigEndian.ReadUIntN(span[8..], cftOffsetSize);
                if (cftPos + (long)ekeySize + 4 > cft.Length)
                    throw new CascFormatException($"TVFS container file table offset {cftPos} is out of range.");
                var ekey = cft.AsSpan((int)cftPos, ekeySize).ToArray();
                var encodedSize = BinaryPrimitives.ReadUInt32BigEndian(cft.AsSpan((int)cftPos + ekeySize));
                spans[i] = new TvfsSpan(ekey, fileOffset, contentSize, encodedSize);
            }
            return spans;
        }
    }
}

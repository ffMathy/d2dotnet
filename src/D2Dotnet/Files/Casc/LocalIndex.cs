using System.Buffers.Binary;
using System.Globalization;

namespace D2Dotnet.Files.Casc;

/// <summary>Where a file's encoded bytes live: <c>data.{Archive:D3}</c>, at <see cref="Offset"/>, <see cref="Size"/> bytes long.</summary>
/// <remarks><see cref="Size"/> includes the 30-byte record header in front of the BLTE data.</remarks>
internal readonly record struct IndexEntry(int Archive, long Offset, uint Size);

/// <summary>
/// The local index: the 16 bucket files <c>Data/data/{bucket:x2}{version:x8}.idx</c>, mapping
/// 9-byte EKeys to positions in the <c>data.###</c> archives.
/// </summary>
/// <remarks>
/// Version-7 index layout, following CascLib (<c>CaptureIndexHeader_V2</c>, <c>LoadIndexFile_V2</c>):
/// <code>
/// 0x00 u32 LE headerBlockSize (16)    u32 LE hashlittle(header, 16, 0)
/// 0x08 u16 LE revision (7)  u8 bucket  u8 flags (0)
///      u8 spanSizeBytes (4)  u8 spanOffsetBytes (5)  u8 keyBytes (9)  u8 segmentBits (usually 30)
///      u64 LE maxFileOffset
/// 0x18 8 bytes padding
/// 0x20 u32 LE entriesSize            u32 LE hash: hashlittle2 chained over each entry (primary result)
/// 0x28 entries { u8 ekey[9]; u40 BE (archive &lt;&lt; segmentBits | offset); u32 LE encodedSize }
/// </code>
/// Only the newest version of each bucket is read. A key's bucket is
/// <c>x ^ (x &gt;&gt; 4) &amp; 0xF</c> where x is the XOR of its 9 bytes.
/// </remarks>
internal sealed class LocalIndex
{
    private readonly Dictionary<EKey9, IndexEntry> _entries;

    private LocalIndex(Dictionary<EKey9, IndexEntry> entries) => _entries = entries;

    public int Count => _entries.Count;

    public bool TryGet(EKey9 key, out IndexEntry entry) => _entries.TryGetValue(key, out entry);

    public bool TryGet(ReadOnlySpan<byte> ekey, out IndexEntry entry) => _entries.TryGetValue(EKey9.From(ekey), out entry);

    /// <summary>The bucket (0..15) an EKey belongs to.</summary>
    public static int BucketOf(ReadOnlySpan<byte> ekey)
    {
        byte x = 0;
        for (var i = 0; i < EKey9.Length; i++)
            x ^= ekey[i];
        return (x ^ (x >> 4)) & 0x0F;
    }

    /// <summary>Load the newest <c>.idx</c> of each bucket in <paramref name="dataDirectory"/>.</summary>
    public static LocalIndex Load(string dataDirectory)
    {
        var newest = new Dictionary<int, (uint Version, string Path)>();
        foreach (var path in Directory.EnumerateFiles(dataDirectory, "*.idx"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length != 10
                || !int.TryParse(name.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bucket)
                || !uint.TryParse(name.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var version)
                || bucket > 0x0F)
                continue;
            if (!newest.TryGetValue(bucket, out var current) || version > current.Version)
                newest[bucket] = (version, path);
        }
        if (newest.Count == 0)
            throw new CascFormatException($"No CASC index files (*.idx) found in '{dataDirectory}'.");

        var entries = new Dictionary<EKey9, IndexEntry>();
        foreach (var (bucket, (_, path)) in newest.OrderBy(kv => kv.Key))
            Parse(File.ReadAllBytes(path), bucket, entries, Path.GetFileName(path));
        return new LocalIndex(entries);
    }

    /// <summary>Parse one index file into <paramref name="into"/>. The first entry for a key wins.</summary>
    public static void Parse(ReadOnlySpan<byte> file, int expectedBucket, Dictionary<EKey9, IndexEntry> into, string name = "index")
    {
        if (file.Length < 0x28)
            throw new CascFormatException($"{name}: index file is truncated ({file.Length} bytes).");

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(file);
        var headerHash = BinaryPrimitives.ReadUInt32LittleEndian(file[4..]);
        if (headerSize < 16 || 8 + headerSize > file.Length)
            throw new CascFormatException($"{name}: bad index header size {headerSize}.");
        var header = file.Slice(8, (int)headerSize);
        if (Jenkins96.HashLittle(header, 0) != headerHash)
            throw new CascFormatException($"{name}: index header fails its checksum.");

        var revision = BinaryPrimitives.ReadUInt16LittleEndian(header);
        var bucket = header[2];
        var spanSizeBytes = header[4];
        var spanOffsetBytes = header[5];
        var keyBytes = header[6];
        var segmentBits = header[7];
        if (revision != 7)
            throw new CascFormatException($"{name}: unsupported index revision {revision} (only version 7 is supported).");
        if (bucket != expectedBucket)
            throw new CascFormatException($"{name}: header says bucket {bucket}, file name says {expectedBucket}.");
        if (spanSizeBytes != 4 || spanOffsetBytes != 5 || keyBytes != 9 || segmentBits is 0 or > 40)
            throw new CascFormatException($"{name}: unsupported index entry layout {keyBytes}/{spanOffsetBytes}/{spanSizeBytes}/{segmentBits}.");

        // The entry block follows the header, aligned up to 16 bytes (0x20 for the standard 16-byte header).
        var blockStart = (int)((8 + headerSize + 15) & ~15u);
        if (blockStart + 8 > file.Length)
            throw new CascFormatException($"{name}: index entry block is missing.");
        var blockSize = BinaryPrimitives.ReadUInt32LittleEndian(file[blockStart..]);
        var blockHash = BinaryPrimitives.ReadUInt32LittleEndian(file[(blockStart + 4)..]);
        const int entryLength = 9 + 5 + 4;
        if (blockStart + 8L + blockSize > file.Length || blockSize % entryLength != 0)
            throw new CascFormatException($"{name}: index entry block of {blockSize} bytes does not fit the file.");
        var block = file.Slice(blockStart + 8, (int)blockSize);

        if (EntryBlockHash(block) != blockHash && BlizzGetEntryBlockHash(block) != blockHash)
            throw new CascFormatException($"{name}: index entries fail their checksum.");

        var offsetMask = (1UL << segmentBits) - 1;
        for (var pos = 0; pos < block.Length; pos += entryLength)
        {
            var e = block.Slice(pos, entryLength);
            var storage = BigEndian.ReadUInt40(e[9..]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(e[14..]);
            into.TryAdd(EKey9.From(e), new IndexEntry((int)(storage >> segmentBits), (long)(storage & offsetMask), size));
        }
    }

    /// <summary>The entry-block checksum the Blizzard agent writes: hashlittle2 chained over each 18-byte entry.</summary>
    public static uint EntryBlockHash(ReadOnlySpan<byte> block)
    {
        uint pc = 0, pb = 0;
        for (var pos = 0; pos + 18 <= block.Length; pos += 18)
            Jenkins96.HashLittle2(block.Slice(pos, 18), ref pc, ref pb);
        return pc;
    }

    // Storages written by the BlizzGet tool chain hashlittle instead (CascLib's CaptureGuardedBlock2 accepts both).
    private static uint BlizzGetEntryBlockHash(ReadOnlySpan<byte> block)
    {
        uint h = 0;
        for (var pos = 0; pos + 18 <= block.Length; pos += 18)
            h = Jenkins96.HashLittle(block.Slice(pos, 18), h);
        return h;
    }
}

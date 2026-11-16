using D2Dotnet.Files;
using D2Dotnet.Files.Casc;
using D2Dotnet.Tests.Files.Writer;

namespace D2Dotnet.Tests.Files;

public class LocalIndexTests
{
    // Two hand-made 18-byte entries: ekey[9], u40 BE (archive << 30 | offset), u32 LE size.
    private static readonly byte[] Entries =
    [
        // ekey 01..09, archive 2, offset 0x1234 -> (2 << 30) | 0x1234 = 0x80001234, size 0x100
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09,
        0x00, 0x80, 0x00, 0x12, 0x34,
        0x00, 0x01, 0x00, 0x00,
        // ekey aa.., archive 0x3FF (the highest 10-bit value), offset 0x3FFFFFFF, size 0x12345678
        0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x11, 0x22,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0x78, 0x56, 0x34, 0x12,
    ];

    private static byte[] HandMadeIndex(int bucket, byte[] entries)
    {
        // 0x00: header block size 16, hashlittle(header, 0); 0x08: 16-byte v7 header; 0x18: padding;
        // 0x20: entry block size, hashlittle2 over entries; 0x28: entries.
        var header = new byte[] { 0x07, 0x00, (byte)bucket, 0x00, 0x04, 0x05, 0x09, 0x1E, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00 };
        var headerHash = Jenkins96.HashLittle(header, 0);
        var blockHash = LocalIndex.EntryBlockHash(entries);
        return
        [
            0x10, 0x00, 0x00, 0x00, .. BitConverter.GetBytes(headerHash),
            .. header,
            0, 0, 0, 0, 0, 0, 0, 0,
            .. BitConverter.GetBytes(entries.Length), .. BitConverter.GetBytes(blockHash),
            .. entries,
        ];
    }

    [Fact]
    public void ParsesHandMadeEntries()
    {
        var into = new Dictionary<EKey9, IndexEntry>();
        LocalIndex.Parse(HandMadeIndex(3, Entries), 3, into);

        Assert.Equal(2, into.Count);
        Assert.Equal(new IndexEntry(2, 0x1234, 0x100), into[EKey9.From([1, 2, 3, 4, 5, 6, 7, 8, 9])]);
        Assert.Equal(new IndexEntry(0x3FF, 0x3FFFFFFF, 0x12345678), into[EKey9.From([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x11, 0x22])]);
    }

    [Fact]
    public void LookupUsesOnlyTheFirstNineBytes()
    {
        var into = new Dictionary<EKey9, IndexEntry>();
        LocalIndex.Parse(HandMadeIndex(3, Entries), 3, into);
        byte[] fullKey = [1, 2, 3, 4, 5, 6, 7, 8, 9, 0xDE, 0xAD, 0xBE, 0xEF, 0, 0, 0];
        Assert.True(into.ContainsKey(EKey9.From(fullKey)));
    }

    [Fact]
    public void WriterProducesTheSameBytes()
    {
        Assert.Equal(HandMadeIndex(3, Entries), CascStorageWriter.IndexFile(3, Entries));
    }

    [Theory]
    [InlineData(0x0C)]  // header byte -> header checksum
    [InlineData(0x30)]  // entry byte -> entry checksum
    public void ChecksumMismatch_Throws(int offset)
    {
        var file = HandMadeIndex(3, Entries);
        file[offset] ^= 0x40;
        Assert.Throws<CascFormatException>(() => LocalIndex.Parse(file, 3, new Dictionary<EKey9, IndexEntry>()));
    }

    [Fact]
    public void WrongBucket_Throws()
    {
        Assert.Throws<CascFormatException>(() => LocalIndex.Parse(HandMadeIndex(3, Entries), 4, new Dictionary<EKey9, IndexEntry>()));
    }

    [Fact]
    public void Truncated_Throws()
    {
        var file = HandMadeIndex(3, Entries);
        Assert.Throws<CascFormatException>(() => LocalIndex.Parse(file.AsSpan(0, file.Length - 5), 3, new Dictionary<EKey9, IndexEntry>()));
        Assert.Throws<CascFormatException>(() => LocalIndex.Parse(file.AsSpan(0, 0x10), 3, new Dictionary<EKey9, IndexEntry>()));
    }

    [Fact]
    public void Bucket_IsXorFoldedToFourBits()
    {
        // 01^02^...^09 = 0x01; 0x01 ^ 0x00 = 1
        Assert.Equal(1, LocalIndex.BucketOf([1, 2, 3, 4, 5, 6, 7, 8, 9]));
        // aa^bb^cc^dd^ee^ff^00^11^22 = 0x22 -> 0x22 ^ 0x02 = 0x20 -> 0
        Assert.Equal(0, LocalIndex.BucketOf([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x11, 0x22]));
        Assert.Equal(0xF, LocalIndex.BucketOf([0x0F, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void Load_PicksTheNewestVersionOfEachBucket()
    {
        using var dir = new TempDirectory();
        var stale = (byte[])Entries.Clone();
        stale[14] = 0x99; // different size for the first key
        File.WriteAllBytes(dir.Combine("0300000001.idx"), HandMadeIndex(3, stale));
        File.WriteAllBytes(dir.Combine("030000000a.idx"), HandMadeIndex(3, Entries));
        File.WriteAllBytes(dir.Combine("not-an-index.idx"), [1, 2, 3]);

        var index = LocalIndex.Load(dir.Path);
        Assert.True(index.TryGet([1, 2, 3, 4, 5, 6, 7, 8, 9], out var entry));
        Assert.Equal(0x100u, entry.Size);
    }

    [Fact]
    public void Load_EmptyFolder_Throws()
    {
        using var dir = new TempDirectory();
        Assert.Throws<CascFormatException>(() => LocalIndex.Load(dir.Path));
    }
}

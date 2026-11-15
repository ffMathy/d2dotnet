using System.Security.Cryptography;
using D2Dotnet.Map.GameData.Casc;
using D2Dotnet.Map.GameData.Tests.Writer;

namespace D2Dotnet.Map.GameData.Tests;

public class EncodingTableTests
{
    private static byte[] Key(byte fill, byte last = 0)
    {
        var k = Enumerable.Repeat(fill, 16).ToArray();
        k[15] = last;
        return k;
    }

    /// <summary>A hand-made ENCODING with one 1 KB CKey page and no EKey pages.</summary>
    private static byte[] HandMade()
    {
        var page = new byte[1024];
        byte[] entries =
        [
            // 1 EKey, content size 0x0102030405 (40-bit), ckey 11.., ekey 22..
            0x01, 0x01, 0x02, 0x03, 0x04, 0x05, .. Key(0x11), .. Key(0x22),
            // 2 EKeys, content size 7, ckey 33.., ekeys 44.. and 55..
            0x02, 0x00, 0x00, 0x00, 0x00, 0x07, .. Key(0x33), .. Key(0x44), .. Key(0x55),
        ];
        entries.CopyTo(page, 0);
        byte[] especs = [.. "z\0b:{1K*=z}\0"u8];
        return
        [
            (byte)'E', (byte)'N', 1, 16, 16,
            0x00, 0x01,              // CKey page size: 1 KB
            0x00, 0x01,              // EKey page size: 1 KB
            0x00, 0x00, 0x00, 0x01,  // 1 CKey page
            0x00, 0x00, 0x00, 0x00,  // 0 EKey pages
            0x00,
            0x00, 0x00, 0x00, (byte)especs.Length,
            .. especs,
            .. Key(0x11), .. MD5.HashData(page),
            .. page,
        ];
    }

    [Fact]
    public void ParsesHandMadeTable()
    {
        var table = EncodingTable.Parse(HandMade());

        Assert.Equal(2, table.Count);
        Assert.Equal(["z", "b:{1K*=z}"], table.ESpecs);

        Assert.True(table.TryGet(Key(0x11), out var first));
        Assert.Equal(0x0102030405UL, first.ContentSize);
        Assert.Equal(Key(0x22), Assert.Single(first.EKeys));

        Assert.True(table.TryGet(Key(0x33), out var second));
        Assert.Equal(7UL, second.ContentSize);
        Assert.Equal([Key(0x44), Key(0x55)], second.EKeys);

        Assert.False(table.TryGet(Key(0x22), out _));
    }

    [Fact]
    public void PageHashMismatch_Throws()
    {
        var data = HandMade();
        data[^1000] ^= 1; // inside the page padding
        Assert.Throws<CascFormatException>(() => EncodingTable.Parse(data));
    }

    [Fact]
    public void BadSignatureOrTruncation_Throws()
    {
        var data = HandMade();
        Assert.Throws<CascFormatException>(() => EncodingTable.Parse(data.AsSpan(0, data.Length - 1)));
        data[0] = (byte)'X';
        Assert.Throws<CascFormatException>(() => EncodingTable.Parse(data));
    }

    [Fact]
    public void ManyEntries_SpanSeveralPages()
    {
        var files = Enumerable.Range(0, 400).Select(i =>
        {
            var ckey = MD5.HashData(BitConverter.GetBytes(i));
            var ekey = MD5.HashData(BitConverter.GetBytes(i + 1_000_000));
            return (ckey, ekey, (long)i * 10, i % 2 == 0 ? "z" : "n", (long)i);
        }).ToList();
        var table = EncodingTable.Parse(EncodingWriter.Write(files));

        Assert.Equal(400, table.Count);
        foreach (var (ckey, ekey, size, _, _) in files)
        {
            Assert.True(table.TryGet(ckey, out var entry));
            Assert.Equal((ulong)size, entry.ContentSize);
            Assert.Equal(ekey, Assert.Single(entry.EKeys));
        }
    }
}

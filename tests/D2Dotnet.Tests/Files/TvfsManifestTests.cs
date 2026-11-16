using System.Buffers.Binary;
using System.Text;
using D2Dotnet.Files;
using D2Dotnet.Files.Casc;
using D2Dotnet.Tests.Files.Writer;

namespace D2Dotnet.Tests.Files;

public class TvfsManifestTests
{
    private static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        return b;
    }

    private static byte[] Name(string s) => [(byte)s.Length, .. Encoding.ASCII.GetBytes(s)];

    /// <summary>A node with a file value: name bytes, 0xFF, u32 VFS offset.</summary>
    private static byte[] FileNode(byte[] name, uint vfsOffset) => [.. name, 0xFF, .. U32(vfsOffset)];

    /// <summary>A folder node: name bytes, 0xFF, u32 (0x80000000 | size of children + 4), children.</summary>
    private static byte[] FolderNode(byte[] name, params byte[][] children)
    {
        var body = children.SelectMany(c => c).ToArray();
        return [.. name, 0xFF, .. U32(0x80000000u | (uint)(body.Length + 4)), .. body];
    }

    private static readonly byte[] KeyA = [0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11];
    private static readonly byte[] KeyB = [0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22];

    /// <summary>
    /// A hand-made manifest exercising: folders with a separator after the name, a split in the middle
    /// of a name, a separator before a name, a fragment without a value, a deleted entry and a
    /// two-span file.
    /// </summary>
    private static byte[] HandMade()
    {
        // VFS table. Span: u32 offset, u32 size, u8 CFT offset (the CFT is < 256 bytes).
        byte[] vfs =
        [
            /* 0  levels.txt   */ 0x01, .. U32(0), .. U32(10), 0x00,
            /* 10 lvlprest.txt */ 0x01, .. U32(0), .. U32(5), 0x0D,
            /* 20 deleted      */ 0xFF,
            /* 21 big.bin      */ 0x02, .. U32(0), .. U32(3), 0x00, .. U32(0), .. U32(4), 0x0D,
            /* 40 abc/def      */ 0x01, .. U32(0), .. U32(1), 0x00,
            /* 50 lib.txt      */ 0x01, .. U32(0), .. U32(2), 0x0D,
            /* 60 lib/a        */ 0x01, .. U32(0), .. U32(3), 0x00,
        ];
        // Container file table: ekey[9], u32 encoded size.
        byte[] cft = [.. KeyA, .. U32(0x20), .. KeyB, .. U32(0x30)];

        var paths = FolderNode([],
            FolderNode([.. Name("data"), 0x00],
                FileNode(Name("big.bin"), 21),
                FolderNode([.. Name("global"), 0x00],
                    FolderNode([.. Name("excel"), 0x00],
                        FolderNode(Name("l"),
                            FileNode(Name("evels.txt"), 0),
                            FileNode(Name("vlprest.txt"), 10)))),
                FileNode(Name("old.txt"), 20)),
            // "abc" has no value: CascLib appends a separator and lets the next node continue the path.
            Name("abc"),
            FileNode(Name("def"), 40),
            FolderNode(Name("lib"),
                FileNode(Name(".txt"), 50),
                FileNode([0x00, .. Name("a")], 60)));

        const int headerSize = 0x26;
        var pathOffset = (uint)headerSize;
        var vfsOffset = pathOffset + (uint)paths.Length;
        var cftOffset = vfsOffset + (uint)vfs.Length;
        byte[] header =
        [
            .. "TVFS"u8, 1, headerSize, 9, 9,
            .. U32(0),
            .. U32(pathOffset), .. U32((uint)paths.Length),
            .. U32(vfsOffset), .. U32((uint)vfs.Length),
            .. U32(cftOffset), .. U32((uint)cft.Length),
            0x00, 0x05,
        ];
        return [.. header, .. paths, .. vfs, .. cft];
    }

    [Fact]
    public void ParsesHandMadeManifest()
    {
        var manifest = TvfsManifest.Parse(HandMade());
        var files = manifest.Files.ToDictionary(f => f.Path);

        Assert.Equal(
            ["data/big.bin", "data/global/excel/levels.txt", "data/global/excel/lvlprest.txt", "abc/def", "lib.txt", "lib/a"],
            manifest.Files.Select(f => f.Path));

        var levels = Assert.Single(files["data/global/excel/levels.txt"].Spans);
        Assert.Equal(KeyA, levels.EKey);
        Assert.Equal(10u, levels.ContentSize);
        Assert.Equal(0x20u, levels.EncodedSize);

        var prest = Assert.Single(files["data/global/excel/lvlprest.txt"].Spans);
        Assert.Equal(KeyB, prest.EKey);
        Assert.Equal(0x30u, prest.EncodedSize);

        var big = files["data/big.bin"];
        Assert.Equal(2, big.Spans.Count);
        Assert.Equal(KeyA, big.Spans[0].EKey);
        Assert.Equal(KeyB, big.Spans[1].EKey);
        Assert.Equal(7, big.ContentSize);
    }

    [Fact]
    public void WriterOutputParsesBack()
    {
        var paths = new[]
        {
            "data/global/excel/levels.txt",
            "data/global/excel/lvlprest.txt",
            "data/global/excel/lvltypes.txt",
            "data/global/excel2/x.txt", // diverges right before a separator: a node with a separator before its name
            "data/global/tiles/act1/town/townn1.ds1",
            "data/global/tiles/act1/town/towne1.ds1",
            "data/global/tiles/act1/town/floor.dt1",
            "data/local/lng/strings/levels.json",
            "data/hd/global/" + new string('x', 300) + ".bin", // a name longer than one node can hold
            "readme",
        };
        var specs = paths.Select((p, i) => new TvfsFileSpec(p, [new TvfsSpanSpec(Enumerable.Repeat((byte)i, 16).ToArray(), 0, (uint)i, (uint)(100 + i))])).ToList();
        var bytes = TvfsWriter.Write(specs);
        var manifest = TvfsManifest.Parse(bytes);

        // The writer really split "excel/" + "l" + "evels.txt" / "vlprest.txt" / "vltypes.txt".
        Assert.Contains(Name("evels.txt"), Windows(bytes, 10));
        Assert.Contains((byte[])[0x00, .. Name("l")], Windows(bytes, 3));

        Assert.Equal(paths.Order(StringComparer.Ordinal), manifest.Files.Select(f => f.Path).Order(StringComparer.Ordinal));
        foreach (var (file, i) in paths.Select((p, i) => (manifest.Files.Single(f => f.Path == p), i)))
        {
            var span = Assert.Single(file.Spans);
            Assert.Equal((uint)i, span.ContentSize);
            Assert.Equal((uint)(100 + i), span.EncodedSize);
            Assert.Equal(Enumerable.Repeat((byte)i, 9), span.EKey);
        }
    }

    private static IEnumerable<byte[]> Windows(byte[] data, int size) =>
        Enumerable.Range(0, data.Length - size + 1).Select(i => data.AsSpan(i, size).ToArray());

    [Fact]
    public void LargeContainerTableUsesWiderOffsets()
    {
        // 30 distinct keys * 13 bytes > 0xFF: the CFT offset in VFS spans becomes 2 bytes.
        var specs = Enumerable.Range(0, 30)
            .Select(i => new TvfsFileSpec($"f/{i:D2}", [new TvfsSpanSpec(Enumerable.Repeat((byte)(i + 1), 9).ToArray(), 0, 1, 1)]))
            .ToList();
        var manifest = TvfsManifest.Parse(TvfsWriter.Write(specs));
        Assert.Equal(30, manifest.Files.Count);
        Assert.Equal(Enumerable.Repeat((byte)30, 9), manifest.Files.Single(f => f.Path == "f/29").Spans[0].EKey);
    }

    [Theory]
    [InlineData(0u, 1)]
    [InlineData(0xFFu, 1)]
    [InlineData(0x100u, 2)]
    [InlineData(0xFFFFu, 2)]
    [InlineData(0x10000u, 3)]
    [InlineData(0x1000000u, 4)]
    public void OffsetFieldSize(uint tableSize, int expected) => Assert.Equal(expected, TvfsManifest.OffsetFieldSize(tableSize));

    [Fact]
    public void BadHeader_Throws()
    {
        var data = HandMade();
        var badMagic = (byte[])data.Clone();
        badMagic[0] = (byte)'X';
        Assert.Throws<CascFormatException>(() => TvfsManifest.Parse(badMagic));

        var badVersion = (byte[])data.Clone();
        badVersion[4] = 2;
        Assert.Throws<CascFormatException>(() => TvfsManifest.Parse(badVersion));

        var badKeySize = (byte[])data.Clone();
        badKeySize[6] = 7;
        Assert.Throws<CascFormatException>(() => TvfsManifest.Parse(badKeySize));
    }

    [Fact]
    public void TablesOutOfRange_Throw()
    {
        var data = HandMade();
        Assert.Throws<CascFormatException>(() => TvfsManifest.Parse(data.AsSpan(0, data.Length - 1)));

        // A VFS offset that points outside the VFS table.
        var manifest = TvfsWriter.Write([new TvfsFileSpec("a", [new TvfsSpanSpec(new byte[9], 0, 1, 1)])]);
        var pathOffset = BinaryPrimitives.ReadUInt32BigEndian(manifest.AsSpan(12));
        // path table: FF u32(root) 01 'a' FF u32(vfs offset)
        BinaryPrimitives.WriteUInt32BigEndian(manifest.AsSpan((int)pathOffset + 1 + 4 + 2 + 1), 0x1000);
        Assert.Throws<CascFormatException>(() => TvfsManifest.Parse(manifest));
    }
}

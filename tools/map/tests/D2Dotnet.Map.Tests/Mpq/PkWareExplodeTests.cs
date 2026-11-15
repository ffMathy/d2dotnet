using D2Dotnet.Map.Mpq.Compression;

namespace D2Dotnet.Map.Tests.Mpq;

/// <summary>Port of the <c>implode-decoder@1.0.1</c> tests (fixtures MIT, (c) 2014 Travis Collins).</summary>
public class PkWareExplodeTests
{
    [Theory]
    [InlineData("small.imploded", "small.decomp")]
    [InlineData("medium.imploded", "medium.decomp")]
    [InlineData("large.imploded", "large.decomp")]
    [InlineData("large.imploded.ascii", "large.decomp")]
    [InlineData("binary.imploded", "binary.decomp")]
    public void DecodesFiles(string imploded, string expected)
    {
        var output = PkWareExplode.Decompress(Fixture.Bytes("implode", imploded));
        Assert.Equal(Fixture.Bytes("implode", expected), output);
    }

    [Fact]
    public void RejectsUnsupportedCompressionType()
    {
        var err = Assert.Throws<InvalidDataException>(() => PkWareExplode.Decompress([7, 4, 0, 0]));
        Assert.Equal("Unsupported compression type: 7", err.Message);
    }

    [Fact]
    public void RejectsUnsupportedDictionarySize()
    {
        var err = Assert.Throws<InvalidDataException>(() => PkWareExplode.Decompress([0, 9, 0, 0]));
        Assert.Equal("Unsupported dictionary size: 9", err.Message);
    }

    [Fact]
    public void TruncatedInputReturnsWhatWasDecoded()
    {
        var full = Fixture.Bytes("implode", "medium.imploded");
        var expected = Fixture.Bytes("implode", "medium.decomp");
        var partial = PkWareExplode.Decompress(full.AsSpan(0, full.Length / 2));
        Assert.True(partial.Length > 0 && partial.Length < expected.Length);
        Assert.Equal(expected.AsSpan(0, partial.Length).ToArray(), partial);
        Assert.Empty(PkWareExplode.Decompress([0, 4]));
    }

    [Fact]
    public void SectorWithUnknownCompressionIsNotSupported()
    {
        var err = Assert.Throws<NotSupportedException>(() => MpqCompression.DecompressSector([0x02, 0, 0], 0, 3));
        Assert.Equal("Mpq.Compression: Zlib:2 not supported", err.Message);
    }
}

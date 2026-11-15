using D2Dotnet.Diablo2.Binary;

namespace D2Dotnet.Diablo2.Binary.Tests;

public class BinaryCursorTests
{
    [Fact]
    public void ReadsLittleEndianIntegers()
    {
        var c = new BinaryCursor([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);
        Assert.Equal(0x01, c.U8());
        Assert.Equal(0x0302, c.Lu16());
        Assert.Equal(0x07060504u, c.Lu32());
        Assert.Equal(7, c.Offset);
        Assert.Throws<BinaryOverflowException>(() => c.U8());
    }

    [Fact]
    public void FixedStringStopsAtNullButConsumesFullLength()
    {
        var c = new BinaryCursor("ab\0cd!"u8.ToArray());
        Assert.Equal("ab", c.FixedString(5));
        Assert.Equal(5, c.Offset);
        Assert.Equal((byte)'!', c.U8());
    }

    [Fact]
    public void NullStringConsumesTerminator()
    {
        var c = new BinaryCursor("abc\0d"u8.ToArray());
        Assert.Equal("abc", c.NullString());
        Assert.Equal(4, c.Offset);
    }

    [Fact]
    public void BitStreamReadsLsbFirst()
    {
        // 0b1010_1100, 0b0000_0011
        var bs = new BitStream([0xAC, 0x03]);
        Assert.Equal(0u, bs.Bits(2));
        Assert.Equal(0b1011u, bs.Bits(4));
        Assert.Equal(0b1110u, bs.Bits(4));
        Assert.Equal(6, bs.RemainingBits);
    }

    [Fact]
    public void ToHexPads()
    {
        Assert.Equal("0x0a", Hex.ToHex(10));
        Assert.Equal("0x00ff", Hex.ToHex(255, 4));
    }
}

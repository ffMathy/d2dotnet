using System.Security.Cryptography;
using System.Text;
using D2Dotnet.Map.GameData.Casc;
using D2Dotnet.Map.GameData.Tests.Writer;

namespace D2Dotnet.Map.GameData.Tests;

public class BlteTests
{
    // zlib("hello") at the default level: 78 9c, deflate data, adler32 0x062c0215.
    private static readonly byte[] ZlibHello = [0x78, 0x9c, 0xcb, 0x48, 0xcd, 0xc9, 0xc9, 0x07, 0x00, 0x06, 0x2c, 0x02, 0x15];

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    [Fact]
    public void Headerless_N()
    {
        byte[] blte = [.. "BLTE"u8, 0, 0, 0, 0, (byte)'N', .. "hello"u8];
        Assert.Equal(Ascii("hello"), Blte.Decode(blte));
    }

    [Fact]
    public void Headerless_Z()
    {
        byte[] blte = [.. "BLTE"u8, 0, 0, 0, 0, (byte)'Z', .. ZlibHello];
        Assert.Equal(Ascii("hello"), Blte.Decode(blte));
    }

    [Fact]
    public void FrameTable_NAndZ()
    {
        // Two frames: N "hi " (3 bytes) and Z "hello" (5 bytes). Header size = 12 + 2 * 24 = 0x3C.
        byte[] f0 = [(byte)'N', .. "hi "u8];
        byte[] f1 = [(byte)'Z', .. ZlibHello];
        byte[] blte =
        [
            .. "BLTE"u8, 0x00, 0x00, 0x00, 0x3C,
            0x0F, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, (byte)f0.Length, 0x00, 0x00, 0x00, 0x03, .. MD5.HashData(f0),
            0x00, 0x00, 0x00, (byte)f1.Length, 0x00, 0x00, 0x00, 0x05, .. MD5.HashData(f1),
            .. f0, .. f1,
        ];
        Assert.Equal(Ascii("hi hello"), Blte.Decode(blte));
    }

    [Fact]
    public void ManyFrames_RoundTripThroughTheWriter()
    {
        var content = Enumerable.Range(0, 10_000).Select(i => (byte)(i * 31 % 251)).ToArray();
        var (blte, _) = BlteWriter.Encode(content, BlteLayout.Chunked(content.Length, 777, 'Z', 'N', 'Z'));
        Assert.Equal(content, Blte.Decode(blte));
    }

    [Fact]
    public void EmptyContent_Headerless()
    {
        var (blte, _) = BlteWriter.Encode([], BlteLayout.HeaderlessN);
        Assert.Empty(Blte.Decode(blte));
    }

    [Fact]
    public void FrameHashMismatch_Throws()
    {
        var (blte, _) = BlteWriter.Encode(Ascii("some content here"), BlteLayout.Single('N'));
        blte[^1] ^= 0xFF;
        var e = Assert.Throws<CascFormatException>(() => Blte.Decode(blte));
        Assert.Contains("MD5", e.Message);
        // Without verification the flipped byte comes through.
        Assert.Equal((byte)('e' ^ 0xFF), Blte.Decode(blte, verifyFrameHashes: false)[^1]);
    }

    [Fact]
    public void Truncated_Throws()
    {
        var (blte, _) = BlteWriter.Encode(Ascii("some content here"), BlteLayout.Chunked(17, 5, 'N'));
        Assert.Throws<CascFormatException>(() => Blte.Decode(blte.AsSpan(0, blte.Length - 1)));
        Assert.Throws<CascFormatException>(() => Blte.Decode(blte.AsSpan(0, 20)));
    }

    [Fact]
    public void BadSignatureOrHeader_Throws()
    {
        Assert.Throws<CascFormatException>(() => Blte.Decode("BLTX\0\0\0\0Nx"u8));
        // Header size that does not match the frame count.
        byte[] wrongSize = [.. "BLTE"u8, 0, 0, 0, 0x30, 0x0F, 0, 0, 1, .. new byte[40]];
        Assert.Throws<CascFormatException>(() => Blte.Decode(wrongSize));
        // Unknown table format byte.
        byte[] wrongFormat = [.. "BLTE"u8, 0, 0, 0, 0x24, 0x10, 0, 0, 1, .. new byte[30]];
        Assert.Throws<CascFormatException>(() => Blte.Decode(wrongFormat));
    }

    [Fact]
    public void CorruptZlib_Throws()
    {
        byte[] blte = [.. "BLTE"u8, 0, 0, 0, 0, (byte)'Z', 0x78, 0x9c, 0xff, 0xff, 0xff, 0xff];
        Assert.Throws<CascFormatException>(() => Blte.Decode(blte));
    }

    [Fact]
    public void FrameThatInflatesTooMuch_Throws()
    {
        // Frame table claims 3 content bytes, zlib holds 5.
        byte[] f0 = [(byte)'Z', .. ZlibHello];
        byte[] blte = [.. "BLTE"u8, 0, 0, 0, 0x24, 0x0F, 0, 0, 1, 0, 0, 0, (byte)f0.Length, 0, 0, 0, 3, .. MD5.HashData(f0), .. f0];
        Assert.Throws<CascFormatException>(() => Blte.Decode(blte));
    }

    [Fact]
    public void NFrameWithWrongSize_Throws()
    {
        byte[] f0 = [(byte)'N', .. "hello"u8];
        byte[] blte = [.. "BLTE"u8, 0, 0, 0, 0x24, 0x0F, 0, 0, 1, 0, 0, 0, (byte)f0.Length, 0, 0, 0, 4, .. MD5.HashData(f0), .. f0];
        Assert.Throws<CascFormatException>(() => Blte.Decode(blte));
    }

    [Fact]
    public void Encrypted_ReportsTheKeyName()
    {
        // 'E', key name size 8, key name (LE 0x0123456789ABCDEF), IV size 4, IV, cipher 'S', payload.
        byte[] blte = [.. "BLTE"u8, 0, 0, 0, 0, (byte)'E', 8, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01, 4, 1, 2, 3, 4, (byte)'S', 0xAA, 0xBB];
        var e = Assert.Throws<CascEncryptedException>(() => Blte.Decode(blte));
        Assert.Equal(0x0123456789ABCDEFUL, e.KeyName);
        Assert.Contains("0123456789ABCDEF", e.Message);
        Assert.Contains("'S'", e.Message);
    }

    [Theory]
    [InlineData('F')]
    [InlineData('4')]
    [InlineData('X')]
    public void UnsupportedModes_Throw(char mode)
    {
        byte[] blte = [.. "BLTE"u8, 0, 0, 0, 0, (byte)mode, 1, 2, 3];
        var e = Assert.Throws<CascFormatException>(() => Blte.Decode(blte));
        Assert.IsNotType<CascEncryptedException>(e);
    }
}

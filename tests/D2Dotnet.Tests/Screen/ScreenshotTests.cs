using System.Buffers.Binary;
using System.IO.Compression;
using D2Dotnet.Screen;

namespace D2Dotnet.Tests.Screen;

/// <summary>Screenshots as pixels and as PNG, on hand-made pictures.</summary>
public sealed class ScreenshotTests
{
    // A 3x2 picture: red, green, blue on top; white, black, grey below (BGRA).
    private static Screenshot Picture() => new(3, 2,
    [
        0, 0, 255, 0, /**/ 0, 255, 0, 0, /**/ 255, 0, 0, 0,
        255, 255, 255, 0, /**/ 0, 0, 0, 0, /**/ 128, 128, 128, 0,
    ]);

    [Fact]
    public void Pixels_AreRedGreenBlue_FromTheTopLeft()
    {
        var picture = Picture();
        Assert.Equal((255, 0, 0), picture[0, 0]);
        Assert.Equal((0, 0, 255), picture[2, 0]);
        Assert.Equal((128, 128, 128), picture[2, 1]);
        Assert.False(picture.IsBlank);
        Assert.True(new Screenshot(2, 2, new byte[16]).IsBlank);
        Assert.Throws<ArgumentOutOfRangeException>(() => picture[3, 0]);
        Assert.Throws<ArgumentException>(() => new Screenshot(3, 2, new byte[5]));
    }

    [Fact]
    public void Crop_KeepsThePart()
    {
        var part = Picture().Crop(new Rect(1, 0, 2, 2));
        Assert.Equal((2, 2), (part.Width, part.Height));
        Assert.Equal((0, 255, 0), part[0, 0]);
        Assert.Equal((128, 128, 128), part[1, 1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Picture().Crop(new Rect(2, 0, 2, 1)));
    }

    [Fact]
    public void WritePng_IsAValidPngOfThePixels()
    {
        using var png = new MemoryStream();
        Picture().WritePng(png);
        var bytes = png.ToArray();

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], bytes[..8]);
        var chunks = Chunks(bytes);
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(c => c.Type));

        var header = chunks[0].Data;
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
        Assert.Equal((8, 2), (header[8], header[9]));

        using var zlib = new ZLibStream(new MemoryStream(chunks[1].Data), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        Assert.Equal(new byte[]
        {
            0, 255, 0, 0, 0, 255, 0, 0, 0, 255,
            0, 255, 255, 255, 0, 0, 0, 128, 128, 128,
        }, raw.ToArray());
    }

    // The chunks of a PNG, each with its CRC checked.
    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        for (var at = 8; at < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png.AsSpan(at + 8, length).ToArray();
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.Equal(Crc(png.AsSpan(at + 4, 4 + length)), crc);
            chunks.Add((type, data));
            at += 12 + length;
        }

        return chunks;
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFF_FFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB8_8320 ^ (crc >> 1) : crc >> 1;
        }

        return crc ^ 0xFFFF_FFFF;
    }
}

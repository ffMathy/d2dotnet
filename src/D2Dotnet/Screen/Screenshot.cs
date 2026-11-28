using System.Buffers.Binary;
using System.IO.Compression;

namespace D2Dotnet.Screen;

/// <summary>A picture of the game window: <see cref="Width"/> by <see cref="Height"/> pixels, 4 bytes each (blue, green, red, unused), row by row from the top.</summary>
public sealed class Screenshot
{
    private readonly byte[] _pixels;

    /// <summary>A picture over BGRA pixels, row by row from the top.</summary>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> does not hold width × height × 4 bytes.</exception>
    public Screenshot(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != width * height * 4)
            throw new ArgumentException($"A {width}x{height} picture needs {width * height * 4} bytes, not {pixels.Length}.", nameof(pixels));
        (Width, Height, _pixels) = (width, height, pixels);
    }

    /// <summary>The width, in pixels.</summary>
    public int Width { get; }

    /// <summary>The height, in pixels.</summary>
    public int Height { get; }

    /// <summary>The colour of a pixel, (0, 0) being the top-left corner.</summary>
    public (byte Red, byte Green, byte Blue) this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException(x < 0 || x >= Width ? nameof(x) : nameof(y));
            var i = (y * Width + x) * 4;
            return (_pixels[i + 2], _pixels[i + 1], _pixels[i]);
        }
    }

    /// <summary>True when every pixel is black: what a capture of a minimized or unrendered window gives.</summary>
    public bool IsBlank
    {
        get
        {
            for (var i = 0; i < _pixels.Length; i += 4)
            {
                if (_pixels[i] != 0 || _pixels[i + 1] != 0 || _pixels[i + 2] != 0)
                    return false;
            }

            return true;
        }
    }

    /// <summary>A part of the picture.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The part is not inside the picture.</exception>
    public Screenshot Crop(Rect area)
    {
        if (area.X < 0 || area.Y < 0 || area.Width <= 0 || area.Height <= 0 || area.X + area.Width > Width || area.Y + area.Height > Height)
            throw new ArgumentOutOfRangeException(nameof(area), $"{area} is not inside the {Width}x{Height} picture.");
        var pixels = new byte[area.Width * area.Height * 4];
        for (var row = 0; row < area.Height; row++)
            Array.Copy(_pixels, ((area.Y + row) * Width + area.X) * 4, pixels, row * area.Width * 4, area.Width * 4);
        return new Screenshot(area.Width, area.Height, pixels);
    }

    /// <summary>Writes the picture as a PNG file (8-bit RGB).</summary>
    public void SavePng(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var file = File.Create(path);
        WritePng(file);
    }

    /// <summary>Writes the picture as PNG (8-bit RGB) to <paramref name="output"/>.</summary>
    public void WritePng(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], Height);
        header[8] = 8;  // bits per channel
        header[9] = 2;  // RGB
        WriteChunk(output, "IHDR", header);

        // Each row: filter type 0 (none), then RGB.
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + Width * 3];
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var i = (y * Width + x) * 4;
                    row[1 + x * 3] = _pixels[i + 2];
                    row[2 + x * 3] = _pixels[i + 1];
                    row[3 + x * 3] = _pixels[i];
                }

                zlib.Write(row);
            }
        }

        WriteChunk(output, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(output, "IEND", []);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        Span<byte> name = [(byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3]];
        output.Write(name);
        output.Write(data);
        var crc = Crc32.Update(Crc32.Update(0xFFFF_FFFF, name), data) ^ 0xFFFF_FFFF;
        Span<byte> check = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(check, crc);
        output.Write(check);
    }

    // The CRC-32 PNG chunks end with (polynomial 0xEDB88320, the zlib one).
    private static class Crc32
    {
        private static readonly uint[] Table = Build();

        private static uint[] Build()
        {
            var table = new uint[256];
            for (var n = 0u; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB8_8320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }

            return table;
        }

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}

using System.Text;

namespace D2Dotnet.Diablo2.Binary;

/// <summary>
/// Little-endian (LSB first) bit reader. Port of binparse's <c>BitStream</c>.
/// </summary>
public sealed class BitStream
{
    private readonly ReadOnlyMemory<byte> _buffer;

    /// <param name="buffer">Bytes to read.</param>
    /// <param name="byteOffset">Byte offset to start reading at.</param>
    /// <param name="maxByteOffset">Byte offset to stop reading at (defaults to end of buffer).</param>
    public BitStream(ReadOnlyMemory<byte> buffer, int byteOffset = 0, int? maxByteOffset = null)
    {
        _buffer = buffer;
        BitOffset = (long)byteOffset * 8;
        MaxBitOffset = (long)(maxByteOffset ?? buffer.Length) * 8;
    }

    public BitStream(byte[] buffer, int byteOffset = 0, int? maxByteOffset = null)
        : this(new ReadOnlyMemory<byte>(buffer), byteOffset, maxByteOffset) { }

    /// <summary>Current absolute position in bits.</summary>
    public long BitOffset { get; set; }

    /// <summary>Absolute limit in bits.</summary>
    public long MaxBitOffset { get; }

    /// <summary>Number of bits left in this stream.</summary>
    public long RemainingBits => MaxBitOffset - BitOffset;

    /// <summary>Current position in whole bytes (rounded down).</summary>
    public int ByteOffset => (int)(BitOffset / 8);

    /// <summary>Skip a number of bits.</summary>
    public void Skip(int bits) => BitOffset += bits;

    /// <summary>Read a single bit.</summary>
    public int Bit()
    {
        if (RemainingBits <= 0) throw new BinaryOverflowException($"BitStream: Overflow {RemainingBits}");
        var bytePos = (int)(BitOffset / 8);
        var bitPos = (int)(BitOffset % 8);
        BitOffset++;
        return (_buffer.Span[bytePos] >> bitPos) & 1;
    }

    /// <summary>Read a boolean from a single bit.</summary>
    public bool Bool() => Bit() == 1;

    /// <summary>Read up to 64 bits, least significant bit first.</summary>
    public ulong Bits64(int length)
    {
        if (length is < 0 or > 64) throw new ArgumentOutOfRangeException(nameof(length));
        ulong value = 0;
        for (var i = 0; i < length; i++)
        {
            if (Bit() == 1) value |= 1UL << i;
        }
        return value;
    }

    /// <summary>Read up to 32 bits, least significant bit first.</summary>
    public uint Bits(int length)
    {
        if (length is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(length));
        return (uint)Bits64(length);
    }

    /// <summary>Read bits as a signed int (value must fit in 31 bits to be non-negative).</summary>
    public int Int(int length) => (int)Bits(length);

    /// <summary>
    /// Read 8-bit characters up to <paramref name="length"/>; stops early (after consuming) at a null character.
    /// </summary>
    public string String(int? length = null)
    {
        var len = length ?? (int)(RemainingBits / 8);
        var sb = new StringBuilder(len);
        for (var i = 0; i < len; i++)
        {
            var chr = Bits(8);
            if (chr == 0) break;
            sb.Append((char)chr);
        }
        return sb.ToString();
    }
}

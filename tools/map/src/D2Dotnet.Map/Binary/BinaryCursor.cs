using System.Buffers.Binary;
using System.Text;

namespace D2Dotnet.Map.Binary;

/// <summary>
/// Sequential little-endian reader over a byte buffer.
/// This is the .NET replacement for the <c>binparse</c> npm package used upstream
/// (<c>bp.u8</c>, <c>bp.lu16</c>, <c>bp.lu32</c>, <c>bp.string(n)</c>, <c>bp.skip(n)</c>, ...).
/// </summary>
public sealed class BinaryCursor
{
    private readonly ReadOnlyMemory<byte> _buffer;

    public BinaryCursor(ReadOnlyMemory<byte> buffer, int offset = 0)
    {
        _buffer = buffer;
        Offset = offset;
        StartOffset = offset;
    }

    public BinaryCursor(byte[] buffer, int offset = 0) : this(new ReadOnlyMemory<byte>(buffer), offset) { }

    /// <summary>Underlying buffer.</summary>
    public ReadOnlyMemory<byte> Buffer => _buffer;

    /// <summary>Span over the underlying buffer.</summary>
    public ReadOnlySpan<byte> Span => _buffer.Span;

    /// <summary>Current read position (absolute, in bytes).</summary>
    public int Offset { get; set; }

    /// <summary>Offset the cursor started at (binparse <c>ctx.startOffset</c>).</summary>
    public int StartOffset { get; set; }

    /// <summary>Total length of the underlying buffer.</summary>
    public int Length => _buffer.Length;

    /// <summary>Bytes remaining from <see cref="Offset"/> to the end of the buffer.</summary>
    public int Remaining => _buffer.Length - Offset;

    /// <summary>Number of bytes read since <see cref="StartOffset"/>.</summary>
    public int Consumed => Offset - StartOffset;

    private ReadOnlySpan<byte> Take(int count, string type)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (Offset + count > _buffer.Length)
            throw new BinaryOverflowException($"BufferOverflow: {type} offset: {Hex.ToHex(Offset)} length: {count} size: {_buffer.Length}");
        var span = _buffer.Span.Slice(Offset, count);
        Offset += count;
        return span;
    }

    public byte U8() => Take(1, "u8")[0];
    public sbyte I8() => (sbyte)Take(1, "i8")[0];
    public ushort Lu16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2, "lu16"));
    public short Li16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2, "li16"));
    public uint Lu32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4, "lu32"));
    public int Li32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4, "li32"));
    public ulong Lu64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8, "lu64"));
    public long Li64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8, "li64"));
    public ushort Bu16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2, "bu16"));
    public uint Bu32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4, "bu32"));

    /// <summary>Read <paramref name="count"/> raw bytes (binparse <c>bp.bytes(n)</c>).</summary>
    public byte[] Bytes(int count) => Take(count, "bytes").ToArray();

    /// <summary>Skip <paramref name="count"/> bytes (binparse <c>bp.skip(n)</c>).</summary>
    public void Skip(int count) => Take(count, "skip");

    /// <summary>Peek at the next byte without advancing.</summary>
    public byte PeekU8()
    {
        if (Offset >= _buffer.Length) throw new BinaryOverflowException($"BufferOverflow: peek offset: {Hex.ToHex(Offset)}");
        return _buffer.Span[Offset];
    }

    /// <summary>
    /// Fixed length string (binparse <c>bp.string(n)</c>): reads exactly <paramref name="maxLength"/> bytes,
    /// the value stops at the first <c>0x00</c>. Each byte maps to one char (latin1), as upstream.
    /// </summary>
    public string FixedString(int maxLength)
    {
        var span = Take(maxLength, $"String:{maxLength}");
        var end = span.IndexOf((byte)0);
        if (end < 0) end = span.Length;
        return Encoding.Latin1.GetString(span[..end]);
    }

    /// <summary>
    /// Null terminated string (binparse <c>bp.string()</c>): reads until <c>0x00</c> (consumed) or end of buffer.
    /// </summary>
    public string NullString()
    {
        var span = _buffer.Span[Offset..];
        var end = span.IndexOf((byte)0);
        if (end < 0)
        {
            // upstream behaviour: consumes size + 1 even when no terminator is found
            Offset += span.Length + 1;
            return Encoding.Latin1.GetString(span);
        }
        Offset += end + 1;
        return Encoding.Latin1.GetString(span[..end]);
    }

    /// <summary>Create a bit stream starting at the current offset and covering <paramref name="byteCount"/> bytes, then advance past them.</summary>
    public BitStream Bits(int byteCount)
    {
        var bs = new BitStream(_buffer, Offset, Offset + byteCount);
        Skip(byteCount);
        return bs;
    }
}

/// <summary>Thrown when reading past the end of a buffer.</summary>
public sealed class BinaryOverflowException(string message) : Exception(message);

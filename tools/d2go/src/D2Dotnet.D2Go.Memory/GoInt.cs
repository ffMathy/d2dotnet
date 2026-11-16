namespace D2Dotnet.D2Go.Memory;

/// <summary>Go integer conversions of the port.</summary>
internal static class GoInt
{
    /// <summary>
    /// Go's <c>int(v)</c> for a <c>uint</c>: the 64-bit two's complement reinterpretation. Every value d2go reads is a
    /// 32-bit or smaller field, so the result is the field's unsigned value, never negative.
    /// </summary>
    public static long ToInt(ulong value) => unchecked((long)value);

    /// <summary>
    /// A Go <c>int</c> (64-bit) result as a 32-bit <see cref="int"/> field of the model: the low 32 bits (two's
    /// complement wrapping). Used only for coordinates, area ids, item ids and qualities, inventory pages, stat layers,
    /// widget counts and distances, which the game keeps far below 2^31. A 32-bit field of 2^31 or more would come out
    /// negative here, where Go reads a large positive number.
    /// </summary>
    public static int Narrow(long value) => unchecked((int)value);

    /// <inheritdoc cref="Narrow(long)"/>
    public static int Narrow(ulong value) => unchecked((int)value);
}

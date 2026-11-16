namespace D2Dotnet.D2Go.Memory;

/// <summary>Go integer conversions of the port.</summary>
internal static class GoInt
{
    /// <summary>
    /// A Go <c>int</c> (64-bit) result as the port's 32-bit <see cref="int"/>: the low 32 bits (two's complement
    /// wrapping). Every value d2go reads is a 32-bit or smaller field, so this only changes Go's result for unsigned
    /// 32-bit values of 2^31 or more, which come out negative (the game's own reading of signed fields).
    /// </summary>
    public static int Narrow(long value) => unchecked((int)value);

    /// <inheritdoc cref="Narrow(long)"/>
    public static int Narrow(ulong value) => unchecked((int)value);
}

using System.Buffers.Binary;

namespace D2Dotnet.Map.GameData.Casc;

/// <summary>
/// The first 9 bytes of an encoding key (EKey). Local <c>.idx</c> files and TVFS manifests
/// identify files by this truncated key.
/// </summary>
internal readonly record struct EKey9(ulong High, byte Low)
{
    public const int Length = 9;

    public static EKey9 From(ReadOnlySpan<byte> key)
    {
        if (key.Length < Length)
            throw new ArgumentException($"An EKey needs at least {Length} bytes, got {key.Length}.", nameof(key));
        return new EKey9(BinaryPrimitives.ReadUInt64BigEndian(key), key[8]);
    }

    public override string ToString() => $"{High:x16}{Low:x2}";
}

internal static class Hex
{
    public static byte[] Parse(string hex)
    {
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException e)
        {
            throw new CascFormatException($"'{hex}' is not a hexadecimal key.", e);
        }
    }

    public static string Of(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(bytes);
}

internal static class BigEndian
{
    public static uint ReadUInt24(ReadOnlySpan<byte> b) => ((uint)b[0] << 16) | ((uint)b[1] << 8) | b[2];

    public static ulong ReadUInt40(ReadOnlySpan<byte> b) =>
        ((ulong)b[0] << 32) | ((ulong)b[1] << 24) | ((ulong)b[2] << 16) | ((ulong)b[3] << 8) | b[4];

    /// <summary>Read an unsigned big-endian integer of 1 to 4 bytes.</summary>
    public static uint ReadUIntN(ReadOnlySpan<byte> b, int size)
    {
        uint v = 0;
        for (var i = 0; i < size; i++)
            v = (v << 8) | b[i];
        return v;
    }
}

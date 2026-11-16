using System.Globalization;

namespace D2Dotnet.MapServer.Binary;

/// <summary>Hex helpers (binparse <c>toHex</c>).</summary>
public static class Hex
{
    /// <summary>Format as <c>0x</c> + lowercase hex, left padded with zeros to <paramref name="padding"/> digits.</summary>
    public static string ToHex(long num, int padding = 2) =>
        "0x" + num.ToString("x", CultureInfo.InvariantCulture).PadLeft(padding, '0');

    /// <summary>Lowercase hex dump of bytes without separators.</summary>
    public static string ToHexString(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(bytes);

    /// <summary>Parse a hex string (whitespace and an optional 0x prefix are ignored).</summary>
    public static byte[] FromHexString(string hex)
    {
        var clean = new string(hex.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) clean = clean[2..];
        return Convert.FromHexString(clean);
    }
}

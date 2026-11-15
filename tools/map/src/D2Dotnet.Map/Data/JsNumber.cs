using System.Globalization;
using System.Text.RegularExpressions;

namespace D2Dotnet.Map.Data;

/// <summary>
/// JavaScript <c>Number(string)</c> semantics, used by upstream to validate URL parameters
/// (e.g. seeds such as <c>0x3607656c</c>, <c>906454380</c> or <c>1e3</c>).
/// </summary>
public static partial class JsNumber
{
    [GeneratedRegex(@"^[+-]?(?:[0-9]+\.?[0-9]*(?:[eE][+-]?[0-9]+)?|\.[0-9]+(?:[eE][+-]?[0-9]+)?)$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalLiteral();

    [GeneratedRegex(@"^0[xX][0-9a-fA-F]+$|^0[oO][0-7]+$|^0[bB][01]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RadixLiteral();

    /// <summary>Convert a string to a number exactly like JavaScript's <c>Number(s)</c>; returns NaN when invalid.</summary>
    public static double Parse(string s)
    {
        var str = TrimJs(s);
        if (str.Length == 0) return 0;

        if (RadixLiteral().IsMatch(str))
        {
            var radix = char.ToLowerInvariant(str[1]) switch { 'x' => 16, 'o' => 8, _ => 2 };
            double value = 0;
            foreach (var c in str.AsSpan(2)) value = value * radix + HexValue(c);
            return value;
        }

        switch (str)
        {
            case "Infinity":
            case "+Infinity":
                return double.PositiveInfinity;
            case "-Infinity":
                return double.NegativeInfinity;
        }

        // ASCII digits only: JavaScript's Number() rejects other Unicode decimal digits (e.g. U+0661)
        if (!DecimalLiteral().IsMatch(str)) return double.NaN;
        return double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : double.NaN;
    }

    /// <summary>
    /// JavaScript <c>num in Enum</c> for a numeric TypeScript enum: true when <c>String(num)</c> is the name of a defined value.
    /// </summary>
    public static bool IsEnumKey<TEnum>(double num) where TEnum : struct, Enum
    {
        if (double.IsNaN(num) || double.IsInfinity(num) || Math.Floor(num) != num) return false;
        if (num < int.MinValue || num > int.MaxValue) return false;
        var value = (int)num;
        foreach (var defined in Enum.GetValuesAsUnderlyingType<TEnum>())
        {
            if (Convert.ToInt32(defined, CultureInfo.InvariantCulture) == value) return true;
        }
        return false;
    }

    /// <summary>Format a number like JavaScript's <c>String(num)</c> for the values used here (integers and plain decimals).</summary>
    public static string ToJsString(double num)
    {
        if (double.IsNaN(num)) return "NaN";
        if (double.IsPositiveInfinity(num)) return "Infinity";
        if (double.IsNegativeInfinity(num)) return "-Infinity";
        if (num == 0) return "0";
        if (Math.Floor(num) == num && Math.Abs(num) < 1e21) return num.ToString("F0", CultureInfo.InvariantCulture);
        return num.ToString("R", CultureInfo.InvariantCulture);
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => c - 'A' + 10,
    };

    private static bool IsJsWhiteSpace(char c) =>
        c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680' or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or '\uFEFF'
        || c is >= '\u2000' and <= '\u200A';

    /// <summary>JavaScript <c>String.prototype.trim</c>.</summary>
    public static string TrimJs(string s)
    {
        var start = 0;
        var end = s.Length;
        while (start < end && IsJsWhiteSpace(s[start])) start++;
        while (end > start && IsJsWhiteSpace(s[end - 1])) end--;
        return s[start..end];
    }
}

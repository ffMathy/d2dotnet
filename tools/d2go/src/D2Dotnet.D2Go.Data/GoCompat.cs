// Helpers that reproduce Go standard library semantics the d2go port relies on.

using System.Text;

namespace D2Dotnet.D2Go.Data;

/// <summary>Go's <c>strings.EqualFold</c> and <c>unicode.SimpleFold</c>.</summary>
internal static partial class GoStrings
{

    /// <summary>
    /// True when <paramref name="s"/> and <paramref name="t"/> are equal under simple Unicode case folding, rune by rune
    /// (Go <c>strings.EqualFold</c>): two runes match when one is in the other's <see cref="SimpleFold"/> orbit, so
    /// U+03F4 (capital theta symbol) matches U+03D1 (theta symbol), the Kelvin sign matches <c>k</c>, and U+0130 matches
    /// neither <c>i</c> nor <c>I</c>.
    /// </summary>
    public static bool EqualFold(string s, string t)
    {
        var a = s.EnumerateRunes();
        var b = t.EnumerateRunes();
        while (true)
        {
            var hasA = a.MoveNext();
            var hasB = b.MoveNext();
            if (!hasA || !hasB)
                return hasA == hasB;

            var sr = a.Current.Value;
            var tr = b.Current.Value;

            // Easy case.
            if (tr == sr)
                continue;

            // Make sr < tr to simplify what follows.
            if (tr < sr)
                (sr, tr) = (tr, sr);

            // Fast check for ASCII: sr and tr must be upper and lower case.
            if (tr < 0x80)
            {
                if (sr is >= 'A' and <= 'Z' && tr == sr + 'a' - 'A')
                    continue;
                return false;
            }

            // General case. SimpleFold(x) returns the next equivalent rune > x or wraps around to smaller values.
            var r = SimpleFold(sr);
            while (r != sr && r < tr)
                r = SimpleFold(r);
            if (r == tr)
                continue;
            return false;
        }
    }

    /// <summary>
    /// The next rune after <paramref name="r"/> in its simple case folding orbit, wrapping around to the smallest
    /// (Go <c>unicode.SimpleFold</c>); <paramref name="r"/> itself when it has no other case. Non-ASCII runes come from
    /// <see cref="SimpleFoldTable"/>, Go 1.24's results, so the answer does not depend on the platform's Unicode data.
    /// </summary>
    public static int SimpleFold(int r)
    {
        if (r < 0 || r > 0x10FFFF)
            return r;

        // Go's asciiFold: ASCII letters swap case, except that k and s continue to the Kelvin sign and the long s.
        if (r < 0x80)
        {
            return r switch
            {
                'K' => 'k',
                'k' => 0x212A,
                'S' => 's',
                's' => 0x017F,
                >= 'A' and <= 'Z' => r + ('a' - 'A'),
                >= 'a' and <= 'z' => r - ('a' - 'A'),
                _ => r,
            };
        }

        var lo = 0;
        var hi = SimpleFoldTable.Length;
        while (lo < hi)
        {
            var m = (lo + hi) >>> 1;
            if (SimpleFoldTable[m].From < r)
                lo = m + 1;
            else
                hi = m;
        }

        return lo < SimpleFoldTable.Length && SimpleFoldTable[lo].From == r ? SimpleFoldTable[lo].To : r;
    }
}

/// <summary>Go numeric conversions.</summary>
internal static class GoConvert
{
    /// <summary>
    /// Go's <c>int(f)</c> for a float64, as compiled for amd64: truncation toward zero, and the "integer indefinite"
    /// value (the minimum integer) for NaN, infinities and values out of range. The result is a 32-bit
    /// <see cref="int"/>, so out of range means out of the <see cref="int"/> range here.
    /// </summary>
    public static int ToInt(double value)
    {
        var truncated = Math.Truncate(value);
        if (double.IsNaN(truncated) || truncated > int.MaxValue || truncated < int.MinValue)
            return int.MinValue;
        return (int)truncated;
    }
}

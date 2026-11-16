// Helpers that reproduce Go standard library semantics the d2go port relies on.

using System.Text;

namespace D2Dotnet.D2Go.Data;

/// <summary>Go's <c>strings.EqualFold</c> and <c>unicode.SimpleFold</c>.</summary>
internal static class GoStrings
{
    // Go unicode.caseOrbit (src/unicode/tables.go, Go 1.24/1.25, Unicode 15.0): the next rune of each simple case
    // folding orbit that is not just a rune and its upper or lower case, sorted by From. U+0130 and U+0131 map to
    // themselves: Go does not fold them with i and I.
    private static readonly (int From, int To)[] CaseOrbit =
    [
        (0x004B, 0x006B), (0x0053, 0x0073), (0x006B, 0x212A), (0x0073, 0x017F), (0x00B5, 0x039C), (0x00C5, 0x00E5),
        (0x00DF, 0x1E9E), (0x00E5, 0x212B), (0x0130, 0x0130), (0x0131, 0x0131), (0x017F, 0x0053), (0x01C4, 0x01C5),
        (0x01C5, 0x01C6), (0x01C6, 0x01C4), (0x01C7, 0x01C8), (0x01C8, 0x01C9), (0x01C9, 0x01C7), (0x01CA, 0x01CB),
        (0x01CB, 0x01CC), (0x01CC, 0x01CA), (0x01F1, 0x01F2), (0x01F2, 0x01F3), (0x01F3, 0x01F1), (0x0345, 0x0399),
        (0x0392, 0x03B2), (0x0395, 0x03B5), (0x0398, 0x03B8), (0x0399, 0x03B9), (0x039A, 0x03BA), (0x039C, 0x03BC),
        (0x03A0, 0x03C0), (0x03A1, 0x03C1), (0x03A3, 0x03C2), (0x03A6, 0x03C6), (0x03A9, 0x03C9), (0x03B2, 0x03D0),
        (0x03B5, 0x03F5), (0x03B8, 0x03D1), (0x03B9, 0x1FBE), (0x03BA, 0x03F0), (0x03BC, 0x00B5), (0x03C0, 0x03D6),
        (0x03C1, 0x03F1), (0x03C2, 0x03C3), (0x03C3, 0x03A3), (0x03C6, 0x03D5), (0x03C9, 0x2126), (0x03D0, 0x0392),
        (0x03D1, 0x03F4), (0x03D5, 0x03A6), (0x03D6, 0x03A0), (0x03F0, 0x039A), (0x03F1, 0x03A1), (0x03F4, 0x0398),
        (0x03F5, 0x0395), (0x0412, 0x0432), (0x0414, 0x0434), (0x041E, 0x043E), (0x0421, 0x0441), (0x0422, 0x0442),
        (0x042A, 0x044A), (0x0432, 0x1C80), (0x0434, 0x1C81), (0x043E, 0x1C82), (0x0441, 0x1C83), (0x0442, 0x1C84),
        (0x044A, 0x1C86), (0x0462, 0x0463), (0x0463, 0x1C87), (0x1C80, 0x0412), (0x1C81, 0x0414), (0x1C82, 0x041E),
        (0x1C83, 0x0421), (0x1C84, 0x1C85), (0x1C85, 0x0422), (0x1C86, 0x042A), (0x1C87, 0x0462), (0x1C88, 0xA64A),
        (0x1E60, 0x1E61), (0x1E61, 0x1E9B), (0x1E9B, 0x1E60), (0x1E9E, 0x00DF), (0x1FBE, 0x0345), (0x2126, 0x03A9),
        (0x212A, 0x004B), (0x212B, 0x00C5), (0xA64A, 0xA64B), (0xA64B, 0x1C88),
    ];

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
    /// (Go <c>unicode.SimpleFold</c>); <paramref name="r"/> itself when it has no other case. Outside Go's orbit table
    /// the orbit is the rune and its lower or upper case, from .NET's invariant simple case mappings.
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

        // Consult the orbit table for special cases.
        var lo = 0;
        var hi = CaseOrbit.Length;
        while (lo < hi)
        {
            var m = (lo + hi) >>> 1;
            if (CaseOrbit[m].From < r)
                lo = m + 1;
            else
                hi = m;
        }

        if (lo < CaseOrbit.Length && CaseOrbit[lo].From == r)
            return CaseOrbit[lo].To;

        // A surrogate code point has no case (and is not a Rune).
        if (!Rune.IsValid(r))
            return r;

        // No folding specified: a one- or two-element orbit of the rune and its lower or upper case.
        var rune = new Rune(r);
        var lower = Rune.ToLowerInvariant(rune).Value;
        if (lower != r)
            return lower;
        return Rune.ToUpperInvariant(rune).Value;
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

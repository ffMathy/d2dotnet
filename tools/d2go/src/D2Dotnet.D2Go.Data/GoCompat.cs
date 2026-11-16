// Helpers that reproduce Go standard library semantics the d2go port relies on.

using System.Text;

namespace D2Dotnet.D2Go.Data;

/// <summary>Go's <c>strings.EqualFold</c>.</summary>
internal static class GoStrings
{
    /// <summary>
    /// True when <paramref name="s"/> and <paramref name="t"/> are equal under simple Unicode case folding, rune by rune
    /// (Go <c>strings.EqualFold</c>). Two runes match when they are equal or have the same upper- or lower-case form,
    /// which also covers folds such as the Kelvin sign and <c>k</c>.
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

            var ra = a.Current;
            var rb = b.Current;
            if (ra == rb || Rune.ToUpperInvariant(ra) == Rune.ToUpperInvariant(rb) || Rune.ToLowerInvariant(ra) == Rune.ToLowerInvariant(rb))
                continue;
            return false;
        }
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

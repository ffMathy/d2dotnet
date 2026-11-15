namespace D2Dotnet.Map.Generator.Cli;

/// <summary>
/// The libc parsing used by main.c: <c>strncmp</c> prefix checks, <c>atoi</c>, and the
/// <c>sscanf</c> patterns <c>"%u"</c>, <c>"%s %u"</c> and <c>"%s %d"</c>.
/// </summary>
public static class CParse
{
    /// <summary>C <c>isspace</c> in the "C" locale.</summary>
    public static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    /// <summary>
    /// main.c <c>starts_with(prefix, search_string)</c>: despite the parameter names it is true
    /// when <paramref name="value"/> begins with <paramref name="search"/>
    /// (<c>strncmp(value, search, strlen(search)) == 0</c>).
    /// </summary>
    public static bool StartsWith(string value, string search) => value.StartsWith(search, StringComparison.Ordinal);

    /// <summary>
    /// <c>atoi</c> (msvcrt, as linked by mingw): skip whitespace, optional sign, decimal digits;
    /// 0 when there are no digits; saturates at <see cref="int.MinValue"/>/<see cref="int.MaxValue"/>.
    /// </summary>
    public static int Atoi(string s)
    {
        var i = 0;
        while (i < s.Length && IsSpace(s[i])) i++;
        return ParseSignedDigits(s, ref i, out var value) ? value : 0;
    }

    private static bool ParseSignedDigits(string s, ref int i, out int value)
    {
        var start = i;
        var negative = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            negative = s[i] == '-';
            i++;
        }
        long acc = 0;
        var digits = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            if (acc <= int.MaxValue + 1L) acc = acc * 10 + (s[i] - '0');
            i++;
            digits++;
        }
        if (digits == 0)
        {
            i = start;
            value = 0;
            return false;
        }
        if (negative) acc = -acc;
        value = (int)Math.Clamp(acc, int.MinValue, int.MaxValue);
        return true;
    }

    /// <summary>
    /// One <c>%u</c> conversion at <paramref name="i"/> (leading whitespace skipped): optional sign
    /// and decimal digits, <c>strtoul</c> semantics (a minus sign negates modulo 2^32, overflow
    /// saturates at 0xFFFFFFFF). Returns false (target untouched) when no digits follow.
    /// </summary>
    public static bool ScanUnsigned(string s, ref int i, out uint value)
    {
        var p = i;
        while (p < s.Length && IsSpace(s[p])) p++;
        var negative = false;
        if (p < s.Length && (s[p] == '+' || s[p] == '-'))
        {
            negative = s[p] == '-';
            p++;
        }
        ulong acc = 0;
        var digits = 0;
        var overflow = false;
        while (p < s.Length && s[p] >= '0' && s[p] <= '9')
        {
            acc = acc * 10 + (ulong)(s[p] - '0');
            if (acc > uint.MaxValue) overflow = true;
            if (overflow) acc = uint.MaxValue;
            p++;
            digits++;
        }
        if (digits == 0)
        {
            value = 0;
            return false;
        }
        i = p;
        var v = overflow ? uint.MaxValue : (uint)acc;
        value = negative && !overflow ? unchecked(0u - v) : v;
        return true;
    }

    /// <summary>One <c>%d</c> conversion at <paramref name="i"/> (leading whitespace skipped); saturating.</summary>
    public static bool ScanSigned(string s, ref int i, out int value)
    {
        var p = i;
        while (p < s.Length && IsSpace(s[p])) p++;
        if (!ParseSignedDigits(s, ref p, out value)) return false;
        i = p;
        return true;
    }

    /// <summary>One <c>%s</c> conversion: skip whitespace, then the next run of non-whitespace.</summary>
    public static bool ScanToken(string s, ref int i, out string token)
    {
        var p = i;
        while (p < s.Length && IsSpace(s[p])) p++;
        var start = p;
        while (p < s.Length && !IsSpace(s[p])) p++;
        if (p == start)
        {
            token = "";
            return false;
        }
        token = s[start..p];
        i = p;
        return true;
    }

    /// <summary><c>sscanf(s, "%u", &amp;target)</c>: target is only assigned on a successful conversion.</summary>
    public static int SscanfUnsigned(string s, ref uint target)
    {
        var i = 0;
        if (!ScanUnsigned(s, ref i, out var v)) return 0;
        target = v;
        return 1;
    }

    /// <summary><c>sscanf(s, "%s %u", buf, &amp;target)</c>.</summary>
    public static int SscanfTokenUnsigned(string s, ref uint target)
    {
        var i = 0;
        if (!ScanToken(s, ref i, out _)) return -1; // EOF before the first conversion
        if (!ScanUnsigned(s, ref i, out var v)) return 1;
        target = v;
        return 2;
    }

    /// <summary><c>sscanf(s, "%s %d", buf, &amp;target)</c>.</summary>
    public static int SscanfTokenSigned(string s, ref int target)
    {
        var i = 0;
        if (!ScanToken(s, ref i, out _)) return -1;
        if (!ScanSigned(s, ref i, out var v)) return 1;
        target = v;
        return 2;
    }
}

/// <summary><c>fgets(buffer, size, stdin)</c> over a byte stream (Latin-1 decoded).</summary>
public sealed class CLineReader
{
    private readonly Stream _stream;
    private readonly int _size;

    /// <param name="stream">stdin.</param>
    /// <param name="size">Buffer size; at most <c>size - 1</c> bytes are returned per call.</param>
    public CLineReader(Stream stream, int size)
    {
        _stream = stream;
        _size = size;
    }

    /// <summary>
    /// Next chunk: bytes up to and including '\n', or <c>size - 1</c> bytes, or what is left
    /// before EOF. Null at EOF with nothing read.
    /// </summary>
    public string? Fgets()
    {
        var bytes = new List<byte>();
        while (bytes.Count < _size - 1)
        {
            var b = _stream.ReadByte();
            if (b < 0) break;
            bytes.Add((byte)b);
            if (b == '\n') break;
        }
        if (bytes.Count == 0) return null;
        return System.Text.Encoding.Latin1.GetString([.. bytes]);
    }
}

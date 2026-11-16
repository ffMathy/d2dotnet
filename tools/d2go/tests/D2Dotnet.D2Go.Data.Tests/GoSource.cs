using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace D2Dotnet.D2Go.Data.Tests;

/// <summary>
/// Just enough of a Go parser to read d2go's sources as an oracle: map and slice literals with one entry per line,
/// constant declarations (with <c>iota</c>), and the <c>case</c> lists of a function.
/// </summary>
internal static partial class GoSource
{
    /// <summary>A constant: its Go type (null when untyped) and value (a long or a string).</summary>
    public sealed record GoConst(string Name, string? Type, object Value, string File);

    /// <summary>
    /// The entries of the composite literal <c>var &lt;name&gt; = ...{</c>, one per line, as
    /// <c>key: {Field: value, ...},</c>. Keys and field values are returned as Go source text, strings unquoted.
    /// </summary>
    public static IReadOnlyList<(string Key, IReadOnlyDictionary<string, string> Fields)> MapOfStructs(string source, string varName)
    {
        var entries = new List<(string, IReadOnlyDictionary<string, string>)>();
        foreach (var line in VarBody(source, varName))
        {
            var m = StructEntry().Match(line);
            Assert.True(m.Success, $"Unexpected line in {varName}: {line}");
            entries.Add((m.Groups[1].Value, ParseFields(m.Groups[2].Value)));
        }

        return entries;
    }

    /// <summary>The entries <c>key: "value",</c> of a <c>map[...]string</c> literal.</summary>
    public static IReadOnlyList<(string Key, string Value)> MapOfStrings(string source, string varName)
    {
        var entries = new List<(string, string)>();
        foreach (var line in VarBody(source, varName))
        {
            var m = StringEntry().Match(line);
            Assert.True(m.Success, $"Unexpected line in {varName}: {line}");
            entries.Add((m.Groups[1].Value, Unquote(m.Groups[2].Value)));
        }

        return entries;
    }

    /// <summary>The raw lines (trimmed, comments and blanks dropped) between <c>var name = ...{</c> and the closing brace.</summary>
    public static IReadOnlyList<string> VarBody(string source, string varName)
    {
        var start = Regex.Match(source, $@"^var {Regex.Escape(varName)} = [^\n]*\{{\n", RegexOptions.Multiline);
        Assert.True(start.Success, $"var {varName} not found");
        var end = source.IndexOf("\n}", start.Index + start.Length - 1, StringComparison.Ordinal);
        return source[(start.Index + start.Length)..end]
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>The elements of a <c>[]string{ "a", "b", ... }</c> literal, one per line.</summary>
    public static IReadOnlyList<string> StringSlice(string source, string varName) =>
        VarBody(source, varName).Select(l =>
        {
            Assert.EndsWith(",", l);
            return Unquote(l[..^1].Trim());
        }).ToArray();

    private static Dictionary<string, string> ParseFields(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && text[i] == ' ')
                i++;
            var colon = text.IndexOf(':', i);
            var name = text[i..colon].Trim();
            i = colon + 1;
            while (text[i] == ' ')
                i++;
            string value;
            if (text[i] == '"')
            {
                var end = i + 1;
                while (text[end] != '"')
                    end += text[end] == '\\' ? 2 : 1;
                value = Unquote(text[i..(end + 1)]);
                i = end + 1;
            }
            else
            {
                var end = text.IndexOf(',', i);
                if (end < 0)
                    end = text.Length;
                value = text[i..end].Trim();
                i = end;
            }

            fields.Add(name, value);
            while (i < text.Length && text[i] == ' ')
                i++;
            if (i < text.Length)
            {
                Assert.Equal(',', text[i]);
                i++;
            }
        }

        return fields;
    }

    /// <summary>A Go interpreted string literal.</summary>
    public static string Unquote(string literal)
    {
        Assert.True(literal.Length >= 2 && literal[0] == '"' && literal[^1] == '"', $"Not a Go string literal: {literal}");
        var sb = new StringBuilder();
        for (var i = 1; i < literal.Length - 1; i++)
        {
            var c = literal[i];
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            c = literal[++i];
            switch (c)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case '\\': sb.Append('\\'); break;
                case '"': sb.Append('"'); break;
                case '\'': sb.Append('\''); break;
                case 'x':
                    sb.Append((char)int.Parse(literal.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 2;
                    break;
                case 'u':
                    sb.Append((char)int.Parse(literal.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default:
                    Assert.Fail($"Unsupported escape \\{c} in {literal}");
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>A Go integer literal (decimal or hex, optionally negative).</summary>
    public static long ParseInt(string literal)
    {
        var text = literal.Trim();
        var negative = text.StartsWith('-');
        if (negative)
            text = text[1..].Trim();
        var value = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }

    /// <summary>
    /// Every constant declared in a Go file, in order, with <c>iota</c> and implicit repetition evaluated. Handles
    /// the forms d2go uses: <c>Name [Type] = expr</c> and bare <c>Name</c>, where expr is <c>iota</c>,
    /// <c>iota ± n</c>, an integer or a string literal.
    /// </summary>
    public static IReadOnlyList<GoConst> Constants(string source, string file)
    {
        var result = new List<GoConst>();
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line == "const (")
            {
                var iota = 0;
                string? lastType = null;
                string? lastExpr = null;
                for (i++; lines[i].Trim() != ")"; i++)
                {
                    var spec = StripComment(lines[i]).Trim();
                    if (spec.Length == 0)
                        continue;
                    var m = ConstSpec().Match(spec);
                    Assert.True(m.Success, $"Unsupported const spec in {file}: {spec}");
                    if (m.Groups[3].Success)
                    {
                        lastType = m.Groups[2].Success ? m.Groups[2].Value : null;
                        lastExpr = m.Groups[3].Value;
                    }

                    Assert.NotNull(lastExpr);
                    result.Add(new GoConst(m.Groups[1].Value, lastType, Evaluate(lastExpr, iota), file));
                    iota++;
                }
            }
            else if (line.StartsWith("const ", StringComparison.Ordinal))
            {
                var m = ConstSpec().Match(StripComment(line)["const ".Length..].Trim());
                Assert.True(m.Success && m.Groups[3].Success, $"Unsupported const in {file}: {line}");
                result.Add(new GoConst(m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null, Evaluate(m.Groups[3].Value, 0), file));
            }
        }

        return result;
    }

    private static object Evaluate(string expr, int iota)
    {
        expr = expr.Trim();
        if (expr.StartsWith('"'))
            return Unquote(expr);
        var m = IotaExpr().Match(expr);
        if (m.Success)
        {
            long value = iota;
            if (m.Groups[1].Success)
            {
                var operand = ParseInt(m.Groups[2].Value);
                value = m.Groups[1].Value == "+" ? value + operand : value - operand;
            }

            return value;
        }

        return ParseInt(expr);
    }

    private static string StripComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
                inString = !inString;
            if (!inString && line[i] == '/' && line[i + 1] == '/')
                return line[..i];
        }

        return line;
    }

    /// <summary>The text of <c>func (recv) name(...</c> up to its closing brace at column 0.</summary>
    public static string FunctionBody(string source, string signaturePrefix)
    {
        var start = source.IndexOf(signaturePrefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signaturePrefix} not found");
        var end = source.IndexOf("\n}", start, StringComparison.Ordinal);
        return source[start..end];
    }

    /// <summary>
    /// The operands of every <c>case a, b, c:</c> clause of a function body, and of every <c>== x</c> comparison
    /// when it has no case clause; each is a number, <c>pkg.Name</c> or <c>Name</c>.
    /// </summary>
    public static IReadOnlyList<string> CaseOperands(string body)
    {
        var cases = CaseClause().Matches(body);
        if (cases.Count > 0)
            return cases.SelectMany(c => c.Groups[1].Value.Split(',')).Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
        return EqualsOperand().Matches(body).Select(m => m.Groups[1].Value).ToArray();
    }

    [GeneratedRegex(@"^(\S+?):\s*\{(.*)\},$")]
    private static partial Regex StructEntry();

    [GeneratedRegex(@"^(\w+):\s*(""(?:[^""\\]|\\.)*""),$")]
    private static partial Regex StringEntry();

    [GeneratedRegex(@"^(\w+)(?:\s+([\w.]+))?(?:\s*=\s*(.+))?$")]
    private static partial Regex ConstSpec();

    [GeneratedRegex(@"^iota(?:\s*([+-])\s*(\w+))?$")]
    private static partial Regex IotaExpr();

    [GeneratedRegex(@"case\s+([^:]+):", RegexOptions.Singleline)]
    private static partial Regex CaseClause();

    [GeneratedRegex(@"==\s*([\w.]+)")]
    private static partial Regex EqualsOperand();
}

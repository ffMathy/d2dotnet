// Port of libd2 packages/drlg/src/txt.zig (MIT, (c) 2026 jaenster).
//
// Generic reader for D2's tab-separated Excel tables (data\global\excel\*.txt). The header
// row maps column name -> index; data rows are kept as string arrays. Empty fields read as
// 0 for integer accessors (D2's convention). Every column is looked up BY NAME, so the
// extra columns Diablo II: Resurrected added to its tables are simply ignored.

using System.Text;

namespace D2Dotnet.Map.Drlg.Data;

/// <summary>One parsed excel table.</summary>
public sealed class TxtTable
{
    private readonly Dictionary<string, int> _cols = new(StringComparer.Ordinal);
    private readonly string[][] _rows;
    private readonly Dictionary<int, Dictionary<long, int>> _intIndex = new();

    private TxtTable(string[] header, string[][] rows)
    {
        // A repeated header name maps to its LAST column, as libd2's hash-map put does.
        for (int i = 0; i < header.Length; i++) _cols[header[i]] = i;
        _rows = rows;
    }

    /// <summary>Parse a whole table from its raw bytes (Latin-1 safe; column text is ASCII).</summary>
    public static TxtTable Parse(byte[] bytes) => Parse(Encoding.Latin1.GetString(bytes));

    /// <summary>Parse a whole table from its text.</summary>
    public static TxtTable Parse(string src)
    {
        var lines = src.Split('\n');
        if (lines.Length == 0) return new TxtTable(Array.Empty<string>(), Array.Empty<string[]>());
        var header = TrimCr(lines[0]).Split('\t');
        var rows = new List<string[]>(lines.Length);
        for (int i = 1; i < lines.Length; i++)
        {
            var line = TrimCr(lines[i]);
            if (line.Length == 0) continue;
            // D2 marks end-of-table with an "Expansion" sentinel row; keep all real rows.
            rows.Add(line.Split('\t'));
        }
        return new TxtTable(header, rows.ToArray());
    }

    private static string TrimCr(string s) => s.TrimEnd('\r');

    public int RowCount => _rows.Length;

    public bool HasColumn(string name) => _cols.ContainsKey(name);

    public int? Col(string name) => _cols.TryGetValue(name, out var c) ? c : null;

    /// <summary>Raw string cell, or "" if out of range / unknown column.</summary>
    public string Str(int row, string name)
    {
        if (!_cols.TryGetValue(name, out var c)) return "";
        if ((uint)row >= (uint)_rows.Length || c >= _rows[row].Length) return "";
        return _rows[row][c];
    }

    /// <summary>Integer cell; empty / non-numeric reads as 0 (D2 convention).</summary>
    public long Int(int row, string name)
    {
        var s = Str(row, name);
        if (s.Length == 0) return 0;
        return TryParseInt(s, out var v) ? v : 0;
    }

    /// <summary>Find the first row whose integer column <paramref name="name"/> equals
    /// <paramref name="value"/>, else null. Memoised per column (first row wins).</summary>
    public int? FindByInt(string name, long value)
    {
        if (!_cols.TryGetValue(name, out var c)) return null;
        Dictionary<long, int>? idx;
        lock (_intIndex)
        {
            if (!_intIndex.TryGetValue(c, out idx))
            {
                idx = new Dictionary<long, int>();
                for (int i = 0; i < _rows.Length; i++)
                {
                    var r = _rows[i];
                    if (c >= r.Length) continue;
                    if (!TryParseInt(r[c], out var v)) continue;
                    idx.TryAdd(v, i);
                }
                _intIndex[c] = idx;
            }
        }
        return idx.TryGetValue(value, out var row) ? row : null;
    }

    /// <summary>Zig std.fmt.parseInt(i64, s, 10): optional sign, decimal digits, underscores
    /// allowed between digits; anything else (including surrounding spaces) fails.</summary>
    internal static bool TryParseInt(string s, out long value)
    {
        value = 0;
        if (s.Length == 0) return false;
        int i = 0;
        bool neg = false;
        if (s[0] == '+' || s[0] == '-')
        {
            neg = s[0] == '-';
            i = 1;
            if (s.Length == 1) return false;
        }
        if (s[i] == '_' || s[^1] == '_') return false;
        ulong acc = 0;
        bool prevUnderscore = false;
        for (; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch == '_')
            {
                if (prevUnderscore) return false;
                prevUnderscore = true;
                continue;
            }
            prevUnderscore = false;
            if (ch < '0' || ch > '9') return false;
            ulong d = (ulong)(ch - '0');
            if (acc > (ulong.MaxValue - d) / 10) return false;
            acc = acc * 10 + d;
        }
        if (neg)
        {
            if (acc > 9223372036854775808UL) return false;
            value = acc == 9223372036854775808UL ? long.MinValue : -(long)acc;
        }
        else
        {
            if (acc > long.MaxValue) return false;
            value = (long)acc;
        }
        return true;
    }
}

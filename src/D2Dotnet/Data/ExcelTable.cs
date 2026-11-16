// The line, header and integer rules follow libd2's packages/drlg/src/txt.zig (MIT, (c) 2026 jaenster), as ported for
// the map generator, and d2go's cmd/txttocode (MIT, (c) 2023 Héctor Giménez) where the two had to be reconciled.

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace D2Dotnet.Data;

/// <summary>
/// One tab-separated excel table of the game (<c>data/global/excel/*.txt</c>): a header row naming the columns, then one
/// row per line. Immutable and safe to use from several threads.
/// </summary>
/// <remarks>
/// <para>Parsing rules (the ones the map generator and the typed tables of <see cref="GameDatabase"/> rely on):</para>
/// <list type="bullet">
/// <item>Bytes: a UTF-8 byte order mark is skipped; the rest is decoded as UTF-8 when it is valid UTF-8, else as
/// Latin-1 (the classic 1.x tables are Windows-1252), so ASCII text always reads back byte for byte.</item>
/// <item>Lines end at <c>\n</c>; trailing <c>\r</c>s are dropped. The first line is the header. Empty lines after it
/// are skipped; every other line is a row, <em>including</em> the game's <c>Expansion</c> divider rows, so row
/// indexes are file order.</item>
/// <item>Cells are split on <c>\t</c> and kept verbatim (no trimming). A row may have fewer or more cells than the
/// header; a missing cell reads as empty.</item>
/// <item>Columns are looked up by header name: an exact (ordinal) match first, else a case-insensitive one. When a
/// header repeats a name, the <em>last</em> such column wins. Lookups of an unknown column read as empty, so the extra
/// columns Diablo II: Resurrected added are simply ignored and missing ones read as empty.</item>
/// </list>
/// <para>Numbers come in three readings:</para>
/// <list type="bullet">
/// <item><see cref="GetInt64(int, int)"/> / <see cref="GetInt32(int, int)"/>: the game's convention, an empty or
/// non-numeric cell is 0. A number is an optional sign and decimal digits (underscores allowed between digits),
/// nothing else, not even surrounding blanks (see <see cref="TryParseInt64"/>).</item>
/// <item><see cref="TryGetInt64(int, int, out long)"/>: the same syntax, but tells a number from anything else.</item>
/// <item><see cref="ParseInt32(int, int)"/>: strict, for typed tables: surrounding blanks are ignored, empty is 0, and
/// anything that is not a 32-bit whole number is an <see cref="InvalidDataException"/>.</item>
/// </list>
/// </remarks>
public sealed class ExcelTable
{
    private static readonly string[] NoColumns = [];

    private readonly string _text;
    private readonly string[] _columns;
    private readonly Dictionary<string, int> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _ignoreCase = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _exactSpan;
    private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _ignoreCaseSpan;

    // Row r's cells are _cells[2 * _rowStart[r] ..] as (offset into _text, length) pairs, up to _rowStart[r + 1].
    private readonly int[] _rowStart;
    private readonly int[] _cells;
    private readonly ConcurrentDictionary<int, FrozenDictionary<long, int>> _intIndex = new();

    private ExcelTable(string name, string text, string[] columns, int[] rowStart, int[] cells)
    {
        Name = name;
        _text = text;
        _columns = columns;
        _rowStart = rowStart;
        _cells = cells;
        for (var i = 0; i < columns.Length; i++)
        {
            _exact[columns[i]] = i;
            _ignoreCase[columns[i]] = i;
        }

        _exactSpan = _exact.GetAlternateLookup<ReadOnlySpan<char>>();
        _ignoreCaseSpan = _ignoreCase.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>The table's name (for example <c>levels.txt</c>), used in error messages; may be empty.</summary>
    public string Name { get; }

    /// <summary>The header row: the column names, in file order (repeated names included).</summary>
    public IReadOnlyList<string> Columns => _columns;

    /// <summary>The number of rows, header excluded.</summary>
    public int RowCount => _rowStart.Length - 1;

    /// <summary>A row by index, as a lightweight view.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="row"/> is not a row index.</exception>
    public ExcelRow this[int row]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
            return new ExcelRow(this, row);
        }
    }

    /// <summary>Every row, in file order.</summary>
    public IEnumerable<ExcelRow> Rows
    {
        get
        {
            for (var row = 0; row < RowCount; row++)
                yield return new ExcelRow(this, row);
        }
    }

    /// <summary>Parse a table from its raw bytes.</summary>
    /// <param name="bytes">The file content.</param>
    /// <param name="name">A name for error messages, such as <c>levels.txt</c>.</param>
    public static ExcelTable Parse(ReadOnlySpan<byte> bytes, string name = "")
    {
        if (bytes.StartsWith("\uFEFF"u8))
            bytes = bytes[3..];
        var text = Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : Encoding.Latin1.GetString(bytes);
        return Parse(text, name);
    }

    /// <summary>Parse a table from its text.</summary>
    /// <param name="text">The file content.</param>
    /// <param name="name">A name for error messages, such as <c>levels.txt</c>.</param>
    public static ExcelTable Parse(string text, string name = "")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(name);
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];

        string[] columns = NoColumns;
        var rowStart = new List<int>();
        var cells = new List<int>();
        var first = true;
        var position = 0;
        while (position < text.Length)
        {
            var newline = text.IndexOf('\n', position);
            var end = newline < 0 ? text.Length : newline;
            var lineEnd = end;
            while (lineEnd > position && text[lineEnd - 1] == '\r')
                lineEnd--;

            if (first)
            {
                columns = text[position..lineEnd].Split('\t');
                first = false;
            }
            else if (lineEnd > position)
            {
                rowStart.Add(cells.Count / 2);
                var cell = position;
                while (true)
                {
                    var tab = text.IndexOf('\t', cell, lineEnd - cell);
                    var cellEnd = tab < 0 ? lineEnd : tab;
                    cells.Add(cell);
                    cells.Add(cellEnd - cell);
                    if (tab < 0)
                        break;
                    cell = tab + 1;
                }
            }

            if (newline < 0)
                break;
            position = newline + 1;
        }

        rowStart.Add(cells.Count / 2);
        return new ExcelTable(name, text, columns, rowStart.ToArray(), cells.ToArray());
    }

    /// <summary>The index of a column, or -1 when the table has none by that name.</summary>
    /// <remarks>An exact match first, else a case-insensitive one; the last column of a repeated name wins.</remarks>
    public int GetColumnIndex(string column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return _exact.TryGetValue(column, out var exact) ? exact
            : _ignoreCase.TryGetValue(column, out var folded) ? folded
            : -1;
    }

    /// <summary>The index of a column, or -1 when the table has none by that name.</summary>
    /// <remarks>An exact match first, else a case-insensitive one; the last column of a repeated name wins.</remarks>
    public int GetColumnIndex(ReadOnlySpan<char> column) =>
        _exactSpan.TryGetValue(column, out var exact) ? exact
        : _ignoreCaseSpan.TryGetValue(column, out var folded) ? folded
        : -1;

    /// <summary>True when the table has a column by that name (ignoring case).</summary>
    public bool HasColumn(string column) => GetColumnIndex(column) >= 0;

    /// <summary>The index of the first of <paramref name="columns"/> the table has, or -1 when it has none of them.</summary>
    /// <remarks>For columns D2R renamed, such as <c>*ID</c> (D2R) and <c>Id</c> (1.x) in objects.txt.</remarks>
    public int FindColumn(params ReadOnlySpan<string> columns)
    {
        foreach (var column in columns)
        {
            var index = GetColumnIndex(column);
            if (index >= 0)
                return index;
        }

        return -1;
    }

    /// <summary>The index of a column the caller cannot do without.</summary>
    /// <exception cref="InvalidDataException">The table has no such column.</exception>
    public int GetRequiredColumnIndex(string column)
    {
        var index = GetColumnIndex(column);
        return index >= 0 ? index : throw new InvalidDataException($"{Describe()} has no '{column}' column.");
    }

    /// <summary>The number of cells of a row as written in the file (it may differ from the number of columns).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="row"/> is not a row index.</exception>
    public int GetCellCount(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
        return _rowStart[row + 1] - _rowStart[row];
    }

    /// <summary>A cell, without allocating; empty when the row, the column or the cell does not exist.</summary>
    public ReadOnlySpan<char> GetSpan(int row, int column)
    {
        if ((uint)row >= (uint)RowCount || column < 0)
            return default;
        var cell = _rowStart[row] + column;
        if (cell >= _rowStart[row + 1])
            return default;
        return _text.AsSpan(_cells[2 * cell], _cells[2 * cell + 1]);
    }

    /// <summary>A cell by column name, without allocating; empty when the row, the column or the cell does not exist.</summary>
    public ReadOnlySpan<char> GetSpan(int row, string column) => GetSpan(row, GetColumnIndex(column));

    /// <summary>A cell as text; empty when the row, the column or the cell does not exist.</summary>
    public string GetString(int row, int column)
    {
        var span = GetSpan(row, column);
        return span.IsEmpty ? "" : span.ToString();
    }

    /// <summary>A cell by column name as text; empty when the row, the column or the cell does not exist.</summary>
    public string GetString(int row, string column) => GetString(row, GetColumnIndex(column));

    /// <summary>True when the cell is empty or does not exist.</summary>
    public bool IsEmpty(int row, int column) => GetSpan(row, column).IsEmpty;

    /// <summary>
    /// A cell as a number, the game's way: an empty or non-numeric cell (see <see cref="TryParseInt64"/>) is 0.
    /// </summary>
    public long GetInt64(int row, int column) => TryParseInt64(GetSpan(row, column), out var value) ? value : 0;

    /// <summary>A cell by column name as a number, the game's way: an empty, non-numeric or missing cell is 0.</summary>
    public long GetInt64(int row, string column) => GetInt64(row, GetColumnIndex(column));

    /// <summary>
    /// <see cref="GetInt64(int, int)"/> truncated to 32 bits (<c>unchecked</c>), as the game stores most columns.
    /// </summary>
    public int GetInt32(int row, int column) => unchecked((int)GetInt64(row, column));

    /// <summary><see cref="GetInt64(int, string)"/> truncated to 32 bits (<c>unchecked</c>).</summary>
    public int GetInt32(int row, string column) => GetInt32(row, GetColumnIndex(column));

    /// <summary>A flag cell, the game's way: true when <see cref="GetInt64(int, int)"/> is not 0.</summary>
    public bool GetBoolean(int row, int column) => GetInt64(row, column) != 0;

    /// <summary>A flag cell by column name, the game's way: true when <see cref="GetInt64(int, string)"/> is not 0.</summary>
    public bool GetBoolean(int row, string column) => GetBoolean(row, GetColumnIndex(column));

    /// <summary>A cell as a number, telling numbers (see <see cref="TryParseInt64"/>) from anything else.</summary>
    public bool TryGetInt64(int row, int column, out long value) => TryParseInt64(GetSpan(row, column), out value);

    /// <summary>A cell by column name as a number, telling numbers from anything else (a missing cell is not a number).</summary>
    public bool TryGetInt64(int row, string column, out long value) => TryGetInt64(row, GetColumnIndex(column), out value);

    /// <summary>
    /// A cell as a 32-bit whole number, strictly: surrounding blanks are ignored and an empty cell is 0, anything else
    /// must be an optionally signed decimal number that fits in 32 bits.
    /// </summary>
    /// <exception cref="InvalidDataException">The cell holds something else.</exception>
    public int ParseInt32(int row, int column)
    {
        var cell = GetSpan(row, column).Trim();
        if (cell.IsEmpty)
            return 0;
        if (int.TryParse(cell, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            return value;
        var name = column >= 0 && column < _columns.Length ? _columns[column] : column.ToString(CultureInfo.InvariantCulture);
        throw new InvalidDataException($"{Describe()}: row {row}, column '{name}' has '{GetString(row, column)}', which is not a whole number.");
    }

    /// <summary>
    /// The first row whose cell in <paramref name="column"/> is the number <paramref name="value"/> (the game's
    /// <c>GetLine</c> lookups), or null. Cells that are not numbers (see <see cref="TryParseInt64"/>) never match.
    /// </summary>
    /// <remarks>The index of each column is built once, on first use.</remarks>
    public int? FindRow(int column, long value)
    {
        if (column < 0 || column >= _columns.Length)
            return null;
        var index = _intIndex.GetOrAdd(column, BuildIntIndex);
        return index.TryGetValue(value, out var row) ? row : null;
    }

    /// <summary>The first row whose cell in the named column is the number <paramref name="value"/>, or null (also for an unknown column).</summary>
    public int? FindRow(string column, long value) => FindRow(GetColumnIndex(column), value);

    private FrozenDictionary<long, int> BuildIntIndex(int column)
    {
        var index = new Dictionary<long, int>();
        for (var row = 0; row < RowCount; row++)
        {
            if (TryParseInt64(GetSpan(row, column), out var v))
                index.TryAdd(v, row);
        }

        return index.ToFrozenDictionary();
    }

    /// <summary>
    /// Parses a number the way the game data is read (Zig's <c>std.fmt.parseInt(i64, s, 10)</c>, which libd2 uses): an
    /// optional <c>+</c> or <c>-</c>, then decimal digits with single underscores allowed between them. Anything else,
    /// surrounding blanks included, or a value outside 64 bits, is not a number.
    /// </summary>
    public static bool TryParseInt64(ReadOnlySpan<char> s, out long value)
    {
        value = 0;
        if (s.IsEmpty)
            return false;
        var i = 0;
        var negative = false;
        if (s[0] is '+' or '-')
        {
            negative = s[0] == '-';
            i = 1;
            if (s.Length == 1)
                return false;
        }

        if (s[i] == '_' || s[^1] == '_')
            return false;
        ulong acc = 0;
        var previousUnderscore = false;
        for (; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '_')
            {
                if (previousUnderscore)
                    return false;
                previousUnderscore = true;
                continue;
            }

            previousUnderscore = false;
            if (ch is < '0' or > '9')
                return false;
            var digit = (ulong)(ch - '0');
            if (acc > (ulong.MaxValue - digit) / 10)
                return false;
            acc = acc * 10 + digit;
        }

        if (negative)
        {
            if (acc > 9223372036854775808UL)
                return false;
            value = acc == 9223372036854775808UL ? long.MinValue : -(long)acc;
        }
        else
        {
            if (acc > long.MaxValue)
                return false;
            value = (long)acc;
        }

        return true;
    }

    private string Describe() => Name.Length > 0 ? Name : "The excel table";

    /// <inheritdoc/>
    public override string ToString() => $"{Describe()} ({_columns.Length} columns, {RowCount} rows)";
}

/// <summary>A row of an <see cref="ExcelTable"/>: the table and the row index, with the table's cell readers.</summary>
/// <param name="Table">The table.</param>
/// <param name="Index">The row index.</param>
public readonly record struct ExcelRow(ExcelTable Table, int Index)
{
    /// <summary>A cell by column name as text; empty when the column or the cell does not exist.</summary>
    public string this[string column] => Table.GetString(Index, column);

    /// <summary>The number of cells of the row as written in the file.</summary>
    public int CellCount => Table.GetCellCount(Index);

    /// <summary>See <see cref="ExcelTable.GetSpan(int, int)"/>.</summary>
    public ReadOnlySpan<char> GetSpan(int column) => Table.GetSpan(Index, column);

    /// <summary>See <see cref="ExcelTable.GetString(int, int)"/>.</summary>
    public string GetString(int column) => Table.GetString(Index, column);

    /// <summary>See <see cref="ExcelTable.GetString(int, string)"/>.</summary>
    public string GetString(string column) => Table.GetString(Index, column);

    /// <summary>See <see cref="ExcelTable.GetInt64(int, int)"/>.</summary>
    public long GetInt64(int column) => Table.GetInt64(Index, column);

    /// <summary>See <see cref="ExcelTable.GetInt64(int, string)"/>.</summary>
    public long GetInt64(string column) => Table.GetInt64(Index, column);

    /// <summary>See <see cref="ExcelTable.GetInt32(int, int)"/>.</summary>
    public int GetInt32(int column) => Table.GetInt32(Index, column);

    /// <summary>See <see cref="ExcelTable.GetInt32(int, string)"/>.</summary>
    public int GetInt32(string column) => Table.GetInt32(Index, column);

    /// <summary>See <see cref="ExcelTable.GetBoolean(int, int)"/>.</summary>
    public bool GetBoolean(int column) => Table.GetBoolean(Index, column);

    /// <summary>See <see cref="ExcelTable.ParseInt32(int, int)"/>.</summary>
    public int ParseInt32(int column) => Table.ParseInt32(Index, column);
}

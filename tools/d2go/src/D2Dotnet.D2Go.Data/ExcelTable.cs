// Reads excel tables the way d2go's cmd/txttocode does (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Globalization;
using System.Text;

namespace D2Dotnet.D2Go.Data;

/// <summary>
/// A tab-separated excel table (<c>data/global/excel/*.txt</c>): a header row, then one row per line. Reads files the
/// way d2go's <c>cmd/txttocode</c> generator does (<c>bufio.ScanLines</c>, <c>strings.Split(line, "\t")</c>), with a
/// few lenient choices where the generator would panic or emit uncompilable code; see the README.
/// </summary>
internal sealed class ExcelTable
{
    private readonly Dictionary<string, int> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _ignoreCase = new(StringComparer.OrdinalIgnoreCase);

    private ExcelTable(string name, string[] headers, IReadOnlyList<string[]> rows)
    {
        Name = name;
        Headers = headers;
        Rows = rows;
        // Later columns win, as in the generator's map[header]value.
        for (var i = 0; i < headers.Length; i++)
        {
            _exact[headers[i]] = i;
            _ignoreCase[headers[i]] = i;
        }
    }

    /// <summary>The file name, for error messages.</summary>
    public string Name { get; }

    public IReadOnlyList<string> Headers { get; }

    /// <summary>Every line after the header, split on tabs (not padded).</summary>
    public IReadOnlyList<string[]> Rows { get; }

    /// <summary>
    /// The rows d2go's generator keeps: those whose second column is not empty (its "Ignore if Code is empty" rule,
    /// whatever the second column is called). Missing cells of a short row read as empty.
    /// </summary>
    public IEnumerable<string[]> GeneratorRows() => Rows.Where(row => Cell(row, 1).Length != 0);

    public static ExcelTable Parse(string name, byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith(Encoding.UTF8.Preamble))
            span = span[Encoding.UTF8.Preamble.Length..];
        var text = Encoding.UTF8.GetString(span);

        var lines = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            var line = end < 0 ? text[start..] : text[start..end];
            if (line.EndsWith('\r'))
                line = line[..^1];
            lines.Add(line);
            if (end < 0)
                break;
            start = end + 1;
        }

        if (lines.Count == 0)
            throw new InvalidDataException($"{name} is empty.");

        var headers = lines[0].Split('\t');
        var rows = lines.Skip(1).Select(line => line.Split('\t')).ToArray();
        return new ExcelTable(name, headers, rows);
    }

    /// <summary>The index of a column: an exact name match, else a case-insensitive one; the last such column wins.</summary>
    public int? ColumnIndex(string column) =>
        _exact.TryGetValue(column, out var exact) ? exact
        : _ignoreCase.TryGetValue(column, out var folded) ? folded
        : null;

    public bool HasColumn(string column) => ColumnIndex(column) is not null;

    /// <summary>The value of a required column.</summary>
    /// <exception cref="InvalidDataException">The table has no such column.</exception>
    public string Get(string[] row, string column) =>
        Cell(row, ColumnIndex(column) ?? throw new InvalidDataException($"{Name} has no '{column}' column."));

    /// <summary>The value of an optional column, or null when the table has no such column.</summary>
    public string? GetOptional(string[] row, string column) =>
        ColumnIndex(column) is { } index ? Cell(row, index) : null;

    /// <summary>A whole number cell, as d2go's generated code reads it (surrounding blanks ignored); empty is 0.</summary>
    /// <exception cref="InvalidDataException">The cell is not a 32-bit integer.</exception>
    public int GetInt(string[] row, string column) => ParseInt(Get(row, column), column);

    /// <summary>A flag cell: true when it is exactly <c>1</c>, as in d2go's templates.</summary>
    public bool GetFlag(string[] row, string column) => Get(row, column) == "1";

    public int ParseInt(string value, string column)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return 0;
        if (int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            return number;
        throw new InvalidDataException($"{Name}: column '{column}' has '{value}', which is not a whole number.");
    }

    private static string Cell(string[] row, int index) => index < row.Length ? row[index] : "";
}

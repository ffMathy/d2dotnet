using System.Text;
using D2Dotnet.Data;

namespace D2Dotnet.Tests.Data;

public class ExcelTableTests
{
    private static ExcelTable Parse(string text) => ExcelTable.Parse(Encoding.UTF8.GetBytes(text), "test.txt");

    // ---- lines and cells (the map generator's TxtTable and d2go's txttocode rules) ----

    [Fact]
    public void Parse_HeaderAndRows()
    {
        var t = ExcelTable.Parse("Name\tId\tVal\nFoo\t3\t10\nBar\t7\t\n");
        Assert.Equal(["Name", "Id", "Val"], t.Columns);
        Assert.Equal(2, t.RowCount);
        Assert.Equal("Foo", t.GetString(0, "Name"));
        Assert.Equal(3, t.GetInt64(0, "Id"));
        Assert.Equal(0, t.GetInt64(1, "Val")); // empty -> 0
    }

    [Fact]
    public void Parse_DropsCarriageReturnsAndSkipsEmptyLines()
    {
        var t = Parse("A\tB\r\n1\t2\r\n\n\r\n3\t4\r\r");
        Assert.Equal(["A", "B"], t.Columns);
        Assert.Equal(2, t.RowCount);
        Assert.Equal("2", t.GetString(0, "B"));
        Assert.Equal("4", t.GetString(1, "B")); // last line without newline, CRs dropped
    }

    [Fact]
    public void Parse_TrailingNewlineDoesNotAddARow()
    {
        Assert.Equal(1, Parse("A\tB\n1\t2\n").RowCount);
    }

    [Fact]
    public void Parse_KeepsDividerRowsAndLinesOfOnlyTabs_SoRowIndexesAreFileOrder()
    {
        var t = Parse("Name\tId\nNull\t0\nExpansion\t\n\t\nNext\t2\n");
        Assert.Equal(4, t.RowCount);
        Assert.Equal("Expansion", t.GetString(1, "Name"));
        Assert.Equal("", t.GetString(2, "Name"));
        Assert.Equal(3, t.FindRow("Id", 2));
    }

    [Fact]
    public void Parse_StripsUtf8Bom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("ItemType\tCode\nx\ty\n")).ToArray();
        var t = ExcelTable.Parse(bytes, "bom.txt");
        Assert.Equal("ItemType", t.Columns[0]);
        Assert.Equal("x", t.GetString(0, "ItemType"));
        Assert.Equal("ItemType", ExcelTable.Parse("\uFEFFItemType\n").Columns[0]);
    }

    [Fact]
    public void Parse_DecodesUtf8_AndFallsBackToLatin1()
    {
        Assert.Equal("Höhle", ExcelTable.Parse(Encoding.UTF8.GetBytes("Name\nHöhle\n")).GetString(0, 0));
        // Classic tables are Windows-1252: 0x92 is not UTF-8, so every byte maps to one char.
        var classic = ExcelTable.Parse([.. "Name\nHunter"u8, 0x92, .. "s Bow\n"u8]);
        Assert.Equal("Hunter\u0092s Bow", classic.GetString(0, 0));
    }

    [Fact]
    public void Parse_EmptyTextHasNoColumnsAndNoRows()
    {
        var t = Parse("");
        Assert.Empty(t.Columns);
        Assert.Equal(0, t.RowCount);
        Assert.Equal("", t.GetString(0, "A"));
    }

    [Fact]
    public void Parse_KeepsCellsVerbatim()
    {
        var t = Parse("name\tcode\nBroad Axe \t bax\n");
        Assert.Equal("Broad Axe ", t.GetString(0, "name"));
        Assert.Equal(" bax", t.GetString(0, "code"));
    }

    [Fact]
    public void Cells_ShortRowsAndUnknownColumnsOrRowsReadAsEmpty()
    {
        var t = Parse("A\tB\tC\n1\n1\t2\t3\t4\n");
        Assert.Equal(1, t.GetCellCount(0));
        Assert.Equal(4, t.GetCellCount(1));
        Assert.Equal("", t.GetString(0, "C"));
        Assert.Equal("4", t.GetString(1, 3)); // an extra cell is still readable by index
        Assert.Equal("", t.GetString(0, "Missing"));
        Assert.Equal("", t.GetString(5, "A"));
        Assert.Equal("", t.GetString(-1, "A"));
        Assert.True(t.GetSpan(0, -1).IsEmpty);
        Assert.True(t.IsEmpty(0, 2));
        Assert.Equal(0, t.GetInt64(9, "A"));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.GetCellCount(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => t[2]);
    }

    // ---- columns ----

    [Fact]
    public void Columns_AreLookedUpByName_SoExtraColumnsAreIgnored()
    {
        var t = ExcelTable.Parse("*Extra\tName\tId\nx\tFoo\t3\n");
        Assert.Equal("Foo", t.GetString(0, "Name"));
        Assert.Equal(3, t.GetInt64(0, "Id"));
        Assert.False(t.HasColumn("Missing"));
        Assert.Equal(-1, t.GetColumnIndex("Missing"));
    }

    [Fact]
    public void Columns_PreferExactThenCaseInsensitive_LastWins()
    {
        var t = Parse("Name\tname\tCode\tCode\tX\n0\t1\t2\t3\t4\n");
        Assert.Equal("0", t.GetString(0, "Name"));
        Assert.Equal("1", t.GetString(0, "name"));
        Assert.Equal("1", t.GetString(0, "NAME")); // no exact match: the last case-insensitive one
        Assert.Equal("3", t.GetString(0, "Code")); // a repeated name maps to its last column
        Assert.Equal("4", t.GetString(0, "x"));
        Assert.True(t.HasColumn("cOdE"));
        Assert.Equal(3, t.GetColumnIndex("Code".AsSpan()));
        Assert.Equal(1, t.GetColumnIndex("NAME".AsSpan()));
        Assert.Equal(-1, t.GetColumnIndex("Nope".AsSpan()));
    }

    [Fact]
    public void FindColumn_TakesTheFirstNameTheTableHas()
    {
        var d2r = Parse("Class\tName\t*ID\nx\ty\t1\n");
        var classic = Parse("Name\tId\nx\t1\n");
        Assert.Equal(2, d2r.FindColumn("*ID", "Id"));
        Assert.Equal(1, classic.FindColumn("*ID", "Id"));
        Assert.Equal(-1, classic.FindColumn("A", "B"));
    }

    [Fact]
    public void GetRequiredColumnIndex_NamesTheTableAndColumn()
    {
        var t = Parse("A\tB\n1\t2\n");
        Assert.Equal(1, t.GetRequiredColumnIndex("b"));
        var error = Assert.Throws<InvalidDataException>(() => t.GetRequiredColumnIndex("C"));
        Assert.Contains("'C'", error.Message);
        Assert.Contains("test.txt", error.Message);
    }

    // ---- numbers ----

    [Theory]
    [InlineData("12", true, 12)]
    [InlineData("-1", true, -1)]
    [InlineData("+7", true, 7)]
    [InlineData("1_000", true, 1000)]
    [InlineData("9223372036854775807", true, long.MaxValue)]
    [InlineData("-9223372036854775808", true, long.MinValue)]
    [InlineData("9223372036854775808", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(" 5", false, 0)] // no blanks
    [InlineData("5 ", false, 0)]
    [InlineData("+", false, 0)]
    [InlineData("-", false, 0)]
    [InlineData("_1", false, 0)]
    [InlineData("1_", false, 0)]
    [InlineData("1__0", false, 0)]
    [InlineData("1.5", false, 0)]
    [InlineData("0x10", false, 0)]
    [InlineData("abc", false, 0)]
    public void TryParseInt64_FollowsZigParseInt(string text, bool ok, long expected)
    {
        Assert.Equal(ok, ExcelTable.TryParseInt64(text, out var value));
        Assert.Equal(expected, value);

        var t = Parse($"A\tN\nx\t{text}\n");
        Assert.Equal(ok, t.TryGetInt64(0, "N", out var cell));
        Assert.Equal(expected, cell);
        Assert.Equal(expected, t.GetInt64(0, "N")); // non-numbers read as 0
    }

    [Fact]
    public void GetInt32_TruncatesLikeTheEngine()
    {
        var t = Parse("N\n4294967297\n-1\n");
        Assert.Equal(1, t.GetInt32(0, "N"));
        Assert.Equal(-1, t.GetInt32(1, 0));
    }

    [Fact]
    public void GetBoolean_IsANonZeroNumber()
    {
        var t = Parse("F\n1\n0\n\n2\nyes\n");
        Assert.Equal([true, false, true, false], Enumerable.Range(0, t.RowCount).Select(r => t.GetBoolean(r, "F")));
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("-1", -1)]
    [InlineData("+7", 7)]
    [InlineData(" 5 ", 5)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void ParseInt32_ReadsWholeNumbers(string cell, int expected)
    {
        var t = Parse($"A\tN\nx\t{cell}\n");
        Assert.Equal(expected, t.ParseInt32(0, t.GetColumnIndex("N")));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData("0x10")]
    [InlineData("1_000")]
    [InlineData("99999999999")]
    public void ParseInt32_RejectsOtherText(string cell)
    {
        var t = Parse($"A\tN\nx\t{cell}\n");
        var error = Assert.Throws<InvalidDataException>(() => t.ParseInt32(0, 1));
        Assert.Contains("'N'", error.Message);
        Assert.Contains("test.txt", error.Message);
    }

    // ---- row lookups (the engine's GetLine) ----

    [Fact]
    public void FindRow_FirstNumericMatchWins()
    {
        var t = Parse("Name\tId\na\tx\nb\t7\nc\t7\nd\t 8\ne\t8\n");
        Assert.Equal(1, t.FindRow("Id", 7));
        Assert.Equal(4, t.FindRow("Id", 8)); // " 8" is not a number
        Assert.Null(t.FindRow("Id", 99));
        Assert.Null(t.FindRow("Missing", 7));
        Assert.Null(t.FindRow(-1, 7));
        Assert.Equal(1, t.FindRow(1, 7)); // cached index
    }

    [Fact]
    public void FindRow_IsThreadSafe()
    {
        var text = new StringBuilder("Id\n");
        for (var i = 0; i < 1000; i++)
            text.Append(i).Append('\n');
        var t = ExcelTable.Parse(text.ToString());
        Parallel.For(0, 1000, i => Assert.Equal(i, t.FindRow("Id", i)));
    }

    // ---- rows ----

    [Fact]
    public void Rows_AreViewsOfTheTable()
    {
        var t = Parse("Name\tN\tF\na\t1\t1\nb\t-2\t0\n");
        var rows = t.Rows.ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(1, rows[1].Index);
        Assert.Equal("b", rows[1]["Name"]);
        Assert.Equal("b", rows[1].GetString(0));
        Assert.Equal("b", rows[1].GetSpan(0).ToString());
        Assert.Equal(-2, rows[1].GetInt64("N"));
        Assert.Equal(-2, rows[1].GetInt32(1));
        Assert.Equal(-2, rows[1].ParseInt32(1));
        Assert.True(rows[0].GetBoolean(2));
        Assert.Equal(3, rows[0].CellCount);
        Assert.Equal(t[1], rows[1]);
    }

    [Fact]
    public void ToString_NamesTheTable()
    {
        Assert.Equal("test.txt (2 columns, 1 rows)", Parse("A\tB\n1\t2\n").ToString());
    }
}

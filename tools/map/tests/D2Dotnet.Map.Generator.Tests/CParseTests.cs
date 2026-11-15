using System.Text;
using D2Dotnet.Map.Generator.Cli;

namespace D2Dotnet.Map.Generator.Tests;

public class CParseTests
{
    [Theory]
    [InlineData("42", 42)]
    [InlineData("  -7xyz", -7)]
    [InlineData("+3", 3)]
    [InlineData("abc", 0)]
    [InlineData("", 0)]
    [InlineData("-", 0)]
    [InlineData("99999999999", int.MaxValue)]
    [InlineData("-99999999999", int.MinValue)]
    [InlineData("\t\n 12 34", 12)]
    public void Atoi(string input, int expected) => Assert.Equal(expected, CParse.Atoi(input));

    [Theory]
    [InlineData("1122334", 1122334u, 1)]
    [InlineData("  4278255360", 4278255360u, 1)]
    [InlineData("-1", 4294967295u, 1)]
    [InlineData("99999999999", 4294967295u, 1)]
    [InlineData("x1", 777u, 0)]
    [InlineData("", 777u, 0)]
    public void SscanfUnsigned(string input, uint expected, int expectedReturn)
    {
        uint v = 777;
        Assert.Equal(expectedReturn, CParse.SscanfUnsigned(input, ref v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("$seed 10\n", 10u, 2)]
    [InlineData("$seed\t\t123456\n", 123456u, 2)]
    [InlineData("$seed\n", 5u, 1)]
    [InlineData("$seed abc\n", 5u, 1)]
    [InlineData("$seedy 9", 9u, 2)]
    [InlineData("   ", 5u, -1)]
    public void SscanfTokenUnsigned(string input, uint expected, int expectedReturn)
    {
        uint v = 5;
        Assert.Equal(expectedReturn, CParse.SscanfTokenUnsigned(input, ref v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("$difficulty 2\n", 2, 2)]
    [InlineData("$act -1\n", -1, 2)]
    [InlineData("$act\n", 3, 1)]
    [InlineData("$act 4junk\n", 4, 2)]
    public void SscanfTokenSigned(string input, int expected, int expectedReturn)
    {
        var v = 3;
        Assert.Equal(expectedReturn, CParse.SscanfTokenSigned(input, ref v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("--seed", "--seed", true)]
    [InlineData("--seedling", "--seed", true)]
    [InlineData("-something", "-s", true)]
    [InlineData("-", "-s", false)]
    [InlineData("$mapx", "$map", true)]
    [InlineData(" $map", "$map", false)]
    public void StartsWithIsStrncmpPrefix(string value, string search, bool expected) => Assert.Equal(expected, CParse.StartsWith(value, search));

    [Fact]
    public void FgetsSplitsLinesAndLongLines()
    {
        var input = "$seed 1\n" + new string('a', 1030) + "\n$exit";
        var r = new CLineReader(new MemoryStream(Encoding.Latin1.GetBytes(input)), 1024);
        Assert.Equal("$seed 1\n", r.Fgets());
        Assert.Equal(new string('a', 1023), r.Fgets());
        Assert.Equal("aaaaaaa\n", r.Fgets());
        Assert.Equal("$exit", r.Fgets());
        Assert.Null(r.Fgets());
    }

    [Fact]
    public void FgetsTextModeTranslatesCrLfAndKeepsLoneCr()
    {
        var r = new CLineReader(new MemoryStream(Encoding.Latin1.GetBytes("$seed 1\r\n$map\rx\r\n$exit\r")), 1024, textMode: true);
        Assert.Equal("$seed 1\n", r.Fgets());
        Assert.Equal("$map\rx\n", r.Fgets());
        Assert.Equal("$exit\r", r.Fgets());
        Assert.Null(r.Fgets());
    }

    [Fact]
    public void FgetsTextModeStopsAtCtrlZ()
    {
        var r = new CLineReader(new MemoryStream(Encoding.Latin1.GetBytes("$seed 1\n$m\u001aap\n$exit\n")), 1024, textMode: true);
        Assert.Equal("$seed 1\n", r.Fgets());
        Assert.Equal("$m", r.Fgets());
        Assert.Null(r.Fgets());
        Assert.Null(r.Fgets());
    }

    [Fact]
    public void FgetsBinaryModeKeepsCrAndCtrlZ()
    {
        var r = new CLineReader(new MemoryStream(Encoding.Latin1.GetBytes("a\r\n\u001ab\n")), 1024);
        Assert.Equal("a\r\n", r.Fgets());
        Assert.Equal("\u001ab\n", r.Fgets());
        Assert.Null(r.Fgets());
    }
}

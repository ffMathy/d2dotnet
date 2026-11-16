using D2Dotnet.MapServer.Data;

namespace D2Dotnet.MapServer.Tests.Data;

public class JsNumberTests
{
    [Theory]
    [InlineData("0x3607656c", 0x3607656c)]
    [InlineData("0X10", 16)]
    [InlineData("906454380", 906454380)]
    [InlineData(" 12 ", 12)]
    [InlineData("", 0)]
    [InlineData("  ", 0)]
    [InlineData("1e3", 1000)]
    [InlineData("1.5", 1.5)]
    [InlineData(".5", 0.5)]
    [InlineData("5.", 5)]
    [InlineData("-7", -7)]
    [InlineData("+7", 7)]
    [InlineData("0b101", 5)]
    [InlineData("0o17", 15)]
    public void ParsesLikeJavaScriptNumber(string input, double expected) => Assert.Equal(expected, JsNumber.Parse(input));

    [Theory]
    [InlineData("abc")]
    [InlineData("12abc")]
    [InlineData("0x")]
    [InlineData("-0x10")]
    [InlineData("1,000")]
    [InlineData("undefined")]
    [InlineData("1e")]
    [InlineData("\u0661")]
    [InlineData("\u0663")]
    [InlineData("1\u0663")]
    [InlineData("\uFF11")]
    [InlineData("1e\u0661")]
    public void InvalidNumbersAreNaN(string input) => Assert.True(double.IsNaN(JsNumber.Parse(input)));

    [Fact]
    public void Infinity()
    {
        Assert.Equal(double.PositiveInfinity, JsNumber.Parse("Infinity"));
        Assert.Equal(double.NegativeInfinity, JsNumber.Parse("-Infinity"));
    }

    [Theory]
    [InlineData(906454380, "906454380")]
    [InlineData(1.5, "1.5")]
    [InlineData(0, "0")]
    [InlineData(-3, "-3")]
    public void FormatsLikeJavaScript(double value, string expected) => Assert.Equal(expected, JsNumber.ToJsString(value));
}

public class ActDifficultyTests
{
    [Theory]
    [InlineData("0", Difficulty.Normal)]
    [InlineData("2", Difficulty.Hell)]
    [InlineData("0x1", Difficulty.Nightmare)]
    [InlineData("1.0", Difficulty.Nightmare)]
    [InlineData("normal", Difficulty.Normal)]
    [InlineData("NightMare", Difficulty.Nightmare)]
    [InlineData("Hell", Difficulty.Hell)]
    [InlineData(" ", Difficulty.Normal)]
    public void DifficultyFromString(string input, Difficulty expected) => Assert.Equal(expected, DifficultyUtil.FromString(input));

    [Theory]
    [InlineData(null)]
    [InlineData("3")]
    [InlineData("1.5")]
    [InlineData("-1")]
    [InlineData("easy")]
    [InlineData("Infinity")]
    [InlineData("\u0661")]
    public void InvalidDifficulty(string? input) => Assert.Null(DifficultyUtil.FromString(input));

    [Theory]
    [InlineData("0", Act.ActI)]
    [InlineData("4", Act.ActV)]
    [InlineData("ActI", Act.ActI)]
    [InlineData("actii", Act.ActII)]
    [InlineData("ACTIII", Act.ActIII)]
    [InlineData("ActIV", Act.ActIV)]
    [InlineData("ActV", Act.ActV)]
    public void ActFromString(string input, Act expected) => Assert.Equal(expected, ActUtil.FromString(input));

    [Theory]
    [InlineData(null)]
    [InlineData("5")]
    [InlineData("Act5")]
    [InlineData("ActVI")]
    public void InvalidAct(string? input) => Assert.Null(ActUtil.FromString(input));

    [Theory]
    [InlineData(0, Act.ActI)]
    [InlineData(39, Act.ActI)]
    [InlineData(40, Act.ActII)]
    [InlineData(74, Act.ActII)]
    [InlineData(75, Act.ActIII)]
    [InlineData(103, Act.ActIV)]
    [InlineData(108, Act.ActIV)]
    [InlineData(109, Act.ActV)]
    [InlineData(141, Act.ActV)]
    [InlineData(500, Act.ActV)]
    [InlineData(double.NaN, Act.ActV)]
    public void ActFromLevel(double level, Act expected) => Assert.Equal(expected, ActUtil.FromLevel(level));
}

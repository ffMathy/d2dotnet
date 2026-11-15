using D2Dotnet.Map.Data;

namespace D2Dotnet.Map.Tests.Data;

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

public class MpqDataTests
{
    [Fact]
    public void LangTablesFollowUpstreamRules()
    {
        var data = new Diablo2MpqData();
        data.Lang.Classic.Add("a", 1, "Alpha");
        data.Lang.Classic.Add("x", 2, "ignored key");
        data.Lang.Classic.Add("blank", 3, "   ");
        data.Lang.Classic.Add("a", 4, " Alpha ");
        data.Lang.Patch.Add("p", 5, "Patch");
        data.Lang.Expansion.Add("a", 6, "Expansion Alpha");

        Assert.Equal("Alpha", data.T(1));
        Assert.Equal("ignored key", data.T(2));
        Assert.Equal("   ", data.T(3));
        Assert.Equal(" Alpha ", data.T(4));
        Assert.Equal("Alpha", data.Lang.Classic.ByKey["a"]);
        Assert.False(data.Lang.Classic.ByKey.ContainsKey("x"));
        Assert.False(data.Lang.Classic.ByKey.ContainsKey("blank"));

        Assert.Equal("Patch", data.T(Diablo2MpqLang.PatchOffset + 5));
        Assert.Equal("Expansion Alpha", data.T(Diablo2MpqLang.ExpansionOffset + 6));
        Assert.Equal("Expansion Alpha", data.T("a"));
        Assert.Equal("Patch", data.T("p"));
        Assert.Null(data.T("missing"));
        Assert.Null(data.T((int?)null));
        Assert.Equal(3, data.Lang.Size);

        data.Lang.Classic.Add("bad", 5382, "bad");
        Assert.Null(data.T(5382));
    }

    [Fact]
    public void MonsterNames()
    {
        var data = FakeGame.MpqData();
        Assert.Equal(3, data.Monsters.Size);
        Assert.Equal("Zombie ", data.Monsters.Name(0));
        Assert.Null(data.Monsters.Name(2));
        Assert.Equal("Bishibosh", data.Monsters.Name(3));
        Assert.Equal("Coldcrow", data.Monsters.Name(5));
        Assert.Equal("Lister the Tormentor", data.Monsters.SuperUniqueName(65));
        Assert.Equal(66, data.Monsters.SuperUniques.Count);
        Assert.Null(data.Monsters.SuperUniqueName(66));

        data.Monsters.AddState(1, [1, 2]);
        Assert.Equal([1, 2], data.Monsters.GetState(1));
        Assert.Empty(data.Monsters.GetState(2));
        Assert.Throws<InvalidOperationException>(() => data.Monsters.AddState(77, [1]));
    }
}

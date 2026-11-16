using D2Dotnet.Game;

namespace D2Dotnet.Tests.Game;

public class MainPlayerTests
{
    private static MainPlayer WithStats(params Stat[] stats) => new() { Stats = new StatList(stats) };

    [Fact]
    public void GetStat_PrefersStats_ThenBaseStats()
    {
        var pu = new MainPlayer
        {
            Stats = new StatList([new Stat(StatId.Level, 5)]),
            BaseStats = new StatList([new Stat(StatId.Level, 9), new Stat(StatId.Strength, 30)]),
        };

        Assert.Equal(5, pu.GetStat(StatId.Level));
        Assert.Equal(5, pu.Level);
        Assert.Equal(30, pu.GetStat(StatId.Strength));
        Assert.Null(pu.GetStat(StatId.Level, layer: 1));
        Assert.Null(pu.GetStat(StatId.Dexterity));
    }

    [Fact]
    public void Gold_IsAddedIn64Bits()
    {
        Assert.Equal(50000, WithStats(new Stat(StatId.Level, 5)).MaxGold);
        Assert.Equal(0, new MainPlayer().MaxGold);
        Assert.Equal(300, WithStats(new Stat(StatId.Gold, 100), new Stat(StatId.StashGold, 200)).TotalGold);
        Assert.Equal(100, WithStats(new Stat(StatId.Gold, 100)).TotalGold);
        Assert.Equal(2L * int.MaxValue, WithStats(new Stat(StatId.Gold, int.MaxValue), new Stat(StatId.StashGold, int.MaxValue)).TotalGold);
    }

    [Fact]
    public void Experience_IsUnsigned()
    {
        Assert.Equal(3_520_485_254u, WithStats(new Stat(StatId.Experience, unchecked((int)3_520_485_254))).Experience);
        Assert.Equal(0u, new MainPlayer().Experience);
    }

    [Theory]
    [InlineData(50, 200, 25)]
    [InlineData(199, 200, 99)]
    [InlineData(200, 200, 100)]
    [InlineData(300, 200, 150)]
    [InlineData(-10, 200, -5)]
    public void LifeAndManaPercent_AreTruncated(int value, int max, int expected)
    {
        var pu = WithStats(new Stat(StatId.Life, value), new Stat(StatId.MaxLife, max), new Stat(StatId.Mana, value), new Stat(StatId.MaxMana, max));
        Assert.Equal(expected, pu.LifePercent);
        Assert.Equal(expected, pu.ManaPercent);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(0, 0)]
    [InlineData(5, -1)]
    public void LifeAndManaPercent_WithoutAMax_AreNull(int value, int max)
    {
        // d2go divided by zero and returned the minimum integer.
        var pu = WithStats(new Stat(StatId.Life, value), new Stat(StatId.MaxLife, max), new Stat(StatId.Mana, value), new Stat(StatId.MaxMana, max));
        Assert.Null(pu.LifePercent);
        Assert.Null(pu.ManaPercent);
        Assert.Null(new MainPlayer().LifePercent);
    }

    [Fact]
    public void LifePercent_UsesBaseStatsWhenStatsLackThem()
    {
        var pu = new MainPlayer { BaseStats = new StatList([new Stat(StatId.Life, 30), new Stat(StatId.MaxLife, 60)]) };
        Assert.Equal(50, pu.LifePercent);
    }

    // Expected frames come from running d2go's Go code.
    [Theory]
    [InlineData(0u, -500, 11)]
    [InlineData(0u, -120, -1)]
    [InlineData(0u, -50, 146)]
    [InlineData(0u, 0, 19)]
    [InlineData(0u, 9, 18)]
    [InlineData(0u, 20, 17)]
    [InlineData(0u, 37, 15)]
    [InlineData(0u, 63, 14)]
    [InlineData(0u, 105, 12)]
    [InlineData(0u, 200, 11)]
    [InlineData(0u, 1000, 11)]
    [InlineData(1u, 0, 13)]
    [InlineData(1u, 9, 12)]
    [InlineData(1u, 20, 11)]
    [InlineData(1u, 37, 10)]
    [InlineData(1u, 63, 9)]
    [InlineData(1u, 105, 8)]
    [InlineData(1u, 200, 7)]
    [InlineData(2u, 0, 15)]
    [InlineData(2u, 105, 10)]
    [InlineData(3u, 0, 15)]
    [InlineData(3u, 200, 9)]
    [InlineData(4u, 0, 13)]
    [InlineData(4u, 63, 9)]
    [InlineData(5u, -50, 109)]
    [InlineData(5u, 0, 14)]
    [InlineData(5u, 37, 11)]
    [InlineData(6u, 0, 16)]
    [InlineData(6u, 105, 10)]
    [InlineData(6u, 1000, 9)]
    [InlineData(99u, 0, 15)]
    [InlineData(99u, 63, 11)]
    public void CastingFrames_MatchD2go(uint playerClass, int fasterCastRate, int expected)
    {
        var pu = new MainPlayer { Class = (CharacterClass)playerClass, Stats = new StatList([new Stat(StatId.FasterCastRate, fasterCastRate)]) };
        Assert.Equal(expected, pu.CastingFrames);
    }

    [Fact]
    public void HasDebuff_IsTrueForAnyCurseState()
    {
        Assert.False(new MainPlayer().HasDebuff);
        Assert.False(new MainPlayer { States = [StateId.Freeze, StateId.ResistFire] }.HasDebuff);
        Assert.True(new MainPlayer { States = [StateId.Freeze, StateId.AmplifyDamage] }.HasDebuff);
        Assert.True(new MainPlayer { States = [StateId.DefenseCurse] }.HasDebuff);
    }

    [Fact]
    public void Defaults_AreEmpty()
    {
        var pu = new MainPlayer();
        Assert.Equal("", pu.Name);
        Assert.Empty(pu.Stats);
        Assert.Empty(pu.BaseStats);
        Assert.Empty(pu.Skills);
        Assert.Empty(pu.States);
        Assert.Empty(pu.AvailableWaypoints);
        Assert.Equal(CharacterClass.Amazon, pu.Class);
    }
}

public class StatListTests
{
    [Fact]
    public void Lookups_FindTheFirstStatOfAnIdAndLayer()
    {
        var stats = new StatList([new Stat(StatId.Strength, 5), new Stat(StatId.Strength, 6), new Stat(StatId.Strength, 7, 2)]);
        Assert.True(stats.TryGet(StatId.Strength, 0, out var s));
        Assert.Equal(5, s.Value);
        Assert.Equal(7, stats.GetValue(StatId.Strength, 2));
        Assert.True(stats.Contains(StatId.Strength, 2));
        Assert.False(stats.Contains(StatId.Strength, 1));
        Assert.False(stats.TryGet(StatId.Energy, 0, out s));
        Assert.Equal(default, s);
        Assert.Equal(3, stats.Count);
        Assert.Equal(new Stat(StatId.Strength, 6), stats[1]);
    }

    [Fact]
    public void Lists_CompareByValue()
    {
        Assert.Equal(new StatList([new Stat(StatId.Life, 1)]), new StatList([new Stat(StatId.Life, 1)]));
        Assert.Equal(new StatList([new Stat(StatId.Life, 1)]).GetHashCode(), new StatList([new Stat(StatId.Life, 1)]).GetHashCode());
        Assert.NotEqual(new StatList([new Stat(StatId.Life, 1)]), new StatList([new Stat(StatId.Life, 2)]));
        Assert.False(StatList.Empty.Equals(null));
        Assert.Equal(new MainPlayer { Stats = new StatList([new Stat(StatId.Life, 1)]) }, new MainPlayer { Stats = new StatList([new Stat(StatId.Life, 1)]) });
    }

    [Fact]
    public void UnsignedValue_ReinterpretsTheField() => Assert.Equal(uint.MaxValue, new Stat(StatId.Experience, -1).UnsignedValue);
}

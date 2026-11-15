using D2Dotnet.Map.Generator.Data;

namespace D2Dotnet.Map.Generator.Tests;

/// <summary>The d2data tables must contain every entry of the C headers with the same values.</summary>
public class DataTableTests
{
    private static void AssertEnumMatches<TEnum>((string Name, int Value)[] expected) where TEnum : struct, Enum
    {
        var actual = Enum.GetNames<TEnum>().Select(n => (n, Convert.ToInt32(Enum.Parse<TEnum>(n), System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected.OrderBy(e => e.Name, StringComparer.Ordinal), actual.OrderBy(a => a.n, StringComparer.Ordinal));
    }

    [Fact]
    public void GameObjectEnumIsComplete()
    {
        Assert.Equal(572, CTables.GameObject.Length);
        AssertEnumMatches<GameObject>(CTables.GameObject);
    }

    [Fact]
    public void NpcCodeEnumIsComplete()
    {
        Assert.Equal(736, CTables.NpcCode.Length);
        AssertEnumMatches<NpcCode>(CTables.NpcCode);
        Assert.Equal(734, (int)NpcCode.Invalid);
        Assert.Equal(0xFFFF, (int)NpcCode.NpcNotApplicable);
    }

    [Fact]
    public void AreaLevelEnumIsComplete()
    {
        Assert.Equal(142, CTables.AreaLevel.Length);
        AssertEnumMatches<AreaLevel>(CTables.AreaLevel);
    }

    [Fact]
    public void ActTypeEnumIsComplete() => AssertEnumMatches<ActType>(CTables.ActType);

    private static void AssertPredicateMatches(Func<int, bool> predicate, int[] expectedTrue)
    {
        var expected = expectedTrue.ToHashSet();
        for (var i = -2; i < 70000; i++)
        {
            Assert.True(expected.Contains(i) == predicate(i), $"id {i}: expected {expected.Contains(i)}");
        }
    }

    [Fact]
    public void ObjectIsDoorMatchesC()
    {
        Assert.Equal(47, CTables.ObjectIsDoor.Length);
        AssertPredicateMatches(GameObjects.IsDoor, CTables.ObjectIsDoor);
    }

    [Fact]
    public void ObjectIsUselessMatchesC()
    {
        Assert.Equal(191, CTables.ObjectIsUseless.Length);
        AssertPredicateMatches(GameObjects.IsUseless, CTables.ObjectIsUseless);
    }

    [Fact]
    public void NpcIsUselessMatchesC()
    {
        Assert.Equal(134, CTables.NpcIsUseless.Length);
        AssertPredicateMatches(Npcs.IsUseless, CTables.NpcIsUseless);
    }
}

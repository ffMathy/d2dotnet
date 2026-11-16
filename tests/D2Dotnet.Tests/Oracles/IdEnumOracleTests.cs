using System.Globalization;
using D2Dotnet.Data;
using static D2Dotnet.Tests.Oracles.GoSource;

namespace D2Dotnet.Tests.Oracles;

/// <summary>
/// The id enums carry d2go's values exactly: every Go constant of the matching Go type has a member with the same value
/// and the same name (ignoring case, the d2go port's capitalisation, apart from the listed renames), and nothing else.
/// </summary>
public class IdEnumOracleTests
{
    private static IReadOnlyList<GoConst> PackageConstants(string package) =>
        Directory.EnumerateFiles(Path.Combine(Fixtures.D2Go, "pkg", "data", package), "*.go")
            .Order(StringComparer.Ordinal)
            .SelectMany(f => Constants(File.ReadAllText(f), Path.GetFileName(f)))
            .ToArray();

    private static void AssertSameMembers<TEnum>(IEnumerable<GoConst> constants, IReadOnlyDictionary<string, string>? renames = null)
        where TEnum : struct, Enum
    {
        renames ??= new Dictionary<string, string>();
        var expected = constants.ToDictionary(c => renames.TryGetValue(c.Name, out var renamed) ? renamed : c.Name, c => (long)c.Value);
        var actual = Enum.GetNames<TEnum>().ToDictionary(n => n, n => Convert.ToInt64(Enum.Parse<TEnum>(n), CultureInfo.InvariantCulture));

        // An exact name first, else the one member whose name differs only in case (Go has StairsR and StairSR).
        var problems = new List<string>();
        var matched = new HashSet<string>();
        foreach (var (name, value) in expected)
        {
            var member = actual.ContainsKey(name) ? name
                : actual.Keys.Where(n => !expected.ContainsKey(n) && string.Equals(n, name, StringComparison.OrdinalIgnoreCase)).SingleOrDefault();
            if (member is null)
                problems.Add($"{typeof(TEnum).Name}.{name} is missing (Go value {value})");
            else if (actual[member] != value)
                problems.Add($"{typeof(TEnum).Name}.{member}: Go {value} != C# {actual[member]}");
            if (member is not null)
                matched.Add(member);
        }

        problems.AddRange(actual.Keys.Where(n => !matched.Contains(n)).Select(n => $"{typeof(TEnum).Name}.{n} is not a Go constant"));
        foreach (var (goName, name) in renames)
            Assert.True(Enum.IsDefined(typeof(TEnum), name), $"rename target {name} of {goName}");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(100)));
        Assert.True(expected.Count > 0);
    }

    [Fact]
    public void AreaId_IsGoAreaId()
    {
        AssertSameMembers<AreaId>(PackageConstants("area").Where(c => c.Type == "ID"), new Dictionary<string, string>
        {
            ["ThePandemoniumFortress"] = nameof(AreaId.PandemoniumFortress),
            ["TheAncientsWay"] = nameof(AreaId.AncientsWay),
            ["TheWorldStoneKeepLevel1"] = nameof(AreaId.WorldstoneKeepLevel1),
            ["TheWorldStoneKeepLevel2"] = nameof(AreaId.WorldstoneKeepLevel2),
            ["TheWorldStoneKeepLevel3"] = nameof(AreaId.WorldstoneKeepLevel3),
            ["TheWorldstoneChamber"] = nameof(AreaId.WorldstoneChamber),
        });
    }

    [Fact]
    public void AreaId_AgreesWithClassicLevelsTxt()
    {
        // libd2's 1.14d levels.txt: one row per id up to Uber Tristram, which the map generator's LevelIds also numbered.
        var levels = new GameDatabase(Fixtures.LibD2Files).Levels;
        Assert.Equal(Enum.GetValues<AreaId>().Where(a => a <= AreaId.UberTristram), levels.Select(l => l.Id));
    }

    [Fact]
    public void MonsterId_IsGoNpcId() => AssertSameMembers<MonsterId>(PackageConstants("npc").Where(c => c.Type == "ID"));

    [Fact]
    public void ObjectId_IsGoObjectName() => AssertSameMembers<ObjectId>(
        PackageConstants("object").Where(c => c.Type == "Name" || (c.Type == null && c.File == "ids.go")),
        new Dictionary<string, string> { ["NotApplicable"] = nameof(ObjectId.None) });

    [Fact]
    public void SkillId_IsGoSkillId() => AssertSameMembers<SkillId>(
        PackageConstants("skill").Where(c => c.Type == "ID"),
        new Dictionary<string, string> { ["Unset"] = nameof(SkillId.None) });

    [Fact]
    public void StatId_IsGoStatId() => AssertSameMembers<StatId>(PackageConstants("stat").Where(c => c.Type == "ID"));

    [Fact]
    public void StateId_IsGoState() => AssertSameMembers<StateId>(
        PackageConstants("state").Where(c => c.Type == "State"),
        new Dictionary<string, string>
        {
            ["Healthpot"] = nameof(StateId.HealthPotion),
            ["Manapot"] = nameof(StateId.ManaPotion),
            ["Staminapot"] = nameof(StateId.StaminaPotion),
            ["Monfrenzy"] = nameof(StateId.MonsterFrenzy),
            ["Corpseexp"] = nameof(StateId.CorpseExplosion),
            ["Delerium"] = nameof(StateId.Delirium),
            ["PassiveResistltng"] = nameof(StateId.PassiveResistLightning),
            ["Invis"] = nameof(StateId.Invisible),
        });

    [Fact]
    public void QuestId_IsGoQuest() => AssertSameMembers<QuestId>(PackageConstants("quest").Where(c => c.Type == "Quest"));

    [Fact]
    public void ItemId_IsTheIndexOfGoItemNames()
    {
        var names = StringSlice(Fixtures.D2GoFile("pkg/data/item/name.go"), "Names");
        Assert.Equal(names, Enum.GetValues<ItemId>().Select(id => id.ToString()));
        Assert.Equal(Enumerable.Range(0, names.Count), Enum.GetValues<ItemId>().Select(id => (int)id));
    }

    [Fact]
    public void ItemTypeCode_IsGoItemTypeConstants()
    {
        var expected = Constants(Fixtures.D2GoFile("pkg/data/item/itemtypes.go"), "itemtypes.go")
            .ToDictionary(c => c.Name["Type".Length..], c => (string)c.Value);
        var actual = typeof(ItemTypeCode).GetFields()
            .Where(f => f.IsLiteral)
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);
        Assert.Equal(expected.OrderBy(e => e.Key, StringComparer.Ordinal), actual.OrderBy(e => e.Key, StringComparer.Ordinal));
    }
}

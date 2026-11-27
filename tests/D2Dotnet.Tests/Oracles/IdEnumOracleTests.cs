using System.Globalization;
using D2Dotnet.Data;
using static D2Dotnet.Tests.Oracles.GoSource;

namespace D2Dotnet.Tests.Oracles;

/// <summary>
/// The id enums carry d2go's values exactly: every Go constant of the matching Go type has a member with the same value
/// and the same name (ignoring case, the d2go port's capitalisation, apart from the listed renames), and nothing else
/// but the Reign of the Warlock additions an enum allows, all numbered after d2go's last value.
/// </summary>
public class IdEnumOracleTests
{
    private static IReadOnlyList<GoConst> PackageConstants(string package) =>
        Directory.EnumerateFiles(Path.Combine(Fixtures.D2Go, "pkg", "data", package), "*.go")
            .Order(StringComparer.Ordinal)
            .SelectMany(f => Constants(File.ReadAllText(f), Path.GetFileName(f)))
            .ToArray();

    private static void AssertSameMembers<TEnum>(
        IEnumerable<GoConst> constants, IReadOnlyDictionary<string, string>? renames = null, bool allowAdditions = false,
        IReadOnlySet<string>? leftOut = null)
        where TEnum : struct, Enum
    {
        renames ??= new Dictionary<string, string>();
        var goConstants = constants.ToArray();
        foreach (var name in leftOut ?? new HashSet<string>())
            Assert.True(goConstants.Any(c => c.Name == name), $"left-out constant {name} is not a Go constant");
        var expected = goConstants.Where(c => leftOut is null || !leftOut.Contains(c.Name)).ToDictionary(c => renames.TryGetValue(c.Name, out var renamed) ? renamed : c.Name, c => (long)c.Value);
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

        // Reign of the Warlock additions: members d2go does not have, numbered after its last value.
        var lastGoValue = expected.Values.Max();
        problems.AddRange(actual.Keys.Where(n => !matched.Contains(n) && !(allowAdditions && actual[n] > lastGoValue))
            .Select(n => $"{typeof(TEnum).Name}.{n} is not a Go constant"));
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
        },
        allowAdditions: true,
        // d2go's "Maps" ids, which no levels.txt has; Reign of the Warlock's 137 is ColossalSummit.
        leftOut: new HashSet<string> { "MapsAncientTemple", "MapsDesecratedTemple", "MapsFrigidPlateau", "MapsInfernalTrial", "MapsRuinedCitadel" });
    }

    [Fact]
    public void AreaId_AgreesWithClassicLevelsTxt()
    {
        // libd2's 1.14d levels.txt: one row per id up to Uber Tristram, which the map generator's LevelIds also numbered.
        var levels = new GameDatabase(Fixtures.LibD2Files).Levels;
        Assert.Equal(Enum.GetValues<AreaId>().Where(a => a <= AreaId.UberTristram), levels.Select(l => l.Id));
    }

    [Fact]
    public void MonsterId_IsGoNpcId() => AssertSameMembers<MonsterId>(PackageConstants("npc").Where(c => c.Type == "ID"), allowAdditions: true);

    [Fact]
    public void ObjectId_IsGoObjectName() => AssertSameMembers<ObjectId>(
        PackageConstants("object").Where(c => c.Type == "Name" || (c.Type == null && c.File == "ids.go")),
        new Dictionary<string, string> { ["NotApplicable"] = nameof(ObjectId.None) },
        allowAdditions: true);

    [Fact]
    public void SkillId_IsGoSkillId() => AssertSameMembers<SkillId>(
        PackageConstants("skill").Where(c => c.Type == "ID"),
        new Dictionary<string, string> { ["Unset"] = nameof(SkillId.None) },
        allowAdditions: true);

    [Fact]
    public void StatId_IsGoStatId() => AssertSameMembers<StatId>(PackageConstants("stat").Where(c => c.Type == "ID"), allowAdditions: true);

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
        },
        allowAdditions: true);

    [Fact]
    public void QuestId_IsGoQuest() => AssertSameMembers<QuestId>(PackageConstants("quest").Where(c => c.Type == "Quest"));

    [Fact]
    public void ItemId_IsTheIndexOfGoItemNames_RenumberedForReignOfTheWarlock()
    {
        // Reign of the Warlock inserts the Warlock's 15 books at 508 (armor.txt wa1 to waf) and appends 18 misc rows: d2go's
        // names keep their order, 15 rows on from Elixir (508), and the new items take the other values.
        var names = StringSlice(Fixtures.D2GoFile("pkg/data/item/name.go"), "Names");
        var values = Enum.GetValues<ItemId>();
        Assert.Equal(Enumerable.Range(0, values.Length), values.Select(id => (int)id));
        for (var i = 0; i < names.Count; i++)
            Assert.Equal(i < 508 ? i : i + 15, (int)Enum.Parse<ItemId>(names[i]));
        Assert.Equal(names.Count + 15 + 18, values.Length);
        Assert.Equal(508, (int)ItemId.OldBook);
        Assert.Equal(522, (int)ItemId.BlasphemousGrimoire);
        Assert.Equal(674, (int)ItemId.WesternWorldstoneShard);
    }

    [Fact]
    public void ItemTypeCode_IsGoItemTypeConstants()
    {
        var expected = Constants(Fixtures.D2GoFile("pkg/data/item/itemtypes.go"), "itemtypes.go")
            .ToDictionary(c => c.Name["Type".Length..], c => (string)c.Value);
        var actual = typeof(ItemTypeCode).GetFields()
            .Where(f => f.IsLiteral)
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);
        // Reign of the Warlock's item types, not in d2go.
        string[] additions = [nameof(ItemTypeCode.WarlockItem), nameof(ItemTypeCode.Grimoire), nameof(ItemTypeCode.ColossalJewel), nameof(ItemTypeCode.CraftedSunderCharm)];
        foreach (var name in additions)
            Assert.True(actual.Remove(name), name);
        Assert.Equal(expected.OrderBy(e => e.Key, StringComparer.Ordinal), actual.OrderBy(e => e.Key, StringComparer.Ordinal));
    }
}

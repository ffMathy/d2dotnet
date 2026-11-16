using D2Dotnet.D2Go.Data.Area;
using D2Dotnet.D2Go.Data.Skill;

namespace D2Dotnet.D2Go.Data.Tests;

// Inside the namespace, so these win over the library's types of the same name in the enclosing D2Dotnet namespace.
using AreaId = D2Dotnet.D2Go.Data.Area.AreaId;
using SkillId = D2Dotnet.D2Go.Data.Skill.SkillId;

/// <summary>
/// The tables <see cref="GameTables.Load"/> reads from d2go's txt files must equal, entry for entry, the Go tables
/// d2go's <c>cmd/txttocode</c> generated from the same files (parsed from the Go source of the pinned commit).
/// </summary>
public class GeneratedTablesOracleTests
{
    private static GameTables Tables => D2GoFixtures.Tables;

    private static int Int(string goLiteral) => checked((int)GoSource.ParseInt(goLiteral));

    private static bool Bool(string goLiteral) => goLiteral switch
    {
        "true" => true,
        "false" => false,
        _ => throw new FormatException($"Not a Go bool: {goLiteral}"),
    };

    /// <summary>Compares key sets and every entry; reports every difference at once.</summary>
    private static void AssertSameTable<TKey, TValue>(
        string what,
        IReadOnlyDictionary<TKey, TValue> expected,
        IReadOnlyDictionary<TKey, TValue> actual)
        where TKey : notnull
    {
        var problems = new List<string>();
        foreach (var (key, value) in expected)
        {
            if (!actual.TryGetValue(key, out var got))
                problems.Add($"missing {key}");
            else if (!EqualityComparer<TValue>.Default.Equals(value, got))
                problems.Add($"{key}: Go {value} != C# {got}");
        }

        problems.AddRange(actual.Keys.Where(k => !expected.ContainsKey(k)).Select(k => $"extra {k}"));
        Assert.True(problems.Count == 0, $"{what}: {problems.Count} differences:\n{string.Join("\n", problems.Take(50))}");
        Assert.NotEmpty(expected);
    }

    [Fact]
    public void ItemDescriptions_MatchGeneratedItemsGo()
    {
        var go = GoSource.MapOfStructs(D2GoFixtures.GoFile("pkg/data/item/items.go"), "Desc");
        var expected = go.ToDictionary(e => Int(e.Key), e => new Item.ItemDescription
        {
            Id = Int(e.Fields["ID"]),
            Name = e.Fields["Name"],
            Code = e.Fields["Code"],
            NormalCode = e.Fields["NormalCode"],
            UberCode = e.Fields["UberCode"],
            UltraCode = e.Fields["UltraCode"],
            InventoryWidth = Int(e.Fields["InventoryWidth"]),
            InventoryHeight = Int(e.Fields["InventoryHeight"]),
            MinDefense = Int(e.Fields["MinDefense"]),
            MaxDefense = Int(e.Fields["MaxDefense"]),
            Type = e.Fields["Type"],
        });

        AssertSameTable("item.Desc", expected, Tables.ItemDescriptions);
        Assert.Equal(659, expected.Count);
    }

    [Fact]
    public void ItemTypes_MatchGeneratedItemTypesGo()
    {
        var source = D2GoFixtures.GoFile("pkg/data/item/itemtypes.go");
        var codes = GoSource.Constants(source, "itemtypes.go").ToDictionary(c => c.Name, c => (string)c.Value);
        var expected = GoSource.MapOfStructs(source, "ItemTypes").ToDictionary(e => codes[e.Key], e => new Item.ItemType
        {
            Id = Int(e.Fields["ID"]),
            Name = e.Fields["Name"],
            Code = e.Fields["Code"],
            Throwable = Bool(e.Fields["Throwable"]),
            Beltable = Bool(e.Fields["Beltable"]),
        });

        AssertSameTable("item.ItemTypes", expected, Tables.ItemTypes);
        Assert.Equal(101, expected.Count);
    }

    [Fact]
    public void Areas_MatchGeneratedAreasGo()
    {
        var go = GoSource.MapOfStructs(D2GoFixtures.GoFile("pkg/data/area/areas.go"), "Areas");
        var expected = go.ToDictionary(e => (AreaId)Int(e.Key), e => new AreaInfo
        {
            Id = (AreaId)Int(e.Fields["ID"]),
            Name = e.Fields["Name"],
        });

        AssertSameTable("area.Areas", expected, Tables.Areas);
    }

    [Fact]
    public void Skills_MatchGeneratedSkillsGo()
    {
        var go = GoSource.MapOfStructs(D2GoFixtures.GoFile("pkg/data/skill/skills.go"), "Skills");
        var expected = go.ToDictionary(e => (SkillId)Int(e.Key), e => new SkillInfo
        {
            Id = (SkillId)Int(e.Fields["ID"]),
            Name = e.Fields["Name"],
            LeftSkill = Bool(e.Fields["LeftSkill"]),
            RightSkill = Bool(e.Fields["RightSkill"]),
        });

        AssertSameTable("skill.Skills", expected, Tables.Skills);
    }

    [Fact]
    public void SkillDescriptions_MatchGeneratedSkillDescGo()
    {
        var go = GoSource.MapOfStructs(D2GoFixtures.GoFile("pkg/data/skill/skilldesc.go"), "Desc");
        var expected = go.ToDictionary(e => (SkillId)Int(e.Key), e => new SkillDescription
        {
            Page = Int(e.Fields["Page"]),
            Row = Int(e.Fields["Row"]),
            Column = Int(e.Fields["Column"]),
            ListRow = Int(e.Fields["ListRow"]),
            IconCel = Int(e.Fields["IconCel"]),
        });

        AssertSameTable("skill.Desc", expected, Tables.SkillDescriptions);
    }

    [Fact]
    public void ObjectDescriptions_MatchGeneratedObjectsGo()
    {
        var go = GoSource.MapOfStructs(D2GoFixtures.GoFile("pkg/data/object/objects.go"), "Desc");
        var expected = go.ToDictionary(e => Int(e.Key), e => new Object.ObjectDescription
        {
            Id = Int(e.Fields["ID"]),
            Name = e.Fields["Name"],
            SizeX = Int(e.Fields["SizeX"]),
            SizeY = Int(e.Fields["SizeY"]),
            HasCollision = Bool(e.Fields["HasCollision"]),
        });

        AssertSameTable("object.Desc", expected, Tables.ObjectDescriptions);
    }

    [Fact]
    public void GeneratedFiles_AreTheOnesThisPortReplaces()
    {
        // Every file d2go marks as generated by cmd/txttocode is covered by a test above.
        var generated = Directory.EnumerateFiles(Path.Combine(D2GoFixtures.Root, "pkg", "data"), "*.go", SearchOption.AllDirectories)
            .Where(f => File.ReadLines(f).FirstOrDefault()?.Contains("Code generated by cmd/txttocode", StringComparison.Ordinal) == true)
            .Select(f => Path.GetRelativePath(D2GoFixtures.Root, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "pkg/data/area/areas.go",
                "pkg/data/item/items.go",
                "pkg/data/item/itemtypes.go",
                "pkg/data/object/objects.go",
                "pkg/data/skill/skilldesc.go",
                "pkg/data/skill/skills.go",
            ],
            generated);
    }

    [Fact]
    public void LookupMethods_ReturnTheEntryOrTheZeroValue()
    {
        Assert.Equal("Hand Axe", Tables.ItemDesc(0).Name);
        Assert.Equal(new Item.ItemDescription(), Tables.ItemDesc(-1));
        Assert.Equal("Healing Potion", Tables.ItemType(Item.ItemTypeCodes.TypeHealingPotion).Name);
        Assert.Equal(new Item.ItemType(), Tables.ItemType("nope"));
        Assert.Equal("Rogue Encampment", Tables.Area(AreaId.RogueEncampment).Name);
        Assert.Equal(new AreaInfo(), Tables.Area((AreaId)10_000));
        Assert.Equal("Teleport", Tables.Skill(SkillId.Teleport).Name);
        Assert.Equal(new SkillInfo(), Tables.Skill((SkillId)30_000));
        Assert.Equal(new SkillDescription(), Tables.SkillDesc(SkillId.Unset));
        Assert.Equal("Shrine", Tables.ObjectDesc(2).Name);
        Assert.Equal(new Object.ObjectDescription(), Tables.ObjectDesc(int.MaxValue));
    }
}

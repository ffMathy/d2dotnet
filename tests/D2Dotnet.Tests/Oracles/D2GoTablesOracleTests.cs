using D2Dotnet.Data;

namespace D2Dotnet.Tests.Oracles;

/// <summary>
/// The typed tables <see cref="GameDatabase"/> reads from d2go's txt files must hold every value of the Go tables d2go's
/// <c>cmd/txttocode</c> generated from the same files (parsed from the Go source of the pinned commit), entry for entry.
/// </summary>
/// <remarks>
/// The records are mapped to the shape of d2go's generated entries here, where d2go differs on purpose: it fixes the
/// spelling of a few item names, numbers item types skipping the rows without a code, and numbers skills by row.
/// </remarks>
public class D2GoTablesOracleTests
{
    private static readonly Lazy<GameDatabase> _db = new(() => new GameDatabase(Fixtures.D2GoFiles));

    private static GameDatabase Db => _db.Value;

    private static int Int(string goLiteral) => checked((int)GoSource.ParseInt(goLiteral));

    private static bool Bool(string goLiteral) => goLiteral switch
    {
        "true" => true,
        "false" => false,
        _ => throw new FormatException($"Not a Go bool: {goLiteral}"),
    };

    /// <summary>The spelling fixes d2go's generator applies to item names (so they match its item name constants), in its order.</summary>
    private static string D2GoItemName(string name) => name
        .Replace("Heirophant", "Hierophant", StringComparison.Ordinal)
        .Replace("Colossal", "Colossus", StringComparison.Ordinal)
        .Replace("Ancient Shield", "Kurast Shield", StringComparison.Ordinal)
        .Replace("Ornate Armor", "Ornate Plate", StringComparison.Ordinal)
        .Replace("Essense", "Essence", StringComparison.Ordinal);

    /// <summary>Compares key sets and every entry; reports every difference at once.</summary>
    private static void AssertSameTable<TKey, TValue>(string what, IReadOnlyDictionary<TKey, TValue> expected, IReadOnlyDictionary<TKey, TValue> actual)
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

    private sealed record GoItem(int Id, string Name, string Code, string NormalCode, string UberCode, string UltraCode,
        int InventoryWidth, int InventoryHeight, int MinDefense, int MaxDefense, string Type);

    private sealed record GoItemType(int Id, string Name, string Code, bool Throwable, bool Beltable);

    private sealed record GoArea(int Id, string Name);

    private sealed record GoSkill(int Id, string Name, bool LeftSkill, bool RightSkill);

    private sealed record GoSkillDesc(int Page, int Row, int Column, int ListRow, int IconCel);

    private sealed record GoObject(int Id, string Name, int SizeX, int SizeY, bool HasCollision);

    [Fact]
    public void Items_MatchGeneratedItemsGo()
    {
        var expected = GoSource.MapOfStructs(Fixtures.D2GoFile("pkg/data/item/items.go"), "Desc").ToDictionary(
            e => Int(e.Key),
            e => new GoItem(Int(e.Fields["ID"]), e.Fields["Name"], e.Fields["Code"], e.Fields["NormalCode"], e.Fields["UberCode"],
                e.Fields["UltraCode"], Int(e.Fields["InventoryWidth"]), Int(e.Fields["InventoryHeight"]), Int(e.Fields["MinDefense"]),
                Int(e.Fields["MaxDefense"]), e.Fields["Type"]));
        var actual = Db.Items.ToDictionary(
            i => (int)i.Id,
            i => new GoItem((int)i.Id, D2GoItemName(i.Name), i.Code, i.NormalCode, i.UberCode, i.UltraCode, i.InventoryWidth,
                i.InventoryHeight, i.MinDefense, i.MaxDefense, i.Type));

        AssertSameTable("item.Desc", expected, actual);
        Assert.Equal(659, expected.Count);
        Assert.Equal(expected.Count, Db.Items.Count); // d2go's (pre-Reign of the Warlock) tables; ItemId has the newer rows too
    }

    [Fact]
    public void ItemTypes_MatchGeneratedItemTypesGo()
    {
        var source = Fixtures.D2GoFile("pkg/data/item/itemtypes.go");
        var codes = GoSource.Constants(source, "itemtypes.go").ToDictionary(c => c.Name, c => (string)c.Value);
        var expected = GoSource.MapOfStructs(source, "ItemTypes").ToDictionary(
            e => codes[e.Key],
            e => new GoItemType(Int(e.Fields["ID"]), e.Fields["Name"], e.Fields["Code"], Bool(e.Fields["Throwable"]), Bool(e.Fields["Beltable"])));
        var actual = Db.ItemTypes.Where(t => t.Code.Length > 0)
            .Select((t, i) => new GoItemType(i, t.Name, t.Code, t.Throwable, t.Beltable))
            .ToDictionary(t => t.Code);

        AssertSameTable("item.ItemTypes", expected, actual);
        Assert.Equal(101, expected.Count);
        // The game rows d2go leaves out: the ones without a code.
        Assert.Equal(["Any", "Not Used", "Not Used", "Not Used"], Db.ItemTypes.Where(t => t.Code.Length == 0).Select(t => t.Name));
    }

    [Fact]
    public void Areas_MatchGeneratedAreasGo()
    {
        var expected = GoSource.MapOfStructs(Fixtures.D2GoFile("pkg/data/area/areas.go"), "Areas")
            .ToDictionary(e => Int(e.Key), e => new GoArea(Int(e.Fields["ID"]), e.Fields["Name"]));
        var actual = Db.Levels.ToDictionary(l => (int)l.Id, l => new GoArea((int)l.Id, l.NameKey));

        AssertSameTable("area.Areas", expected, actual);
    }

    [Fact]
    public void Skills_MatchGeneratedSkillsGo()
    {
        var expected = GoSource.MapOfStructs(Fixtures.D2GoFile("pkg/data/skill/skills.go"), "Skills")
            .ToDictionary(e => Int(e.Key), e => new GoSkill(Int(e.Fields["ID"]), e.Fields["Name"], Bool(e.Fields["LeftSkill"]), Bool(e.Fields["RightSkill"])));
        // d2go's ID is the row index, its key the *Id column.
        var actual = Db.Skills.Select((s, row) => (Key: (int)s.Id, Value: new GoSkill(row, s.Name, s.LeftSkill, s.RightSkill)))
            .ToDictionary(e => e.Key, e => e.Value);

        AssertSameTable("skill.Skills", expected, actual);
    }

    [Fact]
    public void SkillDescriptions_MatchGeneratedSkillDescGo()
    {
        var expected = GoSource.MapOfStructs(Fixtures.D2GoFile("pkg/data/skill/skilldesc.go"), "Desc")
            .ToDictionary(e => Int(e.Key), e => new GoSkillDesc(Int(e.Fields["Page"]), Int(e.Fields["Row"]), Int(e.Fields["Column"]),
                Int(e.Fields["ListRow"]), Int(e.Fields["IconCel"])));
        var actual = Db.SkillDescriptions.ToDictionary(d => d.Index, d => new GoSkillDesc(d.Page, d.Row, d.Column, d.ListRow, d.IconCel));

        AssertSameTable("skill.Desc", expected, actual);
    }

    [Fact]
    public void Objects_MatchGeneratedObjectsGo()
    {
        var expected = GoSource.MapOfStructs(Fixtures.D2GoFile("pkg/data/object/objects.go"), "Desc")
            .ToDictionary(e => Int(e.Key), e => new GoObject(Int(e.Fields["ID"]), e.Fields["Name"], Int(e.Fields["SizeX"]),
                Int(e.Fields["SizeY"]), Bool(e.Fields["HasCollision"])));
        var actual = Db.Objects.ToDictionary(o => (int)o.Id, o => new GoObject((int)o.Id, o.NameKey, o.SizeX, o.SizeY, o.HasCollision));

        AssertSameTable("object.Desc", expected, actual);
    }

    [Fact]
    public void GeneratedFiles_AreTheOnesCoveredHere()
    {
        // Every file d2go marks as generated by cmd/txttocode is covered by a test above.
        var generated = Directory.EnumerateFiles(Path.Combine(Fixtures.D2Go, "pkg", "data"), "*.go", SearchOption.AllDirectories)
            .Where(f => File.ReadLines(f).FirstOrDefault()?.Contains("Code generated by cmd/txttocode", StringComparison.Ordinal) == true)
            .Select(f => Path.GetRelativePath(Fixtures.D2Go, f).Replace('\\', '/'))
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
}

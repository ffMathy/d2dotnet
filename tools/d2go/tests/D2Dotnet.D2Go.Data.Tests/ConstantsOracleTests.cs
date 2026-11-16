using System.Reflection;
using System.Text.RegularExpressions;
using D2Dotnet.D2Go.Data.Area;
using D2Dotnet.D2Go.Data.Difficulty;
using D2Dotnet.D2Go.Data.Item;
using D2Dotnet.D2Go.Data.Npc;
using D2Dotnet.D2Go.Data.Object;
using D2Dotnet.D2Go.Data.Quest;
using D2Dotnet.D2Go.Data.Skill;
using D2Dotnet.D2Go.Data.Stat;
using D2Dotnet.D2Go.Data.State;
using static D2Dotnet.D2Go.Data.Tests.GoSource;

namespace D2Dotnet.D2Go.Data.Tests;

/// <summary>
/// Every constant, hand-written list and hand-written id switch of d2go's pkg/data must be ported exactly: checked
/// against the Go source of the pinned commit.
/// </summary>
public partial class ConstantsOracleTests
{
    /// <summary>Where a Go constant lives in C#: its type and how its name changes.</summary>
    private sealed record Target(Type Type, string StripPrefix = "");

    /// <summary>Package of a pkg/data file ("data" for the root).</summary>
    private static string PackageOf(string relativeFile)
    {
        var dir = Path.GetDirectoryName(relativeFile)!.Replace('\\', '/');
        return dir == "pkg/data" ? "data" : dir["pkg/data/".Length..];
    }

    private static IReadOnlyList<GoConst> AllConstants(out Dictionary<GoConst, string> packages)
    {
        var root = D2GoFixtures.Root;
        var result = new List<GoConst>();
        packages = [];
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "pkg", "data"), "*.go", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            foreach (var c in Constants(File.ReadAllText(file), Path.GetFileName(file)))
            {
                result.Add(c);
                packages[c] = PackageOf(relative);
            }
        }

        return result;
    }

    /// <summary>The C# home of every Go constant; null for the one unexported constant (checked by a unit test).</summary>
    private static Target? TargetOf(string package, GoConst c) => (package, c.Type, c.File) switch
    {
        ("area", "ID", _) => new(typeof(AreaId)),
        ("npc", "ID", _) => new(typeof(NpcId)),
        ("object", "Name", _) or ("object", null, "ids.go") => new(typeof(ObjectName)),
        ("object", "InteractType", _) => new(typeof(InteractType), "InteractType"),
        ("object", "ShrineType", _) => new(typeof(ShrineType)),
        ("quest", "Quest", _) => new(typeof(QuestId)),
        ("quest", "Status", _) => new(typeof(QuestStatus), "Status"),
        ("skill", "ID", _) => new(typeof(SkillId)),
        ("stat", "ID", _) => new(typeof(StatId)),
        ("stat", "Resist", _) => new(typeof(Resist)),
        ("state", "State", _) => new(typeof(StateId)),
        ("item", "Quality", _) => new(typeof(ItemQuality), "Quality"),
        ("item", "Tier", _) => new(typeof(ItemTier), "Tier"),
        ("item", "LocationType", _) => new(typeof(LocationType), "Location"),
        ("item", null, "itemtypes.go") => new(typeof(ItemTypeCodes)),
        ("item", null, "name.go") => new(typeof(ItemNames)),
        ("difficulty", null, _) => new(typeof(GameDifficulty)),
        ("data", "MonsterType", _) => new(typeof(MonsterType), "MonsterType"),
        ("data", "PotionType", _) => new(typeof(PotionType)),
        ("data", "Class", _) => new(typeof(Class)),
        ("data", null, _) when c.Name == "goldPerLevel" => null,
        _ => throw new InvalidOperationException($"No C# home for {package}.{c.Name} ({c.Type ?? "untyped"})"),
    };

    private static string CSharpName(string goName, string stripPrefix)
    {
        var name = char.ToUpperInvariant(goName[0]) + goName[1..];
        return stripPrefix.Length > 0 && name.StartsWith(stripPrefix, StringComparison.Ordinal) ? name[stripPrefix.Length..] : name;
    }

    /// <summary>The C# constants of a target type: enum members, const strings, or static fields of the type itself.</summary>
    private static Dictionary<string, object> CSharpConstants(Type type)
    {
        if (type.IsEnum)
            return Enum.GetNames(type).ToDictionary(n => n, n => (object)Convert.ToInt64(Enum.Parse(type, n), System.Globalization.CultureInfo.InvariantCulture));
        return type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral || f.FieldType == type)
            .ToDictionary(f => f.Name, f => (object)(f.IsLiteral ? (string)f.GetRawConstantValue()! : f.GetValue(null)!.ToString()!));
    }

    [Fact]
    public void EveryGoConstant_HasTheSameValueInCSharp_AndNothingElseIsDeclared()
    {
        var constants = AllConstants(out var packages);
        var problems = new List<string>();
        var expectedByType = new Dictionary<Type, Dictionary<string, object>>();
        var skipped = new List<string>();

        foreach (var c in constants)
        {
            var target = TargetOf(packages[c], c);
            if (target is null)
            {
                skipped.Add(c.Name);
                continue;
            }

            if (!expectedByType.TryGetValue(target.Type, out var expected))
                expectedByType[target.Type] = expected = [];
            var name = CSharpName(c.Name, target.StripPrefix);
            if (!expected.TryAdd(name, c.Value))
                problems.Add($"{target.Type.Name}.{name} would be declared twice");
        }

        foreach (var (type, expected) in expectedByType)
        {
            var actual = CSharpConstants(type);
            foreach (var (name, value) in expected)
            {
                if (!actual.TryGetValue(name, out var got))
                    problems.Add($"{type.Name}.{name} is missing (Go value {value})");
                else if (!Equals(value, got))
                    problems.Add($"{type.Name}.{name}: Go {value} != C# {got}");
            }

            problems.AddRange(actual.Keys.Where(n => !expected.ContainsKey(n)).Select(n => $"{type.Name}.{n} is not a Go constant"));
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(100)));
        Assert.Equal(["goldPerLevel"], skipped);
        Assert.Equal(20, expectedByType.Count);
        Assert.True(constants.Count > 2500, $"Only {constants.Count} constants parsed");
    }

    /// <summary>The Go constants of a package by name (for resolving <c>pkg.Name</c> in lists and switches).</summary>
    private static Dictionary<string, long> IntConstants(string package)
    {
        var constants = AllConstants(out var packages);
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var c in constants.Where(c => packages[c] == package && c.Value is long))
            result[c.Name] = (long)c.Value;
        return result;
    }

    [Fact]
    public void ItemNames_MatchNameGo()
    {
        Assert.Equal(StringSlice(D2GoFixtures.GoFile("pkg/data/item/name.go"), "Names"), ItemNames.Names);
    }

    [Fact]
    public void StatNames_MatchStringStats()
    {
        var expected = StringSlice(D2GoFixtures.GoFile("pkg/data/stat/stats.go"), "StringStats");
        Assert.Equal(expected, StatNames.StringStats);
        // Every stat id has a name, as Go's String() assumes.
        Assert.Equal(Enum.GetValues<StatId>().Length, expected.Count);
    }

    [Fact]
    public void SkillNames_MatchGo()
    {
        var ids = IntConstants("skill");
        var expected = MapOfStrings(D2GoFixtures.GoFile("pkg/data/skill/skill.go"), "SkillNames")
            .ToDictionary(e => (SkillId)ids[e.Key], e => e.Value);
        Assert.Equal(expected.OrderBy(e => e.Key), SkillNames.Names.OrderBy(e => e.Key));
    }

    [Fact]
    public void ShrineTypeNames_MatchGo()
    {
        var ids = IntConstants("object");
        var expected = MapOfStrings(D2GoFixtures.GoFile("pkg/data/object/shrine_type.go"), "ShrineTypeNames")
            .ToDictionary(e => (ShrineType)ids[e.Key], e => e.Value);
        Assert.Equal(expected.OrderBy(e => e.Key), ShrineTypes.ShrineTypeNames.OrderBy(e => e.Key));
    }

    [Fact]
    public void WPAddresses_MatchGo()
    {
        var ids = IntConstants("area");
        var expected = new Dictionary<AreaId, WPAddress>();
        foreach (var line in VarBody(D2GoFixtures.GoFile("pkg/data/area/waypoint.go"), "WPAddresses"))
        {
            var m = WaypointLine().Match(line);
            Assert.True(m.Success, line);
            var linked = m.Groups[4].Value == "nil"
                ? []
                : m.Groups[5].Value.Split(',').Select(n => (AreaId)ids[n.Trim()]).ToArray();
            expected[(AreaId)ids[m.Groups[1].Value]] = new WPAddress(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), linked);
        }

        Assert.Equal(39, expected.Count);
        Assert.Equal(expected.Keys.Order(), Waypoints.WPAddresses.Keys.Order());
        foreach (var (area, address) in expected)
        {
            var actual = Waypoints.WPAddresses[area];
            Assert.Equal(address.Tab, actual.Tab);
            Assert.Equal(address.Row, actual.Row);
            Assert.Equal(address.LinkedFrom, actual.LinkedFrom);
        }
    }

    [Fact]
    public void CanBeTerrorized_MatchesGo()
    {
        var ids = IntConstants("area");
        var expected = VarBody(D2GoFixtures.GoFile("pkg/data/area/terrorzone.go"), "CanBeTerrorized")
            .Select(l =>
            {
                var m = TrueLine().Match(l);
                Assert.True(m.Success, l);
                return (AreaId)ids[m.Groups[1].Value];
            })
            .ToArray();

        Assert.Equal(expected.Order(), TerrorZones.CanBeTerrorized.Order());
    }

    /// <summary>A hand-written id switch of d2go and the C# method that ports it.</summary>
    public sealed record Membership(string File, string Signature, string Package, string Name)
    {
        public override string ToString() => Name;
    }

    private static readonly Dictionary<string, Func<long, bool>> MembershipPorts = new()
    {
        ["IsMerc"] = v => new Monster { Name = (NpcId)v }.IsMerc(),
        ["IsPet"] = v => new Monster { Name = (NpcId)v }.IsPet(),
        ["IsGoodNPC"] = v => new Monster { Name = (NpcId)v }.IsGoodNpc(),
        ["IsMonsterRaiser"] = v => new Monster { Name = (NpcId)v }.IsMonsterRaiser(),
        ["IsSkip"] = v => new Monster { Name = (NpcId)v }.IsSkip(),
        ["IsShrine"] = v => new GameObject { Shrine = new ShrineData { ShrineType = (ShrineType)v } }.IsShrine(),
        ["IsWaypoint"] = v => new GameObject { Name = (ObjectName)v }.IsWaypoint(),
        ["IsPortal"] = v => new GameObject { Name = (ObjectName)v }.IsPortal(),
        ["IsRedPortal"] = v => new GameObject { Name = (ObjectName)v }.IsRedPortal(),
        ["IsChest"] = v => new GameObject { Name = (ObjectName)v }.IsChest(),
        ["IsDoor"] = v => new GameObject { Name = (ObjectName)v }.IsDoor(),
        ["IsSuperChest"] = v => new GameObject { Name = (ObjectName)v }.IsSuperChest(),
        ["IsTown"] = v => ((AreaId)v).IsTown(),
        ["HasDebuff"] = v => new PlayerUnit { States = [(StateId)v] }.HasDebuff(),
    };

    public static TheoryData<Membership> Memberships() =>
    [
        new("pkg/data/npc.go", "func (m Monster) IsMerc(", "npc", "IsMerc"),
        new("pkg/data/npc.go", "func (m Monster) IsPet(", "npc", "IsPet"),
        new("pkg/data/npc.go", "func (m Monster) IsGoodNPC(", "npc", "IsGoodNPC"),
        new("pkg/data/npc.go", "func (m Monster) IsMonsterRaiser(", "npc", "IsMonsterRaiser"),
        new("pkg/data/npc.go", "func (m Monster) IsSkip(", "npc", "IsSkip"),
        new("pkg/data/objects.go", "func (o Object) IsShrine(", "object", "IsShrine"),
        new("pkg/data/objects.go", "func (o Object) IsWaypoint(", "object", "IsWaypoint"),
        new("pkg/data/objects.go", "func (o Object) IsPortal(", "object", "IsPortal"),
        new("pkg/data/objects.go", "func (o Object) IsRedPortal(", "object", "IsRedPortal"),
        new("pkg/data/objects.go", "func (o Object) IsChest(", "object", "IsChest"),
        new("pkg/data/objects.go", "func (o Object) IsDoor(", "object", "IsDoor"),
        new("pkg/data/objects.go", "func (o Object) IsSuperChest(", "object", "IsSuperChest"),
        new("pkg/data/area/area.go", "func (a ID) IsTown(", "area", "IsTown"),
        new("pkg/data/data.go", "func (pu PlayerUnit) HasDebuff(", "state", "HasDebuff"),
    ];

    [Theory]
    [MemberData(nameof(Memberships))]
    public void IdSwitch_MatchesGo(Membership membership)
    {
        var body = FunctionBody(D2GoFixtures.GoFile(membership.File), membership.Signature);
        var operands = membership.Name == "HasDebuff"
            ? DebuffEntry().Matches(body).Select(m => m.Groups[1].Value).ToArray()
            : CaseOperands(body);
        Assert.NotEmpty(operands);

        var constants = IntConstants(membership.Package);
        var expected = new HashSet<long>();
        foreach (var operand in operands)
        {
            var name = operand.Contains('.') ? operand[(operand.IndexOf('.') + 1)..] : operand;
            expected.Add(char.IsAsciiDigit(name[0]) ? ParseInt(name) : constants[name]);
        }

        var port = MembershipPorts[membership.Name];
        var wrong = Enumerable.Range(-2, 1100).Where(v => port(v) != expected.Contains(v)).ToArray();
        Assert.True(wrong.Length == 0, $"{membership.Name} differs from Go for: {string.Join(", ", wrong)}");
    }

    [GeneratedRegex(@"^(\w+):\s*\{(\d+), (\d+), (nil|\[\]ID\{(.*)\})\},$")]
    private static partial Regex WaypointLine();

    [GeneratedRegex(@"^(\w+):\s*true,$")]
    private static partial Regex TrueLine();

    [GeneratedRegex(@"state\.(\w+),")]
    private static partial Regex DebuffEntry();
}

using System.Globalization;
using System.Text.RegularExpressions;
using D2Dotnet.Game;
using static D2Dotnet.Tests.Oracles.GoSource;

namespace D2Dotnet.Tests.Game;

/// <summary>
/// The hand-written lists and id switches of d2go's pkg/data that the snapshot model carries over, against the Go source
/// of the pinned commit: monster and object classifications, debuffs, towns, terror zones, waypoints and shrine names.
/// </summary>
public partial class D2GoGameOracleTests
{
    /// <summary>The Go integer constants of a pkg/data package, by name.</summary>
    private static Dictionary<string, long> IntConstants(string package) =>
        Directory.EnumerateFiles(Path.Combine(Fixtures.D2Go, "pkg", "data", package), "*.go")
            .Order(StringComparer.Ordinal)
            .SelectMany(f => Constants(File.ReadAllText(f), Path.GetFileName(f)))
            .Where(c => c.Value is long)
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => (long)g.First().Value);

    /// <summary>A hand-written id switch of d2go and the C# member that carries it.</summary>
    public sealed record Membership(string File, string Signature, string Package, string Name)
    {
        public override string ToString() => Name;
    }

    private static readonly Dictionary<string, Func<long, bool>> Members = new()
    {
        ["IsMerc"] = v => new Monster { Class = (MonsterId)v }.IsMercenary,
        ["IsPet"] = v => new Monster { Class = (MonsterId)v }.IsPet,
        ["IsGoodNPC"] = v => new Monster { Class = (MonsterId)v }.IsFriendlyNpc,
        ["IsMonsterRaiser"] = v => new Monster { Class = (MonsterId)v }.IsMonsterRaiser,
        ["IsSkip"] = v => new Monster { Class = (MonsterId)v }.IsUntargetable,
        ["IsShrine"] = v => v is >= byte.MinValue and <= byte.MaxValue && new GameObject { Shrine = (ShrineType)v }.IsShrine,
        ["IsWaypoint"] = v => new GameObject { Class = (ObjectId)v }.IsWaypoint,
        ["IsPortal"] = v => new GameObject { Class = (ObjectId)v }.IsPortal,
        ["IsRedPortal"] = v => new GameObject { Class = (ObjectId)v }.IsRedPortal,
        ["IsChest"] = v => new GameObject { Class = (ObjectId)v }.IsChest,
        ["IsDoor"] = v => new GameObject { Class = (ObjectId)v }.IsDoor,
        ["IsSuperChest"] = v => new GameObject { Class = (ObjectId)v }.IsSuperChest,
        ["IsTown"] = v => ((AreaId)v).IsTown(),
        ["HasDebuff"] = v => v >= 0 && new MainPlayer { States = [(StateId)v] }.HasDebuff,
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
    public void IdSwitch_MatchesD2go(Membership membership)
    {
        var body = FunctionBody(Fixtures.D2GoFile(membership.File), membership.Signature);
        var operands = membership.Name == "HasDebuff"
            ? DebuffEntry().Matches(body).Select(m => m.Groups[1].Value).ToArray()
            : CaseOperands(body);
        Assert.NotEmpty(operands);

        var constants = IntConstants(membership.Package);
        var expected = operands
            .Select(o => o.Contains('.', StringComparison.Ordinal) ? o[(o.IndexOf('.', StringComparison.Ordinal) + 1)..] : o)
            .Select(n => char.IsAsciiDigit(n[0]) ? ParseInt(n) : constants[n])
            .ToHashSet();

        var member = Members[membership.Name];
        var wrong = Enumerable.Range(-2, 1100).Where(v => member(v) != expected.Contains(v)).ToArray();
        Assert.True(wrong.Length == 0, $"{membership.Name} differs from d2go for: {string.Join(", ", wrong)}");
    }

    [Fact]
    public void ShrineNames_MatchD2go()
    {
        var ids = IntConstants("object");
        var expected = MapOfStrings(Fixtures.D2GoFile("pkg/data/object/shrine_type.go"), "ShrineTypeNames")
            .ToDictionary(e => (ShrineType)ids[e.Key], e => e.Value);
        Assert.Equal(expected.Keys.Order(), Enum.GetValues<ShrineType>());
        foreach (var (type, name) in expected)
            Assert.Equal(name, type.GetName());
    }

    [Fact]
    public void Waypoints_MatchD2go()
    {
        var ids = IntConstants("area");
        var expected = new Dictionary<AreaId, (int Tab, int Row, AreaId[] Linked)>();
        foreach (var line in VarBody(Fixtures.D2GoFile("pkg/data/area/waypoint.go"), "WPAddresses"))
        {
            var m = WaypointLine().Match(line);
            Assert.True(m.Success, line);
            var linked = m.Groups[4].Value == "nil" ? [] : m.Groups[5].Value.Split(',').Select(n => (AreaId)ids[n.Trim()]).ToArray();
            expected[(AreaId)ids[m.Groups[1].Value]] = (
                int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), linked);
        }

        Assert.Equal(39, expected.Count);
        Assert.Equal(expected.Keys.Order(), Areas.Waypoints.Keys.Order());
        foreach (var (area, (tab, row, linked)) in expected)
        {
            var actual = Areas.Waypoints[area];
            Assert.Equal((tab, row), (actual.Tab, actual.Row));
            Assert.Equal(linked, actual.LinkedFrom);
        }
    }

    [Fact]
    public void TerrorZoneAreas_MatchD2go()
    {
        var ids = IntConstants("area");
        var expected = VarBody(Fixtures.D2GoFile("pkg/data/area/terrorzone.go"), "CanBeTerrorized")
            .Select(l =>
            {
                var m = TrueLine().Match(l);
                Assert.True(m.Success, l);
                return (AreaId)ids[m.Groups[1].Value];
            });

        Assert.Equal(expected.Order(), Areas.TerrorZoneAreas.Order());
    }

    [Fact]
    public void Enums_MatchD2goConstants()
    {
        static void Same<TEnum>(string package, string file, string prefix = "")
            where TEnum : struct, Enum
        {
            var go = Constants(Fixtures.D2GoFile($"pkg/data/{package}/{file}"), file)
                .Where(c => c.Value is long)
                .ToDictionary(c => c.Name.StartsWith(prefix, StringComparison.Ordinal) ? c.Name[prefix.Length..] : c.Name, c => (long)c.Value);
            var cs = Enum.GetValues<TEnum>().ToDictionary(v => v.ToString(), v => Convert.ToInt64(v, CultureInfo.InvariantCulture));
            Assert.Equal(go.OrderBy(e => e.Key, StringComparer.Ordinal), cs.OrderBy(e => e.Key, StringComparer.Ordinal));
        }

        Same<ShrineType>("object", "shrine_type.go");
        Same<InteractType>("object", "interact_type.go", "InteractType");
        Same<ItemQuality>("item", "quality.go", "Quality");
        Same<ItemTier>("item", "tier.go", "Tier");

        // quest.Status is a bit index; QuestStatus holds the bit (plus None).
        var status = Constants(Fixtures.D2GoFile("pkg/data/quest/status.go"), "status.go").ToDictionary(c => c.Name["Status".Length..], c => (long)c.Value);
        Assert.Equal(status.OrderBy(e => e.Key, StringComparer.Ordinal), Enum.GetValues<QuestStatus>().Where(s => s != QuestStatus.None)
            .ToDictionary(s => s.ToString(), s => (long)Math.Log2((int)s)).OrderBy(e => e.Key, StringComparer.Ordinal));

        var classes = Constants(Fixtures.D2GoFile("pkg/data/data.go"), "data.go").Where(c => c.Type == "Class").ToDictionary(c => c.Name, c => (long)c.Value);
        Assert.Equal(classes.OrderBy(e => e.Key, StringComparer.Ordinal), Enum.GetValues<CharacterClass>().ToDictionary(c => c.ToString(), c => (long)c).OrderBy(e => e.Key, StringComparer.Ordinal));
    }

    [GeneratedRegex(@"^(\w+):\s*\{(\d+), (\d+), (nil|\[\]ID\{(.*)\})\},$")]
    private static partial Regex WaypointLine();

    [GeneratedRegex(@"^(\w+):\s*true,$")]
    private static partial Regex TrueLine();

    [GeneratedRegex(@"state\.(\w+),")]
    private static partial Regex DebuffEntry();
}

using System.Text.Json;
using D2Dotnet.Maps;
using D2Dotnet.MapServer.Generation;
using D2Dotnet.MapServer.Server;

namespace D2Dotnet.MapServer.Tests.Generation;

public class LevelConverterTests
{
    private static string Rle(CollisionMap? grid, int width) =>
        JsonSerializer.Serialize(LevelConverter.EncodeCollision(grid, width));

    private static MapLevel Level(int id, IReadOnlyList<PresetUnit>? presets = null, IReadOnlyList<LevelExit>? exits = null,
        IReadOnlyList<Shrine>? shrines = null) => new()
        {
            Id = (AreaId)id,
            Bounds = new Rect(3, 7, 2, 1),
            Rooms = [new MapRoom(new Rect(3, 7, 2, 1), 1, 2, 0)],
            Presets = presets ?? [],
            Exits = exits ?? [],
            Shrines = shrines ?? [],
        };

    private static PresetUnit Preset(PresetKind kind, int classId, int x, int y) => new(kind, classId, new Point(x, y));

    private static LevelExit Exit(int destination, int x, int y, ExitKind kind) => new((AreaId)destination, new Point(x, y), kind);

    private static Shrine Shrine(int classId, int x, int y) => new((ObjectId)classId, new Point(x, y));

    private static readonly GeneratedAct ActI = new() { Act = ActId.Act1, Levels = [] };

    [Fact]
    public void RunsAlternateStartingWithCollisionAndDropTheTrailingRun()
    {
        Assert.Equal("[[1,5,1]]", Rle(Synthetic.Grid("#.....#..."), 10));
        Assert.Equal("[[0,3,2]]", Rle(Synthetic.Grid("...##....."), 10));
        // one run only: nothing is written (blocked) or a single 0 (walkable)
        Assert.Equal("[[0]]", Rle(Synthetic.Grid(".........."), 10));
        Assert.Equal("[[]]", Rle(Synthetic.Grid("##########"), 10));
    }

    [Fact]
    public void RowsRunToTheLastRowWithAWalkableSubtile()
    {
        Assert.Equal("[[1,1],[],[0,1]]", Rle(Synthetic.Grid("#.##", "####", ".###", "####", "####"), 4));
        // no walkable subtile at all, or no collision: one empty row, as upstream's grid reset kept MAX_SIZE_Y at 0
        Assert.Equal("[[]]", Rle(Synthetic.Grid("####", "####"), 4));
        Assert.Equal("[[]]", Rle(null, 25));
    }

    [Fact]
    public void UncoveredAnd1024AreBlockedAndOddValuesAreBlocked()
    {
        ushort[] cells = [CollisionMap.Uncovered, 0, 1024, 0, 3, 2, 0x404, 0x405];
        var grid = new CollisionMap(8, 1, cells);
        // walkable: 0, 0, 2, 0x404 (even, not exactly 1024)
        Assert.Equal("[[1,1,1,1,1,2]]", Rle(grid, 8));
    }

    [Fact]
    public void RowsCoverTheLevelWidthEvenWhereTheGridIsNarrower()
    {
        Assert.Equal("[[0,2]]", Rle(Synthetic.Grid(".."), 5));
        Assert.Equal("[[0]]", Rle(Synthetic.Grid("....."), 2));
    }

    [Fact]
    public void OffsetSizeAndNameAreInSubtiles()
    {
        var level = Level(2);
        var json = JsonSerializer.Serialize(LevelConverter.Convert(level, ActI, Synthetic.GameData()), MapHttpServer.JsonOptions);
        Assert.Equal("{\"type\":\"map\",\"id\":2,\"name\":\"Blood Moor\",\"offset\":{\"x\":15,\"y\":35},\"size\":{\"width\":10,\"height\":5},\"objects\":[],\"map\":[[]]}", json);

        // a level the typed Levels table leaves out (its Name cell is blank) keeps its raw (trimmed) LevelName
        Assert.Equal("raw name", LevelConverter.Convert(Level(77), ActI, Synthetic.GameData()).Name);
        // and a level levels.txt does not have at all has no name
        Assert.Equal("", LevelConverter.Convert(Level(78), ActI, Synthetic.GameData()).Name);
    }

    [Fact]
    public void ObjectsFollowUpstreamRules()
    {
        var level = Level(1,
            presets:
            [
                Preset(PresetKind.Object, 5, 1, 2), // not selectable: no op, class from op -1
                Preset(PresetKind.Object, 119, 3, 4),
                Preset(PresetKind.Object, 581, 5, 6),
                Preset(PresetKind.Object, 9999, 7, 8), // not in objects.txt
                Preset(PresetKind.Object, 0, 7, 8), // TestData1: never exported
                Preset(PresetKind.Npc, 1, 9, 10),
                Preset(PresetKind.Npc, 2, 9, 10), // MonStats row without a name
                Preset(PresetKind.Npc, 3, 11, 12), // first super unique
                Preset(PresetKind.Npc, 6, 13, 14), // past the super uniques: a MonPlace entry
                Preset(PresetKind.Npc, 152, 15, 16), // Rogue: never exported
            ],
            shrines: [Shrine(2, 20, 21), Shrine(119, 3, 4)]);
        var objects = LevelConverter.Objects(level, ActI, Synthetic.GameData())
            .Select(o => JsonSerializer.Serialize(o, MapHttpServer.JsonOptions));
        Assert.Equal(
        [
            "{\"id\":5,\"type\":\"object\",\"x\":1,\"y\":2,\"name\":\"LargeChestR\"}",
            "{\"id\":119,\"type\":\"object\",\"x\":3,\"y\":4,\"name\":\"Waypoint\",\"op\":23,\"class\":\"waypoint\"}",
            "{\"id\":581,\"type\":\"object\",\"x\":5,\"y\":6,\"class\":\"chest-super\"}",
            "{\"id\":9999,\"type\":\"object\",\"x\":7,\"y\":8}",
            "{\"id\":1,\"type\":\"npc\",\"x\":9,\"y\":10,\"name\":\"Fallen\"}",
            "{\"id\":2,\"type\":\"npc\",\"x\":9,\"y\":10}",
            "{\"id\":3,\"type\":\"npc\",\"x\":11,\"y\":12,\"name\":\"Bishibosh\",\"isSuperUnique\":true,\"superId\":0}",
            "{\"id\":6,\"type\":\"npc\",\"x\":13,\"y\":14}",
            // the seeded shrine; the second one duplicates a preset and is dropped
            "{\"id\":2,\"type\":\"object\",\"x\":20,\"y\":21,\"name\":\"Shrine\",\"op\":2,\"class\":\"shrine\"}",
        ], objects);
    }

    [Fact]
    public void OnlyWarpsAreExits()
    {
        var level = Level(2, exits:
        [
            Exit(8, 10, 11, ExitKind.Warp),
            Exit(3, 12, 13, ExitKind.Seam),
            Exit(1, 14, 15, ExitKind.Warp),
        ]);
        var objects = LevelConverter.Objects(level, ActI, Synthetic.GameData())
            .Select(o => JsonSerializer.Serialize(o, MapHttpServer.JsonOptions));
        Assert.Equal(
        [
            "{\"id\":8,\"type\":\"exit\",\"x\":10,\"y\":11,\"name\":\"Den of Evil\",\"isGoodExit\":true}",
            "{\"id\":1,\"type\":\"exit\",\"x\":14,\"y\":15,\"name\":\"Rogue Encampment\"}",
        ], objects);
    }

    [Theory]
    [InlineData(2, 8, 0, true)]
    [InlineData(7, 12, 0, true)]
    [InlineData(6, 20, 0, true)]
    [InlineData(43, 62, 0, true)]
    [InlineData(45, 58, 0, true)]
    [InlineData(41, 55, 0, true)]
    [InlineData(44, 65, 0, true)]
    [InlineData(76, 85, 0, true)]
    [InlineData(78, 88, 0, true)]
    [InlineData(80, 94, 0, true)]
    [InlineData(113, 114, 0, true)]
    [InlineData(46, 68, 68, true)]
    [InlineData(46, 69, 68, false)]
    [InlineData(2, 3, 0, false)]
    [InlineData(8, 2, 0, false)]
    [InlineData(1, 0, 0, false)]
    public void GoodExits(int level, int exit, int staffTomb, bool expected) =>
        Assert.Equal(expected, LevelConverter.IsGoodExit(level, exit, staffTomb));

    [Theory]
    [InlineData(1, 0, "casket")]
    [InlineData(2, 0, "shrine")]
    [InlineData(4, 0, "chest")]
    [InlineData(58, 0, "chest")]
    [InlineData(8, 0, "door")]
    [InlineData(14, 0, "bolder")]
    [InlineData(23, 0, "waypoint")]
    [InlineData(25, 0, "quest")]
    [InlineData(68, 0, "urn-evil")]
    [InlineData(-1, 580, "chest-super")]
    [InlineData(-1, 581, "chest-super")]
    [InlineData(-1, 582, null)]
    [InlineData(99, 0, null)]
    public void ObjectClasses(int operateFn, int classId, string? expected) =>
        Assert.Equal(expected, LevelConverter.ObjectClass(classId, operateFn));
}

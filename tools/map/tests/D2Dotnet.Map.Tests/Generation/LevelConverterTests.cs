using System.Text.Json;
using D2Dotnet.Map.Drlg;
using D2Dotnet.Map.Generation;
using D2Dotnet.Map.Server;

namespace D2Dotnet.Map.Tests.Generation;

public class LevelConverterTests
{
    private static string Rle(CollisionGrid? grid, int width) =>
        JsonSerializer.Serialize(LevelConverter.EncodeCollision(grid, width));

    private static Level Level(int id, IReadOnlyList<PresetUnit>? presets = null, IReadOnlyList<LevelExit>? exits = null,
        IReadOnlyList<Shrine>? shrines = null) => new()
        {
            Id = id,
            Name = "raw name ",
            OriginX = 3,
            OriginY = 7,
            Width = 2,
            Height = 1,
            Rooms = [new Room(3, 7, 2, 1, 1, 2, 0)],
            Presets = presets ?? [],
            Exits = exits ?? [],
            Shrines = shrines ?? [],
        };

    private static readonly Drlg.Act ActI = new() { Number = 0, Levels = [] };

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
        ushort[] cells = [CollisionGrid.Uncovered, 0, 1024, 0, 3, 2, 0x404, 0x405];
        var grid = new CollisionGrid(8, 1, cells);
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
        var json = JsonSerializer.Serialize(LevelConverter.Convert(level, ActI, Synthetic.GameData()), MapServer.JsonOptions);
        Assert.Equal("{\"type\":\"map\",\"id\":2,\"name\":\"Blood Moor\",\"offset\":{\"x\":15,\"y\":35},\"size\":{\"width\":10,\"height\":5},\"objects\":[],\"map\":[[]]}", json);

        // a level missing from Levels.txt keeps the generator's (trimmed) name
        Assert.Equal("raw name", LevelConverter.Convert(Level(77), ActI, Synthetic.GameData()).Name);
    }

    [Fact]
    public void ObjectsFollowUpstreamRules()
    {
        var level = Level(1,
            presets:
            [
                new PresetUnit(PresetUnitType.Object, 5, 1, 2), // not selectable: no op, class from op -1
                new PresetUnit(PresetUnitType.Object, 119, 3, 4),
                new PresetUnit(PresetUnitType.Object, 581, 5, 6),
                new PresetUnit(PresetUnitType.Object, 9999, 7, 8), // not in objects.txt
                new PresetUnit(PresetUnitType.Object, 0, 7, 8), // TestData1: never exported
                new PresetUnit(PresetUnitType.Npc, 1, 9, 10),
                new PresetUnit(PresetUnitType.Npc, 2, 9, 10), // MonStats row without a name
                new PresetUnit(PresetUnitType.Npc, 3, 11, 12), // first super unique
                new PresetUnit(PresetUnitType.Npc, 6, 13, 14), // past the super uniques: a MonPlace entry
                new PresetUnit(PresetUnitType.Npc, 152, 15, 16), // Rogue: never exported
            ],
            shrines: [new Shrine(2, 20, 21), new Shrine(119, 3, 4)]);
        var objects = LevelConverter.Objects(level, ActI, Synthetic.GameData())
            .Select(o => JsonSerializer.Serialize(o, MapServer.JsonOptions));
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
            new LevelExit(8, 10, 11, ExitKind.Warp),
            new LevelExit(3, 12, 13, ExitKind.Seam),
            new LevelExit(1, 14, 15, ExitKind.Warp),
        ]);
        var objects = LevelConverter.Objects(level, ActI, Synthetic.GameData())
            .Select(o => JsonSerializer.Serialize(o, MapServer.JsonOptions));
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

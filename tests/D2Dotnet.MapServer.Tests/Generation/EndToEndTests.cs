using D2Dotnet.Data;
using D2Dotnet.Maps;
using D2Dotnet.MapServer.Data;
using D2Dotnet.MapServer.Generation;
using D2Dotnet.MapServer.Render;
using D2Dotnet.Tests;
using D2Dotnet.Tests.Maps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace D2Dotnet.MapServer.Tests.Generation;

/// <summary>
/// The real DRLG generator over libd2's baked game data (downloaded into the gitignored <c>.cache/libd2/</c> by the
/// build; the tests fail, never skip, without it), through the map service and the JSON conversion. libd2 ships no
/// D2R string tables, so names are the raw table keys.
/// </summary>
public class EndToEndTests
{
    private sealed class LibD2Generator : IMapGenerator
    {
        public static readonly LibD2Generator Instance = new();

        // libd2's excel tables served as data/global/excel/*.txt; no string tables
        private readonly Lazy<GameDatabase> _data = new(() => new GameDatabase(Fixtures.LibD2Files));

        public GameDatabase Data => _data.Value;

        public GeneratedAct Generate(uint seed, Difficulty difficulty, ActId act) => LibD2Fixtures.Generator.Generate(seed, difficulty, act);
    }

    private static MapService Service() => new(LibD2Generator.Instance, Options.Create(new MapOptions { MaxConcurrency = 2 }));

    /// <summary>Expand a run-length encoded row back to walkable flags over <paramref name="width"/> subtiles.</summary>
    private static bool[] Decode(List<int> row, int width)
    {
        var walkable = new bool[width];
        var x = 0;
        var fill = false;
        foreach (var run in row)
        {
            for (var i = 0; i < run; i++) walkable[x++] = fill;
            fill = !fill;
        }
        // the trailing run is implied
        for (; x < width; x++) walkable[x] = fill;
        return walkable;
    }

    [Fact]
    public async Task ActOneMatchesTheGeneratorModel()
    {
        const uint seed = 0x3607656c;
        using var service = Service();
        var levels = await service.MapAsync(seed, 0, 0, NullLogger.Instance);
        var act = LibD2Fixtures.Generator.Generate(seed, Difficulty.Normal, ActId.Act1);

        Assert.Equal(act.Levels.Where(l => l.Rooms.Count > 0).Select(l => (int)l.Id), levels.Select(l => l.Id));
        Assert.All(levels, l => Assert.Equal(D2Dotnet.MapServer.Data.Act.ActI, ActUtil.FromLevel(l.Id)));

        foreach (var level in levels)
        {
            var model = act[(AreaId)level.Id];
            Assert.Equal(new LevelOffset(model.Bounds.X * 5, model.Bounds.Y * 5), level.Offset);
            Assert.Equal(new LevelSize(model.Bounds.Width * 5, model.Bounds.Height * 5), level.Size);
            Assert.InRange(level.Map.Count, 1, level.Size.Height);

            // every row decodes to the model's walkable subtiles
            var grid = model.Collision!;
            for (var y = 0; y < level.Map.Count; y++)
            {
                Assert.True(level.Map[y].Sum() <= level.Size.Width, $"level {level.Id} row {y} is wider than the level");
                var row = Decode(level.Map[y], level.Size.Width);
                for (var x = 0; x < level.Size.Width; x++)
                {
                    var c = grid[x, y];
                    var expected = c != CollisionMap.Uncovered && c != 1024 && c % 2 == 0;
                    if (row[x] != expected) Assert.Fail($"level {level.Id} subtile {x},{y}: {c:x4} decoded as walkable={row[x]}");
                }
            }
            // and nothing below the last row is walkable
            for (var y = level.Map.Count; y < grid.Height; y++)
                for (var x = 0; x < grid.Width; x++)
                    Assert.False(grid.IsWalkable(x, y) && grid[x, y] != 1024, $"level {level.Id} has walkable subtile {x},{y} below its last row");

            foreach (var o in level.Objects)
            {
                Assert.InRange(o.X, 0, level.Size.Width - 1);
                Assert.InRange(o.Y, 0, level.Size.Height - 1);
            }
        }

        var town = levels.Single(l => l.Id == 1);
        Assert.Equal("Rogue Encampment", town.Name);
        var waypoint = Assert.Single(town.Objects, o => o.Class == Diablo2ItemClasses.Waypoint);
        Assert.Equal(Diablo2LevelObjectType.Object, waypoint.Type);
        Assert.Equal(23, waypoint.Op);
        Assert.Contains(town.Objects, o => o.Type == Diablo2LevelObjectType.Npc && o.Name != null);

        // the Blood Moor's cave entrance is the quest exit to the Den of Evil
        var bloodMoor = levels.Single(l => l.Id == 2);
        var den = Assert.Single(bloodMoor.Objects, o => o.Type == Diablo2LevelObjectType.Exit && o.Id == 8);
        Assert.True(den.IsGoodExit);
        Assert.Equal("Den of Evil", den.Name);

        using var bmp = SKBitmap.Decode(MapImage.RenderPng(town));
        Assert.Equal(town.Size.Width, bmp.Width);
    }

    [Fact]
    public async Task ActTwoMarksTheTrueStaffTomb()
    {
        const uint seed = 1337;
        using var service = Service();
        var levels = await service.MapAsync(seed, 2, 1, NullLogger.Instance);
        var staffTomb = (int)LibD2Fixtures.Generator.Generate(seed, Difficulty.Hell, ActId.Act2).StaffTomb!.Value;
        Assert.InRange(staffTomb, 66, 72);

        // the Canyon of the Magi (46) has a warp to each of the seven tombs, and only the true one is a good exit
        var canyon = levels.Single(l => l.Id == 46);
        var tombs = canyon.Objects.Where(o => o.Type == Diablo2LevelObjectType.Exit && o.Id is >= 66 and <= 72).ToList();
        Assert.Equal(Enumerable.Range(66, 7), tombs.Select(t => t.Id).Distinct().Order());
        Assert.All(tombs, t => Assert.Equal(t.Id == staffTomb ? true : null, t.IsGoodExit));
    }

    [Fact]
    public async Task EveryActOfEveryDifficultyConverts()
    {
        using var service = Service();
        foreach (var difficulty in new[] { 0, 1, 2 })
        {
            var levels = await service.MapAsync(7, difficulty, -1, NullLogger.Instance);
            Assert.Equal(levels.Select(l => l.Id).Order(), levels.Select(l => l.Id));
            Assert.Equal(levels.Count, levels.Select(l => l.Id).Distinct().Count());
            foreach (var act in Enum.GetValues<D2Dotnet.MapServer.Data.Act>())
                Assert.Contains(levels, l => ActUtil.FromLevel(l.Id) == act);
            Assert.All(levels, l => Assert.False(string.IsNullOrEmpty(l.Name)));
        }
    }
}

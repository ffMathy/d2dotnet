using D2Dotnet.Data;
using D2Dotnet.Maps;

namespace D2Dotnet.Tests.Maps;

/// <summary>Public API behaviour of <see cref="MapGenerator"/>.</summary>
public sealed class GeneratorTests
{
    private static string Fingerprint(GeneratedAct a)
    {
        var h = new HashCode();
        foreach (var l in a.Levels)
        {
            h.Add(l.Id);
            h.Add(l.Bounds);
            foreach (var r in l.Rooms) h.Add(r);
            foreach (var p in l.Presets) h.Add(p);
            foreach (var e in l.Exits) h.Add(e);
            foreach (var s in l.Shrines) h.Add(s);
            if (l.Collision != null)
                foreach (var c in l.Collision.Cells) h.Add(c);
        }
        return $"{a.Levels.Count}:{h.ToHashCode():X8}";
    }

    [Fact]
    public void IsDeterministicAndThreadSafe()
    {
        var gen = LibD2Fixtures.Generator;
        var expected = new Dictionary<(uint, ActId), string>();
        var work = new List<(uint Seed, ActId Act)>();
        foreach (uint seed in new uint[] { 7, 1337 })
            foreach (var act in Enum.GetValues<ActId>())
            {
                expected[(seed, act)] = Fingerprint(gen.Generate(seed, Difficulty.Hell, act));
                work.Add((seed, act));
            }
        var results = new System.Collections.Concurrent.ConcurrentBag<(uint, ActId, string)>();
        Parallel.ForEach(work.Concat(work), new ParallelOptions { MaxDegreeOfParallelism = 4 },
            w => results.Add((w.Seed, w.Act, Fingerprint(gen.Generate(w.Seed, Difficulty.Hell, w.Act)))));
        Assert.Equal(work.Count * 2, results.Count);
        foreach (var (seed, act, fp) in results) Assert.Equal(expected[(seed, act)], fp);
    }

    [Fact]
    public void ActLevelsAreTheActsLevelsTxtRows()
    {
        var gen = LibD2Fixtures.Generator;
        var act = gen.Generate(1, Difficulty.Normal, ActId.Act1);
        Assert.Equal(ActId.Act1, act.Act);
        Assert.Equal(1u, act.Seed);
        Assert.Equal(Difficulty.Normal, act.Difficulty);
        Assert.Null(act.StaffTomb);
        Assert.Equal(gen.GetAreas(ActId.Act1), act.Levels.Select(l => l.Id).ToArray());
        var town = act[AreaId.RogueEncampment];
        Assert.Same(town, act.Find(AreaId.RogueEncampment));
        Assert.Null(act.Find(AreaId.LutGholein));
        Assert.Throws<KeyNotFoundException>(() => act[AreaId.LutGholein]);
        Assert.Equal("Rogue Encampment", LibD2Fixtures.LevelName(town.Id));
        Assert.Equal(LevelLayout.Preset, town.Layout);
        Assert.NotNull(town.Collision);
        Assert.Equal(town.Bounds.Width * MapLevel.SubtilesPerTile, town.Collision!.Width);
        Assert.Equal(town.Bounds.Height * MapLevel.SubtilesPerTile, town.Collision.Height);
        Assert.Contains(town.Exits, e => e.Destination == AreaId.BloodMoor);
        Assert.NotEmpty(town.Waypoints);
        Assert.All(town.Waypoints, w => Assert.Contains(w, town.Presets));
    }

    [Fact]
    public void ActTwoNamesItsStaffTomb()
    {
        var act = LibD2Fixtures.Generator.Generate(1337, Difficulty.Hell, ActId.Act2);
        Assert.NotNull(act.StaffTomb);
        Assert.InRange(act.StaffTomb!.Value, AreaId.TalRashasTomb1, AreaId.TalRashasTomb7);
    }

    [Fact]
    public void RejectsBadArguments()
    {
        var gen = LibD2Fixtures.Generator;
        Assert.Throws<ArgumentOutOfRangeException>(() => gen.Generate(1, Difficulty.Normal, (ActId)5));
        Assert.Throws<ArgumentOutOfRangeException>(() => gen.Generate(1, (Difficulty)3, ActId.Act1));
        Assert.Throws<ArgumentNullException>(() => new MapGenerator((GameDatabase)null!));
        Assert.Throws<ArgumentNullException>(() => new MapGenerator((D2Dotnet.Files.GameInstall)null!));
    }

    [Fact]
    public void CollisionMapReadsRawFlags()
    {
        var map = new CollisionMap(3, 2, [0, 1, CollisionMap.Uncovered, 4, 5, 0x400]);
        Assert.Equal(3, map.Width);
        Assert.Equal(2, map.Height);
        Assert.Equal(6, map.Cells.Length);
        Assert.Equal(4, map[0, 1]);
        Assert.Equal(5, map[new Point(1, 1)]);
        Assert.True(map.IsWalkable(0, 0));
        Assert.False(map.IsWalkable(1, 0));
        Assert.False(map.IsWalkable(2, 0));
        Assert.False(map.IsCovered(2, 0));
        Assert.True(map.IsCovered(1, 0));
        Assert.True(map.IsWalkable(new Point(0, 1)));
        Assert.False(map.IsWalkable(-1, 0));
        Assert.False(map.IsWalkable(3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map[3, 0]);
        Assert.Throws<ArgumentException>(() => new CollisionMap(2, 2, new ushort[3]));
    }

    [Theory]
    [InlineData(AreaId.RogueEncampment, ActId.Act1)]
    [InlineData(AreaId.CanyonOfTheMagi, ActId.Act2)]
    [InlineData(AreaId.DuranceOfHateLevel3, ActId.Act3)]
    [InlineData(AreaId.PandemoniumFortress, ActId.Act4)]
    [InlineData(AreaId.WorldstoneChamber, ActId.Act5)]
    public void Generate_FromAMapContext_IsTheAreasAct(AreaId area, ActId act)
    {
        var gen = LibD2Fixtures.Generator;
        var context = new MapContext(0x3607656C, Difficulty.Nightmare, area);

        var fromContext = gen.Generate(context);

        Assert.Equal(act, fromContext.Act);
        Assert.Equal(Fingerprint(gen.Generate(0x3607656C, Difficulty.Nightmare, act)), Fingerprint(fromContext));
        Assert.NotNull(fromContext.Find(area));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5000)]
    public void Generate_FromAContextWithoutAGameArea_Throws(int area) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LibD2Fixtures.Generator.Generate(new MapContext(1, Difficulty.Normal, (AreaId)area)));

    [Fact]
    public void Generate_FromAContextWithAnUnknownDifficulty_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LibD2Fixtures.Generator.Generate(new MapContext(1, (Difficulty)7, AreaId.BloodMoor)));
}

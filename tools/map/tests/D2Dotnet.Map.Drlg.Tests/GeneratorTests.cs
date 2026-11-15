namespace D2Dotnet.Map.Drlg.Tests;

/// <summary>Public API behaviour of <see cref="DrlgGenerator"/>.</summary>
public sealed class GeneratorTests
{
    private static string Fingerprint(Act a)
    {
        var h = new HashCode();
        foreach (var l in a.Levels)
        {
            h.Add(l.Id);
            h.Add((l.OriginX, l.OriginY, l.Width, l.Height));
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
        var expected = new Dictionary<(uint, int), string>();
        var work = new List<(uint Seed, int Act)>();
        foreach (uint seed in new uint[] { 7, 1337 })
            for (int act = 0; act < 5; act++)
            {
                expected[(seed, act)] = Fingerprint(gen.GenerateAct(seed, Difficulty.Hell, act));
                work.Add((seed, act));
            }
        var results = new System.Collections.Concurrent.ConcurrentBag<(uint, int, string)>();
        Parallel.ForEach(work.Concat(work), new ParallelOptions { MaxDegreeOfParallelism = 4 },
            w => results.Add((w.Seed, w.Act, Fingerprint(gen.GenerateAct(w.Seed, Difficulty.Hell, w.Act)))));
        Assert.Equal(work.Count * 2, results.Count);
        foreach (var (seed, act, fp) in results) Assert.Equal(expected[(seed, act)], fp);
    }

    [Fact]
    public void ActLevelsAreTheActsLevelsTxtRows()
    {
        var gen = LibD2Fixtures.Generator;
        var act = gen.GenerateAct(1, Difficulty.Normal, 0);
        Assert.Equal(gen.LevelIds(0), act.Levels.Select(l => l.Id).ToArray());
        var town = act.GetLevel(1)!;
        Assert.Equal("Rogue Encampment", town.Name);
        Assert.Equal(DrlgType.Preset, town.DrlgType);
        Assert.NotNull(town.Collision);
        Assert.Equal(town.Width * 5, town.Collision!.Width);
        Assert.Equal(town.Height * 5, town.Collision.Height);
        Assert.Contains(town.Exits, e => e.DestinationLevelId == 2);
        Assert.NotEmpty(town.Waypoints);
    }

    [Fact]
    public void RejectsBadArguments()
    {
        var gen = LibD2Fixtures.Generator;
        Assert.Throws<ArgumentOutOfRangeException>(() => gen.GenerateAct(1, Difficulty.Normal, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => gen.GenerateAct(1, (Difficulty)3, 0));
        Assert.Throws<ArgumentNullException>(() => new DrlgGenerator(null!));
    }
}

using D2Dotnet.MapServer.Generation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace D2Dotnet.MapServer.Tests.Generation;

/// <summary>Drive <see cref="MapService"/> over the fake generator.</summary>
public class MapServiceTests
{
    private static MapService Create(FakeMapGenerator generator, int maxConcurrency = 1, int cacheSize = 100) =>
        new(generator, Options.Create(new MapOptions { MaxConcurrency = maxConcurrency, CacheSize = cacheSize }));

    [Fact]
    public async Task GeneratesAllActsInLevelOrder()
    {
        var generator = new FakeMapGenerator();
        using var service = Create(generator);
        var levels = await service.MapAsync(10, 1, -1, NullLogger.Instance);
        Assert.Equal([1, 2, 40], levels.Select(l => l.Id));
        Assert.Equal("Rogue Encampment", levels[0].Name);
        Assert.Equal("Waypoint", levels[0].Objects[0].Name);
        Assert.Equal([0, 1, 2, 3, 4], generator.Requests.Select(r => r.Act));
        Assert.All(generator.Requests, r => Assert.Equal((10u, Difficulty.Nightmare), (r.Seed, r.Difficulty)));
        Assert.Equal(1, service.GeneratedCount);
    }

    [Fact]
    public async Task GeneratesASingleActAndCaches()
    {
        var generator = new FakeMapGenerator();
        using var service = Create(generator);
        var act1 = await service.MapAsync(10, 0, 1, NullLogger.Instance);
        Assert.Equal([40], act1.Select(l => l.Id));
        var act0 = await service.MapAsync(11, 0, 0, NullLogger.Instance);
        Assert.Equal([1, 2], act0.Select(l => l.Id));

        Assert.Same(act1, await service.MapAsync(10, 0, 1, NullLogger.Instance));
        Assert.NotSame(act1, await service.MapAsync(10, 1, 1, NullLogger.Instance));
        Assert.Equal(3, generator.Calls);
        Assert.Equal(3, service.GeneratedCount);
    }

    [Fact]
    public async Task TheCacheKeepsTwoGenerations()
    {
        var generator = new FakeMapGenerator();
        using var service = Create(generator, cacheSize: 1);
        var a = await service.MapAsync(1, 0, 0, NullLogger.Instance);
        await service.MapAsync(2, 0, 0, NullLogger.Instance);
        await service.MapAsync(3, 0, 0, NullLogger.Instance);
        // 1 fell out of both generations
        Assert.NotSame(a, await service.MapAsync(1, 0, 0, NullLogger.Instance));
        Assert.Equal(4, generator.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ConcurrencyIsCapped(int maxConcurrency)
    {
        var generator = new FakeMapGenerator { Delay = TimeSpan.FromMilliseconds(30) };
        using var service = Create(generator, maxConcurrency);
        var results = await Task.WhenAll(Enumerable.Range(1, 8).Select(seed => service.MapAsync(seed, 2, 0, NullLogger.Instance)));
        Assert.All(results, r => Assert.Equal([1, 2], r.Select(l => l.Id)));
        Assert.Equal(8, generator.Calls);
        Assert.InRange(generator.MaxRunning, 1, maxConcurrency);
        Assert.Equal(maxConcurrency, service.MaxConcurrency);
    }

    [Fact]
    public async Task ConcurrentRequestsForTheSameMapGenerateOnce()
    {
        var generator = new FakeMapGenerator { Delay = TimeSpan.FromMilliseconds(50) };
        // room for every request: only the in-flight sharing keeps them from generating the map four times
        using var service = Create(generator, maxConcurrency: 4);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => service.MapAsync(5, 0, 1, NullLogger.Instance)));
        Assert.All(results, r => Assert.Same(results[0], r));
        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task FailuresAreNotCached()
    {
        var generator = new FakeMapGenerator { Failure = new InvalidDataException("bad data") };
        using var service = Create(generator);
        var err = await Assert.ThrowsAsync<InvalidDataException>(() => service.MapAsync(10, 0, 0, NullLogger.Instance));
        Assert.Equal("bad data", err.Message);
        generator.Failure = null;
        Assert.Equal([1, 2], (await service.MapAsync(10, 0, 0, NullLogger.Instance)).Select(l => l.Id));
    }

    [Theory]
    [InlineData(1.5, 0, 0, "Failed to set seed=1.5")]
    [InlineData(-1, 0, 0, "Failed to set seed=-1")]
    [InlineData(4294967296, 0, 0, "Failed to set seed=4294967296")]
    [InlineData(10, 3, 0, "Failed to set difficulty=3")]
    [InlineData(10, 0, 5, "Failed to set act=5")]
    [InlineData(10, 0, -2, "Failed to set act=-2")]
    public async Task InvalidArgumentsFail(double seed, int difficulty, int act, string message)
    {
        var generator = new FakeMapGenerator();
        using var service = Create(generator);
        var err = await Assert.ThrowsAsync<MapGenerationException>(() => service.MapAsync(seed, difficulty, act, NullLogger.Instance));
        Assert.Equal(message, err.Message);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task TheFullUnsignedSeedRangeIsPassedThrough()
    {
        var generator = new FakeMapGenerator();
        using var service = Create(generator);
        await service.MapAsync(4294967294, 0, 0, NullLogger.Instance);
        Assert.Equal(4294967294u, generator.Requests.Single().Seed);
    }
}

public class GameMapGeneratorTests
{
    [Fact]
    public void AMissingGameFolderFailsEveryCallTheSameWay()
    {
        var path = Path.Join(Path.GetTempPath(), "d2map-missing-" + Guid.NewGuid().ToString("N"));
        using var generator = new GameMapGenerator(Options.Create(new MapOptions { Diablo2Path = path }), NullLogger<GameMapGenerator>.Instance);
        var first = Assert.Throws<DirectoryNotFoundException>(() => generator.Data);
        var second = Assert.Throws<DirectoryNotFoundException>(() => generator.Generate(1, Difficulty.Normal, ActId.Act1));
        Assert.Same(first, second);
    }

    [Fact]
    public void AFolderWithoutTheExcelTablesFails()
    {
        var root = Directory.CreateTempSubdirectory("d2map-empty-");
        try
        {
            using var generator = new GameMapGenerator(Options.Create(new MapOptions { Diablo2Path = root.FullName }), NullLogger<GameMapGenerator>.Instance);
            Assert.ThrowsAny<Exception>(() => generator.Generate(1, Difficulty.Normal, ActId.Act1));
        }
        finally
        {
            root.Delete(true);
        }
    }
}

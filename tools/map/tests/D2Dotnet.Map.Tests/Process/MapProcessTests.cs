using D2Dotnet.Map.Data;
using D2Dotnet.Map.Process;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace D2Dotnet.Map.Tests.Process;

/// <summary>Drive <see cref="Diablo2MapProcess"/> and <see cref="MapCluster"/> against the fake generator script.</summary>
public class MapProcessTests
{
    private static Diablo2MapProcess Create(FakeGame game, MapProcessOptions? options = null) =>
        new(FakeGame.MpqData(), Fixture.Path("fake-d2-map.sh"), options ?? game.Options(), new LruCache<IReadOnlyList<Diablo2Level>>(100), NullLogger.Instance);

    [Fact]
    public async Task GeneratesAllActs()
    {
        using var game = new FakeGame();
        var proc = Create(game);
        try
        {
            var levels = await proc.MapAsync(10, 1, -1, NullLogger.Instance);
            Assert.Equal([1, 2, 40], levels.Select(l => l.Id));
            Assert.Equal("Rogue Encampment", levels[0].Name);
            Assert.Equal("Waypoint", levels[0].Objects[0].Name);
            Assert.True(proc.IsRunning);
            Assert.Equal(1, proc.GeneratedCount);
        }
        finally
        {
            proc.Stop(NullLogger.Instance);
        }
    }

    [Fact]
    public async Task GeneratesASingleActAndCaches()
    {
        using var game = new FakeGame();
        var proc = Create(game);
        try
        {
            var act1 = await proc.MapAsync(10, 0, 1, NullLogger.Instance);
            Assert.Equal([40], act1.Select(l => l.Id));

            // act resets to -1 after each $map, generation of act 0 then all acts works on the same process
            var act0 = await proc.MapAsync(11, 0, 0, NullLogger.Instance);
            Assert.Equal([1, 2], act0.Select(l => l.Id));

            var cached = await proc.MapAsync(10, 0, 1, NullLogger.Instance);
            Assert.Same(act1, cached);
            Assert.Equal(2, proc.GeneratedCount);
            Assert.Equal(1, game.Starts);
        }
        finally
        {
            proc.Stop(NullLogger.Instance);
        }
    }

    [Fact]
    public async Task ConcurrentRequestsAreQueuedOnOneProcess()
    {
        using var game = new FakeGame();
        var proc = Create(game);
        try
        {
            var tasks = Enumerable.Range(1, 5).Select(seed => proc.MapAsync(seed, 2, 0, NullLogger.Instance)).ToList();
            var results = await Task.WhenAll(tasks);
            for (var i = 0; i < results.Length; i++)
            {
                Assert.Equal([1, 2], results[i].Select(l => l.Id));
            }
            Assert.Equal(1, game.Starts);
            Assert.Equal(5, proc.GeneratedCount);
        }
        finally
        {
            proc.Stop(NullLogger.Instance);
        }
    }

    [Fact]
    public async Task RetriesWhenTheGeneratorDies()
    {
        using var game = new FakeGame("crash-once");
        var proc = Create(game);
        try
        {
            var levels = await proc.MapAsync(10, 0, -1, NullLogger.Instance);
            Assert.Equal([1, 2, 40], levels.Select(l => l.Id));
            Assert.Equal(2, game.Starts);
        }
        finally
        {
            proc.Stop(NullLogger.Instance);
        }
    }

    [Fact]
    public async Task GivesUpAfterMaxAttempts()
    {
        using var game = new FakeGame("big-error");
        var options = game.Options();
        options.MaxAttempts = 2;
        var proc = Create(game, options);
        var err = await Assert.ThrowsAsync<MapProcessException>(() => proc.MapAsync(10, 0, -1, NullLogger.Instance));
        Assert.Equal("MapProcess:Closed", err.Message);
        Assert.Equal(3, game.Starts);
        Assert.False(proc.IsRunning);
    }

    [Fact]
    public async Task FailsWhenTheGeneratorEchoesAnotherValue()
    {
        using var game = new FakeGame("bad-echo");
        var proc = Create(game);
        try
        {
            var err = await Assert.ThrowsAsync<MapProcessException>(() => proc.MapAsync(10, 0, -1, NullLogger.Instance));
            Assert.StartsWith("Failed to set seed=10 (output: {\"type\":\"info\",\"seed\":1,\"difficulty\":0}: $seed 10", err.Message);
        }
        finally
        {
            proc.Stop(NullLogger.Instance);
        }
    }

    [Fact]
    public async Task TimesOutWaitingForInit()
    {
        using var game = new FakeGame("no-init");
        var proc = Create(game, game.Options(TimeSpan.FromMilliseconds(500)));
        try
        {
            var err = await Assert.ThrowsAsync<TimeoutException>(() => proc.StartAsync(NullLogger.Instance));
            Assert.Equal("Event: init Timeout after 500ms", err.Message);
        }
        finally
        {
            proc.Stop(NullLogger.Instance);
        }
    }

    [Fact]
    public async Task RestartsAfterMaxMapsToGenerate()
    {
        using var game = new FakeGame();
        var options = game.Options();
        options.MaxMapsToGenerate = 1;
        var proc = Create(game, options);
        try
        {
            for (var seed = 1; seed <= 4; seed++) await proc.MapAsync(seed, 0, 1, NullLogger.Instance);
            Assert.True(game.Starts >= 2);
        }
        finally
        {
            proc.Stop(NullLogger.Instance);
        }
    }

    [Fact]
    public async Task FailsToStartAMissingCommand()
    {
        using var game = new FakeGame();
        var options = game.Options();
        options.WineCommand = Path.Join(game.GamePath, "no-such-wine");
        var proc = Create(game, options);
        var err = await Assert.ThrowsAsync<MapProcessException>(() => proc.StartAsync(NullLogger.Instance));
        Assert.Equal("Failed to start command", err.Message);
        Assert.False(proc.IsRunning);
    }

    [Fact]
    public async Task ClusterUsesProcessesRoundRobinAndSharesTheCache()
    {
        using var game = new FakeGame();
        var options = game.Options();
        options.ProcessCount = 2;
        var source = new FakeMpqDataSource();
        var cluster = new MapCluster(Options.Create(options), source, NullLogger<MapCluster>.Instance);
        try
        {
            var a = await cluster.MapAsync(1, 0, 0, NullLogger.Instance);
            var b = await cluster.MapAsync(2, 0, 0, NullLogger.Instance);
            var c = await cluster.MapAsync(1, 0, 0, NullLogger.Instance);
            Assert.Same(a, c);
            Assert.NotSame(a, b);
            Assert.Equal(2, cluster.Processes.Count);
            Assert.All(cluster.Processes, p => Assert.True(p.IsRunning));
            Assert.Equal(1, source.Loads);
            Assert.Equal(game.GamePath, source.LastPath);
            Assert.Equal(2, game.Starts);
        }
        finally
        {
            cluster.StopAll();
        }
    }

    [Fact]
    public async Task ClusterFailsWithoutTheGeneratorExe()
    {
        using var game = new FakeGame();
        var options = game.Options();
        options.MapCommand = [Path.Join(game.GamePath, "bin/d2-map.exe")];
        var cluster = new MapCluster(Options.Create(options), new FakeMpqDataSource(), NullLogger<MapCluster>.Instance);
        var err = await Assert.ThrowsAsync<MapProcessException>(() => cluster.MapAsync(1, 0, 0, NullLogger.Instance));
        Assert.Equal("MapProcess:MissingMapExe", err.Message);
        // the failure is cached like upstream
        await Assert.ThrowsAsync<MapProcessException>(() => cluster.MapAsync(1, 0, 0, NullLogger.Instance));
    }
}

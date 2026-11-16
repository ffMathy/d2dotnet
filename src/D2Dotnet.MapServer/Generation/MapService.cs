using System.Collections.Concurrent;
using System.Diagnostics;
using D2Dotnet.MapServer.Binary;
using D2Dotnet.MapServer.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace D2Dotnet.MapServer.Generation;

/// <summary>Errors of the map generation.</summary>
public sealed class MapGenerationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Something that can produce the levels of a seed (the HTTP routes depend on this).</summary>
public interface IMapProvider
{
    /// <summary>Generate all the levels of a seed/difficulty, for one act or every act when <paramref name="actId"/> is -1.</summary>
    Task<IReadOnlyList<Diablo2Level>> MapAsync(double seed, int difficulty, int actId, ILogger log);
}

/// <summary>
/// Generates maps in-process with an <see cref="IMapGenerator"/>, at most <see cref="MapOptions.MaxConcurrency"/> at a
/// time, and caches the results (upstream <c>MapCluster</c> + <c>Diablo2MapProcess</c>, without the processes).
/// </summary>
public sealed class MapService(IMapGenerator generator, IOptions<MapOptions> options) : IMapProvider, IDisposable
{
    private readonly LruCache<IReadOnlyList<Diablo2Level>> _cache = new(Math.Max(1, options.Value.CacheSize));
    private readonly SemaphoreSlim _gate = new(Math.Max(1, options.Value.MaxConcurrency));
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<Diablo2Level>>>> _inFlight = new();

    /// <summary>Number of map sets that may be generated at the same time.</summary>
    public int MaxConcurrency { get; } = Math.Max(1, options.Value.MaxConcurrency);

    /// <summary>Number of map sets generated (cache misses).</summary>
    public int GeneratedCount => _generated;

    private int _generated;

    /// <summary>Generate (or fetch from the cache) all levels of a seed/difficulty/act; act -1 is every act.</summary>
    /// <exception cref="MapGenerationException">The seed is not a whole number (upstream's generator echoed another seed and failed).</exception>
    public async Task<IReadOnlyList<Diablo2Level>> MapAsync(double seed, int difficulty, int actId, ILogger log)
    {
        var mapKey = $"{JsNumber.ToJsString(seed)}_{difficulty}_{actId}";
        var cached = _cache.Get(mapKey);
        if (cached != null) return cached;

        // Upstream sent "$seed 1.5" to the generator, which read 1 and the echo check failed the request
        if (Math.Floor(seed) != seed || seed < 0 || seed > uint.MaxValue)
            throw new MapGenerationException($"Failed to set seed={JsNumber.ToJsString(seed)}");
        if (difficulty is < 0 or > 2) throw new MapGenerationException($"Failed to set difficulty={difficulty}");
        if (actId is < -1 or > 4) throw new MapGenerationException($"Failed to set act={actId}");

        // Concurrent requests for the same map share one generation
        var lazy = _inFlight.GetOrAdd(mapKey, key => new Lazy<Task<IReadOnlyList<Diablo2Level>>>(
            () => GenerateAndCacheAsync(key, (uint)seed, (Difficulty)difficulty, actId, log)));
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(KeyValuePair.Create(mapKey, lazy));
        }
    }

    private async Task<IReadOnlyList<Diablo2Level>> GenerateAndCacheAsync(string mapKey, uint seed, Difficulty difficulty, int actId, ILogger log)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var cached = _cache.Get(mapKey);
            if (cached != null) return cached;

            var result = await Task.Run(() => Generate(seed, difficulty, actId, log)).ConfigureAwait(false);
            _cache.Set(mapKey, result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<Diablo2Level> Generate(uint seed, Difficulty difficulty, int actId, ILogger log)
    {
        var startTime = Stopwatch.GetTimestamp();
        var count = Interlocked.Increment(ref _generated);
        var seedHex = Hex.ToHex(seed, 8);
        log.LogInformation("GenerateMap:Start {Seed} {Difficulty} {Act} {Generated}", seedHex, (int)difficulty, actId, count);

        var data = generator.Data;
        var levels = new List<Diablo2Level>();
        var acts = actId > -1 ? [actId] : Enumerable.Range(0, 5);
        foreach (var actNo in acts)
        {
            var act = generator.Generate(seed, difficulty, (ActId)actNo);
            foreach (var level in act.Levels.OrderBy(l => l.Id))
            {
                // Upstream skipped levels whose rooms could not be loaded ("Map:SkippingLevel:FailedRoomLoading")
                if (level.Rooms.Count == 0) continue;
                levels.Add(LevelConverter.Convert(level, act, data));
            }
        }

        log.LogInformation("GenerateMap:Generated {Seed} {Difficulty} {Act} {Count} {Duration}",
            seedHex, (int)difficulty, actId, levels.Count, Stopwatch.GetElapsedTime(startTime).TotalMilliseconds);
        return levels;
    }

    public void Dispose() => _gate.Dispose();
}

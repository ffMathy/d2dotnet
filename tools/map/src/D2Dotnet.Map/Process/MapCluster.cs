using D2Dotnet.Map.Bintools;
using D2Dotnet.Map.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace D2Dotnet.Map.Process;

/// <summary>Something that can produce the levels of a seed (the HTTP routes depend on this).</summary>
public interface IMapProvider
{
    /// <summary>Generate all the levels of a seed/difficulty, for one act or every act when <paramref name="actId"/> is -1.</summary>
    Task<IReadOnlyList<Diablo2Level>> MapAsync(double seed, int difficulty, int actId, ILogger log);
}

/// <summary>Source of the game data used to name levels, npcs and objects.</summary>
public interface IMpqDataSource
{
    Task<Diablo2MpqData> LoadAsync(string basePath, ILogger log);
}

/// <summary>Loads game data from the MPQ archives in the game folder (upstream <c>Diablo2MpqLoader.load</c>).</summary>
public sealed class MpqFileDataSource : IMpqDataSource
{
    public Task<Diablo2MpqData> LoadAsync(string basePath, ILogger log) => Diablo2MpqLoader.LoadAsync(basePath, log);
}

/// <summary>
/// Pool of generator processes used round robin (upstream <c>MapCluster</c>).
/// The cluster is created lazily by the first request; like upstream a failed creation is cached and every later
/// request fails the same way.
/// </summary>
public sealed class MapCluster(IOptions<MapProcessOptions> options, IMpqDataSource mpqSource, ILogger<MapCluster> logger) : IMapProvider
{
    private readonly MapProcessOptions _options = options.Value;
    private readonly Lock _lock = new();
    private Task<Pool>? _instance;

    /// <summary>Number of generator processes in the pool.</summary>
    public int ProcessCount => Math.Max(1, _options.ProcessCount);

    /// <summary>The processes of the pool, once created.</summary>
    public IReadOnlyList<Diablo2MapProcess> Processes =>
        _instance is { IsCompletedSuccessfully: true } t ? t.Result.Processes : [];

    private Task<Pool> GetAsync(ILogger log)
    {
        lock (_lock)
        {
            return _instance ??= CreateAsync(log);
        }
    }

    private async Task<Pool> CreateAsync(ILogger log)
    {
        var mpq = await mpqSource.LoadAsync(_options.Diablo2Path, log).ConfigureAwait(false);

        var mapCommand = FindD2MapExe() ?? throw new MapProcessException("MapProcess:MissingMapExe");
        log.LogInformation("MapProcess:ExeFound {Exe}", mapCommand);

        if (File.Exists(_options.RegistryPath))
        {
            var res = await ChildProcess.RunAsync(_options.WineCommand, ["regedit", _options.RegistryPath]).ConfigureAwait(false);
            log.LogInformation("MapProcess:Registry:Update {Data}", res.Stdout);
        }

        var cache = new LruCache<IReadOnlyList<Diablo2Level>>(_options.CacheSize);
        var processes = new List<Diablo2MapProcess>();
        for (var i = 0; i < ProcessCount; i++) processes.Add(new Diablo2MapProcess(mpq, mapCommand, _options, cache, logger));
        return new Pool(processes);
    }

    private string? FindD2MapExe() => _options.MapCommand.FirstOrDefault(mc => File.Exists(mc) || Directory.Exists(mc));

    public async Task<IReadOnlyList<Diablo2Level>> MapAsync(double seed, int difficulty, int actId, ILogger log)
    {
        var cluster = await GetAsync(log).ConfigureAwait(false);
        var process = cluster.Next();
        if (!process.IsRunning) await process.StartAsync(log).ConfigureAwait(false);
        return await process.MapAsync(seed, difficulty, actId, log).ConfigureAwait(false);
    }

    /// <summary>Stop every generator process.</summary>
    public void StopAll()
    {
        foreach (var p in Processes) p.Stop(logger);
    }

    private sealed class Pool(List<Diablo2MapProcess> processes)
    {
        private int _currentIndex;
        public List<Diablo2MapProcess> Processes { get; } = processes;

        public Diablo2MapProcess Next()
        {
            lock (Processes)
            {
                var currentIndex = _currentIndex;
                _currentIndex = (currentIndex + 1) % Processes.Count;
                return Processes[currentIndex];
            }
        }
    }
}

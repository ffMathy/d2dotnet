using D2Dotnet.Files;
using D2Dotnet.Map.Drlg;
using D2Dotnet.Map.Drlg.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace D2Dotnet.Map.Generation;

// Inside the namespace, so these win over the library's types of the same name in the enclosing D2Dotnet namespace.
using Difficulty = D2Dotnet.Map.Drlg.Difficulty;

/// <summary>The map generator the server depends on: generates acts and knows the names to label them with.</summary>
/// <remarks>Implementations must be thread-safe; the server calls them from several requests at once.</remarks>
public interface IMapGenerator
{
    /// <summary>The level, npc and object names.</summary>
    MapGameData GameData { get; }

    /// <summary>Generate every level of one act (0..4).</summary>
    Act GenerateAct(uint seed, Difficulty difficulty, int act);
}

/// <summary>
/// The production generator: the managed DRLG port (<see cref="DrlgGenerator"/>) over the game files at
/// <see cref="MapOptions.Diablo2Path"/>, a Diablo II: Resurrected install (CASC) or a folder of extracted files.
/// </summary>
/// <remarks>
/// The game files are opened on first use. Like upstream's process pool, a failure to open them is kept and every later
/// request fails the same way (restart the server after fixing the path).
/// </remarks>
public sealed class DrlgMapGenerator : IMapGenerator, IDisposable
{
    private readonly Lazy<Loaded> _loaded;

    public DrlgMapGenerator(IOptions<MapOptions> options, ILogger<DrlgMapGenerator> logger)
    {
        var o = options.Value;
        _loaded = new Lazy<Loaded>(() => Load(o, logger), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public MapGameData GameData => _loaded.Value.Data;

    public Act GenerateAct(uint seed, Difficulty difficulty, int act) => _loaded.Value.Generator.GenerateAct(seed, difficulty, act);

    private static Loaded Load(MapOptions options, ILogger logger)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        logger.LogInformation("GameData:Load {Path}", options.Diablo2Path);
        var files = GameFiles.Open(options.Diablo2Path);
        try
        {
            var source = new GameFilesDataSource(files);
            var generator = new DrlgGenerator(source);
            var data = MapGameData.Load(source, files, options.Language);
            if (data.Strings.Count == 0) logger.LogWarning("GameData:NoStrings {Directory}", GameStrings.Directory);
            logger.LogInformation("GameData:Loaded {Provider} {Language} {Strings} {Duration}",
                files.GetType().Name, data.Strings.Language, data.Strings.Count, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            return new Loaded(files, generator, data);
        }
        catch
        {
            (files as IDisposable)?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_loaded is { IsValueCreated: true } l) (l.Value.Files as IDisposable)?.Dispose();
    }

    private sealed record Loaded(IGameFileProvider Files, DrlgGenerator Generator, MapGameData Data);
}

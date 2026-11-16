using D2Dotnet.Data;
using D2Dotnet.Files;
using D2Dotnet.Maps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace D2Dotnet.MapServer.Generation;

/// <summary>The map generator the server depends on: generates acts and knows the names to label them with.</summary>
/// <remarks>Implementations must be thread-safe; the server calls them from several requests at once.</remarks>
public interface IMapGenerator
{
    /// <summary>The game's tables and string tables, for the level, npc and object names.</summary>
    GameDatabase Data { get; }

    /// <summary>Generate every level of one act.</summary>
    GeneratedAct Generate(uint seed, Difficulty difficulty, ActId act);
}

/// <summary>
/// The production generator: the library's <see cref="MapGenerator"/> over the game at <see cref="MapOptions.Diablo2Path"/>,
/// a Diablo II: Resurrected install (CASC) or a folder of extracted files.
/// </summary>
/// <remarks>
/// The game is opened on first use. Like upstream's process pool, a failure to open it is kept and every later request
/// fails the same way (restart the server after fixing the path).
/// </remarks>
public sealed class GameMapGenerator : IMapGenerator, IDisposable
{
    private readonly Lazy<GameInstall> _game;

    /// <summary>A generator over the game <paramref name="options"/> names.</summary>
    public GameMapGenerator(IOptions<MapOptions> options, ILogger<GameMapGenerator> logger)
    {
        var o = options.Value;
        _game = new Lazy<GameInstall>(() => Load(o, logger), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc/>
    public GameDatabase Data => _game.Value.Data;

    /// <inheritdoc/>
    public GeneratedAct Generate(uint seed, Difficulty difficulty, ActId act) => _game.Value.Maps.Generate(seed, difficulty, act);

    private static GameInstall Load(MapOptions options, ILogger logger)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        logger.LogInformation("GameData:Load {Path}", options.Diablo2Path);
        var game = GameInstall.Open(options.Diablo2Path, options.Language);
        try
        {
            // Read every table now (the generator's, and the ones names come from), so a broken install fails here
            _ = game.Maps;
            _ = game.Data.Levels;
            _ = game.Data.Monsters;
            _ = game.Data.SuperUniques;
            _ = game.Data.Objects;
            var strings = game.Data.Localization;
            if (strings.Count == 0) logger.LogWarning("GameData:NoStrings {Directory}", Localization.Directory);
            logger.LogInformation("GameData:Loaded {Provider} {Language} {Strings} {Duration}",
                game.Files.GetType().Name, strings.Language, strings.Count, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            return game;
        }
        catch
        {
            game.Dispose();
            throw;
        }
    }

    /// <summary>Close the game files, when they were opened.</summary>
    public void Dispose()
    {
        if (_game.IsValueCreated) _game.Value.Dispose();
    }
}

using D2Dotnet.MapServer.Binary;
using D2Dotnet.MapServer.Data;
using D2Dotnet.MapServer.Generation;
using Microsoft.Extensions.Logging;

namespace D2Dotnet.MapServer.Server.Routes;

/// <summary>Shared parameter validation (upstream <c>routes/map.ts</c>).</summary>
public static class MapRouteParams
{
    /// <summary>Upstream <c>isInSeedRange</c>.</summary>
    public static bool IsInSeedRange(double seed) => seed > 0 && seed < 0xffffffff;

    /// <summary>Validate <c>:seed</c> and <c>:difficulty</c>.</summary>
    /// <exception cref="HttpError">422 Invalid seed / Invalid difficulty.</exception>
    public static (double Seed, Difficulty Difficulty) SeedDifficulty(RouteRequest req)
    {
        var seed = JsNumber.Parse(req.Param("seed") ?? "undefined");
        if (double.IsNaN(seed) || !IsInSeedRange(seed)) throw new HttpError(422, "Invalid seed");

        var difficulty = DifficultyUtil.FromString(req.Param("difficulty")) ?? throw new HttpError(422, "Invalid difficulty");
        return (seed, difficulty);
    }

    /// <summary>Validate <c>:act</c>, then <c>:seed</c> and <c>:difficulty</c>.</summary>
    /// <exception cref="HttpError">422 Invalid act / Invalid seed / Invalid difficulty.</exception>
    public static (double Seed, Difficulty Difficulty, Act Act) SeedDifficultyAct(RouteRequest req)
    {
        var act = ActUtil.FromString(req.Param("act")) ?? throw new HttpError(422, "Invalid act");
        var (seed, difficulty) = SeedDifficulty(req);
        return (seed, difficulty, act);
    }

    internal static ILogger Child(ILogger log, IReadOnlyDictionary<string, object?> fields) =>
        new ScopedLogger(log, fields);
}

/// <summary>Dump the entire world for a difficulty/seed: <c>GET /v1/map/:seed/:difficulty.json</c>.</summary>
public sealed class MapRoute(IMapProvider maps) : IRoute
{
    public string Url => "/v1/map/:seed/:difficulty.json";
    public string Pattern => "/v1/map/{seed}/{difficulty}.json";

    public async Task<object?> ProcessAsync(RouteRequest req)
    {
        var (seed, difficulty) = ValidateParams(req);
        var levels = await maps.MapAsync(seed, (int)difficulty, -1, req.Log).ConfigureAwait(false);
        req.Log = MapRouteParams.Child(req.Log, new Dictionary<string, object?>
        {
            ["seed"] = Hex.ToHex((long)seed, 8),
            ["difficulty"] = difficulty.ToString(),
        });
        return new Diablo2Map { Id = req.Id, Seed = seed, Difficulty = difficulty, Levels = levels };
    }

    public static (double Seed, Difficulty Difficulty) ValidateParams(RouteRequest req) => MapRouteParams.SeedDifficulty(req);
}

/// <summary>Grab a specific act from a difficulty: <c>GET /v1/map/:seed/:difficulty/:act.json</c>.</summary>
public sealed class MapActRoute(IMapProvider maps) : IRoute
{
    public string Url => "/v1/map/:seed/:difficulty/:act.json";
    public string Pattern => "/v1/map/{seed}/{difficulty}/{act}.json";

    public async Task<object?> ProcessAsync(RouteRequest req)
    {
        var (seed, difficulty, act) = ValidateParams(req);
        var levels = await maps.MapAsync(seed, (int)difficulty, (int)act, req.Log).ConfigureAwait(false);
        req.Log = MapRouteParams.Child(req.Log, new Dictionary<string, object?>
        {
            ["seed"] = Hex.ToHex((long)seed, 8),
            ["difficulty"] = difficulty.ToString(),
            ["act"] = act.ToString(),
        });
        return new Diablo2Map { Id = req.Id, Seed = seed, Difficulty = difficulty, Levels = levels, Act = act };
    }

    public static (double Seed, Difficulty Difficulty, Act Act) ValidateParams(RouteRequest req) => MapRouteParams.SeedDifficultyAct(req);
}

/// <summary>Grab a specific level from an act: <c>GET /v1/map/:seed/:difficulty/:act/:level.json</c>.</summary>
public sealed class MapActLevelRoute(IMapProvider maps) : IRoute
{
    public string Url => "/v1/map/:seed/:difficulty/:act/:level.json";
    public string Pattern => "/v1/map/{seed}/{difficulty}/{act}/{level}.json";

    public async Task<object?> ProcessAsync(RouteRequest req)
    {
        var (seed, difficulty, act, mapId) = ValidateParams(req);
        var levels = await maps.MapAsync(seed, (int)difficulty, (int)act, req.Log).ConfigureAwait(false);
        req.Log = MapRouteParams.Child(req.Log, new Dictionary<string, object?>
        {
            ["seed"] = Hex.ToHex((long)seed, 8),
            ["difficulty"] = difficulty.ToString(),
            ["act"] = act.ToString(),
            ["mapId"] = double.IsNaN(mapId) ? null : mapId,
        });

        var found = levels.Where(level => level.Id == mapId).ToList();
        return new Diablo2Map { Id = req.Id, Seed = seed, Difficulty = difficulty, Levels = found, Act = act };
    }

    /// <exception cref="HttpError">422 Invalid act / seed / difficulty / map id.</exception>
    public static (double Seed, Difficulty Difficulty, Act Act, double MapId) ValidateParams(RouteRequest req)
    {
        var (seed, difficulty, act) = MapActRoute.ValidateParams(req);
        // Like upstream a non numeric level (NaN) is treated as ActV and simply matches no level
        var mapId = JsNumber.Parse(req.Param("level") ?? "undefined");
        if (ActUtil.FromLevel(mapId) != act) throw new HttpError(422, "Invalid map id");
        return (seed, difficulty, act, mapId);
    }
}

/// <summary>Logger that adds fields as a scope to every entry (pino <c>log.child({...})</c>).</summary>
internal sealed class ScopedLogger(ILogger inner, IReadOnlyDictionary<string, object?> fields) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        using var scope = inner.BeginScope(fields);
        inner.Log(logLevel, eventId, state, exception, formatter);
    }
}

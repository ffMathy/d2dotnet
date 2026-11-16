using D2Dotnet.Map.Data;
using D2Dotnet.Map.Generation;
using D2Dotnet.Map.Render;

namespace D2Dotnet.Map.Server.Routes;

// Inside the namespace, so these win over the library's types of the same name in the enclosing D2Dotnet namespace.
using Difficulty = D2Dotnet.Map.Data.Difficulty;

/// <summary>Render a single level as a PNG: <c>GET /v1/map/:seed/:difficulty/:level.png</c> (upstream <c>routes/map.image.ts</c>).</summary>
public sealed class MapImageRoute(IMapProvider maps) : IRoute
{
    public string Url => "/v1/map/:seed/:difficulty/:level.png";
    public string Pattern => "/v1/map/{seed}/{difficulty}/{level}.png";

    public async Task<object?> ProcessAsync(RouteRequest req)
    {
        var (seed, difficulty, level) = ValidateParams(req);
        var act = ActUtil.FromLevel(level);

        var levels = await maps.MapAsync(seed, (int)difficulty, (int)act, req.Log).ConfigureAwait(false);
        var zone = levels.FirstOrDefault(f => f.Id == level) ?? throw new HttpError(422, "Invalid level");

        return MapImage.RenderPng(zone);
    }

    /// <exception cref="HttpError">422 Invalid seed / Invalid difficulty / Invalid level.</exception>
    public static (double Seed, Difficulty Difficulty, double Level) ValidateParams(RouteRequest req)
    {
        var (seed, difficulty) = MapRouteParams.SeedDifficulty(req);

        var level = JsNumber.Parse(req.Param("level") ?? "undefined");
        if (double.IsNaN(level)) throw new HttpError(422, "Invalid level");

        return (seed, difficulty, level);
    }
}

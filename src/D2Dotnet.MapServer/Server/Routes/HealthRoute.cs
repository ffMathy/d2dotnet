namespace D2Dotnet.MapServer.Server.Routes;

/// <summary>Response of <see cref="HealthRoute"/>.</summary>
public sealed record HealthResponse(string Message);

/// <summary><c>GET /health</c> (upstream <c>routes/health.ts</c>).</summary>
public sealed class HealthRoute : IRoute
{
    public string Url => "/health";
    public string Pattern => "/health";

    public Task<object?> ProcessAsync(RouteRequest req) => Task.FromResult<object?>(new HealthResponse("ok"));
}

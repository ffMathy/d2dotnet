using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace D2Dotnet.Map.Server;

/// <summary>An error with an HTTP status code, its message is returned to the caller (upstream <c>HttpError</c>).</summary>
public sealed class HttpError(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>A request as seen by a route (upstream <c>Request</c>: express request + <c>id</c> + <c>log</c>).</summary>
public sealed class RouteRequest(string id, ILogger log, IReadOnlyDictionary<string, string?> routeParams, string url)
{
    /// <summary>Unique request id (an upper case ULID).</summary>
    public string Id { get; } = id;

    /// <summary>Request logger, scoped to the request id.</summary>
    public ILogger Log { get; set; } = log;

    /// <summary>Decoded route parameters (express <c>req.params</c>).</summary>
    public IReadOnlyDictionary<string, string?> Params { get; } = routeParams;

    /// <summary>Request url (path and query).</summary>
    public string Url { get; } = url;

    public string? Param(string name) => Params.GetValueOrDefault(name);
}

/// <summary>
/// A GET route (upstream <c>Route</c>). A <c>byte[]</c> result is sent as <c>image/png</c>, any other non null result as JSON.
/// </summary>
public interface IRoute
{
    /// <summary>The express style url upstream binds, e.g. <c>/v1/map/:seed/:difficulty.json</c>.</summary>
    string Url { get; }

    /// <summary>The ASP.NET Core route template matching <see cref="Url"/>.</summary>
    string Pattern { get; }

    Task<object?> ProcessAsync(RouteRequest req);
}

/// <summary>ULID generator (the <c>ulid</c> npm package used upstream for request and server ids).</summary>
public static class Ulid
{
    private const string Encoding = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string NewUlid() => NewUlid(DateTimeOffset.UtcNow);

    public static string NewUlid(DateTimeOffset time)
    {
        Span<char> chars = stackalloc char[26];
        var ms = time.ToUnixTimeMilliseconds();
        for (var i = 9; i >= 0; i--)
        {
            chars[i] = Encoding[(int)(ms % 32)];
            ms /= 32;
        }
        Span<byte> random = stackalloc byte[16];
        RandomNumberGenerator.Fill(random);
        for (var i = 10; i < 26; i++) chars[i] = Encoding[random[i - 10] % 32];
        return new string(chars);
    }
}

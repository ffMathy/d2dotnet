using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using D2Dotnet.MapServer.Generation;
using D2Dotnet.MapServer.Server.Routes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace D2Dotnet.MapServer.Server;

/// <summary>Error body returned for failed requests (upstream <c>{ id, message }</c>).</summary>
public sealed record ErrorResponse(string Id, string Message);

/// <summary>The HTTP server (upstream <c>server.ts</c> and the static routes of <c>cli.ts</c>).</summary>
public static class MapHttpServer
{
    /// <summary>Logger category used for request logs.</summary>
    public const string LogCategory = "D2Dotnet.MapServer.Server";

    /// <summary>JSON settings of every response: camelCase like upstream, enums as numbers.</summary>
    /// <remarks>
    /// The relaxed encoder writes <c>'</c>, <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>+</c> and non-ASCII text as-is,
    /// like express's <c>res.json</c> (<c>JSON.stringify</c>), instead of <c>\uXXXX</c> escapes.
    /// </remarks>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Register the map generator, the map service and the routes.</summary>
    public static IServiceCollection AddMapServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MapOptions>(configuration.GetSection(MapOptions.Section));
        services.AddSingleton<IMapGenerator, GameMapGenerator>();
        services.AddSingleton<MapService>();
        services.AddSingleton<IMapProvider>(sp => sp.GetRequiredService<MapService>());

        // Bound in the same order as upstream
        services.AddSingleton<IRoute, HealthRoute>();
        services.AddSingleton<IRoute, MapRoute>();
        services.AddSingleton<IRoute, MapActRoute>();
        services.AddSingleton<IRoute, MapActLevelRoute>();
        services.AddSingleton<IRoute, MapImageRoute>();

        return services;
    }

    /// <summary>Add CORS, the static viewer page and every <see cref="IRoute"/>.</summary>
    public static WebApplication UseMapServer(this WebApplication app)
    {
        // upstream: app.use(cors())
        app.Use(CorsDefaults);

        MapStatic(app);

        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
        foreach (var route in app.Services.GetServices<IRoute>()) Bind(app, route, log);
        return app;
    }

    /// <summary>The <c>cors</c> npm package with its default options.</summary>
    /// <remarks>
    /// Every response gets <c>Access-Control-Allow-Origin: *</c> (with or without an <c>Origin</c> header);
    /// every <c>OPTIONS</c> request is answered with 204, the default method list and the requested headers echoed back.
    /// </remarks>
    internal static Task CorsDefaults(HttpContext ctx, Func<Task> next)
    {
        var req = ctx.Request;
        var headers = ctx.Response.Headers;
        headers.AccessControlAllowOrigin = "*";
        if (!HttpMethods.IsOptions(req.Method)) return next();

        headers.AccessControlAllowMethods = "GET,HEAD,PUT,PATCH,POST,DELETE";
        var requestHeaders = req.Headers.AccessControlRequestHeaders;
        headers.Append("Vary", "Access-Control-Request-Headers");
        if (requestHeaders.Count > 0) headers.AccessControlAllowHeaders = requestHeaders.ToString();
        ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        headers.ContentLength = 0;
        return Task.CompletedTask;
    }

    private static void Bind(WebApplication app, IRoute route, ILogger log)
    {
        log.LogInformation("Bind {Url}", route.Url);
        app.MapMethods(route.Pattern, [HttpMethods.Get, HttpMethods.Head], ctx => HandleAsync(ctx, route, log));
    }

    internal static async Task HandleAsync(HttpContext ctx, IRoute route, ILogger log)
    {
        var id = Ulid.NewUlid();
        using var scope = log.BeginScope(new Dictionary<string, object?> { ["id"] = id });
        var routeParams = ctx.Request.RouteValues.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        var url = ctx.Request.Path + ctx.Request.QueryString;
        var req = new RouteRequest(id, log, routeParams, url);
        var startTime = Stopwatch.GetTimestamp();
        var res = ctx.Response;
        try
        {
            var output = await route.ProcessAsync(req).ConfigureAwait(false);
            if (output != null)
            {
                res.StatusCode = StatusCodes.Status200OK;
                if (output is byte[] png)
                {
                    res.ContentType = "image/png";
                    res.ContentLength = png.Length;
                    await res.Body.WriteAsync(png, ctx.RequestAborted).ConfigureAwait(false);
                }
                else
                {
                    await res.WriteAsJsonAsync(output, output.GetType(), JsonOptions, ctx.RequestAborted).ConfigureAwait(false);
                }
            }
        }
        catch (HttpError e)
        {
            req.Log.LogWarning("{Message}", e.Message);
            res.StatusCode = e.Status;
            await res.WriteAsJsonAsync(new ErrorResponse(req.Id, e.Message), JsonOptions).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            req.Log.LogError(e, "Failed to run");
            res.StatusCode = StatusCodes.Status500InternalServerError;
            await res.WriteAsJsonAsync(new ErrorResponse(req.Id, "Internal server error"), JsonOptions).ConfigureAwait(false);
        }
        var duration = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
        req.Log.LogInformation("{Url} {Duration} {Status}", req.Url, duration, res.StatusCode);
    }

    /// <summary>
    /// <c>GET /</c> serves <c>wwwroot/index.html</c>. <c>GET /index.js</c> serves <c>wwwroot/index.js</c> (the browser
    /// viewer bundle, which is not part of this port) when present, with <c>process.env.MAP_HOST</c> replaced by <c>''</c>.
    /// </summary>
    private static void MapStatic(WebApplication app)
    {
        var env = app.Services.GetRequiredService<IWebHostEnvironment>();

        app.MapMethods("/", [HttpMethods.Get, HttpMethods.Head], async ctx =>
        {
            var file = env.WebRootFileProvider.GetFileInfo("index.html");
            if (!file.Exists)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.SendFileAsync(file).ConfigureAwait(false);
        });

        app.MapMethods("/index.js", [HttpMethods.Get, HttpMethods.Head], async ctx =>
        {
            var file = env.WebRootFileProvider.GetFileInfo("index.js");
            if (!file.Exists)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            string js;
            await using (var stream = file.CreateReadStream())
            using (var reader = new StreamReader(stream))
            {
                js = await reader.ReadToEndAsync().ConfigureAwait(false);
            }
            // upstream String.replace with a string pattern only replaces the first occurrence
            var idx = js.IndexOf("process.env.MAP_HOST", StringComparison.Ordinal);
            if (idx >= 0) js = string.Concat(js.AsSpan(0, idx), "''", js.AsSpan(idx + "process.env.MAP_HOST".Length));
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            ctx.Response.ContentType = "text/javascript; charset=utf-8";
            await ctx.Response.WriteAsync(js).ConfigureAwait(false);
        });
    }
}

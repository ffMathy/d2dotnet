using D2Dotnet.Map;
using D2Dotnet.Map.Generation;
using D2Dotnet.Map.Server;
using Microsoft.Extensions.Options;

CliOptions cli;
try
{
    cli = CliOptions.Parse(args, Environment.GetEnvironmentVariable);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(CliOptions.Usage);
    return 1;
}

if (cli.ShowHelp)
{
    Console.WriteLine(CliOptions.Usage);
    return 0;
}

var builder = WebApplication.CreateBuilder([.. cli.Remaining]);
builder.Configuration.AddInMemoryCollection(cli.Configuration);
builder.WebHost.UseUrls($"http://*:{cli.Port}");

// pino: JSON lines when not attached to a terminal, pretty output otherwise; level debug
builder.Logging.ClearProviders();
if (Console.IsOutputRedirected)
{
    builder.Logging.AddJsonConsole(o =>
    {
        o.IncludeScopes = true;
        o.TimestampFormat = "O";
    });
}
else
{
    builder.Logging.AddSimpleConsole(o =>
    {
        o.IncludeScopes = true;
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss.fff ";
    });
}
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

builder.Services.AddMapServer(builder.Configuration);

var app = builder.Build();
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(MapServer.LogCategory);
var options = app.Services.GetRequiredService<IOptions<MapOptions>>().Value;

if (!Directory.Exists(options.Diablo2Path) && !File.Exists(options.Diablo2Path))
    log.LogWarning("Diablo2Path:Missing {Path}", options.Diablo2Path);

app.UseMapServer();

app.Lifetime.ApplicationStarted.Register(() => log.LogInformation(
    "Server started... {Id} {Port} {Url} {Concurrency} {Version} {Hash}",
    Ulid.NewUlid().ToLowerInvariant(),
    cli.Port,
    "http://localhost:" + cli.Port,
    app.Services.GetRequiredService<MapService>().MaxConcurrency,
    Environment.GetEnvironmentVariable("GIT_VERSION"),
    Environment.GetEnvironmentVariable("GIT_HASH")));

try
{
    await app.RunAsync();
    return 0;
}
catch (Exception e)
{
    log.LogCritical(e, "Uncaught Exception");
    return 1;
}

/// <summary>Entry point (public for <c>WebApplicationFactory</c>).</summary>
public partial class Program;

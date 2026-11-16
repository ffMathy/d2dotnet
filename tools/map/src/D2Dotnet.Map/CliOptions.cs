using System.Globalization;
using D2Dotnet.Map.Data;
using D2Dotnet.Map.Generation;

namespace D2Dotnet.Map;

/// <summary>
/// Command line options of the server executable. Upstream (<c>cli.ts</c>) is configured only by environment
/// variables, which are still honoured; every switch overrides its environment variable.
/// </summary>
public sealed class CliOptions
{
    public const string Usage = """
        Usage:
            D2Dotnet.Map [options]

        Options:
            --port <port>             HTTP port to listen on (env PORT, default 8899)
            --concurrency <n>         Maximum number of maps generated at the same time (env DIABLO2_CONCURRENCY, default 1)
            --game-path <path>        Diablo II: Resurrected install folder, or a folder of extracted game files (default /app/game)
            --language <code>         Language of level/npc/object names: enUS, deDE, frFR, ... (default enUS)
            --help [-h]               Show this help

        Any other argument is passed to ASP.NET Core configuration (e.g. --Logging:LogLevel:Default=Information).
        """;

    /// <summary>HTTP port (upstream <c>parseInt(process.env.PORT ?? '8899', 10)</c>).</summary>
    public int Port { get; private set; } = 8899;

    public bool ShowHelp { get; private set; }

    /// <summary>Configuration overrides for the <see cref="MapOptions.Section"/> section.</summary>
    public Dictionary<string, string?> Configuration { get; } = [];

    /// <summary>Arguments not understood here, forwarded to the ASP.NET Core host.</summary>
    public List<string> Remaining { get; } = [];

    /// <exception cref="ArgumentException">A switch is missing its value or a value is invalid.</exception>
    public static CliOptions Parse(IReadOnlyList<string> args, Func<string, string?> getEnv)
    {
        var o = new CliOptions();
        const string section = MapOptions.Section;

        if (getEnv("PORT") is { } envPort) o.Port = ParseIntPrefix(envPort) ?? throw new ArgumentException($"Invalid PORT: {envPort}");

        // DIABLO2_CLUSTER_SIZE (upstream's generator process count) still works as the older name
        foreach (var name in new[] { "DIABLO2_CLUSTER_SIZE", "DIABLO2_CONCURRENCY" })
        {
            if (getEnv(name) is not { Length: > 0 } envConcurrency) continue;
            // upstream: if (!isNaN(Number(value))) MapCluster.ProcessCount = value
            var concurrency = JsNumber.Parse(envConcurrency);
            if (!double.IsNaN(concurrency)) o.SetConcurrency(concurrency);
        }

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"Missing value for {arg}");
            switch (arg)
            {
                case "--help" or "-h":
                    o.ShowHelp = true;
                    break;
                case "--port":
                    var port = Value();
                    o.Port = ParseIntPrefix(port) ?? throw new ArgumentException($"Invalid --port: {port}");
                    break;
                case "--concurrency" or "--cluster-size":
                    var size = Value();
                    var parsed = JsNumber.Parse(size);
                    if (double.IsNaN(parsed)) throw new ArgumentException($"Invalid {arg}: {size}");
                    o.SetConcurrency(parsed);
                    break;
                case "--game-path":
                    o.Configuration[$"{section}:{nameof(MapOptions.Diablo2Path)}"] = Value();
                    break;
                case "--language":
                    var language = Value();
                    if (string.IsNullOrWhiteSpace(language)) throw new ArgumentException("Invalid --language: a language code is required");
                    o.Configuration[$"{section}:{nameof(MapOptions.Language)}"] = GameStrings.ResolveLanguage(language);
                    break;
                default:
                    o.Remaining.Add(arg);
                    break;
            }
        }

        return o;
    }

    /// <summary>
    /// Like upstream's process count (<c>for (i = 0; i &lt; ProcessCount; i++)</c>) fractional values round up, and
    /// values below one (which would leave upstream without any generator) allow one generation at a time.
    /// </summary>
    private void SetConcurrency(double value)
    {
        var count = double.IsPositiveInfinity(value) ? 1 : Math.Max(1, (int)Math.Min(Math.Ceiling(value), 1024));
        Configuration[$"{MapOptions.Section}:{nameof(MapOptions.MaxConcurrency)}"] = count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>JavaScript <c>parseInt(value, 10)</c>: leading digits only.</summary>
    private static int? ParseIntPrefix(string value)
    {
        var s = JsNumber.TrimJs(value);
        var end = 0;
        if (end < s.Length && s[end] is '+' or '-') end++;
        while (end < s.Length && char.IsAsciiDigit(s[end])) end++;
        return int.TryParse(s.AsSpan(0, end), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}

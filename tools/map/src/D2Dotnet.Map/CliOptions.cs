using System.Globalization;
using D2Dotnet.Map.Data;
using D2Dotnet.Map.Process;

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
            --cluster-size <n>        Number of map generator processes (env DIABLO2_CLUSTER_SIZE, default 1)
            --game-path <path>        Diablo 2 game folder with the MPQ files (default /app/game)
            --registry-path <path>    Registry file imported with "wine regedit" (default /app/d2.install.reg)
            --wine <command>          Command used to run the generator (default wine)
            --map-exe <path>          Generator executable, may be repeated (default ./bin/d2-map.exe ../bin/d2-map.exe)
            --help [-h]               Show this help

        Any other argument is passed to ASP.NET Core configuration (e.g. --Logging:LogLevel:Default=Information).
        """;

    /// <summary>HTTP port (upstream <c>parseInt(process.env.PORT ?? '8899', 10)</c>).</summary>
    public int Port { get; private set; } = 8899;

    public bool ShowHelp { get; private set; }

    /// <summary>Configuration overrides for the <see cref="MapProcessOptions.Section"/> section.</summary>
    public Dictionary<string, string?> Configuration { get; } = [];

    /// <summary>Arguments not understood here, forwarded to the ASP.NET Core host.</summary>
    public List<string> Remaining { get; } = [];

    /// <exception cref="ArgumentException">A switch is missing its value or a value is invalid.</exception>
    public static CliOptions Parse(IReadOnlyList<string> args, Func<string, string?> getEnv)
    {
        var o = new CliOptions();
        const string section = MapProcessOptions.Section;

        if (getEnv("PORT") is { } envPort) o.Port = ParseIntPrefix(envPort) ?? throw new ArgumentException($"Invalid PORT: {envPort}");

        if (getEnv("DIABLO2_CLUSTER_SIZE") is { Length: > 0 } envCluster)
        {
            // upstream: if (!isNaN(Number(value))) MapCluster.ProcessCount = value
            var clusterSize = JsNumber.Parse(envCluster);
            if (!double.IsNaN(clusterSize)) o.SetClusterSize(clusterSize);
        }

        var mapExes = new List<string>();
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
                case "--cluster-size":
                    var size = Value();
                    var parsed = JsNumber.Parse(size);
                    if (double.IsNaN(parsed)) throw new ArgumentException($"Invalid --cluster-size: {size}");
                    o.SetClusterSize(parsed);
                    break;
                case "--game-path":
                    o.Configuration[$"{section}:{nameof(MapProcessOptions.Diablo2Path)}"] = Value();
                    break;
                case "--registry-path":
                    o.Configuration[$"{section}:{nameof(MapProcessOptions.RegistryPath)}"] = Value();
                    break;
                case "--wine":
                    o.Configuration[$"{section}:{nameof(MapProcessOptions.WineCommand)}"] = Value();
                    break;
                case "--map-exe":
                    mapExes.Add(Value());
                    break;
                default:
                    o.Remaining.Add(arg);
                    break;
            }
        }

        for (var i = 0; i < mapExes.Count; i++)
            o.Configuration[$"{section}:{nameof(MapProcessOptions.MapCommand)}:{i}"] = mapExes[i];

        return o;
    }

    /// <summary>
    /// Upstream loops <c>for (i = 0; i &lt; ProcessCount; i++)</c>, so fractional sizes round up. Sizes below one
    /// would leave upstream without any process; at least one process is always started here.
    /// </summary>
    private void SetClusterSize(double value)
    {
        var count = double.IsPositiveInfinity(value) ? 1 : Math.Max(1, (int)Math.Min(Math.Ceiling(value), 1024));
        Configuration[$"{MapProcessOptions.Section}:{nameof(MapProcessOptions.ProcessCount)}"] = count.ToString(CultureInfo.InvariantCulture);
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

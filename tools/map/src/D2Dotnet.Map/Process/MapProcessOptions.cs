namespace D2Dotnet.Map.Process;

/// <summary>
/// Configuration of the map generator processes. Defaults are the constants hard coded upstream in <c>map.process.ts</c>.
/// Bound from the <c>Map</c> configuration section (see <c>Program.cs</c> for the command line switches).
/// </summary>
public sealed class MapProcessOptions
{
    public const string Section = "Map";

    /// <summary>Candidate locations of the generator executable, first existing one wins (upstream <c>MapCommand</c>).</summary>
    public List<string> MapCommand { get; set; } = ["./bin/d2-map.exe", "../bin/d2-map.exe"];

    /// <summary>Diablo 2 game folder containing the MPQ files (upstream <c>Diablo2Path</c>).</summary>
    public string Diablo2Path { get; set; } = "/app/game";

    /// <summary>Registry file imported with <c>wine regedit</c> before the first generator starts (upstream <c>RegistryPath</c>).</summary>
    public string RegistryPath { get; set; } = "/app/d2.install.reg";

    /// <summary>Command used to run the generator, which is passed the exe and game path (upstream <c>WineCommand</c>).</summary>
    public string WineCommand { get; set; } = "wine";

    /// <summary>Number of generator processes (upstream <c>MapCluster.ProcessCount</c>, env <c>DIABLO2_CLUSTER_SIZE</c>).</summary>
    public int ProcessCount { get; set; } = 1;

    /// <summary>How long to wait for any single generator response (upstream <c>ProcessTimeout</c>, 30 seconds).</summary>
    public TimeSpan ProcessTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Number of retries when the generator dies while generating (upstream <c>MaxAttempts</c>).</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Restart a generator after it has generated this many map sets (upstream <c>MaxMapsToGenerate</c>).</summary>
    public int MaxMapsToGenerate { get; set; } = 5000;

    /// <summary>Generation size of the shared map cache (upstream <c>new LruCache(100)</c>).</summary>
    public int CacheSize { get; set; } = 100;
}

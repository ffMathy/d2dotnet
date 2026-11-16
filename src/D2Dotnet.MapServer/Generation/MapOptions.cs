using D2Dotnet.Data;

namespace D2Dotnet.MapServer.Generation;

/// <summary>
/// Configuration of the in-process map generator, bound from the <c>Map</c> configuration section
/// (see <see cref="CliOptions"/> for the command line switches).
/// </summary>
public sealed class MapOptions
{
    public const string Section = "Map";

    /// <summary>
    /// The Diablo II: Resurrected install (the folder holding <c>.build.info</c>) or a folder of extracted game files
    /// (upstream <c>Diablo2Path</c>).
    /// </summary>
    public string Diablo2Path { get; set; } = "/app/game";

    /// <summary>
    /// Maximum number of map sets generated at the same time; further requests wait. Replaces upstream's
    /// generator process count (<c>DIABLO2_CLUSTER_SIZE</c>), which had the same effect.
    /// </summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>Generation size of the map cache (upstream <c>new LruCache(100)</c>).</summary>
    public int CacheSize { get; set; } = 100;

    /// <summary>
    /// Language of level, npc and object names: a D2R string table field such as <c>enUS</c> or <c>deDE</c>, or a classic
    /// language code such as <c>ENG</c> (see <see cref="Localization.ResolveLanguage"/>). Upstream always used <c>ENG</c>.
    /// </summary>
    public string Language { get; set; } = Localization.DefaultLanguage;
}

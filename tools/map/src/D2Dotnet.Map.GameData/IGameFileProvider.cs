namespace D2Dotnet.Map.GameData;

/// <summary>
/// Read-only access to Diablo II: Resurrected game files.
/// </summary>
/// <remarks>
/// Paths are relative to the game's <c>data</c> root and use the classic layout, for example
/// <c>data/global/excel/levels.txt</c> or <c>data/global/tiles/act1/town/townn1.ds1</c>.
/// Lookups are case-insensitive and accept both <c>/</c> and <c>\</c> as separators.
/// Implementations: a CASC reader for an installed game, and a plain folder of extracted files.
/// </remarks>
public interface IGameFileProvider
{
    /// <summary>True when the file exists.</summary>
    bool Exists(string path);

    /// <summary>Read a whole file, or return null when it does not exist.</summary>
    byte[]? ReadFile(string path);

    /// <summary>All file paths (normalized: lowercase, forward slashes, relative to the data root) under <paramref name="directory"/>, recursively.</summary>
    IEnumerable<string> EnumerateFiles(string directory);
}

/// <summary>Path helpers shared by providers.</summary>
public static class GamePath
{
    /// <summary>Normalize to lowercase, forward slashes, no leading slash.</summary>
    public static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
}

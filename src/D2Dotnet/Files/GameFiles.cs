namespace D2Dotnet.Files;

/// <summary>Picks the right <see cref="IGameFileProvider"/> for a folder.</summary>
public static class GameFiles
{
    /// <summary>
    /// A <see cref="CascFileProvider"/> when <paramref name="path"/> is a D2R install (it contains
    /// <c>.build.info</c>, or is that file), otherwise a <see cref="DirectoryFileProvider"/> over extracted files.
    /// </summary>
    /// <remarks>Dispose the result when it is <see cref="IDisposable"/> (the CASC provider keeps the data archives open).</remarks>
    /// <exception cref="DirectoryNotFoundException"><paramref name="path"/> does not exist.</exception>
    /// <exception cref="NotSupportedException"><paramref name="path"/> is a static (Steam) CASC storage, which this reader does not support.</exception>
    public static IGameFileProvider Open(string path)
    {
        if (File.Exists(path) && string.Equals(Path.GetFileName(path), ".build.info", StringComparison.OrdinalIgnoreCase))
            return CascFileProvider.Open(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(Path.Combine(path, ".build.info")))
            return CascFileProvider.Open(path);
        if (File.Exists(Path.Combine(path, "Data", ".build.config")) || File.Exists(Path.Combine(path, "data", ".build.config")))
            throw new NotSupportedException(
                $"'{path}' looks like a static (Steam) CASC storage (Data/.build.config). Only Battle.net installs with a .build.info are supported; extract the files and open the folder instead.");
        return new DirectoryFileProvider(path);
    }
}

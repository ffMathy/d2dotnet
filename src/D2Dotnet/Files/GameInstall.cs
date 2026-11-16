using D2Dotnet.Data;

namespace D2Dotnet.Files;

/// <summary>
/// A Diablo II: Resurrected install (or a folder of extracted game files): its files and, created on first use, its
/// data. The entry point of the library. Dispose it to close the game files.
/// </summary>
/// <example>
/// <code>
/// using var game = GameInstall.Open(@"C:\Program Files (x86)\Diablo II Resurrected");
/// var level = game.Data.Levels.Find(AreaId.BloodMoor);
/// var name = game.Data.Localization[AreaId.BloodMoor];
/// </code>
/// </example>
public sealed class GameInstall : IDisposable
{
    private readonly bool _ownsFiles;
    private readonly Lazy<GameDatabase> _data;
    private int _disposed;

    /// <summary>An install over game files the caller already opened.</summary>
    /// <param name="files">The game files.</param>
    /// <param name="language">The language of <see cref="GameDatabase.Localization"/> (see <see cref="Localization.ResolveLanguage"/>).</param>
    /// <param name="ownsFiles">True to dispose <paramref name="files"/> (when it is <see cref="IDisposable"/>) with this instance.</param>
    /// <exception cref="ArgumentException"><paramref name="language"/> is blank.</exception>
    public GameInstall(IGameFileProvider files, string language = Localization.DefaultLanguage, bool ownsFiles = true)
    {
        Files = files ?? throw new ArgumentNullException(nameof(files));
        Language = Localization.ResolveLanguage(language);
        _ownsFiles = ownsFiles;
        _data = new Lazy<GameDatabase>(() => new GameDatabase(Files, Language), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Open the game at <paramref name="path"/>: a D2R install (read from its CASC storage) or a folder of extracted files
    /// (see <see cref="GameFiles.Open"/>).
    /// </summary>
    /// <param name="path">The install folder (the one with <c>.build.info</c>), or a folder of extracted files.</param>
    /// <param name="language">The language of <see cref="GameDatabase.Localization"/> (see <see cref="Localization.ResolveLanguage"/>).</param>
    /// <exception cref="DirectoryNotFoundException"><paramref name="path"/> does not exist.</exception>
    /// <exception cref="NotSupportedException"><paramref name="path"/> is a static (Steam) CASC storage.</exception>
    /// <exception cref="CascFormatException">The CASC storage is malformed.</exception>
    public static GameInstall Open(string path, string language = Localization.DefaultLanguage)
    {
        var resolved = Localization.ResolveLanguage(language);
        return new GameInstall(GameFiles.Open(path), resolved);
    }

    /// <summary>The game files.</summary>
    public IGameFileProvider Files { get; }

    /// <summary>The D2R language field of <see cref="GameDatabase.Localization"/>.</summary>
    public string Language { get; }

    /// <summary>The game's data (excel and string tables), created on first use.</summary>
    /// <exception cref="ObjectDisposedException">The install was disposed.</exception>
    public GameDatabase Data
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _data.Value;
        }
    }

    /// <summary>Close the game files (when this instance owns them).</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsFiles)
            (Files as IDisposable)?.Dispose();
    }

    /// <inheritdoc/>
    public override string ToString() => Files.ToString() ?? nameof(GameInstall);
}

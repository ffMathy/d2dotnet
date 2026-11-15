using D2Dotnet.Map.Generator.Output;

namespace D2Dotnet.Map.Generator.Game;

/// <summary>Port of <c>enum D2Version</c> (upstream map/d2_version.h).</summary>
public enum D2Version
{
    VersionUnknown = 0,
    VersionDiablo2 = 1,
    VersionPathOfDiablo = 2,
    VersionProjectDiablo2 = 3,
}

/// <summary>Port of upstream map/d2_version.h: which mod is installed in a game folder.</summary>
public sealed class GameVersions
{
    private static readonly LogSource SrcGamePath = new("map/d2_version.h", 38);

    public const string PathPathOfDiablo = "Path of Diablo/";
    public const string PathProjectDiablo2 = "ProjectD2/";
    public const string PathDiablo2 = "";

    private readonly IFileProbe _files;
    private readonly Logger _log;

    public GameVersions(IFileProbe files, Logger log)
    {
        _files = files;
        _log = log;
    }

    /// <summary><c>game_version_path</c>: folder (with trailing slash) holding the game files, null when unknown.</summary>
    public static string? GameVersionPath(D2Version version) => version switch
    {
        D2Version.VersionPathOfDiablo => PathPathOfDiablo,
        D2Version.VersionProjectDiablo2 => PathProjectDiablo2,
        D2Version.VersionDiablo2 => PathDiablo2,
        _ => null,
    };

    /// <summary><c>game_version_exists</c>: checks for <c>"%s\\%sGame.exe"</c>.</summary>
    public bool GameVersionExists(string folderName, D2Version version)
    {
        var gamePath = GameVersionPath(version);
        if (gamePath == null) return false;

        var gamePathExe = folderName + "\\" + gamePath + "Game.exe";
        var exists = _files.CanOpen(gamePathExe);
        _log.Trace(SrcGamePath, "Init:GamePath", LogField.B("exists", exists), LogField.S("game", gamePathExe));
        return exists;
    }

    /// <summary><c>game_version</c>: Path of Diablo, then Project Diablo 2, then plain Diablo 2.</summary>
    public D2Version GameVersion(string folderName)
    {
        if (GameVersionExists(folderName, D2Version.VersionPathOfDiablo)) return D2Version.VersionPathOfDiablo;
        if (GameVersionExists(folderName, D2Version.VersionProjectDiablo2)) return D2Version.VersionProjectDiablo2;
        if (GameVersionExists(folderName, D2Version.VersionDiablo2)) return D2Version.VersionDiablo2;
        return D2Version.VersionUnknown;
    }
}

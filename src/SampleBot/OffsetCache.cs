using D2Dotnet.Memory;

/// <summary>
/// Keeps the offsets a scan found, one file per game build. The running game keeps part of its code unreadable, and which
/// part changes while it runs, so a later scan can miss a pattern an earlier one found.
/// </summary>
internal static class OffsetCache
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Dotnet", "offsets");

    /// <summary>The cached offsets of the running build, or a fresh scan (saved for next time) when there are none.</summary>
    public static GameOffsets LoadOrScan(GameProcess process)
    {
        var build = process.ReadBuildTimestamp();
        var file = Path.Combine(Folder, $"{build:X8}.txt");
        if (build != 0 && File.Exists(file))
            return GameOffsets.Parse(File.ReadAllText(file));

        var offsets = GameOffsets.Find(process.ReadModule());
        if (build != 0)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(file, offsets.ToText());
        }

        return offsets;
    }
}

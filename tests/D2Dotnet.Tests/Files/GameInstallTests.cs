using D2Dotnet.Data;
using D2Dotnet.Files;

namespace D2Dotnet.Tests.Files;

public class GameInstallTests
{
    private sealed class DisposableFiles(IGameFileProvider inner) : IGameFileProvider, IDisposable
    {
        public int Disposed { get; private set; }

        public bool Exists(string path) => inner.Exists(path);

        public byte[]? ReadFile(string path) => inner.ReadFile(path);

        public IEnumerable<string> EnumerateFiles(string directory) => inner.EnumerateFiles(directory);

        public void Dispose() => Disposed++;
    }

    [Fact]
    public void Open_ReadsAnExtractedFolder()
    {
        using var temp = new TempDirectory();
        var excel = Directory.CreateDirectory(Path.Combine(temp.Path, "data", "global", "excel"));
        File.WriteAllText(Path.Combine(excel.FullName, "Levels.txt"), "Name\tId\tLevelName\r\nAct 1 - Town\t1\tRogue Encampment\r\n");

        using var game = GameInstall.Open(temp.Path, "ENG");
        Assert.IsType<DirectoryFileProvider>(game.Files);
        Assert.Equal("enUS", game.Language);
        Assert.Same(game.Data, game.Data);
        Assert.Equal("enUS", game.Data.Language);
        Assert.Equal("Rogue Encampment", game.Data.Levels.Find(AreaId.RogueEncampment)?.NameKey);
        Assert.Equal("Rogue Encampment", game.Data.Localization[AreaId.RogueEncampment]); // no string tables: the key
    }

    [Fact]
    public void Open_MissingFolderIsAnError()
    {
        Assert.Throws<DirectoryNotFoundException>(() => GameInstall.Open(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        Assert.Throws<ArgumentException>(() => GameInstall.Open(".", " "));
    }

    [Fact]
    public void Dispose_ClosesTheFilesItOwns_Once()
    {
        var files = new DisposableFiles(SyntheticFiles());
        var game = new GameInstall(files);
        Assert.Equal(AreaId.RogueEncampment, game.Data.Levels[0].Id);
        game.Dispose();
        game.Dispose();
        Assert.Equal(1, files.Disposed);
        Assert.Throws<ObjectDisposedException>(() => game.Data);
    }

    [Fact]
    public void Dispose_LeavesFilesItDoesNotOwn()
    {
        var files = new DisposableFiles(SyntheticFiles());
        new GameInstall(files, ownsFiles: false).Dispose();
        Assert.Equal(0, files.Disposed);
        Assert.Throws<ArgumentNullException>(() => new GameInstall(null!));
    }

    private static MemoryFiles SyntheticFiles() =>
        new MemoryFiles().Add(GameDatabase.ExcelDirectory + "levels.txt", "Id\tLevelName\n1\tTown\n");
}

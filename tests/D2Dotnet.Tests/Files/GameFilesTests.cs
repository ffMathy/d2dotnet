using D2Dotnet.Files;
using D2Dotnet.Tests.Files.Writer;

namespace D2Dotnet.Tests.Files;

public class GameFilesTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void InstallFolder_OpensCasc()
    {
        new CascStorageWriter().Add("data/global/excel/levels.txt", "levels").Write(_dir.Path);
        var provider = GameFiles.Open(_dir.Path);
        try
        {
            Assert.IsType<CascFileProvider>(provider);
            Assert.True(provider.Exists("data/global/excel/levels.txt"));
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void BuildInfoFile_OpensCasc()
    {
        new CascStorageWriter().Add("data/global/excel/levels.txt", "levels").Write(_dir.Path);
        using var provider = Assert.IsType<CascFileProvider>(GameFiles.Open(_dir.Combine(".build.info")));
        Assert.True(provider.Exists("data/global/excel/levels.txt"));
    }

    [Fact]
    public void OtherFolder_OpensDirectory()
    {
        _dir.WriteFile("data/global/excel/levels.txt", "levels");
        var provider = Assert.IsType<DirectoryFileProvider>(GameFiles.Open(_dir.Path));
        Assert.True(provider.Exists("data/global/excel/levels.txt"));
    }

    [Fact]
    public void StaticSteamStorage_IsRejectedClearly()
    {
        _dir.WriteFile("Data/.build.config", "# Build Configuration\n");
        var e = Assert.Throws<NotSupportedException>(() => GameFiles.Open(_dir.Path));
        Assert.Contains("Steam", e.Message);
    }

    [Fact]
    public void MissingFolder_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => GameFiles.Open(_dir.Combine("missing")));
    }
}

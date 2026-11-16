using System.Text;
using D2Dotnet.Files;
using D2Dotnet.Tests.Files.Writer;

namespace D2Dotnet.Tests.Files;

public class DirectoryFileProviderTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static string Text(byte[]? b) => Encoding.UTF8.GetString(b!);

    [Theory]
    [InlineData("data/global/excel/levels.txt")]
    [InlineData("DATA/GLOBAL/EXCEL/LEVELS.TXT")]
    [InlineData(@"data\global\excel\Levels.txt")]
    [InlineData(@"\data\global\excel\levels.txt")]
    public void ReadsCaseAndSeparatorInsensitively(string path)
    {
        _dir.WriteFile("Data/Global/Excel/Levels.txt", "levels");
        var provider = new DirectoryFileProvider(_dir.Path);
        Assert.True(provider.Exists(path));
        Assert.Equal("levels", Text(provider.ReadFile(path)));
    }

    [Fact]
    public void ModLayout_RootContainsData()
    {
        _dir.WriteFile("data/global/excel/levels.txt", "levels");
        _dir.WriteFile("data/global/tiles/act1/town/townn1.ds1", "ds1");
        _dir.WriteFile("data/local/lng/strings/levels.json", "[]");
        _dir.WriteFile("modinfo.json", "{}");
        var provider = new DirectoryFileProvider(_dir.Path);

        Assert.Equal(_dir.Path, provider.BaseDirectory);
        Assert.Equal("", provider.PathPrefix);
        Assert.Equal(
            ["data/global/excel/levels.txt", "data/global/tiles/act1/town/townn1.ds1", "data/local/lng/strings/levels.json", "modinfo.json"],
            provider.EnumerateFiles(""));
        Assert.Equal(["data/global/tiles/act1/town/townn1.ds1"], provider.EnumerateFiles("Data/Global/Tiles"));
        Assert.Empty(provider.EnumerateFiles("data/glob"));
    }

    [Fact]
    public void CascViewExport_DataMountBecomesAFolder()
    {
        // CascView saves "data:data/global/excel/levels.txt" as <out>/data/data/global/excel/levels.txt.
        _dir.WriteFile("data/data/global/excel/levels.txt", "levels");
        var provider = new DirectoryFileProvider(_dir.Path);

        Assert.Equal(_dir.Combine("data"), provider.BaseDirectory);
        Assert.Equal("levels", Text(provider.ReadFile("data/global/excel/levels.txt")));
        Assert.Equal(["data/global/excel/levels.txt"], provider.EnumerateFiles("data"));
    }

    [Fact]
    public void DataFolderItself()
    {
        _dir.WriteFile("data/global/excel/levels.txt", "levels");
        var provider = new DirectoryFileProvider(_dir.Combine("data"));

        Assert.Equal("data/", provider.PathPrefix);
        Assert.Equal("levels", Text(provider.ReadFile("data/global/excel/levels.txt")));
        Assert.Null(provider.ReadFile("global/excel/levels.txt"));
        Assert.Equal(["data/global/excel/levels.txt"], provider.EnumerateFiles("data/global"));
    }

    [Theory]
    [InlineData("data/global/excel/missing.txt")]
    [InlineData("data/global/excel")]
    [InlineData("data/global/excel/")]
    [InlineData("")]
    [InlineData("../outside.txt")]
    [InlineData("data/../../outside.txt")]
    public void MissingFilesAndEscapes(string path)
    {
        _dir.WriteFile("root/data/global/excel/levels.txt", "levels");
        _dir.WriteFile("outside.txt", "secret");
        var provider = new DirectoryFileProvider(_dir.Combine("root"));
        Assert.False(provider.Exists(path));
        Assert.Null(provider.ReadFile(path));
    }

    [Fact]
    public void MissingRoot_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new DirectoryFileProvider(_dir.Combine("nope")));
    }

    [Fact]
    public void ServesTheSameFilesAsCasc()
    {
        // The same paths through both providers.
        var install = _dir.Combine("install");
        var export = _dir.Combine("export");
        Directory.CreateDirectory(install);
        var files = new Dictionary<string, string>
        {
            ["data/global/excel/levels.txt"] = "levels",
            ["data/global/tiles/act1/town/townn1.ds1"] = "ds1",
            ["data/local/lng/strings/levels.json"] = "[]",
        };
        var writer = new CascStorageWriter();
        foreach (var (path, content) in files)
        {
            writer.Add(path, content);
            var full = Path.Combine(export, "data", path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        writer.Write(install);

        using var casc = CascFileProvider.Open(install);
        var folder = new DirectoryFileProvider(export);
        Assert.Equal(casc.EnumerateFiles(""), folder.EnumerateFiles(""));
        foreach (var path in files.Keys)
            Assert.Equal(casc.ReadFile(path), folder.ReadFile(path));
    }
}

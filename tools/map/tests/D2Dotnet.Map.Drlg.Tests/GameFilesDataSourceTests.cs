using D2Dotnet.Map.Drlg.Data;
using D2Dotnet.Map.Drlg.Formats;
using D2Dotnet.Map.GameData;

namespace D2Dotnet.Map.Drlg.Tests;

/// <summary>The production data source over an in-memory game file tree.</summary>
public sealed class GameFilesDataSourceTests
{
    private sealed class MemoryFiles : IGameFileProvider
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public void Add(string path, byte[] bytes) => _files[GamePath.Normalize(path)] = bytes;

        public bool Exists(string path) => _files.ContainsKey(GamePath.Normalize(path));

        public byte[]? ReadFile(string path) => _files.TryGetValue(GamePath.Normalize(path), out var b) ? b : null;

        public IEnumerable<string> EnumerateFiles(string directory)
        {
            var d = GamePath.Normalize(directory).TrimEnd('/') + "/";
            return _files.Keys.Where(k => k.StartsWith(d, StringComparison.Ordinal));
        }
    }

    private static MemoryFiles LibD2Tree()
    {
        var files = new MemoryFiles();
        foreach (var f in Directory.GetFiles(LibD2Fixtures.ExcelDir, "*.txt"))
            files.Add("data/global/excel/" + Path.GetFileName(f), File.ReadAllBytes(f));
        files.Add(@"data\global\tiles\ACT1\Town\TownN1.ds1", File.ReadAllBytes(LibD2Fixtures.PathOf("packages", "formats", "src", "maps", "Act1_Town_TownN1.ds1")));
        files.Add("data/global/tiles/act1/town/floor.dt1", File.ReadAllBytes(LibD2Fixtures.PathOf("packages", "formats", "src", "maps", "Act1_Town_Floor.dt1")));
        return files;
    }

    [Fact]
    public void ReadsTablesByNameAndTilesByRelativePath()
    {
        var src = new GameFilesDataSource(LibD2Tree(), LibD2Fixtures.Source.GetPresetObjectTable());
        foreach (var name in ExcelTables.Required) Assert.NotNull(src.GetTable(name));
        Assert.Null(src.GetTable("NoSuchTable"));

        var ds1 = src.GetDs1("act1/town/townn1.ds1");
        Assert.NotNull(ds1);
        Assert.Equal(BakedBlob.PackDs1(LibD2Fixtures.Source.GetDs1("act1/town/townn1.ds1")!), BakedBlob.PackDs1(ds1!));
        Assert.NotNull(src.GetDt1(@"Act1\Town\Floor.dt1"));
        Assert.Null(src.GetDs1("act1/town/missing.ds1"));
        Assert.Same(LibD2Fixtures.Source.GetPresetObjectTable(), src.GetPresetObjectTable());
    }

    [Fact]
    public void TablesMatchTheBakedSource()
    {
        var src = new GameFilesDataSource(LibD2Tree(), LibD2Fixtures.Source.GetPresetObjectTable());
        foreach (var name in ExcelTables.Required)
        {
            var a = src.GetTable(name)!;
            var b = LibD2Fixtures.Source.GetTable(name)!;
            Assert.Equal(b.RowCount, a.RowCount);
        }
    }

    [Fact]
    public void BuildsThePresetObjectTableFromAnObjPresetTable()
    {
        var files = LibD2Tree();
        files.Add("data/global/excel/objpreset.txt", System.Text.Encoding.ASCII.GetBytes("Index\tAct\tObjectClass\n5\t1\t37\n7\t2\t261\n149\t5\t9\n"));
        var src = new GameFilesDataSource(files);
        var t = src.GetPresetObjectTable();
        Assert.Equal(750, t.Length);
        Assert.Equal(37, t[0 * 150 + 5]);
        Assert.Equal(261, t[1 * 150 + 7]);
        Assert.Equal(9, t[4 * 150 + 149]);
    }

    [Fact]
    public void ThrowsWithoutAnObjectTable()
    {
        var src = new GameFilesDataSource(LibD2Tree());
        Assert.Throws<InvalidDataException>(() => src.GetPresetObjectTable());
    }
}

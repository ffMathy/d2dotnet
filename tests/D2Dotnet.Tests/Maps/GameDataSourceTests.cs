using D2Dotnet.Data;
using D2Dotnet.Files;
using D2Dotnet.Maps;
using D2Dotnet.Maps.Engine;

namespace D2Dotnet.Tests.Maps;

/// <summary>The production data source (a <see cref="GameDatabase"/> and its game files) over an in-memory game file tree.</summary>
public sealed class GameDataSourceTests
{
    private static MemoryFiles LibD2Tree()
    {
        var files = MemoryFiles.FromExcelFolder(LibD2Fixtures.ExcelDir);
        files.Add(@"data\global\tiles\ACT1\Town\TownN1.ds1", File.ReadAllBytes(LibD2Fixtures.PathOf("packages", "formats", "src", "maps", "Act1_Town_TownN1.ds1")));
        files.Add("data/global/tiles/act1/town/floor.dt1", File.ReadAllBytes(LibD2Fixtures.PathOf("packages", "formats", "src", "maps", "Act1_Town_Floor.dt1")));
        return files;
    }

    [Fact]
    public void PresetObjectTable_TakesNumbers_OrD2RClassNamesThroughObjectsTxt()
    {
        // D2R's objpreset.txt names the objects.txt Class; older tables give the row number.
        var objects = ExcelTable.Parse("Class\tName\t*ID\nFountain\tDummy\t12\nTikiTorch1\tDummy\t37\nWaypointPortal\tWaypoint\t119\n", "objects");
        var named = ExcelTable.Parse("Index\tAct\tObjectClass\n0\t1\tFountain\n1\t1\ttikitorch1\n5\t2\tWaypointPortal\n6\t2\tNoSuchClass\n", "objpreset");
        var table = GameDataSource.BuildPresetObjectTable(named, objects)!;
        Assert.Equal(12, table[0]);
        Assert.Equal(37, table[1]);        // the class is matched ignoring case
        Assert.Equal(119, table[150 + 5]); // act 2
        Assert.Equal(0, table[150 + 6]);   // an unknown class places nothing

        var numbered = ExcelTable.Parse("Act\tId\tObject\n1\t0\t12\n1\t1\t37\n", "obj");
        Assert.Equal([12, 37], GameDataSource.BuildPresetObjectTable(numbered)![..2]);
    }

    [Fact]
    public void ReadsTablesByNameAndTilesByRelativePath()
    {
        var src = new GameDataSource(new GameDatabase(LibD2Tree()), LibD2Fixtures.Source.GetPresetObjectTable());
        foreach (var name in ExcelTables.Required) Assert.NotNull(src.GetTable(name));
        Assert.Null(src.GetTable("NoSuchTable"));

        var ds1 = src.GetDs1("act1/town/townn1.ds1");
        Assert.NotNull(ds1);
        Assert.Equal(BakedBlob.PackDs1(LibD2Fixtures.Source.GetDs1("act1/town/townn1.ds1")!), BakedBlob.PackDs1(ds1!));
        Assert.NotNull(src.GetDt1(@"Act1\Town\Floor.dt1"));
        Assert.Null(src.GetDs1("act1/town/missing.ds1"));
        Assert.Equal(LibD2Fixtures.Source.GetPresetObjectTable(), src.GetPresetObjectTable());
    }

    [Fact]
    public void TablesMatchTheBakedSource()
    {
        var src = new GameDataSource(new GameDatabase(LibD2Tree()), LibD2Fixtures.Source.GetPresetObjectTable());
        foreach (var name in ExcelTables.Required)
        {
            var a = src.GetTable(name)!;
            var b = LibD2Fixtures.Source.GetTable(name)!;
            Assert.Equal(b.RowCount, a.RowCount);
            Assert.Equal(b.Columns, a.Columns);
        }
    }

    [Fact]
    public void BuildsThePresetObjectTableFromAnObjPresetTable()
    {
        var files = LibD2Tree();
        files.Add("data/global/excel/objpreset.txt", "Index\tAct\tObjectClass\n5\t1\t37\n7\t2\t261\n149\t5\t9\n");
        var src = new GameDataSource(new GameDatabase(files));
        var t = src.GetPresetObjectTable();
        Assert.Equal(750, t.Length);
        Assert.Equal(37, t[0 * 150 + 5]);
        Assert.Equal(261, t[1 * 150 + 7]);
        Assert.Equal(9, t[4 * 150 + 149]);
    }

    [Fact]
    public void ThrowsWithoutAnObjectTable()
    {
        var src = new GameDataSource(new GameDatabase(LibD2Tree()));
        Assert.Throws<InvalidDataException>(() => src.GetPresetObjectTable());
        Assert.Throws<InvalidDataException>(() => new MapGenerator(new GameDatabase(LibD2Tree())));
    }

    [Fact]
    public void RejectsAShortPresetObjectTable() =>
        Assert.Throws<ArgumentException>(() => new MapGenerator(new GameDatabase(LibD2Tree()), new int[749]));

    [Fact]
    public void AMissingLevelTableIsAMissingFile()
    {
        var files = LibD2Tree().Remove("data/global/excel/lvlwarp.txt");
        var err = Assert.Throws<FileNotFoundException>(() => new MapGenerator(new GameDatabase(files), LibD2Fixtures.Source.GetPresetObjectTable()));
        Assert.Contains("lvlwarp.txt", err.Message);
    }

    [Fact]
    public void GameInstallOpensItsGeneratorOnce()
    {
        var files = LibD2Tree();
        files.Add("data/global/excel/objpreset.txt", "Index\tAct\tObjectClass\n5\t1\t37\n");
        using var game = new GameInstall(files);
        Assert.Same(game.Maps, game.Maps);
        Assert.Equal(game.Maps.GetAreas(ActId.Act1), game.Data.Levels.Where(l => l.Act == ActId.Act1 && l.Id != AreaId.None).Select(l => l.Id));
        game.Dispose();
        Assert.Throws<ObjectDisposedException>(() => game.Maps);
    }
}

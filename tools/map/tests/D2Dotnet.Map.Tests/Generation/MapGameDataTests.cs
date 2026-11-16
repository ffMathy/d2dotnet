using D2Dotnet.Map.Drlg.Data;
using D2Dotnet.Map.Generation;

namespace D2Dotnet.Map.Tests.Generation;

public class MapGameDataTests
{
    [Fact]
    public void LevelNamesAreTranslatedTrimmedOrTheRawKey()
    {
        var data = Synthetic.GameData();
        Assert.Equal("Rogue Encampment", data.LevelName(1));
        Assert.Equal("Blood Moor", data.LevelName(2)); // no string: the LevelName key itself
        Assert.Equal("Tal Rasha's Tömb <&+>", data.LevelName(40));
        Assert.Null(data.LevelName(0));
        Assert.Null(data.LevelName(66));
    }

    [Fact]
    public void LanguageSelectsTheStringField()
    {
        var data = Synthetic.GameData("deDE");
        Assert.Equal("Lager der Jägerinnen", data.LevelName(1));
        // blank deDE text falls back to enUS
        Assert.Equal("Tal Rasha's Tömb <&+>", data.LevelName(40));
        Assert.Equal("Gefallener", data.Npc(1).Name);
        Assert.Equal("Kaltkrähe", data.Npc(5).Name);
    }

    [Fact]
    public void NpcIdsCountGameRowsThenSuperUniques()
    {
        var data = Synthetic.GameData();
        Assert.Equal(3, data.MonsterCount); // the Expansion divider is not a game row
        Assert.Equal(3, data.SuperUniqueCount);
        Assert.Equal(new NpcInfo("Zombie", null), data.Npc(0));
        Assert.Equal(new NpcInfo(null, null), data.Npc(2));
        Assert.Equal(new NpcInfo("Bishibosh", 0), data.Npc(3));
        Assert.Equal(new NpcInfo("Coldcrow", 2), data.Npc(5));
        Assert.Equal(default, data.Npc(6));
        Assert.Equal(default, data.Npc(-1));
    }

    [Fact]
    public void ObjectsAreKeyedByTheirIdColumn()
    {
        var data = Synthetic.GameData();
        Assert.Equal(new GameObjectInfo("Waypoint", 23, true), data.Object(119));
        Assert.Equal(new GameObjectInfo("LargeChestR", 4, false), data.Object(5));
        Assert.Null(data.Object(0));
    }

    [Fact]
    public void ObjectsWithoutAnIdColumnAreKeyedByRow()
    {
        var data = new MapGameData(
            Synthetic.Table(["Id", "LevelName"]),
            Synthetic.Table(["Id", "NameStr"]),
            Synthetic.Table(["Superunique", "Name"]),
            Synthetic.Table(["Name", "OperateFn", "Selectable0"], ["first", "1", "1"], ["", "0", "0"], ["third", "2", "0"]),
            GameStrings.Empty);
        Assert.Equal(new GameObjectInfo("first", 1, true), data.Object(0));
        Assert.Null(data.Object(1));
        Assert.Equal(new GameObjectInfo("third", 2, false), data.Object(2));
    }

    [Fact]
    public void LoadNeedsTheExcelTables()
    {
        var root = Directory.CreateTempSubdirectory("d2map-data-");
        try
        {
            var excel = Directory.CreateDirectory(Path.Join(root.FullName, "data", "global", "excel"));
            File.WriteAllText(Path.Join(excel.FullName, "levels.txt"), "Id\tLevelName\r\n1\tTown\r\n");
            var files = new D2Dotnet.Map.GameData.DirectoryFileProvider(root.FullName);
            var err = Assert.Throws<InvalidDataException>(() => MapGameData.Load(new GameFilesDataSource(files), files, "enUS"));
            Assert.Contains("monstats.txt", err.Message);

            File.WriteAllText(Path.Join(excel.FullName, "MonStats.txt"), "Id\tNameStr\r\nzombie\tZombie\r\n");
            File.WriteAllText(Path.Join(excel.FullName, "superuniques.txt"), "Superunique\tName\r\n");
            File.WriteAllText(Path.Join(excel.FullName, "objects.txt"), "Name\tId\r\nWaypoint\t119\r\n");
            files = new D2Dotnet.Map.GameData.DirectoryFileProvider(root.FullName);
            var data = MapGameData.Load(new GameFilesDataSource(files), files, "enUS");
            Assert.Equal("Town", data.LevelName(1));
            Assert.Equal("Zombie", data.Npc(0).Name);
            Assert.Equal("Waypoint", data.Object(119)!.Name);
        }
        finally
        {
            root.Delete(true);
        }
    }
}

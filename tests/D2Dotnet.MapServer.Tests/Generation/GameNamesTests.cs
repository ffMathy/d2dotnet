using D2Dotnet.Data;
using D2Dotnet.MapServer.Generation;
using D2Dotnet.Tests;

namespace D2Dotnet.MapServer.Tests.Generation;

/// <summary>The names the server labels levels, npcs and objects with (<see cref="GameNames"/> over a <see cref="GameDatabase"/>).</summary>
public class GameNamesTests
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
        Assert.Equal(3, data.Monsters.Count); // the Expansion divider is not a game row
        Assert.Equal(3, data.SuperUniques.Count);
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
        var data = Synthetic.Database(
            Synthetic.Table(["Id", "LevelName"]),
            Synthetic.Table(["Id", "NameStr"]),
            Synthetic.Table(["Superunique", "Name"]),
            Synthetic.Table(["OperateFn", "Name", "Selectable0"], ["1", "first", "1"], ["0", "", "0"], ["2", "third", "0"]),
            []);
        Assert.Equal(new GameObjectInfo("first", 1, true), data.Object(0));
        Assert.Null(data.Object(1));
        Assert.Equal(new GameObjectInfo("third", 2, false), data.Object(2));
    }

    [Fact]
    public void TheTablesAreRequired()
    {
        var files = new MemoryFiles().Add("data/global/excel/levels.txt", "Id\tLevelName\r\n1\tTown\r\n");
        var err = Assert.Throws<FileNotFoundException>(() => new GameDatabase(files).Npc(0));
        Assert.Contains("monstats.txt", err.Message);

        files.Add("data/global/excel/MonStats.txt", "Id\tNameStr\r\nzombie\tZombie\r\n");
        files.Add("data/global/excel/superuniques.txt", "Superunique\tName\r\n");
        files.Add("data/global/excel/objects.txt", "Name\tId\r\nWaypoint\t119\r\n");
        var data = new GameDatabase(files);
        Assert.Equal("Town", data.LevelName(1));
        Assert.Equal("Zombie", data.Npc(0).Name);
        Assert.Equal("Waypoint", data.Object(119)!.Name);
    }
}

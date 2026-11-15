using D2Dotnet.Map.Data;
using D2Dotnet.Map.Process;
using Microsoft.Extensions.Logging.Abstractions;

namespace D2Dotnet.Map.Tests.Process;

public class MapGenMessageTests
{
    [Theory]
    [InlineData("{\"type\":\"init\"}", "init")]
    [InlineData("{\"type\":\"info\",\"seed\":1,\"difficulty\":0}", "info")]
    [InlineData("{\"type\":\"done\"}", "done")]
    [InlineData("{\"level\":30,\"time\":1,\"msg\":\"x\",\"type\":\"map\"}", "log")]
    [InlineData("{\"time\":1}", "log")]
    [InlineData("{\"type\":1}", "1")]
    [InlineData("{\"type\":\"\"}", null)]
    [InlineData("{\"type\":0}", null)]
    [InlineData("{\"type\":null}", null)]
    [InlineData("{\"other\":true}", null)]
    [InlineData("[1,2]", null)]
    [InlineData("null", null)]
    [InlineData("12", null)]
    [InlineData("", null)]
    [InlineData("wine: Read access denied", null)]
    [InlineData("{\"type\":\"map\"", null)]
    public void ClassifiesGeneratorLines(string line, string? expected) => Assert.Equal(expected, MapGenMessage.Classify(line, out _));

    [Fact]
    public void SerializesLikeJsonStringify()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { name = "Tal Rasha's Tomb", other = "<a&b+c> Ćorrupted" }, MapGenMessage.JsonOptions);
        Assert.Equal("{\"name\":\"Tal Rasha's Tomb\",\"other\":\"<a&b+c> Ćorrupted\"}", json);
    }

    [Fact]
    public void ParsesMapMessages()
    {
        const string line = "{\"type\":\"map\",\"id\":74,\"name\":\"Arcane Sanctuary\",\"offset\":{\"x\":25000,\"y\":5000},\"size\":{\"width\":1000,\"height\":1000},"
            + "\"objects\":[{\"id\":53,\"type\":\"exit\",\"x\":137,\"y\":0,\"isGoodExit\":true},{\"id\":305,\"type\":\"object\",\"x\":237,\"y\":401,\"name\":\"teleportation pad\",\"op\":27,\"class\":\"quest\",\"extra\":[1]}],"
            + "\"map\":[[1,149],[]],\"future\":\"kept\"}";
        Assert.Equal("map", MapGenMessage.Classify(line, out var json));
        var level = MapGenMessage.ParseLevel(json);
        Assert.Equal("map", level.Type);
        Assert.Equal(74, level.Id);
        Assert.Equal("Arcane Sanctuary", level.Name);
        Assert.Equal(new LevelOffset(25000, 5000), level.Offset);
        Assert.Equal(new LevelSize(1000, 1000), level.Size);
        Assert.Equal(2, level.Objects.Count);
        Assert.True(level.Objects[0].IsGoodExit);
        Assert.Equal(27, level.Objects[1].Op);
        Assert.Equal("quest", level.Objects[1].Class);
        Assert.Equal("[1]", level.Objects[1].Extra!["extra"].GetRawText());
        Assert.Equal([1, 149], level.Map[0]);
        Assert.Empty(level.Map[1]);
        Assert.Equal("kept", level.Extra!["future"].GetString());
    }

    [Fact]
    public void FixMapCorrectsNames()
    {
        using var game = new FakeGame();
        var proc = new Diablo2MapProcess(FakeGame.MpqData(), "d2-map.exe", game.Options(), new LruCache<IReadOnlyList<Diablo2Level>>(10), NullLogger.Instance);
        var level = new Diablo2Level
        {
            Id = 1,
            Name = "from client",
            Objects =
            [
                new() { Id = 0, Type = "npc" },
                new() { Id = 2, Type = "npc", Name = "had a name" },
                new() { Id = 5, Type = "npc" },
                new() { Id = 999, Type = "npc" },
                new() { Id = 119, Type = "object", Name = "waypoint" },
                new() { Id = 580, Type = "object", Name = "client name" },
                new() { Id = 2, Type = "exit" },
                new() { Id = 77, Type = "exit" },
            ],
        };

        proc.FixMap(level);

        Assert.Equal("Rogue Encampment", level.Name);
        Assert.Equal("Zombie", level.Objects[0].Name);
        Assert.Null(level.Objects[1].Name);
        Assert.Equal("Coldcrow", level.Objects[2].Name);
        Assert.True(level.Objects[2].IsSuperUnique);
        Assert.Equal(2, level.Objects[2].SuperId);
        Assert.Null(level.Objects[3].Name);
        Assert.Null(level.Objects[3].IsSuperUnique);
        Assert.Equal("Waypoint", level.Objects[4].Name);
        Assert.Null(level.Objects[5].Name);
        Assert.Equal("Blood Moor", level.Objects[6].Name);
        Assert.Null(level.Objects[7].Name);

        var unknown = new Diablo2Level { Id = 150, Name = "kept" };
        Assert.Equal("kept", proc.FixMap(unknown).Name);
    }
}

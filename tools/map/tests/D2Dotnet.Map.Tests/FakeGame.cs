using D2Dotnet.Map.Data;
using D2Dotnet.Map.Process;
using Microsoft.Extensions.Logging;

namespace D2Dotnet.Map.Tests;

/// <summary>
/// A temporary "game folder" plus options that run <c>Fixtures/fake-d2-map.sh</c> under <c>/bin/sh</c>
/// instead of <c>wine d2-map.exe</c>.
/// </summary>
public sealed class FakeGame : IDisposable
{
    public FakeGame(params string[] markers)
    {
        GamePath = Directory.CreateTempSubdirectory("d2map-test-").FullName;
        foreach (var m in markers) File.WriteAllText(System.IO.Path.Join(GamePath, m), m);
    }

    public string GamePath { get; }

    public int Starts
    {
        get
        {
            var path = System.IO.Path.Join(GamePath, "starts");
            return File.Exists(path) ? File.ReadAllLines(path).Length : 0;
        }
    }

    public MapProcessOptions Options(TimeSpan? timeout = null) => new()
    {
        WineCommand = "/bin/sh",
        MapCommand = [Fixture.Path("fake-d2-map.sh")],
        Diablo2Path = GamePath,
        RegistryPath = System.IO.Path.Join(GamePath, "missing.reg"),
        ProcessTimeout = timeout ?? TimeSpan.FromSeconds(10),
    };

    public void Dispose()
    {
        try
        {
            Directory.Delete(GamePath, true);
        }
        catch (IOException)
        {
            // still in use by a generator that is shutting down
        }
    }

    /// <summary>Game data matching the levels/objects/npcs the fake generator emits.</summary>
    public static Diablo2MpqData MpqData()
    {
        var data = new Diablo2MpqData();
        data.Levels[1] = new Diablo2MpqLevel(1, Act.ActI, "Rogue Encampment  ", false, false);
        data.Levels[2] = new Diablo2MpqLevel(2, Act.ActI, "Blood Moor", true, true);
        data.Levels[40] = new Diablo2MpqLevel(40, Act.ActII, "Lut Gholein", false, false);
        data.Objects[119] = new Diablo2MpqObject(" Waypoint ");
        data.Lang.Classic.Add("Zombie", 10, "Zombie ");
        data.Lang.Classic.Add("Fallen", 11, "Fallen");
        data.Monsters.Add(0, new Diablo2MpqMonster(0, 0, 10));
        data.Monsters.Add(1, new Diablo2MpqMonster(1, 1, 11));
        data.Monsters.Add(2, new Diablo2MpqMonster(2, 2, 12));
        return data;
    }
}

public sealed class FakeMpqDataSource : IMpqDataSource
{
    public int Loads { get; private set; }
    public string? LastPath { get; private set; }

    public Task<Diablo2MpqData> LoadAsync(string basePath, ILogger log)
    {
        Loads++;
        LastPath = basePath;
        return Task.FromResult(FakeGame.MpqData());
    }
}

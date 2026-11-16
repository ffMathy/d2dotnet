using System.Text;
using D2Dotnet.Map.Drlg;
using D2Dotnet.Map.Drlg.Data;
using D2Dotnet.Map.Generation;

namespace D2Dotnet.Map.Tests;

/// <summary>Small synthetic game data and acts (no game files), shared by the server and generation tests.</summary>
public static class Synthetic
{
    /// <summary>A tab-separated excel table: a header row and data rows.</summary>
    public static TxtTable Table(params string[][] rows) =>
        TxtTable.Parse(string.Join("\r\n", rows.Select(r => string.Join('\t', r))) + "\r\n");

    public static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>
    /// Levels 1, 2, 8 and 40; monsters zombie (0), fallen (1) and an unnamed one (2) around an Expansion divider row;
    /// super uniques Bishibosh (3), Bonebreaker (4), Coldcrow (5); a waypoint (119), a barrel (7), a shrine (2) and an
    /// unselectable chest (5).
    /// </summary>
    public static MapGameData GameData(string language = "enUS") => new(
        Table(
            ["Name", "Id", "LevelName"],
            ["Act 1 - Town", "1", "strRogue"],
            ["Act 1 - Wilderness 1", "2", "Blood Moor"],
            ["Act 1 - Cave 1", "8", "Den of Evil"],
            ["Expansion", "", ""],
            ["Act 2 - Town", "40", "Lut Gholein"]),
        Table(
            ["Id", "hcIdx", "NameStr"],
            ["zombie", "0", "Zombie"],
            ["fallen", "1", "Fallen1"],
            ["Expansion", "", ""],
            ["unnamed", "2", ""]),
        Table(
            ["Superunique", "Name"],
            ["Bishibosh", "Bishibosh"],
            ["Bonebreaker", "Bonebreaker"],
            ["Expansion", ""],
            ["Coldcrow", "Coldcrow"]),
        Table(
            ["Name", "*Description", "Id", "Selectable0", "OperateFn"],
            ["Shrine", "", "2", "1", "2"],
            ["LargeChestR", "", "5", "0", "4"],
            ["barrel", "", "7", "1", "5"],
            [" Waypoint ", "", "119", "1", "23"]),
        GameStrings.Parse(
        [
            ("data/local/lng/strings/levels.json", Json("""
                [
                    { "id": 1, "Key": "strRogue", "enUS": "Rogue Encampment  ", "deDE": "Lager der Jägerinnen" },
                    { "id": 2, "Key": "Lut Gholein", "enUS": "Tal Rasha's Tömb <&+>", "deDE": "" }
                ]
                """)),
            ("data/local/lng/strings/monsters.json", Json("""
                [
                    { "id": 3, "Key": "Zombie", "enUS": "Zombie ", "deDE": "Zombie" },
                    { "id": 4, "Key": "Fallen1", "enUS": "Fallen", "deDE": "Gefallener" },
                    { "id": 5, "Key": "Coldcrow", "enUS": "Coldcrow", "deDE": "Kaltkrähe" }
                ]
                """)),
        ], language));

    /// <summary>A collision grid from rows of '.' (walkable, 0), '#' (blocked, 1) and ' ' (uncovered).</summary>
    public static CollisionGrid Grid(params string[] rows)
    {
        var width = rows.Max(r => r.Length);
        var cells = new ushort[width * rows.Length];
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < width; x++)
                cells[y * width + x] = (x < rows[y].Length ? rows[y][x] : ' ') switch
                {
                    '.' => 0,
                    '#' => 1,
                    _ => CollisionGrid.Uncovered,
                };
        return new CollisionGrid(width, rows.Length, cells);
    }

    private static readonly Room OneRoom = new(0, 0, 1, 1, 1, 2, 0);

    /// <summary>
    /// Act I: Rogue Encampment (1, origin 20,40, 8x6 tiles) with presets, a shrine, a warp to Blood Moor and a seam to Blood
    /// Moor; Blood Moor (2, 2x2 tiles, all walkable) with a warp to the Den of Evil; level 3 without rooms.
    /// Act II: Lut Gholein (40) with a warp to the staff tomb (66). Other acts are empty.
    /// </summary>
    public static Act Act(uint seed, Difficulty difficulty, int act)
    {
        var levels = new List<Level>();
        if (act == 0)
        {
            levels.Add(new Level
            {
                Id = 1,
                Name = "strRogue",
                OriginX = 20,
                OriginY = 40,
                Width = 8,
                Height = 6,
                Rooms = [OneRoom],
                Presets =
                [
                    new PresetUnit(PresetUnitType.Object, 119, 20, 20),
                    new PresetUnit(PresetUnitType.Npc, 0, 3, 3),
                    new PresetUnit(PresetUnitType.Npc, 149, 9, 9), // a chicken: never exported
                    new PresetUnit(PresetUnitType.Npc, 5, 4, 4),
                    new PresetUnit(PresetUnitType.Npc, 999, 5, 5),
                    new PresetUnit(PresetUnitType.Object, 7, 8, 8), // a barrel: never exported
                    new PresetUnit(PresetUnitType.Object, 580, 6, 6),
                ],
                Shrines = [new Shrine(2, 30, 25)],
                Exits =
                [
                    new LevelExit(2, 35, 10, ExitKind.Warp),
                    new LevelExit(2, 39, 15, ExitKind.Seam),
                ],
                Collision = Grid(
                    "#.....#.................................",
                    "##...##.................................",
                    "########################################",
                    "#.....#................................."),
            });
            levels.Add(new Level
            {
                Id = 2,
                Name = "Blood Moor",
                OriginX = 28,
                OriginY = 40,
                Width = 2,
                Height = 2,
                Rooms = [OneRoom],
                Exits = [new LevelExit(8, 5, 5, ExitKind.Warp)],
                Collision = Grid(Enumerable.Repeat("..........", 10).ToArray()),
            });
            levels.Add(new Level { Id = 3, Name = "Cold Plains" });
        }
        else if (act == 1)
        {
            levels.Add(new Level
            {
                Id = 40,
                Name = "Lut Gholein",
                OriginX = 1000,
                OriginY = 1000,
                Width = 4,
                Height = 4,
                Rooms = [OneRoom],
                Exits = [new LevelExit(66, 0, 0, ExitKind.Warp)],
                Collision = Grid(Enumerable.Repeat("....................", 20).ToArray()),
            });
        }
        return new Act { Number = act, Seed = seed, Difficulty = difficulty, StaffTombLevelId = act == 1 ? 66 : 0, Levels = levels };
    }
}

/// <summary>An <see cref="IMapGenerator"/> over <see cref="Synthetic"/> data that records how it is called.</summary>
public sealed class FakeMapGenerator : IMapGenerator
{
    private readonly Lazy<MapGameData> _data = new(() => Synthetic.GameData());
    private int _running;
    private int _maxRunning;
    private int _calls;

    /// <summary>How long each <see cref="GenerateAct"/> takes.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>Thrown by <see cref="GenerateAct"/> when set.</summary>
    public Exception? Failure { get; set; }

    public int Calls => _calls;
    public int MaxRunning => _maxRunning;
    public List<(uint Seed, Difficulty Difficulty, int Act)> Requests { get; } = [];

    public MapGameData GameData => _data.Value;

    public Act GenerateAct(uint seed, Difficulty difficulty, int act)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests) Requests.Add((seed, difficulty, act));
        var running = Interlocked.Increment(ref _running);
        try
        {
            int seen;
            while ((seen = _maxRunning) < running && Interlocked.CompareExchange(ref _maxRunning, running, seen) != seen)
            {
            }
            if (Delay > TimeSpan.Zero) Thread.Sleep(Delay);
            if (Failure != null) throw Failure;
            return Synthetic.Act(seed, difficulty, act);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }
}

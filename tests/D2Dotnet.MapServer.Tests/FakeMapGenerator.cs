using System.Text;
using D2Dotnet.Data;
using D2Dotnet.Maps;
using D2Dotnet.MapServer.Generation;
using D2Dotnet.Tests;

namespace D2Dotnet.MapServer.Tests;

/// <summary>Small synthetic game data and acts (no game files), shared by the server and generation tests.</summary>
public static class Synthetic
{
    /// <summary>A tab-separated excel table: a header row and data rows.</summary>
    public static string Table(params string[][] rows) =>
        string.Join("\r\n", rows.Select(r => string.Join('\t', r))) + "\r\n";

    public static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>A database over in-memory game files: the four excel tables names come from, and string tables.</summary>
    internal static GameDatabase Database(string levels, string monStats, string superUniques, string objects,
        IEnumerable<(string Path, byte[] Json)> strings, string language = "enUS")
    {
        var files = new MemoryFiles()
            .Add("data/global/excel/levels.txt", levels)
            .Add("data/global/excel/monstats.txt", monStats)
            .Add("data/global/excel/superuniques.txt", superUniques)
            .Add("data/global/excel/objects.txt", objects);
        foreach (var (path, json) in strings) files.Add(path, json);
        return new GameDatabase(files, language);
    }

    /// <summary>
    /// Levels 1, 2, 8 and 40 (and 77, a row without a <c>Name</c> that the typed table leaves out); monsters zombie (0),
    /// fallen (1) and an unnamed one (2) around an Expansion divider row; super uniques Bishibosh (3), Bonebreaker (4),
    /// Coldcrow (5); a waypoint (119), a barrel (7), a shrine (2) and an unselectable chest (5).
    /// </summary>
    public static GameDatabase GameData(string language = "enUS") => Database(
        Table(
            ["Name", "Id", "LevelName"],
            ["Act 1 - Town", "1", "strRogue"],
            ["Act 1 - Wilderness 1", "2", "Blood Moor"],
            ["Act 1 - Cave 1", "8", "Den of Evil"],
            ["Expansion", "", ""],
            ["Act 2 - Town", "40", "Lut Gholein"],
            ["", "77", "raw name "]),
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
        ],
        language);

    /// <summary>A collision map from rows of '.' (walkable, 0), '#' (blocked, 1) and ' ' (uncovered).</summary>
    public static CollisionMap Grid(params string[] rows)
    {
        var width = rows.Max(r => r.Length);
        var cells = new ushort[width * rows.Length];
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < width; x++)
                cells[y * width + x] = (x < rows[y].Length ? rows[y][x] : ' ') switch
                {
                    '.' => 0,
                    '#' => 1,
                    _ => CollisionMap.Uncovered,
                };
        return new CollisionMap(width, rows.Length, cells);
    }

    private static readonly MapRoom OneRoom = new(new Rect(0, 0, 1, 1), 1, 2, 0);

    private static PresetUnit Preset(PresetKind kind, int classId, int x, int y) => new(kind, classId, new Point(x, y));

    private static LevelExit Exit(int destination, int x, int y, ExitKind kind) => new((AreaId)destination, new Point(x, y), kind);

    /// <summary>
    /// Act I: Rogue Encampment (1, origin 20,40, 8x6 tiles) with presets, a shrine, a warp to Blood Moor and a seam to Blood
    /// Moor; Blood Moor (2, 2x2 tiles, all walkable) with a warp to the Den of Evil; level 3 without rooms.
    /// Act II: Lut Gholein (40) with a warp to the staff tomb (66). Other acts are empty.
    /// </summary>
    public static GeneratedAct Act(uint seed, Difficulty difficulty, ActId act)
    {
        var levels = new List<MapLevel>();
        if (act == ActId.Act1)
        {
            levels.Add(new MapLevel
            {
                Id = AreaId.RogueEncampment,
                Bounds = new Rect(20, 40, 8, 6),
                Rooms = [OneRoom],
                Presets =
                [
                    Preset(PresetKind.Object, 119, 20, 20),
                    Preset(PresetKind.Npc, 0, 3, 3),
                    Preset(PresetKind.Npc, 149, 9, 9), // a chicken: never exported
                    Preset(PresetKind.Npc, 5, 4, 4),
                    Preset(PresetKind.Npc, 999, 5, 5),
                    Preset(PresetKind.Object, 7, 8, 8), // a barrel: never exported
                    Preset(PresetKind.Object, 580, 6, 6),
                ],
                Shrines = [new Shrine((ObjectId)2, new Point(30, 25))],
                Exits =
                [
                    Exit(2, 35, 10, ExitKind.Warp),
                    Exit(2, 39, 15, ExitKind.Seam),
                ],
                Collision = Grid(
                    "#.....#.................................",
                    "##...##.................................",
                    "########################################",
                    "#.....#................................."),
            });
            levels.Add(new MapLevel
            {
                Id = AreaId.BloodMoor,
                Bounds = new Rect(28, 40, 2, 2),
                Rooms = [OneRoom],
                Exits = [Exit(8, 5, 5, ExitKind.Warp)],
                Collision = Grid(Enumerable.Repeat("..........", 10).ToArray()),
            });
            levels.Add(new MapLevel { Id = AreaId.ColdPlains });
        }
        else if (act == ActId.Act2)
        {
            levels.Add(new MapLevel
            {
                Id = AreaId.LutGholein,
                Bounds = new Rect(1000, 1000, 4, 4),
                Rooms = [OneRoom],
                Exits = [Exit(66, 0, 0, ExitKind.Warp)],
                Collision = Grid(Enumerable.Repeat("....................", 20).ToArray()),
            });
        }
        return new GeneratedAct
        {
            Act = act,
            Seed = seed,
            Difficulty = difficulty,
            StaffTomb = act == ActId.Act2 ? AreaId.TalRashasTomb1 : null,
            Levels = levels,
        };
    }
}

/// <summary>An <see cref="IMapGenerator"/> over <see cref="Synthetic"/> data that records how it is called.</summary>
public sealed class FakeMapGenerator : IMapGenerator
{
    private readonly Lazy<GameDatabase> _data = new(() => Synthetic.GameData());
    private int _running;
    private int _maxRunning;
    private int _calls;

    /// <summary>How long each <see cref="Generate"/> takes.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>Thrown by <see cref="Generate"/> when set.</summary>
    public Exception? Failure { get; set; }

    public int Calls => _calls;
    public int MaxRunning => _maxRunning;
    public List<(uint Seed, Difficulty Difficulty, int Act)> Requests { get; } = [];

    public GameDatabase Data => _data.Value;

    public GeneratedAct Generate(uint seed, Difficulty difficulty, ActId act)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests) Requests.Add((seed, difficulty, (int)act));
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

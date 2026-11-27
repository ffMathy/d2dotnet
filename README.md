# d2dotnet

D2Dotnet is one .NET 10 library for Diablo II: Resurrected (D2R). It has no third-party dependencies, and it can:

- read the game's files, from a Battle.net install (CASC) or from a folder of extracted files;
- read the excel tables into typed records, and read D2R's string tables;
- generate the maps of an act from a seed, as the game's level generator (DRLG) does, in managed code;
- read a snapshot of a running game (player, monsters, items, objects, party, quests, key bindings and more) from
  its process memory.

Game data always comes from the user's own install. Nothing derived from Blizzard's files is in this repository, and
the repository ships no apps: programs reference the library.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned in `global.json`).

```bash
dotnet build D2Dotnet.slnx
dotnet test D2Dotnet.slnx
dotnet pack src/D2Dotnet -c Release      # D2Dotnet.nupkg, with the third-party licenses at its root
```

To use it, reference the project (`src/D2Dotnet/D2Dotnet.csproj`) or the package `dotnet pack` builds. CI
(`.github/workflows/build.yml`) builds and tests every push and pull request.

## Usage

`GameInstall` is the entry point. It opens the game's files and creates the other parts the first time they are
used.

```csharp
using D2Dotnet;
using D2Dotnet.Files;

using var game = GameInstall.Open(@"C:\Program Files (x86)\Diablo II Resurrected");
```

### `D2Dotnet.Files`: game files

```csharp
IGameFileProvider files = game.Files;                    // or GameFiles.Open(path): CASC or a folder
byte[]? levels = files.ReadFile("data/global/excel/levels.txt");   // null when missing
foreach (string ds1 in files.EnumerateFiles("data/global/tiles/act1/town"))
    Console.WriteLine(ds1);
```

Paths use the classic layout (`data/global/...`), ignoring case and accepting `/` or `\`. See
[src/D2Dotnet/Files/README.md](src/D2Dotnet/Files/README.md) for the CASC reader, the folder layouts it accepts and
its limits.

### `D2Dotnet.Data`: excel and string tables

```csharp
using D2Dotnet.Data;

GameDatabase data = game.Data;
LevelInfo? tomb = data.Levels.Find(AreaId.TalRashasTomb1);
SkillInfo? teleport = data.Skills.Find(SkillId.Teleport);
string? name = data.Localization[AreaId.BloodMoor];      // or GetLevelName, GetMonsterName, GetItemName...

ExcelTable runes = data.GetRequiredTable("runes");        // any table, raw
foreach (ExcelRow row in runes.Rows)
    Console.WriteLine(row.GetString("Name"));
```

Typed tables are loaded the first time they are used. A missing file is a `FileNotFoundException`; a missing column or
a malformed number is an `InvalidDataException` that names the table and the column (and the row).

### `D2Dotnet.Maps`: map generation

```csharp
using D2Dotnet.Maps;

GeneratedAct act = game.Maps.Generate(seed: 0x3607656c, Difficulty.Hell, ActId.Act2);
MapLevel canyon = act[AreaId.CanyonOfTheMagi];
foreach (LevelExit exit in canyon.Exits)
    Console.WriteLine($"{exit.Destination} at {exit.Position}{(exit.Destination == act.StaffTomb ? " (true tomb)" : "")}");
bool walkable = canyon.Collision!.IsWalkable(new Point(120, 80));
```

Each level has its rooms, collision map, preset units, waypoints, exits and (in Act I) shrines. See
[docs/maps.md](docs/maps.md).

### `D2Dotnet.Game` and `D2Dotnet.Memory`: a running game

```csharp
using D2Dotnet.Game;
using D2Dotnet.Memory;

using var process = GameProcess.Attach();                 // Windows, a running d2r.exe
var reader = new GameReader(process, game.Data);
GameSnapshot snapshot = reader.ReadSnapshot();
if (snapshot.Player is { } player)
{
    Console.WriteLine($"{player.Name} ({player.Class}, level {player.Level}) in {player.Area}, life {player.LifePercent}%");
    Item? potion = snapshot.Inventory.Belt.FindPotion(PotionType.Healing);
    int fireImmune = snapshot.Enemies.Count(m => m.IsImmune(Element.Fire));
}

// The map of the game being played: its seed, difficulty and the player's area, read from memory.
if (snapshot.Map is { } context)
{
    GeneratedAct current = game.Maps.Generate(context);
    MapLevel here = current[context.Area];
}
```

Attaching needs Windows and the D2R build the memory layout targets. Everything else, including `GameReader`, runs on
any OS over an `IProcessMemory`. See [docs/memory.md](docs/memory.md).

## Layout

```
src/D2Dotnet/                the library (no third-party dependencies)
    *.cs                     shared vocabulary: AreaId, Difficulty, ActId, Point, Rect, and the id enums MonsterId,
                             ObjectId, ItemId, ItemTypeCode, SkillId, StatId, StateId, QuestId
    Files/                   IGameFileProvider, the CASC (Files/Casc/) and folder providers, GameFiles, GameInstall
    Data/                    ExcelTable (the excel reader), GameDatabase and its typed tables, Localization
    Maps/                    MapGenerator and its model (public); Maps/Engine/ (the DRLG, internal);
                             Maps/Formats/ (DS1, DT1)
    Game/                    the snapshot model of a running game
    Memory/                  IProcessMemory, WindowsProcessMemory, GameProcess, GameReader, MapSeed
tests/D2Dotnet.Tests/        xUnit tests of the library (files, data, maps, game, memory), oracles included
tests/Fixtures.targets       downloads the pinned libd2 and d2go sources into .cache/ for the oracle tests
docs/                        design.md, maps.md, memory.md
```

Shared build settings are in the repository root: `Directory.Build.props` (net10.0, nullable reference types,
implicit usings, warnings as errors, XML docs, xUnit for every `*.Tests` project), `global.json` (the .NET 10 SDK) and
the `D2Dotnet.slnx` solution.

## Documentation

- [docs/design.md](docs/design.md): the design, and why the library draws its lines where it does.
- [docs/maps.md](docs/maps.md): `D2Dotnet.Maps`: usage, the model and its units, what it reads, the libd2 port.
- [docs/memory.md](docs/memory.md): `D2Dotnet.Game` and `D2Dotnet.Memory`: usage, the API, the d2go mapping and
  every change from d2go.
- [src/D2Dotnet/Files/README.md](src/D2Dotnet/Files/README.md): `D2Dotnet.Files`: providers, paths, the CASC format
  as read, sources and limits.

## No game data in the repository

Nothing derived from Blizzard's files is committed: no excel txt files, string tables, DS1/DT1 files, baked blobs, or
generated tables of names, codes or stats. The id enums are committed because they are identifiers; everything
descriptive is read from the user's install at run time.

The tests follow the same rule. They build synthetic data (in-memory tables, a synthetic CASC storage, synthetic
memory images), and for the oracle tests the build of `tests/D2Dotnet.Tests` downloads two pinned sources into the
gitignored `.cache/` folder (see `tests/Fixtures.targets`):

- [libd2](https://github.com/jaenster/libd2) at `00004e6`: its excel tables, baked DS1/DT1 blobs and raw tile
  files, for the map generator's oracle against libd2's native generator (the test-only `LibD2` NuGet package);
- [d2go](https://github.com/Farmith/d2go) at `1072577`: its Go source and the excel tables it generates from, for the
  table, id enum and memory layout oracles.

Without them the tests that need them fail, never skip. The full libd2 oracle (`OracleTests.MatchesLibD2`) takes
several minutes; `dotnet test tests/D2Dotnet.Tests --filter "FullyQualifiedName!~OracleTests.MatchesLibD2"` leaves it
out, and `D2_ORACLE_SEEDS=200` runs it over more seeds.

## Credits and licenses

d2dotnet is MIT licensed, see [LICENSE](LICENSE). It contains code ported from other MIT projects, which keeps its
original copyright; [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) has their licenses, and the package carries them:

- [libd2](https://github.com/jaenster/libd2) by jaenster: the map generator (`Maps/`) and the excel reader's rules;
- [d2go](https://github.com/hectorgimenez/d2go) by Héctor Giménez: the memory layout and model (`Game/`,
  `Memory/`) and the id enums;
- [CascLib](https://github.com/ladislav-zezula/CascLib) by Ladislav Zezula: the CASC reader (`Files/Casc/`).

Diablo II and Diablo II: Resurrected are trademarks of Blizzard Entertainment, Inc. This project is not affiliated
with Blizzard and contains no game files.

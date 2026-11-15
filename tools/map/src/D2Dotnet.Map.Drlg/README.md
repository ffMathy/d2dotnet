# D2Dotnet.Map.Drlg

A pure managed (64-bit, no game DLLs) C# port of the Diablo II map generator (DRLG) of
[libd2](https://github.com/jaenster/libd2). Give it a seed, a difficulty and an act, and it
generates every level of the act the way the 1.14d engine does:

- the level list with world origin and size, and every room;
- the raw subtile collision map (the engine's per-room CollMaps composited per level);
- preset units (objects and NPCs) with class ids and positions, waypoints among them;
- exits/adjacents (warp doors and cross-level seams) with destination level and position;
- the seeded Act I outdoor shrines and wells.

Monster population and pathfinding are not part of the port.

```csharp
using D2Dotnet.Map.Drlg;
using D2Dotnet.Map.Drlg.Data;

var generator = new DrlgGenerator(new GameFilesDataSource(gameFiles)); // IGameFileProvider
Act act = generator.GenerateAct(seed: 1337, Difficulty.Normal, act: 0); // act is 0..4

foreach (Level level in act.Levels)
    Console.WriteLine($"{level.Id} {level.Name}: {level.Rooms.Count} rooms, {level.Presets.Count} presets");

CollisionGrid grid = act.GetLevel(3)!.Collision!; // level-local subtiles, raw COLLIDE_* flags
```

All positions of presets, exits and shrines are level-local subtiles (multiply tiles by 5);
room rectangles and level origins are world tiles.

`DrlgGenerator` is thread-safe: each call runs on its own per-thread engine state (a native
memory pool freed at the end of the call), and the parsed tables and DS1/DT1 caches are shared.

## Credit and licence

The engine code is a port of libd2 by jaenster, MIT licensed, pinned at commit
`00004e6383890d895507ab4d12dfbb1a857c3bdf`. See [LICENSE-libd2](LICENSE-libd2). The port keeps
libd2's structure and its comments citing the reconstructed 1.14d sources and Game.exe
addresses, so the two can be read side by side:

| libd2 (packages/drlg/src/…) | here |
| --- | --- |
| `drlg/structs.zig` | `Engine/Structs.cs` |
| `drlg/pool.zig` | `Engine/Pool.cs` |
| `drlg/rng.zig`, core `rng.zig` | `Engine/Rng.cs` |
| `drlg/tables.zig`, `tables.zig`, `txt.zig` | `Engine/Tables.cs`, `Data/TxtTable.cs` |
| `drlg/DrlgGrid.zig` | `Engine/DrlgGrid.cs` |
| `drlg/DrlgRoom.zig`, `DrlgWarp.zig`, `RoomTile.zig` | `Engine/DrlgRoom.cs` |
| `drlg/DrlgVer.zig` | `Engine/DrlgVer.cs` |
| `drlg/drlg.zig`, `Transform.zig` | `Engine/Drlg.cs` |
| `drlg/preset.zig` | `Engine/Preset.cs` |
| `drlg/presettables.zig` | `Engine/PresetTables.cs` |
| `act.zig` | `Engine/ActGraph.cs` |
| `drlg/outdoors/*.zig`, `drlg/TileSub.zig` | `Engine/Outdoors/*.cs` |
| `drlg/maze/Maze.zig`, `maze/deps.zig` | `Engine/Maze/MazeGen.cs`, `Engine/Maze/MazeDeps.cs` |
| `drlg/maze/Act*.zig` | `Engine/Maze/MazeActs.cs`, `Engine/Maze/MazeTables.cs` |
| `drlg/tilegen.zig` | `Engine/TileGen.cs` |
| `drlg/materialize.zig` | `Engine/Materialize.cs` |
| `lib.zig` (generateActFull and its collectors) | `DrlgGenerator.cs`, `Model.cs` |
| formats `ds1.zig`, `dt1.zig` | `Formats/Ds1File.cs`, `Formats/Dt1File.cs` |
| `enums.zig` | `LevelIds.cs`, `Engine/PresetIds.cs` |

## Data sources

The generator reads everything through `IDrlgDataSource`:

```csharp
public interface IDrlgDataSource
{
    TxtTable? GetTable(string name);          // excel table by name (see ExcelTables)
    Ds1File? GetDs1(string tilesRelativePath); // e.g. "act1/town/townn1.ds1"
    Dt1File? GetDt1(string tilesRelativePath); // e.g. "act1/town/floor.dt1"
    int[] GetPresetObjectTable();              // 750 entries: act*150 + DS1 object id -> Objects.txt row
}
```

Tables used: Levels, LvlPrest, LvlTypes, LvlMaze, LvlSub, LvlWarp, MonStats, SuperUniques,
MonPlace, MonPreset. Every column is looked up by its header name. A missing DS1/DT1 is allowed
(null): the engine then sees it as absent, exactly like libd2 with a file its blob lacks.

- **`GameFilesDataSource(IGameFileProvider)`** (production) parses the raw files of a Diablo II:
  Resurrected install: `data/global/excel/<name>.txt` and `data/global/tiles/<path>` (the DS1 and
  DT1 parsers are ports of libd2's raw parsers). The preset object table is built from
  `objpreset.txt` (or `obj.txt`) by column name, or can be passed to the constructor.
- **`LibD2BakedDataSource`** (test project only) reads libd2's own baked data — the excel files
  of `packages/data/src/excel`, `ds1_blob.bin`, `dt1_blob.bin` and `PresetObjectTable.bin` — the
  same inputs the libd2 native library embeds, so the port can be compared against it.

## Tests and the libd2 oracle

`tools/map/tests/D2Dotnet.Map.Drlg.Tests` never contains Blizzard-derived files. Its build
downloads the pinned libd2 tarball
(`https://codeload.github.com/jaenster/libd2/tar.gz/00004e6383890d895507ab4d12dfbb1a857c3bdf`)
into `<repo>/.cache/libd2/` (gitignored) and extracts it. If that fails the build still succeeds
and every test that needs the fixtures fails with a message saying so; they are never skipped.
Placing the codeload tarball as `.cache/libd2/libd2-<sha>.tar.gz` by hand works too.

- **Oracle** (`OracleTests`): the port fed by `LibD2BakedDataSource` against the LibD2 NuGet
  (the libd2 native generator, P/Invoked by its wrapper) for every act × difficulty over a seed
  set: level ids, names, types, placement, origins and sizes; every room; the raw collision grid
  cell for cell; exits; preset units; outdoor shrines. The newest NuGet (0.1.1) is older than
  the pinned commit, whose last three commits changed what a level's preset list is (room
  preset lists after the room build, LvlSub waypoints/shrines on outdoor rooms, tile-marked
  presets, MonPresets that are objects, Act II shrine files). Presets and shrines are therefore
  compared through a rebuild of the 0.1.1 collector over the port's engine state
  (`LegacyPresets`), and the pinned commit's own preset rules are checked against the 1.14d
  game server data that libd2's tests carry (`EnginePresetTests`).
- **Formats** (`RawFormatTests`, `FormatUnitTests`): each raw libd2 `.ds1`/`.dt1` fixture parsed
  by the production parsers and re-packed with the bake tools' `packRecord` must reproduce its
  baked record byte for byte (three DS1 fixtures are a different revision of the game file
  than the one the blob was baked from; for those the header and layer layout are checked),
  plus libd2's own parser unit tests.
- **RNG** (`RngTests`): libd2's rng unit tests.

Run them with

```sh
dotnet test tools/map/tests/D2Dotnet.Map.Drlg.Tests
```

The default oracle set is 10 seeds. For a larger set:

```sh
D2_ORACLE_SEEDS=200 dotnet test tools/map/tests/D2Dotnet.Map.Drlg.Tests --filter OracleTests
```

## Diablo II: Resurrected

The port reproduces 1.14d (libd2's target). With D2R game files expect: tables with extra
columns (ignored, lookups are by name), paths with backslashes (normalised), and possibly a
different DS1 object table (pass it to `GameFilesDataSource` if `objpreset.txt` is absent).
Where D2R changed level data or the generator itself, the output follows the data but not any
changed engine logic.

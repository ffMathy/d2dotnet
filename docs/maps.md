# Maps

`D2Dotnet.Maps` generates the maps of Diablo II the way the game does: give it a seed, a difficulty and an act, and it
builds every level of the act with the game's level generator (DRLG), in managed code, from the files of a Diablo II:
Resurrected install. No game DLLs, no wine, any 64-bit platform .NET runs on.

For each level you get:

- its place in the world and its rooms;
- its collision map: the game's raw collision flags for every subtile;
- the units it places (objects, monsters and NPCs), waypoints among them;
- its exits: warps (doors, stairs, cave entrances) and open borders to the outdoor levels it touches;
- the shrines and wells the game seeds into Act I's outdoor rooms.

Monster population (the random packs) and pathfinding are not part of it.

## Usage

```csharp
using D2Dotnet;
using D2Dotnet.Files;
using D2Dotnet.Maps;

using var game = GameInstall.Open(@"C:\Program Files (x86)\Diablo II Resurrected");

GeneratedAct act = game.Maps.Generate(seed: 0x3607656c, Difficulty.Hell, ActId.Act2);

foreach (MapLevel level in act.Levels)
{
    var name = game.Data.Localization.GetLevelName(level.Id);
    Console.WriteLine($"{level.Id} {name}: {level.Rooms.Count} rooms at {level.Bounds}");
}

MapLevel canyon = act[AreaId.CanyonOfTheMagi];
foreach (LevelExit exit in canyon.Exits)
{
    var good = exit.Destination == act.StaffTomb ? " (the true tomb)" : "";
    Console.WriteLine($"exit to {exit.Destination} at {exit.Position}{good}");
}

CollisionMap collision = canyon.Collision!;
bool walkable = collision.IsWalkable(new Point(120, 80));
```

`game.Maps` is created the first time it is used and reads the level tables then; tile files are read the first time
a level needs them. You can also create a generator over a `GameDatabase` you already have:

```csharp
var maps = new MapGenerator(game.Data);
```

`MapGenerator` is thread-safe: one instance can generate several acts at once. Each call runs on its own state (a
native memory pool freed when the call returns), and the tables and parsed tile files are shared.

## The API

| Type | What it is |
| --- | --- |
| `MapGenerator` | `Generate(seed, Difficulty, ActId)` returns a `GeneratedAct`; `GetAreas(ActId)` lists an act's levels. Created by `GameInstall.Maps`, or from a `GameInstall` or a `GameDatabase`. |
| `GeneratedAct` | `Act`, `Seed`, `Difficulty`, `StaffTomb` (Act II's true Tal Rasha's Tomb, null elsewhere) and `Levels`; `Find(AreaId)` and the `[AreaId]` indexer look a level up. |
| `MapLevel` | `Id` (`AreaId`), `Layout` (maze, preset or wilderness), `IsPlaced`, `Bounds`, `Rooms`, `Presets`, `Waypoints`, `Exits`, `Shrines` and `Collision`. |
| `MapRoom` | A room's `Bounds` and the engine's room type, preset type and picked file. |
| `PresetUnit` | `Kind` (object or NPC), `ClassId` and `Position`. |
| `LevelExit` | `Destination` (`AreaId`), `Position` and `Kind` (warp or open border). |
| `Shrine` | `Object` (`ObjectId`) and `Position`. |
| `CollisionMap` | `Width`, `Height`, the raw flags by `[x, y]` or `[Point]`, `Cells`, `IsWalkable` and `IsCovered`. |

Units:

- `MapLevel.Bounds` and `MapRoom.Bounds` are **world tiles**.
- Everything inside a level (`PresetUnit.Position`, `LevelExit.Position`, `Shrine.Position`, the `CollisionMap`) is in
  **level-local subtiles**: a tile is `MapLevel.SubtilesPerTile` (5) subtiles across, and (0, 0) is the level's
  top-left corner. The collision map is the level's size in subtiles.
- A collision cell is the game's `COLLIDE_*` flag word: `CollisionMap.BlockPlayer` (0x01) blocks walking, the other
  bits block line of sight, missiles and so on. `CollisionMap.Uncovered` (0xFFFF) marks subtiles no room covers.
- A monster `PresetUnit.ClassId` is a monstats.txt game row (a `MonsterId`); a monster a DS1 map names through
  monpreset.txt is reported as libd2 does, in one id space: the monstats.txt game rows, then the superuniques.txt game
  rows, then the monplace.txt rows. An object `ClassId` is an objects.txt row (an `ObjectId`).

Names and other descriptive data are not copied into the maps: look them up in the game's tables
(`GameDatabase.Levels`, `GameDatabase.Objects`, ...) and string tables (`Localization.GetLevelName`,
`GetObjectName`, `GetMonsterName`, `GetSuperUniqueName`).

Errors: creating a generator throws `FileNotFoundException` when a level table is missing and `InvalidDataException`
when the game has no DS1 object table (`objpreset.txt`); `Generate` throws `ArgumentOutOfRangeException` for an
undefined act or difficulty.

## What it reads

| File | Used for |
| --- | --- |
| `data/global/excel/levels.txt` | the act's levels, their sizes per difficulty, layout, placement and links |
| `lvlprest.txt`, `lvltypes.txt`, `lvlmaze.txt`, `lvlsub.txt`, `lvlwarp.txt` | preset maps, tile sets, maze sizes, outdoor sub-presets, warps |
| `monstats.txt`, `superuniques.txt`, `monplace.txt`, `monpreset.txt` | the monsters a DS1 map places |
| `objpreset.txt` (or `obj.txt`) | the objects a DS1 map places, per act (or pass the table to the `MapGenerator` constructor) |
| `data/global/tiles/**.ds1`, `**.dt1` | the preset maps and the tile libraries (collision) |

Columns are found by their header name, so the columns D2R added to the tables are ignored.

## Credit: libd2

The generator is a port of the DRLG of [libd2](https://github.com/jaenster/libd2) by jaenster (MIT, see
[`src/D2Dotnet/Maps/LICENSE-libd2`](../src/D2Dotnet/Maps/LICENSE-libd2)), pinned at commit
`00004e6383890d895507ab4d12dfbb1a857c3bdf`. libd2 reproduces the 1.14d engine from its reconstructed sources; the
port keeps libd2's structure and its comments citing those sources and Game.exe addresses, so the two can be read side
by side. The engine is `internal` (`D2Dotnet.Maps.Engine`, unsafe structs in native memory, as libd2 mirrors the
engine's pointer layout); only the model above is public.

| libd2 (`packages/drlg/src/…`) | D2Dotnet (`src/D2Dotnet/Maps/…`) |
| --- | --- |
| `drlg/structs.zig` | `Engine/Structs.cs` |
| `drlg/pool.zig` | `Engine/Pool.cs` |
| `drlg/rng.zig`, core `rng.zig` | `Engine/Rng.cs` |
| `drlg/tables.zig`, `tables.zig` | `Engine/Tables.cs` |
| `txt.zig` | `../Data/ExcelTable.cs` (the library's excel reader) |
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
| `lib.zig` (`generateActFull` and its collectors) | `MapGenerator.cs`, with the model in `MapLevel.cs` and `CollisionMap.cs` |
| formats `ds1.zig`, `dt1.zig` | `Formats/Ds1File.cs`, `Formats/Dt1File.cs` |
| `enums.zig` | `../AreaId.cs`, `Engine/PresetIds.cs` |

The engine reads its data through an internal `IMapDataSource` (`Engine/IMapDataSource.cs`); in production
`GameDataSource` feeds it from a `GameDatabase` and its game files.

## Diablo II: Resurrected

The port reproduces the 1.14d generator (libd2's target) over the data it is given. With D2R's files the tables and
tile files are D2R's, so the levels follow D2R's data; where D2R changed the generator's own logic, maps can differ
from the game.

## Tests

The library tests (`tests/D2Dotnet.Tests/Maps`) never contain Blizzard-derived files. Their build downloads the pinned
libd2 source into the gitignored `.cache/libd2/` (see `tests/Fixtures.targets`); without it the tests that need it
fail, never skip.

- **Oracle** (`OracleTests`): the port, fed libd2's own baked data (`LibD2BakedDataSource`, the excel files, DS1/DT1
  blobs and preset object table the native library embeds), against the libd2 native generator (the `LibD2` NuGet,
  test-only) for every act and difficulty over a seed set: level ids, names, layouts, placement and bounds, every
  room, the collision map cell for cell, exits, preset units and outdoor shrines. The newest NuGet (0.1.1) is older
  than the pinned commit, whose last three commits changed what a level's preset list is; presets and shrines are
  therefore compared through a rebuild of the 0.1.1 collector over the port's engine state (`LegacyPresets`), and the
  pinned commit's own preset rules are checked against the 1.14d game server data libd2's tests carry
  (`EnginePresetTests`). The default set is 10 seeds; `D2_ORACLE_SEEDS=200` runs more.
- **Formats** (`RawFormatTests`, `FormatUnitTests`): libd2's raw `.ds1`/`.dt1` fixtures parsed and re-packed must
  reproduce their baked records byte for byte, plus libd2's parser unit tests.
- **RNG** (`RngTests`): libd2's rng unit tests.
- **API** (`GeneratorTests`, `GameDataSourceTests`): determinism and thread safety, the model, the data source over
  in-memory game files.

```sh
dotnet test tests/D2Dotnet.Tests --filter "FullyQualifiedName~D2Dotnet.Tests.Maps"
D2_ORACLE_SEEDS=200 dotnet test tests/D2Dotnet.Tests --filter OracleTests
```

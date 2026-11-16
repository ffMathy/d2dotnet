# D2Dotnet design

D2Dotnet is one .NET library for Diablo II: Resurrected. It reads the game's files and generates its maps, and it
reads a running game. It started as three ports (blacha/diablo2's map server, libd2's map generator, d2go's data and
memory packages). This document records where it draws its lines, and why. It does not follow the shape of any of
the projects it came from.

## Decisions

1. **One library, plus apps that use it.** All reusable code is in a single assembly, `D2Dotnet`, with no
   third-party dependencies. Programs are separate projects that reference it: the map server is the first. The old
   split (map, map generator, game files, d2go data, d2go memory) mirrored where the code came from, not how it is
   used: every part needed the same IDs, the same excel tables and the same file access.

2. **One vocabulary.** Each game concept has exactly one type, in the root namespace `D2Dotnet`:
   - `AreaId` (Levels.txt row id): replaces the map generator's `LevelIds` and d2go's `area.ID`.
   - `Difficulty`, `ActId`: replace three difficulty enums and plain `int` acts.
   - `Point` (an x/y pair) and `Rect`.
   - The other ID enums (monsters, objects, items, skills, stats, states, quests).

   ID enums are committed code because they are identifiers. Everything descriptive (names, sizes, codes, stats) is
   read from the game.

3. **Game data comes from the user's install, never from this repository.**
   - `D2Dotnet.Files`: access to files. `IGameFileProvider` has two implementations, a CASC install and an extracted
     folder; `GameFiles.Open(path)` picks one.
   - `D2Dotnet.Data`: one tab-separated excel reader replaces the three that existed. `GameDatabase` holds the
     strongly typed tables, each loaded lazily the first time it is used, and `Localization` reads D2R's JSON string
     tables.
   - Nothing derived from Blizzard data is committed, as before.

4. **Maps are a feature of the library, not a port.** `D2Dotnet.Maps` exposes a small public surface:
   - `MapGenerator`, `GeneratedAct`, `MapLevel`, `CollisionMap`, presets, exits and shrines, all keyed by `AreaId`.
   - The DRLG engine ported from libd2 is `internal`. Its bit-exactness against libd2 stays covered by the oracle
     tests (via `InternalsVisibleTo`).
   - The engine reads its data through an internal source that `GameDatabase` and `GameFiles` feed.

5. **Reading a running game is idiomatic .NET, not Go in C#.** `D2Dotnet.Memory` reads the process; the model lives
   in `D2Dotnet.Game` (snapshot of player, monsters, items, objects, roster, quests and so on).
   - Go compatibility shims are gone: `GoStrings`, `ValueArray`, `GoInt`, "zero value on miss". Lookups return
     `null` or use `TryGet`; text comparisons use .NET comparers; numbers use the width the game stores.
   - Behaviour d2go had by accident (the documented "kept on purpose" Go bugs) is fixed, and each fix is listed in
     "Changes from d2go" in `docs/memory.md`.
   - Memory layout facts (offsets, patterns, pointer chains) stay exactly as d2go has them. They describe the game,
     not d2go.
   - Win32 access stays behind `IProcessMemory`; only `WindowsProcessMemory` is Windows-only.
   - The snapshot reuses the shared types: `AreaId`, `Point`, `Rect`, `CollisionMap`.

6. **Modern .NET.**
   - Nullable reference types; `IsAotCompatible` and trimming-safe (no reflection-based serialisation in the
     library).
   - `ReadOnlySpan`/`Span` for parsing; immutable records for data; `IReadOnlyList` and `FrozenDictionary` for
     tables.
   - Thread-safe readers; `IDisposable` for anything holding a handle or file.
   - XML docs on every public member, and `[SupportedOSPlatform("windows")]` where needed.

7. **Small, discoverable entry points.**
   ```csharp
   using var game = GameInstall.Open(@"C:\Program Files (x86)\Diablo II Resurrected");
   var act = game.Maps.Generate(seed: 0x1234, Difficulty.Hell, ActId.Act2);
   var name = game.Data.Localization[act.Levels[0].Id];

   using var process = GameProcess.Attach();          // Windows, running D2R
   var reader = new GameReader(process, game.Data);
   GameSnapshot snapshot = reader.ReadSnapshot();
   ```

8. **The map server is an app.** It references the library and keeps what is specific to it:
   - HTTP routes, blacha/diablo2-compatible JSON, SkiaSharp PNG rendering, its cache and its Dockerfile.
   - SkiaSharp stays out of the library.

## Layout

```
src/D2Dotnet/                 the library (no third-party dependencies)
    *.cs                      shared vocabulary: AreaId, Difficulty, ActId, Point, Rect, ID enums
    Files/                    IGameFileProvider, CASC, folder provider, GameFiles, GameInstall
    Data/                     excel reader, GameDatabase and typed tables, Localization
    Maps/                     MapGenerator and model (public); Maps/Engine/ (internal DRLG); Maps/Formats/ (DS1, DT1)
    Game/                     snapshot model of a running game
    Memory/                   IProcessMemory, WindowsProcessMemory, GameProcess, GameReader
src/D2Dotnet.MapServer/       the map server app
tests/D2Dotnet.Tests/         library tests (files, data, maps, game, memory), oracles included
tests/D2Dotnet.MapServer.Tests/
tests/Fixtures.targets        downloads the pinned libd2 and d2go sources into .cache/ for the oracle tests
docs/design.md                this file
```

## Tests that carry over

- **Kept, as checks that the library is correct:**
  - the libd2 DRLG oracle;
  - the d2go table oracle (tables loaded from txt compared with d2go's generated literals);
  - CASC reader tests on a synthetic store;
  - the synthetic memory images.
- **Dropped:** tests that pin Go-only behaviour (Go `EqualFold` orbits, Go zero values, Go integer narrowing).
  Tests of the idiomatic behaviour replace them.

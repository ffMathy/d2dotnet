# D2Dotnet.Map

Diablo II: Resurrected map server: a .NET port of [`@diablo2/map`](https://github.com/blacha/diablo2/tree/master/packages/map)
(MIT, (c) 2021 Blayne Chard), with maps generated in-process by a pure managed C# port of the Diablo II map generator
(DRLG) of [libd2](https://github.com/jaenster/libd2) (MIT, (c) 2026 jaenster).

The server serves the generated levels of a game seed as JSON, or a single level as a PNG image. It reads the game
data of a **Diablo II: Resurrected** install (or a folder of files extracted from one) directly: no wine, no
32-bit process, no game DLLs. Classic Diablo II (1.13c/1.14d MPQs) is not supported.

| Folder | Namespace | What |
| --- | --- | --- |
| `../../src/D2Dotnet/Files` | `D2Dotnet.Files` | in the `D2Dotnet` library: reads a D2R install (CASC) or an extracted folder, see [its README](../../src/D2Dotnet/Files/README.md) |
| `src/D2Dotnet.Map.Drlg` | `D2Dotnet.Map.Drlg` | the DRLG port of libd2: levels, rooms, collision, presets, exits, see [its README](src/D2Dotnet.Map.Drlg/README.md) |
| `src/D2Dotnet.Map/Generation` | `D2Dotnet.Map.Generation` | in-process generation, cache, D2R string tables, and the conversion of the DRLG model to upstream's JSON |
| `src/D2Dotnet.Map/Data` | `D2Dotnet.Map.Data` | `@diablo2/data`: `Act`, `Difficulty`, `Diablo2Level` |
| `src/D2Dotnet.Map/Render` | `D2Dotnet.Map.Render` | `@diablo2/viewer`: `LevelRender`, `LevelBounds`, on SkiaSharp |
| `src/D2Dotnet.Map/Server` | `D2Dotnet.Map.Server` | `src/server.ts`, `route.ts`, `routes/*` |
| `src/D2Dotnet.Map/Binary` | `D2Dotnet.Map.Binary` | `binparse`'s `toHex` (seed logging) |
| `src/D2Dotnet.Map/Program.cs`, `CliOptions.cs` | `D2Dotnet.Map` | `src/cli.ts`, `logger.ts` |
| `src/D2Dotnet.Map/wwwroot/index.html` | | `static/index.html` (unchanged) |
| `Dockerfile`, `build.docker.sh` | | a plain .NET image |

## Requirements

- **Diablo II: Resurrected** game files, either the Battle.net install folder (the one with `.build.info`) or a folder
  of extracted files (`data/global/excel/*.txt`, `data/global/tiles/**`, `data/local/lng/strings/*.json`). The game
  files are not part of this repository; mount or point the server at your own copy.
- .NET 10 SDK to build, or Docker which does it for you. Any 64-bit platform .NET runs on.

## Running the server

### Locally

```bash
dotnet run --project tools/map/src/D2Dotnet.Map -- --game-path "/path/to/Diablo II Resurrected"
```

### Docker

`build.docker.sh` builds a two-stage image (`mcr.microsoft.com/dotnet/sdk:10.0` to publish for linux-x64,
`mcr.microsoft.com/dotnet/aspnet:10.0` to run). The build context is the repository root.

```bash
./tools/map/build.docker.sh
docker run -v "/path/to/Diablo II Resurrected":/app/game:ro -p 8899:8899 d2dotnet/map
```

### Configuration

```
D2Dotnet.Map [options]

    --port <port>             HTTP port to listen on (env PORT, default 8899)
    --concurrency <n>         Maximum number of maps generated at the same time (env DIABLO2_CONCURRENCY, default 1)
    --game-path <path>        Diablo II: Resurrected install folder, or a folder of extracted game files (default /app/game)
    --language <code>         Language of level/npc/object names: enUS, deDE, frFR, ... (default enUS)
    --help [-h]               Show this help
```

| Environment variable | Meaning |
| --- | --- |
| `PORT` | HTTP port (upstream). |
| `DIABLO2_CONCURRENCY` | Maximum number of concurrent generations. `DIABLO2_CLUSTER_SIZE` (upstream's generator process count) and `--cluster-size` are still read as older names for it. |
| `GIT_VERSION`, `GIT_HASH` | Logged at start up (set by the Docker build). |
| `Map__<Option>` | Any `MapOptions` value through ASP.NET Core configuration: `Map__Diablo2Path`, `Map__MaxConcurrency`, `Map__CacheSize` (default 100, upstream's), `Map__Language`. |

`--language` takes a D2R string table field (`enUS`, `zhTW`, `deDE`, `esES`, `frFR`, `itIT`, `koKR`, `plPL`, `esMX`,
`jaJP`, `ptBR`, `ruRU`, `zhCN`, in any letter case) or a classic language code (`ENG`, `DEU`, `FRA`, ...), which is
mapped to its D2R field. Upstream always used `ENG`. Logs are JSON lines when stdout is redirected (pino style) and
single line text on a terminal, at debug level.

## HTTP API

| Route | Response |
| --- | --- |
| `GET /health` | `{"message":"ok"}` |
| `GET /v1/map/:seed/:difficulty.json` | every level of every act: `{ id, seed, difficulty, levels }` |
| `GET /v1/map/:seed/:difficulty/:act.json` | one act: `{ id, seed, difficulty, levels, act }` |
| `GET /v1/map/:seed/:difficulty/:act/:level.json` | one level of an act: `{ id, seed, difficulty, levels, act }` |
| `GET /v1/map/:seed/:difficulty/:level.png` | PNG render of one level |
| `GET /` | `wwwroot/index.html` |

- `seed` is any JavaScript number (`0x3607656c`, `906454380`), read as an unsigned 32 bit integer; a fractional
  seed passes validation but fails generation with a 500, as upstream's generator did.
- `difficulty` is `Normal`, `Nightmare`, `Hell` or `0`-`2`.
- `act` is `ActI`..`ActV` or `0`-`4`; `level` is a level id.
- Invalid parameters give `422 {"id","message"}`, any other failure (including missing or unreadable game files)
  `500 {"id","message":"Internal server error"}`.
- CORS is open (`Access-Control-Allow-Origin: *`), as upstream's `cors()`.

```bash
curl localhost:8899/health
# Act 1 in Normal
curl localhost:8899/v1/map/0x3607656c/Normal/ActI.json
curl localhost:8899/v1/map/0x3607656c/0/0.json
# Act 5 in Hell
curl localhost:8899/v1/map/0x3607656c/2/4.json
# Blood Moor (level 2) of act 1
curl localhost:8899/v1/map/0x3607656c/Normal/ActI/2.json
# Tower Cellar Level 3 in Hell as an image
curl localhost:8899/v1/map/0xff00ff/Hell/23.png -o tower.png
```

### Level format

The JSON is the one upstream's generator printed:

```jsonc
{
    "type": "map",
    "id": 74,                                   // level id (levels.txt)
    "name": "Arcane Sanctuary",                 // levels.txt LevelName, translated with the D2R string tables
    "offset": { "x": 25000, "y": 5000 },        // offset of the level from the top left of the game world, in subtiles
    "size": { "width": 1000, "height": 1000 },  // in subtiles
    "objects": [                                // important objects / npcs / exits in this level, level-local subtiles
        { "id": 250, "type": "npc", "x": 440, "y": 20, "name": "The Summoner", "isSuperUnique": true, "superId": 18 },
        { "id": 402, "type": "object", "x": 449, "y": 449, "name": "Waypoint", "op": 23, "class": "waypoint" },
        { "id": 53, "type": "exit", "x": 137, "y": 0, "name": "Palace Cellar Level 3" }
    ],
    "map": [[1, 149]]                           // collision map, one run length encoded row per y
}
```

Each row alternates collision and open space, starting with collision; the rest of a row after its last run is
the opposite of the last written run. `[1,5,1], [2,3,2], [1,5,1]` over a 7 wide level is:

```
X.....X
XX...XX
X.....X
```

How the DRLG model (`D2Dotnet.Map.Drlg.Level`) becomes this JSON (`Generation/LevelConverter.cs`), following
upstream's `map/d2_client.c`:

- `offset`/`size`: the level's world tile origin and size times 5.
- `map`: rows 0 to the last row with a walkable subtile; a subtile is walkable when its raw collision value is even
  (`COLLIDE_BLOCK_PLAYER` clear). Subtiles no room covers, and the value 1024, are collision, as upstream.
- `objects`: the level's preset npcs and objects (upstream's never-exported ones, such as torches and critters, are
  left out), then the seeded Act I shrines and wells, then one `exit` per warp (dungeon entrance, stairs) at the warp
  room's centre. `op` is objects.txt `OperateFn` for selectable objects below id 580, `class` is derived from it
  (580/581 are `chest-super`), `isGoodExit` marks upstream's quest exits (including Act II's true Tal Rasha's Tomb).
- Names: levels.txt `LevelName`, monstats.txt `NameStr`, superuniques.txt `Name` and objects.txt `Name`, looked up
  in `data/local/lng/strings/*.json` (the `levels`, `monsters` and `objects` files first) in the configured language,
  falling back to `enUS` and then to the key itself.
- Npc ids are MonStats game rows; a super unique is the MonStats row count plus its SuperUniques row (`superId`).

## Differences from upstream's output

The server keeps upstream's routes, parameters, status codes, response shapes, PNG rendering and cache. Where the
generator changed, so did some values:

- **Exit positions** are the centre of the room holding the warp, not the position of the warp tile itself (the
  DRLG model has no tile presets).
- **Exits between outdoor levels** (open borders, e.g. Rogue Encampment to Blood Moor) are not listed, as upstream's
  generator, which only reported warp tiles, never did. The DRLG model knows them (`ExitKind.Seam`).
- **Object order**: npcs and objects in the model's order, then shrines, then exits; upstream listed each room's
  presets in turn.
- **MonPlace npcs** (preset monster spots that are not a MonStats or SuperUniques row) have ids above the super
  uniques and no name; upstream gave them ids that overlapped the super unique range, so some were misreported as
  super uniques.
- **Names** come from the D2R string tables; upstream used the untranslated level and object names of the classic
  tables and the classic `.tbl` files for monsters. Super unique names come from the game data instead of a
  hard-coded English list. A key with no string falls back to the key (upstream: no name for monsters).
- `name` is in its declared position (after `y`); upstream's server appended corrected names after the generator's fields.
- The DRLG port reproduces the 1.14d engine's generation logic over D2R's data; where D2R changed the generator
  itself, maps can differ from the game (see the [DRLG README](src/D2Dotnet.Map.Drlg/README.md)).

Other behaviour differences from upstream:

- No generator processes: maps are generated in-process by at most `--concurrency` requests at a time; there is no
  timeout, retry or restart. A request for a map that another request is already generating waits for it and uses
  the cached result.
- The game files are opened on the first map request; when that fails (missing folder or tables) every map
  request fails with a 500 until the server is restarted, as upstream's pool creation.
- Not ported: the maplibre browser viewer (`/index.js` is served from `wwwroot/index.js` only if such a file is
  added), `src/index.ts`, `package.json`, `tsconfig.json`, `CHANGELOG.md`, and everything else of upstream's other
  packages.
- Express specifics not reproduced: `X-Powered-By`/`ETag` headers, the HTML 404 page, and the stray numeric headers
  the upstream `/` handler sets via `res.header('text/html')`; `/` is served as `text/html`.
- Logging uses `Microsoft.Extensions.Logging` (JSON console when redirected) instead of pino; the request id is a
  ULID.

## Tests

```bash
dotnet test tools/map/tests/D2Dotnet.Map.Tests
```

- The server tests run the real routes, map service, cache and JSON conversion over `FakeMapGenerator`, an
  `IMapGenerator` returning small synthetic acts and tables (`FakeMapGenerator.cs`), so no game files are needed.
- `LevelConverterTests`, `GameStringsTests` and `MapGameDataTests` cover the model to JSON conversion and the D2R
  string table reader with synthetic inputs.
- `EndToEndTests` run the real DRLG generator over libd2's baked game data, which the build downloads into the
  gitignored `.cache/libd2/` (shared with `D2Dotnet.Map.Drlg.Tests`, see `tests/LibD2Fixtures.targets`); they fail,
  never skip, when that download is not available.

No Blizzard files are committed: game data used by tests is downloaded into `.cache/`.

## License

MIT. Ported from blacha/diablo2, MIT, Copyright (c) 2021 Blayne Chard (see `LICENSE` in this folder), and libd2,
MIT, Copyright (c) 2026 jaenster (see `src/D2Dotnet.Map.Drlg/LICENSE-libd2`). See the repository's
`THIRD-PARTY-NOTICES.md`.

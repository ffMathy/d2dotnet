# D2Dotnet.Map

Diablo 2 map server: a .NET port of [`@diablo2/map`](https://github.com/blacha/diablo2/tree/master/packages/map)
(MIT, (c) 2021 Blayne Chard).

The server drives the native Diablo 2 map generator `d2-map.exe` (built from `native/`, run under wine) and
serves the generated levels of a game seed as JSON, or a single level as a PNG image. Generated maps are cached,
several generator processes can run in parallel, and a generator that crashes, hangs or has served too many
seeds is restarted.

Only the map package is ported. The parts of `@diablo2/data`, `@diablo2/bintools`, `@diablo2/mpq` and
`@diablo2/viewer` that it uses are folded into this one project:

| Folder | Namespace | Upstream |
| --- | --- | --- |
| `src/D2Dotnet.Map/Binary` | `D2Dotnet.Map.Binary` | the `binparse` npm package (cursor reads, bit stream, `toHex`) |
| `src/D2Dotnet.Map/Mpq` | `D2Dotnet.Map.Mpq` | `@diablo2/mpq`: MPQ v1 reader, encryption, PKWARE DCL explode (port of `implode-decoder`) |
| `src/D2Dotnet.Map/Bintools` | `D2Dotnet.Map.Bintools` | `@diablo2/bintools`: `Diablo2MpqLoader`, `levels.txt`/`objects.txt`/`monstats`/`superuniques`/item `.bin` and `.tbl` string table readers |
| `src/D2Dotnet.Map/Data` | `D2Dotnet.Map.Data` | `@diablo2/data`: `Act`, `Difficulty`, `Diablo2Level`, `Diablo2MpqData` |
| `src/D2Dotnet.Map/Render` | `D2Dotnet.Map.Render` | `@diablo2/viewer`: `LevelRender`, `LevelBounds`, on SkiaSharp |
| `src/D2Dotnet.Map/Process` | `D2Dotnet.Map.Process` | `src/map/map.process.ts`, `lru.ts`, `child.process.ts`, `map.ts` |
| `src/D2Dotnet.Map/Server` | `D2Dotnet.Map.Server` | `src/server.ts`, `route.ts`, `routes/*` |
| `src/D2Dotnet.Map/Program.cs`, `CliOptions.cs` | `D2Dotnet.Map` | `src/cli.ts`, `logger.ts` |
| `src/D2Dotnet.Map/wwwroot/index.html` | | `static/index.html` (unchanged) |
| `native/` | | `map/`: C sources of `d2-map.exe` (unchanged) |
| `Dockerfile`, `build.docker.sh`, `build.mapgen.sh`, `run.sh`, `d2.install.reg` | | same files upstream, adapted to this layout |

## Requirements

- Diablo 2 Lord of Destruction **1.13c**, or **1.14d** (incl. ProjectD2). The game files are not part of this
  repository; mount or point the server at your own installation.
- wine (32 bit prefix, `WINEARCH=win32`) to run `d2-map.exe`.
- mingw-w64 (`i686-w64-mingw32-g++`) to build `d2-map.exe`, or Docker which does it for you.
- .NET 10 SDK to build the server.

## The native map generator

`native/` is the upstream generator, copied unchanged. It loads the game DLLs, initialises the game client and
prints every level as one JSON line on stdout.

### Building

```bash
cd tools/map
sudo apt-get install mingw-w64   # or any distribution's mingw-w64 package
./build.mapgen.sh                # writes bin/d2-map.exe
```

`build.mapgen.sh` compiles `native/*.c` (or `map/*.c` inside the Docker build stage, override with `MAP_SRC`) with
`GIT_VERSION`/`GIT_HASH` baked in.

### Command line

```
Usage:
    d2-map.exe [D2 Game Path] [options]

Options:
    --seed [-s]          Map Seed
    --difficulty [-d]    Game Difficulty [0: Normal, 1: Nightmare, 2: Hell]
    --act [-a]           Dump a specific act [0: ActI, 1: ActII, 2: ActIII, 3: ActIV, 4: Act5]
    --map [-m]           Dump a specific Map [0: Rogue Encampment ...]
    --verbose [-v]       Increase logging level

Examples:

    # Dump ActI from Normal mode for seed 1122334
    wine bin/d2-map.exe /home/diablo2 --seed 1122334 --difficulty 0 --act 0

    # Dump all acts from Hell mode for seed 1122334
    wine bin/d2-map.exe /home/diablo2 --seed 1122334 --difficulty 2
```

Without `--seed` the generator reads commands from stdin (`$seed <n>`, `$difficulty <n>`, `$act <n>`, `$map`,
`$exit`); this is how the server talks to it. Every output line is JSON: log lines (`{"level":30,"msg":...}`) and
messages (`{"type":"init"}`, `{"type":"info",...}`, `{"type":"map",...}`, `{"type":"done"}`).

wine needs the game's install path in the registry; `d2.install.reg` sets it to `/app/game` (`Z:\app\game`):

```bash
wine regedit d2.install.reg
wine bin/d2-map.exe /app/game --seed 10 --map 1 --difficulty 0
```

The last command should print the JSON of one level, which is a good check that wine and the game files work.
`run.sh` is upstream's helper for a lutris wine install.

### Fixing offsets

When the game client updates, the offsets in `native/d2_ptrs.h` need to change. The easiest way to trace the
initialisation is to run Diablo under wine with `WINEDEBUG=+snoop`, which logs every external call:

```bash
export WINEDEBUG=+snoop
wine Game.exe 2> snoop.log
```

For example the call `Fog.10021` relates to `FUNCPTR(FOG, 10021, VOID __fastcall, (CHAR * szProg), -10021)` in
`d2_ptrs.h`. The initialisation always starts with a few `Fog.dll` calls (`FOG_10021`, `FOG_10019`, `FOG_10101`,
`FOG_10089`, `FOG_10218`) followed by two `D2Win.dll` calls; the line after the final `RET Fog.10218` shows the
first `D2Win` call (`D2Win_10086` and `D2Win_10005` for 1.13c). See the upstream README for the full walkthrough.

## Running the server

### Locally

```bash
cd tools/map
./build.mapgen.sh
wine regedit d2.install.reg          # or a .reg file pointing at your game folder
dotnet run --project src/D2Dotnet.Map -- --game-path "/path/to/Diablo II" --registry-path d2.install.reg
```

`./bin/d2-map.exe` and `../bin/d2-map.exe` (relative to the working directory) are tried in order, as upstream;
use `--map-exe` for another location.

### Docker

`build.docker.sh` builds `d2-map.exe` with mingw (`build.mapgen.sh`), publishes the server self contained and
packages both in a wine image. The build context is the repository root.

```bash
./tools/map/build.docker.sh
docker run -v "/E/Games/Diablo II":/app/game -p 8899:8899 d2dotnet/map

# check the generator inside the image
docker run -it -v "/E/Games/Diablo II":/app/game d2dotnet/map /bin/bash
wine regedit /app/d2.install.reg
wine bin/d2-map.exe game --seed 10 --map 1 --difficulty 0
```

### Configuration

```
D2Dotnet.Map [options]

    --port <port>             HTTP port to listen on (env PORT, default 8899)
    --cluster-size <n>        Number of map generator processes (env DIABLO2_CLUSTER_SIZE, default 1)
    --game-path <path>        Diablo 2 game folder with the MPQ files (default /app/game)
    --registry-path <path>    Registry file imported with "wine regedit" (default /app/d2.install.reg)
    --wine <command>          Command used to run the generator (default wine)
    --map-exe <path>          Generator executable, may be repeated (default ./bin/d2-map.exe ../bin/d2-map.exe)
    --help [-h]               Show this help
```

| Environment variable | Meaning |
| --- | --- |
| `PORT` | HTTP port (upstream). |
| `DIABLO2_CLUSTER_SIZE` | Number of generator processes (upstream). |
| `WINEPREFIX` | Passed through to the generator; the rest of its environment is empty, as upstream. |
| `GIT_VERSION`, `GIT_HASH` | Logged at start up (set by the Docker build). |
| `Map__<Option>` | Any `MapProcessOptions` value through ASP.NET Core configuration, e.g. `Map__ProcessTimeout=00:01:00`, `Map__MaxAttempts`, `Map__MaxMapsToGenerate`, `Map__CacheSize`. |

The upstream constants are the defaults: 30 s timeout per generator response, 3 attempts per request, a
generator is restarted after 5000 map sets and the cache keeps 100 entries per generation. Logs are JSON lines
when stdout is redirected (pino style) and single line text on a terminal, at debug level.

## HTTP API

| Route | Response |
| --- | --- |
| `GET /health` | `{"message":"ok"}` |
| `GET /v1/map/:seed/:difficulty.json` | every level of every act: `{ id, seed, difficulty, levels }` |
| `GET /v1/map/:seed/:difficulty/:act.json` | one act: `{ id, seed, difficulty, levels, act }` |
| `GET /v1/map/:seed/:difficulty/:act/:level.json` | one level of an act: `{ id, seed, difficulty, levels, act }` |
| `GET /v1/map/:seed/:difficulty/:level.png` | PNG render of one level |
| `GET /` | `wwwroot/index.html` |

- `seed` is any JavaScript number (`0x3607656c`, `906454380`), read as an unsigned 32 bit integer.
- `difficulty` is `Normal`, `Nightmare`, `Hell` or `0`-`2`.
- `act` is `ActI`..`ActV` or `0`-`4`; `level` is a level id.
- Invalid parameters give `422 {"id","message"}`, any other failure `500 {"id","message":"Internal server error"}`.
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

Levels are returned exactly as the generator prints them, with names of levels, npcs, super uniques and objects
corrected from the game's MPQ files (`--game-path`):

```jsonc
{
    "id": 74,                                   // level code
    "name": "Arcane Sanctuary",                 // name given back by the diablo2 client
    "offset": { "x": 25000, "y": 5000 },        // offset of the level from the top left of the game world
    "size": { "width": 1000, "height": 1000 },
    "objects": [                                // important objects / npcs / exits in this level
        { "id": 53, "type": "exit", "x": 137, "y": 0, "name": "Palace Cellar Level 2" },
        { "id": 250, "type": "npc", "x": 440, "y": 20, "name": "The Summoner" },
        { "id": 402, "type": "object", "x": 449, "y": 449, "name": "Waypoint", "op": 23 }
    ],
    "map": [[1, 149]]                           // collision map, one run length encoded row per y
}
```

Each row alternates collision and open space, starting with collision; the rest of a row after its last run is
collision. `[1,5,1], [2,3,2], [1,5,1]` is:

```
X.....X
XX...XX
X.....X
```

## Design notes

- **Process pool.** `MapCluster` owns `DIABLO2_CLUSTER_SIZE` `Diablo2MapProcess` instances. Each one serialises
  its requests (upstream `p-limit(1)`), writes `$difficulty`/`$act`/`$seed`/`$map` to the generator's stdin and
  collects the `map` lines until `done`. All processes share one LRU cache keyed like upstream.
- **Generator lifecycle.** The registry file is imported once with `wine regedit`, the generator is started
  lazily, restarted after `MaxMapsToGenerate` map sets, on a timeout, or on the
  `We got a big Error here` stderr message, and a request whose generator dies is retried up to `MaxAttempts`.
- **MPQ data.** `Diablo2MpqLoader` reads `d2data.mpq`, `d2exp.mpq` and `patch_d2.mpq` from the game path (`patch_d2.mpq`
  is searched first, then the expansion, then classic data, as upstream) for the level/object/monster/super
  unique names. The archives are required: like upstream, the pool is not created and every map request fails
  with a 500 when they cannot be opened (a file missing inside an archive is only logged).
- **Rendering.** `LevelRender` draws the collision map and objects with SkiaSharp
  (`SkiaSharp.NativeAssets.Linux.NoDependencies`, so no system fontconfig/freetype is needed); the tests render
  headlessly.
- **JSON.** System.Text.Json with camelCase names and upstream's field order; numbers are written like
  JavaScript's `JSON.stringify`.

## Deviations from upstream

Not ported (intentionally):

- The maplibre browser viewer (`src/viewer/index.ts` and the `@diablo2/viewer` bundle behind `GET /index.js`).
  `index.html` is kept unchanged and served at `/`, but the interactive map it hosts needs that bundle: `/index.js`
  is served from `wwwroot/index.js` only if such a file is added (with `process.env.MAP_HOST` replaced as
  upstream), otherwise it is a 404.
- `src/index.ts` (npm library entry point), `package.json`, `tsconfig.json` (replaced by the `.csproj`) and
  `CHANGELOG.md` (upstream release history).
- Everything in other upstream packages that the map package does not use: sniffer, memory reader, packets,
  core, state, huffman, other `.bin` readers and the viewer app.
- MPQ compressions other than PKWARE: upstream's `@diablo2/mpq` supports only PKWARE as well, and throws for
  any other compression type in the same way.

Behaviour differences (bug fixes or platform differences):

- A generator that never sends `init`, or that cannot be started, fails the request (upstream leaves an unhandled
  rejection which crashes node).
- When a generator dies mid generation the retry no longer leaks the previous attempt's `done`/`log` listeners
  (upstream could resolve with the partial maps of the failed attempt).
- Concurrent first requests start one generator per process slot (upstream could spawn two).
- `DIABLO2_CLUSTER_SIZE` below 1 starts one process (upstream would fail every request).
- MPQ decryption handles whole dwords like StormLib; extracted blocks are not marked decrypted (upstream reads
  views of the archive and decrypts in place).
- JSON fields are the same, but an object `name` filled in from the MPQ data is written in its declared position
  (after `y`) rather than appended after the generator's own fields.
- Command line switches were added; upstream is configured by environment variables only, which still work.
- Express specifics not reproduced: `X-Powered-By`/`ETag` headers, the HTML 404 page, and the stray numeric
  headers the upstream `/` handler sets via `res.header('text/html')`; `/` is served as `text/html`.
- Logging uses `Microsoft.Extensions.Logging` (JSON console when redirected) instead of pino; the request id is
  still a lower case ULID.

## Tests

```bash
dotnet test tools/map/tests/D2Dotnet.Map.Tests
```

The tests cover the ported upstream tests (MPQ hashing, crypt table, decryption, reading the `test-implode.mpq`
and `test-pkware.mpq` fixtures, lang `.tbl` reader with `patchstring.tbl`, viewer bounds), the `implode-decoder`
fixtures, the data tables, PNG rendering with SkiaSharp, and the HTTP server end to end. The server tests run the
real process pool against `tests/D2Dotnet.Map.Tests/Fixtures/fake-d2-map.sh`, a shell script speaking the
generator's stdin/stdout protocol, so no game files or wine are needed.

## License

MIT. Ported from blacha/diablo2, MIT, Copyright (c) 2021 Blayne Chard (see `LICENSE` in this folder and the
repository's `THIRD-PARTY-NOTICES.md`).

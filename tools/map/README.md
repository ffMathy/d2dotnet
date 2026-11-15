# D2Dotnet.Map

.NET port of [`@diablo2/map`](https://github.com/blacha/diablo2/tree/master/packages/map) (MIT, (c) 2021 Blayne Chard):
an HTTP server that drives the native Diablo 2 (v1.13c / 1.14d) map generator `d2-map.exe` under wine and
serves the generated levels as JSON or PNG.

Only the map package is ported. The parts of `@diablo2/data`, `@diablo2/bintools`, `@diablo2/mpq` and
`@diablo2/viewer` it uses are folded into this project:

| Folder | Namespace | Upstream |
| --- | --- | --- |
| `src/D2Dotnet.Map/Mpq` | `D2Dotnet.Map.Mpq` | `@diablo2/mpq` (MPQ v1 reader, PKWARE explode from `implode-decoder`) |
| `src/D2Dotnet.Map/Bintools` | `D2Dotnet.Map.Bintools` | `@diablo2/bintools` (`Diablo2MpqLoader`, levels/objects/monstats/items/.tbl readers) |
| `src/D2Dotnet.Map/Data` | `D2Dotnet.Map.Data` | `@diablo2/data` (acts, difficulties, level model, MPQ data) |
| `src/D2Dotnet.Map/Render` | `D2Dotnet.Map.Render` | `@diablo2/viewer` `LevelRender` and `LevelBounds`, on SkiaSharp |
| `src/D2Dotnet.Map/Process` | `D2Dotnet.Map.Process` | `map/map.process.ts`, `lru.ts`, `child.process.ts` |
| `src/D2Dotnet.Map/Server` | `D2Dotnet.Map.Server` | `server.ts`, `route.ts`, `routes/*` |
| `native/` | | `map/` C sources of `d2-map.exe`, copied verbatim |

## HTTP API

| Route | Response |
| --- | --- |
| `GET /health` | `{"message":"ok"}` |
| `GET /v1/map/:seed/:difficulty.json` | every level of every act: `{ id, seed, difficulty, levels }` |
| `GET /v1/map/:seed/:difficulty/:act.json` | one act: `{ id, seed, difficulty, levels, act }` |
| `GET /v1/map/:seed/:difficulty/:act/:level.json` | one level of an act: `{ id, seed, difficulty, levels, act }` |
| `GET /v1/map/:seed/:difficulty/:level.png` | PNG render of one level |
| `GET /` | `wwwroot/index.html` |

`seed` is any JavaScript number (`0x3607656c`, `906454380`), `difficulty` is `Normal`/`Nightmare`/`Hell` or
`0`-`2`, `act` is `ActI`..`ActV` or `0`-`4`. Invalid parameters give `422 {"id","message"}`, any other failure
`500 {"id","message":"Internal server error"}`.

```bash
curl localhost:8899/v1/map/0x3607656c/Normal/ActI.json
curl localhost:8899/v1/map/0xff00ff/Hell/23.png -o tower.png
```

Levels are returned exactly as the generator prints them (see `native/README.md` and the upstream README for the
format, collision maps are run length encoded rows) with names of levels, npcs, super uniques and objects
corrected from the game's MPQ files.

## Running

```
D2Dotnet.Map [options]

    --port <port>             HTTP port to listen on (env PORT, default 8899)
    --cluster-size <n>        Number of map generator processes (env DIABLO2_CLUSTER_SIZE, default 1)
    --game-path <path>        Diablo 2 game folder with the MPQ files (default /app/game)
    --registry-path <path>    Registry file imported with "wine regedit" (default /app/d2.install.reg)
    --wine <command>          Command used to run the generator (default wine)
    --map-exe <path>          Generator executable, may be repeated (default ./bin/d2-map.exe ../bin/d2-map.exe)
```

`WINEPREFIX` is passed to the generator, `GIT_VERSION`/`GIT_HASH` are logged at start up. Logs are JSON lines
when stdout is redirected and single line text on a terminal.

### Docker

`build.docker.sh` builds `d2-map.exe` with mingw (`build.mapgen.sh`), publishes the server and packages both in
a wine image:

```bash
./tools/map/build.docker.sh
docker run -v "/E/Games/Diablo II":/app/game -p 8899:8899 d2dotnet/map
```

## Differences from upstream

- `GET /index.js` (the maplibre browser viewer bundle, `@diablo2/viewer`) is not ported; it is served from
  `wwwroot/index.js` only if such a file is added, otherwise it is a 404. `index.html` is kept but the interactive
  map it hosts needs that bundle.
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
- Express specifics not reproduced: `X-Powered-By`/`ETag` headers, the HTML 404 page, and the stray numeric
  headers the upstream `/` handler sets via `res.header('text/html')`; `/` is served as `text/html`.

## Tests

`dotnet test` runs the ported upstream tests (MPQ hashing/decryption/decompression, crypt table, lang reader,
bounds), the `implode-decoder` fixtures, and server tests that run the real process pool against
`tests/D2Dotnet.Map.Tests/Fixtures/fake-d2-map.sh`, a shell script speaking the generator's stdin/stdout protocol.

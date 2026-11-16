# d2dotnet

A monorepo of Diablo II: Resurrected tools ported to .NET.

## Layout

Every tool lives in its own folder with the same shape:

```
tools/<tool>/
    src/<Project>/       the tool itself
    tests/<Project>.Tests/  xUnit tests
    README.md            what the tool does and how to run it
```

Shared build settings are in the repository root: `Directory.Build.props` (net10.0, nullable reference types,
implicit usings, warnings as errors, xUnit for every `*.Tests` project), `global.json` (.NET 10 SDK) and the
`D2Dotnet.slnx` solution that contains every project.

## Building and testing

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build D2Dotnet.slnx
dotnet test D2Dotnet.slnx
```

The same commands run in CI (`.github/workflows/build.yml`) for every push and pull request.

The map tests download the pinned [libd2](https://github.com/jaenster/libd2) source (its test fixtures include
game data), and the d2go tests the pinned [d2go](https://github.com/Farmith/d2go) source (its excel tables), into the
gitignored `.cache/` folder on their first build; nothing derived from the game is committed.

## Tools

| Tool | Description |
| --- | --- |
| [`tools/d2go`](tools/d2go/README.md) | Port of [d2go](https://github.com/hectorgimenez/d2go) (as forked at [Farmith/d2go](https://github.com/Farmith/d2go)): the game state model (`pkg/data`: ids of areas, NPCs, objects, skills, stats, states and quests, items, monsters, inventory, belt and key bindings, with the excel tables d2go compiles in loaded from a D2R install at run time instead) and the memory reader (`pkg/memory`, `pkg/utils`) that fills it from a running D2R process on Windows, behind a process-memory interface so everything but the Win32 calls is platform independent. |
| [`tools/map`](tools/map/README.md) | Diablo II: Resurrected map server: port of the [`map` package](https://github.com/blacha/diablo2/tree/master/packages/map) of [github.com/blacha/diablo2](https://github.com/blacha/diablo2) that generates maps in-process with a pure C# port of the map generator (DRLG) of [libd2](https://github.com/jaenster/libd2), reading a D2R install directly (no wine, no game DLLs), and serves level data as JSON and PNG. D2R only. |

## License

MIT, see [LICENSE](LICENSE). Ported code keeps its original copyright, see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

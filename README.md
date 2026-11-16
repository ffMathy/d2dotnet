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
game data) into the gitignored `.cache/` folder on their first build; nothing derived from the game is committed.

## Tools

| Tool | Description |
| --- | --- |
| [`tools/map`](tools/map/README.md) | Diablo II: Resurrected map server: port of the [`map` package](https://github.com/blacha/diablo2/tree/master/packages/map) of [github.com/blacha/diablo2](https://github.com/blacha/diablo2) that generates maps in-process with a pure C# port of the map generator (DRLG) of [libd2](https://github.com/jaenster/libd2), reading a D2R install directly (no wine, no game DLLs), and serves level data as JSON and PNG. D2R only. |

## License

MIT, see [LICENSE](LICENSE). Ported code keeps its original copyright, see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

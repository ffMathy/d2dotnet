# d2dotnet

One .NET library for Diablo II: Resurrected.

## Layout

See [docs/design.md](docs/design.md) for the design.

```
src/D2Dotnet/                the library (no third-party dependencies)
    *.cs                     shared ids: AreaId, Difficulty, ActId, Point, Rect, MonsterId, ObjectId, ItemId, ...
    Files/                   game files: IGameFileProvider, CASC and folder providers, GameFiles, GameInstall
    Data/                    ExcelTable (the excel reader), GameDatabase (typed tables), Localization (D2R strings)
tests/D2Dotnet.Tests/        xUnit tests of the library, oracles included
tests/Fixtures.targets       downloads the pinned libd2 and d2go sources into .cache/ for the oracle tests
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

The library tests download the pinned [libd2](https://github.com/jaenster/libd2) source (its test fixtures
include game data), and the pinned [d2go](https://github.com/Farmith/d2go) source (its excel
tables), into the gitignored `.cache/` folder on their first build; nothing derived from the game is committed.

## License

MIT, see [LICENSE](LICENSE). Ported code keeps its original copyright, see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

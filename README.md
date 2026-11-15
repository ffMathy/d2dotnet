# d2dotnet

A monorepo of Diablo 2 tools ported to .NET.

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

## Tools

| Tool | Description |
| --- | --- |
| [`tools/map`](tools/map/README.md) | Diablo 2 map server: port of the [`map` package](https://github.com/blacha/diablo2/tree/master/packages/map) of [github.com/blacha/diablo2](https://github.com/blacha/diablo2). Drives the native map generator under wine and serves level data as JSON and PNG. |

## License

MIT, see [LICENSE](LICENSE). Ported code keeps its original copyright, see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

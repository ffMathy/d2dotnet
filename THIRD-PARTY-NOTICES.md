# Third-party notices

d2dotnet contains code ported from the projects below. Each is used under its
own license, reproduced here.

## blacha/diablo2

<https://github.com/blacha/diablo2>, MIT, Copyright (c) 2021 Blayne Chard.

`tools/map` is a port of the `packages/map` package and of the parts of `packages/data` and `packages/viewer` it
uses. The never-exported object and npc lists of `tools/map/src/D2Dotnet.Map/Generation/UselessPresets.cs` and the
object classes and quest exits of `LevelConverter.cs` come from its native generator (`packages/map/map`).
`tools/map/src/D2Dotnet.Map/wwwroot/index.html` is copied unchanged. A copy of this license is also kept in
`tools/map/LICENSE`.

```
MIT License

Copyright (c) 2021 Blayne Chard

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## binparse

<https://github.com/blacha/binparse>, MIT, Copyright (c) 2021 Blayne Chard.

`tools/map/src/D2Dotnet.Map/Binary/Hex.cs` replaces binparse's `toHex` (same license text as above).

## libd2

<https://github.com/jaenster/libd2>, MIT, Copyright (c) 2026 jaenster.

`tools/map/src/D2Dotnet.Map.Drlg` is a C# port of libd2's Diablo II map generator (DRLG, `packages/drlg`) and of its
DS1/DT1 parsers (`packages/formats`), pinned at commit `00004e6383890d895507ab4d12dfbb1a857c3bdf`. A copy of this
license is also kept in `tools/map/src/D2Dotnet.Map.Drlg/LICENSE-libd2`. The excel reader of the library,
`src/D2Dotnet/Data/ExcelTable.cs`, follows libd2's `packages/drlg/src/txt.zig` (line, header and integer rules). The
tests download that commit into the gitignored `.cache/libd2/` at build time; none of it is committed.

```
MIT License

Copyright (c) 2026 jaenster

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## d2go

<https://github.com/hectorgimenez/d2go>, MIT, Copyright (c) 2023 Héctor Giménez.

The game snapshot and memory reader of the library (`src/D2Dotnet/Game`, `src/D2Dotnet/Memory`, see
[docs/memory.md](docs/memory.md)) are a redesign of d2go's `pkg/data`, `pkg/memory` and `pkg/utils`, from the fork
<https://github.com/Farmith/d2go> pinned at commit `1072577f86e79cb68cd74295666041f6010e07a7`: the memory layout
(code patterns, fixed addresses, structure offsets, pointer chains and read sizes), the hand-written lists (ignored
monsters, monster and object classes, debuffs, terror zones, waypoints, shrine names) and the stat rescaling are
d2go's. The tables d2go generates from the game's excel files (`cmd/txttocode`) are not carried over: they are read
from the user's game files at run time. d2go credits [MapAssist](https://github.com/OneXDeveloper/MapAssist) for the
map seed reversal of `pkg/utils/map_seed_hash.go`; that search loop is not carried over: `MapSeed` computes the same
result directly from the hash formula. The id enums of the library (`src/D2Dotnet/AreaId.cs`, `MonsterId.cs`,
`ObjectId.cs`, `SkillId.cs`, `StatId.cs`, `StateId.cs`, `QuestId.cs`, `ItemId.cs`, `ItemTypeCode.cs`) take their
values and names from d2go's `pkg/data`. A copy of this license is also kept in `src/D2Dotnet/Memory/LICENSE-d2go`.
The tests download that commit into the gitignored `.cache/d2go/` at build time; none of it is committed.

```
MIT License

Copyright (c) 2023 Héctor Giménez

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## NuGet dependencies

These are not redistributed in this repository; they are restored from NuGet at build time.

| Package | License |
| --- | --- |
| [SkiaSharp](https://github.com/mono/SkiaSharp), SkiaSharp.NativeAssets.Linux.NoDependencies | MIT, Copyright (c) Microsoft Corporation (includes Skia, BSD-3-Clause, Copyright Google Inc.) |
| Microsoft.AspNetCore (shared framework), Microsoft.NET.Test.Sdk | MIT, Copyright (c) .NET Foundation and Contributors |
| xunit, xunit.runner.visualstudio | Apache-2.0, Copyright (c) .NET Foundation and Contributors |
| coverlet.collector | MIT, Copyright (c) Toni Solarin-Sodara |
| [LibD2](https://www.nuget.org/packages/LibD2) (test-only oracle in `D2Dotnet.Map.Drlg.Tests`) | MIT, Copyright (c) 2026 jaenster |

## Diablo II

Diablo II and Diablo II: Resurrected are trademarks of Blizzard Entertainment, Inc. This project contains no game
files; the map server requires a legitimate Diablo II: Resurrected installation supplied by the user.

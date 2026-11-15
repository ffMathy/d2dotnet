# Third-party notices

d2dotnet contains code ported from, and test fixtures copied from, the projects below. Each is used under its
own license, reproduced here.

## blacha/diablo2

<https://github.com/blacha/diablo2>, MIT, Copyright (c) 2021 Blayne Chard.

`tools/map` is a port of the `packages/map` package and of the parts of `packages/data`, `packages/bintools`,
`packages/mpq` and `packages/viewer` it uses, including a C# port of the native C map generator (`d2-map.exe`,
`packages/map/map`) in `tools/map/src/D2Dotnet.Map.Generator*`. `tools/map/d2.install.reg`, `tools/map/src/D2Dotnet.Map/wwwroot/index.html` and the test fixtures
`test-implode.mpq`, `test-pkware.mpq` and `patchstring.tbl` are copied unchanged. A copy of this license is also
kept in `tools/map/LICENSE`.

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

`tools/map/src/D2Dotnet.Map/Binary` replaces the parts of `binparse` used by the ported readers (same license
text as above).

## implode-decoder

<https://www.npmjs.com/package/implode-decoder> (v1.0.1), MIT, Copyright (c) 2014 Travis Collins.

`tools/map/src/D2Dotnet.Map/Mpq/Compression/PkWareExplode.cs` is a port of its PKWARE DCL explode decoder, and
`tools/map/tests/D2Dotnet.Map.Tests/Fixtures/implode` contains its test fixtures and full license text.

```
The MIT License (MIT)

Copyright (c) 2014 Travis Collins

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

## Diablo II

Diablo II is a trademark of Blizzard Entertainment, Inc. This project contains no game files; the map server
requires a legitimate Diablo II installation supplied by the user.

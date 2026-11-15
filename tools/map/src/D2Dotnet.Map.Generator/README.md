# d2-map (D2Dotnet.Map.Generator)

C# port of the native C map generator from
[blacha/diablo2](https://github.com/blacha/diablo2) `packages/map/map` (MIT, (c) 2021 Blayne Chard).
It **replaces the native C sources** (`map/*.c`, `map/*.h`, `map/d2data/*.h`, built upstream with
`i686-w64-mingw32-g++` by `build.mapgen.sh`). The CLI arguments, usage text, stdin command protocol
(`$seed`, `$difficulty`, `$act`, `$map`, `$exit`), stdout JSON lines and log lines are kept
byte-for-byte, including the upstream `__FILE__:__LINE__` values in `"source"`.

| Project | Contents |
| --- | --- |
| `D2Dotnet.Map.Generator.Core` (AnyCPU) | all logic: JSON writer, logger, CLI/stdin parsing, d2data tables, struct layouts, d2_ptrs.h table, DLL offset resolution, x86 thunk encoder, map dumping over an abstract game memory |
| `D2Dotnet.Map.Generator` (win-x86 exe, `d2-map.exe`) | Win32 interop: LoadLibrary/GetProcAddress, registry, function pointers, executable thunks, callbacks |
| `tests/D2Dotnet.Map.Generator.Tests` | runs on linux-x64 against Core with a fake game |

## Build and publish

```sh
dotnet build tools/map/src/D2Dotnet.Map.Generator
dotnet test  tools/map/tests/D2Dotnet.Map.Generator.Tests
dotnet publish tools/map/src/D2Dotnet.Map.Generator -c Release -r win-x86 --self-contained -p:PublishSingleFile=true
```

The game DLLs are 32-bit and are loaded into this process, so the exe must be **win-x86**.
`GIT_VERSION`/`GIT_HASH` (printed in `Cli:Start`/`Map:Init:Done`) come from the environment
variables of the same name, else `git describe --abbrev=4 --dirty --always --tags` / `git rev-parse HEAD`;
override with `-p:GitVersion=... -p:GitHash=...`.

The single file is published with `IncludeAllContentForSelfExtract=true`: Wine 9 fails to map
`System.Private.CoreLib.dll` directly from the bundle (`0x8007046C`, incorrect alignment), so the
runtime is extracted to disk (`DOTNET_BUNDLE_EXTRACT_BASE_DIR`, default `%TEMP%\.net`) on first start.

## Run under wine

```sh
export WINEARCH=win32 WINEPREFIX=$HOME/.wine-d2 WINEDEBUG=-all
wine d2-map.exe /path/to/game --seed 1122334 --difficulty 0 --act 0   # one-shot
wine d2-map.exe /path/to/game                                         # stdin protocol
```

`/path/to/game` holds Diablo 2 1.13c (`Game.exe`), or `Path of Diablo/Game.exe`, or
`ProjectD2/Game.exe`. As upstream, the registry key
`HKCU\SOFTWARE\Blizzard Entertainment\Diablo II` must exist (upstream `d2.install.reg`).

## How the native parts are handled

* **stdcall** exports: `delegate* unmanaged[Stdcall]` at the resolved address.
* **fastcall** exports (no .NET support on x86): a hand-assembled stdcall-to-fastcall bridge in
  `VirtualAlloc(PAGE_EXECUTE_READWRITE)` memory (`X86Thunks.FastcallBridge`: arg1/arg2 into ECX/EDX,
  remaining args re-pushed, frame-based so ESP is restored whatever the callee pops).
* **magic jumps** (`D2CLIENT_Pod_InitGameMisc` / `D2CLIENT_Pd2_InitGameMisc`, naked inline asm):
  `X86Thunks.MagicJump` reproduces `push ecx; push ebp; push esi; push edi; jmp target`, wrapped in an
  EBP frame that restores ESP after the D2 code returns.
* **callbacks** (`D2ClientInterface`, `ExceptionHandler`): `[UnmanagedCallersOnly]` with cdecl / stdcall.
* Globals written into DLL memory (`*p_STORM_*_MPQHashTable = 0`) go through `IGameMemory`.
* Encodings are pinned in unit tests and were cross-checked with nasm; struct layouts are asserted
  against `offsetof`/`sizeof` from mingw; output is compared with golden files produced by the
  upstream C code itself (`tests/.../CReference/generate_golden.py`).

## Text-mode stdio

The upstream exe is built with mingw without `binmode.o` or `_setmode`, so msvcrt keeps stdin,
stdout and stderr in text mode. The port copies that: every `\n` it writes (JSON lines, log lines,
usage text, the exception message on stderr) goes out as `\r\n`; on stdin `\r\n` is read as `\n`
(a lone `\r` is kept) and a `0x1A` (Ctrl-Z) byte ends input like EOF. The golden tests compare
against a Linux build of the C code, so they run with text mode off; separate tests pin the
CRLF bytes and the stdin translation.

## Deviations

* Magic jump targets are the upstream constants (0x6faf559b / 0x6faf454b) when D2Client.dll is at its
  preferred base 0x6FAB0000; if it was relocated they are rebased (upstream would jump into unmapped memory).
* An option given without a value (`--seed` last) exits with 0xC0000005 instead of actually crashing.
* Strings are written as Latin-1; non-Latin-1 characters in command-line paths become `?`
  (upstream writes the ANSI code page bytes).
* Out-of-range collision coordinates throw instead of corrupting memory (undefined behaviour upstream).

## Limitation

This port has **not been run against real Diablo 2 DLLs**: no game files were available in the
environment it was written in. Under wine (win32 prefix) the published exe starts, prints the
`Cli:Start` log line and the usage text, and exits 1, exactly like the C program without a game
path. Everything up to the D2 calls is verified with fakes and the golden C comparison; the thunks
and the interaction with the real DLLs are verified only at the byte-encoding level.

# D2Dotnet.Files

Read-only access to the game files of Diablo II: Resurrected (D2R): the excel tables
(`data/global/excel/*.txt`), the level layouts and tiles (`data/global/tiles/**/*.ds1`, `*.dt1`), and the
string tables (`data/local/lng/strings/*.json`). `GameInstall` is the library's entry point: it owns the
provider and creates the `D2Dotnet.Data.GameDatabase` over it on first use.

```csharp
using var game = GameInstall.Open(@"C:\Program Files (x86)\Diablo II Resurrected");
var bloodMoor = game.Data.Localization[AreaId.BloodMoor];

IGameFileProvider provider = GameFiles.Open(@"C:\Program Files (x86)\Diablo II Resurrected");
try
{
    byte[]? levels = provider.ReadFile("data/global/excel/levels.txt");
    foreach (var ds1 in provider.EnumerateFiles("data/global/tiles/act1").Where(p => p.EndsWith(".ds1")))
        Console.WriteLine(ds1);
}
finally
{
    (provider as IDisposable)?.Dispose();
}
```

| Type | Use |
| --- | --- |
| `IGameFileProvider` | `Exists`, `ReadFile` (null when missing), `EnumerateFiles` (recursive, normalized, sorted). |
| `CascFileProvider` | Reads the CASC storage of an installed game. `Open(installDir, product?)`, `IDisposable`, thread-safe. Extras: `VfsNames`, `ReadVfsFile`, `Product`, `Version`. |
| `DirectoryFileProvider` | Reads a folder of extracted files (CascView export, `-direct -txt` / mod folder). |
| `GameFiles.Open(path)` | `CascFileProvider` when `path` contains `.build.info` (or is that file), else `DirectoryFileProvider`. |
| `GameInstall` | `Open(path, language?)`: the provider (`Files`) and a lazily created `GameDatabase` (`Data`); `IDisposable`. |
| `CascFormatException` | Corrupt, truncated or unsupported storage data (an `IOException`). |
| `CascEncryptedException` | A file is encrypted with a TACT key this reader does not have; carries `KeyName`. |

The CASC reader uses no third-party library at run time. It is written in C# from CascLib's source
(see [Sources](#sources)), and its TVFS parser (`Casc/TvfsManifest.cs`) is a port of CascLib's
`CascRootFile_TVFS.cpp`, so CascLib's MIT licence applies to it: see [`Casc/LICENSE-CascLib`](Casc/LICENSE-CascLib)
and [THIRD-PARTY-NOTICES.md](../../../THIRD-PARTY-NOTICES.md). The Jenkins hash is a port of `lookup3.c`, which is
public domain.

## Paths

Every provider uses the same convention: paths relative to the game's data root in the classic
layout, e.g. `data/global/excel/levels.txt`. Lookups ignore case and accept `/` or `\` (and a leading
separator). `EnumerateFiles` returns paths normalized by `GamePath.Normalize`: lowercase, `/`, no
leading slash. `DirectoryFileProvider` never reads outside its folder: a path with a `.` or `..` segment, an
empty segment (`data//x`) or a drive or stream colon (`C:/x`) is not found.

### D2R names in CASC

D2R's root manifest is a TVFS directory that *mounts* further TVFS manifests. CascLib appends `:` to a
mount point when it descends into it, so the file this library calls `data/global/excel/levels.txt` is
`data:data/global/excel/levels.txt` in CascLib and CascView (the root has an entry `data` that is one
of the build config's `vfs-N` manifests, whose own paths start with `data/`). CascLib's own D2R test opens
`data:data\local\sfx\...`, which confirms the scheme.

`CascFileProvider` maps a CASC name to a game path by dropping everything up to the last `:`:

| CASC name (CascLib/CascView) | Game path |
| --- | --- |
| `data:data/global/excel/levels.txt` | `data/global/excel/levels.txt` |
| `data:data/global/tiles/act1/town/townn1.ds1` | `data/global/tiles/act1/town/townn1.ds1` |
| `data:data/local/lng/strings/levels.json` | `data/local/lng/strings/levels.json` |
| `<mount>:<path>` (any other mount) | `<path>`, unless the `data` mount already has it |
| `<name>` (no mount) | `<name>` |

If two mounts contain the same path, the `data` mount wins, then the first in manifest order. The
original names stay available through `VfsNames` / `ReadVfsFile("data:data/...")`.

### Extracted folders

`DirectoryFileProvider` detects three layouts (folder names matched ignoring case):

* `<root>/data/global/...`: a mod folder (`mods/<name>/<name>.mpq/`) or files extracted for `-direct -txt`;
* `<root>/data/data/global/...`: a CascView export, where the `data:` mount became a folder;
* `<root>/global/...`: the `data` folder itself.

It lists the folder once, on first need, so lookups are case-insensitive on Linux too.

## The CASC format, as implemented

A Battle.net install looks like this:

```
.build.info                       |-separated table; the active row's "Build Key" names the build config
Data/config/ab/cd/abcd...         build config (text, key = value)
Data/data/0000000002.idx ...      local index: 16 buckets, newest version of each is used
Data/data/data.000 ...            data archives
```

**`.build.info`**: header row `Name!TYPE:size|...` (`Build Key!HEX:16`, `Active!DEC:1`, `Version!STRING:0`,
`Product!STRING:0` ...). The first active row is used, optionally filtered by product (D2R is `osi`).

**Build config**: `key = value...` lines. Used keys: `vfs-root = <ckey> [<ekey>]` (the root manifest,
preferred over `root` like CascLib does), `vfs-N = <ckey> [<ekey>]` (manifests that can be mounted),
`encoding = <ckey> <ekey>`. Content keys (CKey) are MD5s of the decoded file; encoding keys (EKey)
identify the encoded (BLTE) form.

**Local index (`.idx`, version 7)**: file name `{bucket:x2}{version:x8}.idx`.

```
0x00 u32 LE 16, u32 LE hashlittle(header, 0)
0x08 u16 LE 7, u8 bucket, u8 0, u8 4 (size bytes), u8 5 (offset bytes), u8 9 (key bytes), u8 30 (segment bits), u64 LE max archive size
0x20 u32 LE entries size, u32 LE hashlittle2 chained over each entry
0x28 entries: ekey[9], u40 BE (archive << 30 | offset), u32 LE size
```

A key's bucket is the XOR of its 9 bytes, folded to 4 bits. Both checksums are verified (the BlizzGet
variant of the entry checksum is also accepted, as in CascLib).

**Data archives**: each record is a 30-byte header (EKey byte-reversed, u32 LE size, flags, two
checksums) followed by BLTE. The index size includes the header. As in CascLib only the `BLTE`
signature after the header is checked, because shipped storages have been seen with zeroed headers.

**BLTE**: `"BLTE"`, u32 BE header size; 0 means one frame running to the end, otherwise
`0x0F`, u24 frame count and per frame u32 encoded size, u32 content size, MD5 of the encoded frame.
Frame modes: `N` (stored) and `Z` (zlib) are decoded; `E` (encrypted) raises
`CascEncryptedException` with the key name; `F` (nested), `4` and anything else raise
`CascFormatException`. Frame MD5s are verified, and the manifests read through the build config are
checked against their CKey.

**ENCODING**: `EN`, version 1, key sizes, page sizes and counts (big-endian), the ESpec string block,
then the CKey page table (first key + page MD5) and 4 KB pages of
`u8 ekeyCount, u40 contentSize, ckey[16], ekey[16][ekeyCount]`. Page MD5s are verified. D2R's TVFS refers
to files by EKey, so ENCODING is only loaded (lazily) when the build config gives a manifest's CKey
without its EKey.

**TVFS**: header `"TVFS"`, version 1, header size (0x26), EKey size 9, patch key size 9, then
big-endian flags, path/VFS/container-file table offsets and sizes, max depth. The path table is a
prefix tree of name fragments `[0x00] len name [0x00] [0xFF u32]` (the `0x00`s are separators before
and after the fragment; a value with the top bit set is a folder holding the next `size - 4` bytes, else
an offset into the VFS table; a fragment without a value is followed by a separator and continues into
the next entry). A VFS entry is a span count (1-224; 225-255 are skipped) and per span u32 offset,
u32 size and a 1-4 byte container table offset; the container entry holds the 9-byte EKey and the
encoded size. A file whose single span is one of the `vfs-N` manifests is a mount: its files are named
`<mount>:<path>`. Files with several spans are the concatenation of their spans. Files whose spans are
not all in the local index (not installed) are left out.

## Sources

* **CascLib** by Ladislav Zezula, <https://github.com/ladislav-zezula/CascLib> (read from source, the
  primary reference): `CascRootFile_TVFS.cpp` (TVFS parsing, mounts and the `:` naming,
  span handling), `CascIndexFiles.cpp` and `CascStructs.h` (index v7 layout, guarded-block checksums,
  bucket naming), `CascReadFile.cpp` (record header, BLTE header and frame decoding),
  `CascDecrypt.cpp` (`E` frame header), `CascOpenStorage.cpp` (ENCODING header, root selection:
  `vfs-root` over `root`), `CascFiles.cpp` (`.build.info` and build config parsing, `vfs-N` entries,
  the Steam `Data/.build.config` static storages), `test/CascTest.cpp` (D2R storages, the
  `data:data\...` names), and the bundled `jenkins/lookup3.c` (Jenkins hash and its test vectors).
* **TACTLib** (overtools, MIT), <https://github.com/overtools/TACTLib>: `Core/VFS/VFSManifestReader.cs`, used
  to cross-check the TVFS header and path table. It reads the TVFS `flags` as big-endian where CascLib
  reads them little-endian; this reader stores them big-endian but does not depend on them.
* **wowdev.wiki** (CASC, TACT, BLTE, TVFS pages). It was not reachable from the build environment
  (Cloudflare challenge), so the details it is usually cited for (ENCODING entry layout with a 40-bit
  content size, EKey = MD5 of the BLTE header, the record header layout) were taken from CascLib's
  structures and code instead, and from memory of the wiki where CascLib is silent (the test writer's EKey
  derivation only).

The unit tests use the official lookup3 self-test vectors (`driver5()` in lookup3.c, public domain).
No Blizzard files are used or committed: the tests build synthetic storages.

## Tests

`tests/D2Dotnet.Tests/Files` contains a test-only CASC *writer* (`Writer/`) that builds a small
but structurally real storage in a temp folder: a `.build.info` with an inactive and an active row,
build and CDN configs under `Data/config`, version-7 index buckets with real checksums (plus stale
older versions that must be ignored), several `data.###` archives with 30-byte record headers,
ENCODING with CKey and EKey pages, a TVFS root that mounts `data` (`vfs-1`) and a second mount, and
files encoded headerless, with one frame, and with many N/Z frames, plus a multi-span file, a
file that is not installed and an encrypted one. Hand-made byte vectors cover BLTE, the index,
ENCODING and TVFS (mid-name splits, separators before names, value-less fragments, deleted entries).

## Limitations

* **Only tested against synthetic storages, never against a real D2R install.** No install was
  available while writing this. The format details follow CascLib's source, but the first run on a
  real game may surface something the synthetic storage does not model.
* Battle.net installs only. The Steam edition's static storage (`Data/.build.config`, keys carrying
  the archive offset) is rejected with a clear `NotSupportedException` by `GameFiles.Open`.
* No decryption. CascLib's own D2R test opens a `data:data\local\sfx\...` file with
  `CASC_OVERCOME_ENCRYPTED`, so some D2R audio is encrypted; the map data is not expected to be.
* Only index version 7 with the standard 9/5/4 entry layout, BLTE frame tables of format `0x0F`, and
  ENCODING version 1 with 16-byte keys are supported; others raise `CascFormatException`.
* Mount names other than `data` in D2R's root are not known; any extra mounts map by the rule above.
* Files are read whole into memory. The provider reads a snapshot: if the Battle.net agent updates the
  game while it is open, reopen it.

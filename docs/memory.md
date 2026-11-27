# Reading a running game

`D2Dotnet.Memory` reads the state of a running Diablo II: Resurrected game from its process memory into the snapshot
model of `D2Dotnet.Game`. It is a redesign of [d2go](https://github.com/hectorgimenez/d2go)'s `pkg/data`,
`pkg/memory` and `pkg/utils` (MIT, (c) 2023 Héctor Giménez), as forked at
[Farmith/d2go](https://github.com/Farmith/d2go) commit `1072577f86e79cb68cd74295666041f6010e07a7`: what d2go knows
about the game's memory (code patterns, fixed addresses, structure offsets, pointer chains, read sizes, the stat
rescaling and its hand-written lists) is kept byte for byte; the API is .NET's own, and the behaviour d2go had by
accident is fixed (see [Changes from d2go](#changes-from-d2go)). d2go's license is in
[`src/D2Dotnet/Memory/LICENSE-d2go`](../src/D2Dotnet/Memory/LICENSE-d2go) and
[THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

**Windows only, for one game build.** Attaching to a game needs Windows and a running `d2r.exe`. The fixed addresses
and structure offsets are those of the D2R build d2go `1072577` targets, updated for Reign of the Warlock where a
running game showed them to have moved (the player class and the act structures; see the README): on another build
a required code pattern is not found (`GameReader` throws `InvalidDataException`), an optional one leaves its offset
null (`GameOffsets.Missing`), or the fixed addresses read garbage. Everything except
`WindowsProcessMemory` is platform independent and reads through `IProcessMemory`, so it runs over any memory source
(a dump, or the synthetic images of the tests).

### Reign of the Warlock

Found in a running Reign of the Warlock game (build `6AD81202`) by comparing the module's `.data` before and after
one change in the game:

| Address | d2go | Now | Found by |
| --- | --- | --- | --- |
| Selected character name | `0x2A01A50` | `0x1E15848` | selecting another character |
| Waypoints | `0x2140E20` | `0x1D705F0` | the Act I list with the character's waypoints |
| Key bindings | `0x1DFA914` | `0x19E95F4` | rebinding the character screen key |
| Skill key bindings | `0x21E29B0` | `0x1E12330` | putting two skills on F1 and F2 |
| Quests | `0x22E2978` | `0x1EDAF78` | the pointer chain to a buffer equal to the save file's quest block |
| Panel flags | UI - `0xA` | UI - `0x14` | opening the inventory, automap and waypoint panels |
| Loading screen | flag `0x16C` | flag `0x168` | waypoint travel |
| In game | the byte at `0x22D5D78` | the main player unit | the unit table is empty outside a game |

The quest buffer has the layout of a save file's quest block, a little-endian word per quest after Act I's
introduction; d2go read big-endian words 3 bytes in, which only got bit 12 (completed) right. The key binding blob and
the panel flags kept d2go's layout. Not checked: the online, legacy graphics, last game, frame rate and terror zone
addresses. The UI pattern matches six instructions that set different panel flags; the offsets assume the first one
in the module, as d2go did, so keep the offsets of a scan that found it.

## Usage

```csharp
using D2Dotnet.Files;
using D2Dotnet.Game;
using D2Dotnet.Memory;

using var game = GameInstall.Open(@"C:\Program Files (x86)\Diablo II Resurrected");
using var process = GameProcess.Attach();                // the first process that loaded d2r.exe (Windows)
var reader = new GameReader(process, game.Data);          // scans the game code for the offsets

GameSnapshot snapshot = reader.ReadSnapshot();
if (snapshot.Player is { } player)
{
    Console.WriteLine($"{player.Name} ({player.Class}, level {player.Level}) in {player.Area} at {player.Position}");
    Console.WriteLine($"life {player.LifePercent}%, {snapshot.Enemies.Count()} enemies, nearest first");
    var potion = snapshot.Inventory.Belt.FindPotion(PotionType.Healing);
    var teleport = snapshot.KeyBindings.FindSkill(SkillId.Teleport);
}

// Focused reads, when a whole snapshot is not needed:
IReadOnlyList<Monster> monsters = reader.ReadMonsters();
bool inGame = reader.IsInGame();
```

The map of the game is not in memory as a whole, but what generates it is: `snapshot.Map` (or `reader.ReadMapContext()`)
is a `MapContext` with the game's map seed, difficulty and the player's area, and `D2Dotnet.Maps` rebuilds the map
from it:

```csharp
if (reader.ReadMapContext() is { } context)
{
    GeneratedAct act = game.Maps.Generate(context);    // the act the player is in
    MapLevel here = act[context.Area];
    bool walkable = here.Collision!.IsWalkable(new Point(120, 80));
}
```

The reader follows the main player unit (`+0x20`) to its act, the act (`+0x70`) to its act misc structure, and reads
the difficulty (16 bits at `+0x828`) and the init and end seed hashes (32 bits at `+0x838` and `+0x860`); `MapSeed`
turns the hashes into the seed. Reign of the Warlock moved these: before it they were `+0x78`, `+0x830`, `+0x840` and
`+0x868`. The new ones were checked in a running game, where the decoded seed is the map id in the character's save
file. These offsets come from MapAssist (the unit, act and difficulty layout) and koolo (the
seed hashes) rather than from d2go, which does not read them. MapAssist's 2022 layout has a pointer at `+0x868`, so
the act misc structure has changed between game patches: like every offset here, they need checking against the D2R
build in use. The context is null when the player is not in a game, the difficulty is not 0 to 2, or the hashes do
not give a seed.

## API

### `D2Dotnet.Game`: the snapshot

Immutable records; lookups return `null` when there is nothing; numbers have the width the game stores.

| Type | What it is |
| --- | --- |
| `GameSnapshot` | Everything below, plus `Enemies`, `Mercenary`, `MercenaryLifePercent`, `FindMonster`, `FindObject`, `FindPartyMember`, `GetQuestStatus` |
| `PlayerUnit`, `MainPlayer` | A unit of the player table (players, corpses, shared stash tabs); the controlled player with class, skills (`SkillPoints`), selected skills, waypoints, `Level`, `Experience`, `LifePercent`, `ManaPercent`, `MaxGold`, `TotalGold`, `CastingFrames`, `HasDebuff` |
| `Stat`, `StatList` | A stat (signed 32-bit value, 16-bit layer) and an ordered stat list with `GetValue`/`TryGet`; players, monsters and items all use it, and their `GetStat(StatId, layer)` returns null when the stat is missing |
| `Monster`, `MonsterType`, `Element` | A monster unit (class `MonsterId`, stats as the game stores them (life in 1/256 points), `IsImmune`, `IsMercenary`, `IsPet`, `IsFriendlyNpc`, `IsElite`, `IsMonsterRaiser`, `IsUntargetable`, `IsEnemy`) |
| `GameObject`, `ShrineType`, `ShrineTypes`, `InteractType` | An object unit (class `ObjectId`, shrine kind, `IsWaypoint`, `IsPortal`, `IsChest`, `IsDoor`...) |
| `Inventory`, `Item`, `Belt`, `ItemLocation`, `ItemQuality`, `ItemTier`, `PotionType` | Items with their base item (`ItemInfo` from the game data), location, flags and stats; gold; the belt |
| `KeyBindings`, `KeyBinding`, `Key`, `SkillBinding` | The key binding table |
| `QuestStatus` | A quest's 16 status flags (`[Flags]`), `IsCompleted()` |
| `PartyMember`, `PlayerCorpse`, `HoveredUnit`, `UnitId`, `UnitType`, `CharacterClass` | Smaller parts |
| `OpenMenus`, `Widget`, `OnlineGameInfo` | User interface state |
| `Areas`, `WaypointLocation` | `IsTown()`, `GetAct()`, `CanBeTerrorized()` on `AreaId`; the terror zone areas and the waypoint menu |

### `D2Dotnet.Memory`: the reader

| Type | What it is |
| --- | --- |
| `IProcessMemory` | Process id, game module base and size, `TryRead(address, buffer)` |
| `WindowsProcessMemory` | `[SupportedOSPlatform("windows")]`: `OpenGame()`, `Open(pid)`, `GetModules(pid)` (`LibraryImport`, AOT-safe) |
| `ProcessModuleInfo` | A loaded module |
| `GameProcess` | `Attach()`, `Attach(pid)` (Windows) or `new GameProcess(memory)`; typed reads, `ReadString`, `ReadModule`, `FindPattern` |
| `GameReader` | `new GameReader(process, database)`; `ReadSnapshot()` and the focused reads `ReadMapContext`, `ReadPlayers`, `ReadMainPlayer`, `ReadMonsters`, `ReadCorpses`, `ReadObjects`, `ReadInventory`, `ReadParty`, `ReadQuests`, `ReadKeyBindings`, `ReadTerrorZones`, `ReadMenus`, `ReadHover`, `ReadWidgets`, `ReadOnlineGame`, `ReadSelectedCharacterName`, `IsInGame`, `IsOnline`, `IsLegacyGraphics`, `IsInLobby`, `IsInCharacterSelection`, `IsInCharacterCreation`, `IsWidgetShown`, `IsPlayerInWorld` |
| `GameOffsets` | The offsets found by the pattern scan (`GameOffsets.Find(image)`) |
| `MapSeed` | `Hash`, `Reverse` (closed form: the multiplier's inverse modulo 2^32) and `TryGetMapSeed` |

A `GameReader` keeps nothing but its offsets: it is thread-safe, and every read sees the game as it is at that moment.
It does not own the `GameProcess`; dispose the process to close the handle.

## Go to C#

| d2go | D2Dotnet |
| --- | --- |
| `pkg/memory/process.go` | `Memory/IProcessMemory.cs`, `Memory/GameProcess.cs`, `Memory/WindowsProcessMemory.cs`; the widget containers in `Memory/GameReader.Interface.cs` |
| `pkg/memory/offset.go`, `addresses.go` | `Memory/GameOffsets.cs` |
| `pkg/memory/game_reader.go` | `Memory/GameReader.cs` (snapshot, hover, menus, fixed addresses, stat lists); `Memory/GameReader.Interface.cs` (widgets, screens) |
| `pkg/memory/player.go`, `player_unit.go` | `Memory/GameReader.Players.cs`; model `Game/Players.cs` |
| `pkg/memory/monsters.go`, `object.go` | `Memory/GameReader.Units.cs`; model `Game/Monsters.cs`, `Game/Objects.cs` |
| `pkg/memory/item.go` | `Memory/GameReader.Items.cs`; model `Game/Items.cs` |
| `pkg/memory/roster.go`, `quests.go`, `keybindings.go`, `terrorzones.go` | `Memory/GameReader.Interface.cs`; model `Game/Quests.cs`, `Game/KeyBindings.cs` |
| `pkg/utils/map_seed_hash.go` | `Memory/MapSeed.cs` |
| `pkg/utils/utils.go` (`DistanceFromPoint`) | `Point.DistanceSquaredTo` (sorting) |
| `pkg/data/data.go` | `Game/GameSnapshot.cs`, `Game/Players.cs`, `Game/Interface.cs`, `Game/Units.cs` |
| `pkg/data/npc.go`, `objects.go`, `items.go`, `belt.go`, `keybindings.go` | `Game/Monsters.cs`, `Game/Objects.cs`, `Game/Items.cs`, `Game/KeyBindings.cs` |
| `pkg/data/stat`, `state`, `quest`, `skill`, `object`, `item`, `npc`, `area` ids | the root id enums `StatId`, `StateId`, `QuestId`, `SkillId`, `ObjectId`, `ItemId`, `ItemTypeCode`, `MonsterId`, `AreaId` |
| `pkg/data/area/area.go`, `terrorzone.go`, `waypoint.go` | `Game/Areas.cs` |
| `pkg/data/object/shrine_type.go`, `interact_type.go` | `Game/Objects.cs` |
| `pkg/data/quest/status.go` | `Game/Quests.cs` (`QuestStatus` flags) |
| the generated tables (`cmd/txttocode`: items, item types, levels, skills, skill descriptions, objects) | `D2Dotnet.Data.GameDatabase`, read from the user's install |
| `pkg/data/difficulty`, `item.Names`, `stat.StringStats`, `skill.SkillNames`, `data.Drop`, `data.PointOfInterest` | not carried over: the reader does not use them (difficulty is `D2Dotnet.Difficulty`, names are the enum names) |
| `pkg/nip`, `cmd/`, `memory/game_reader_test.go` (benchmarks) | not carried over |

## Changes from d2go

The memory layout is unchanged. Everything else that behaves differently:

**Values**

- Stat values are the game's 32-bit field read as a **signed** number: a resistance of -10 is -10. d2go kept the
  unsigned field in Go's 64-bit `int`, so it read 4294967286, and `IsImmune` called a monster with a negative
  resistance immune. Unsigned stats (experience) are `Stat.UnsignedValue` and `MainPlayer.Experience`.
- The rescaled stats (life, mana, stamina, per-level stats...) use d2go's formulas, saturated to the `int` range.
- A replenish stat of 0 is 0; d2go divided 2 by it and panicked.
- `LifePercent`, `ManaPercent` and `MercenaryLifePercent` are null when the max is 0 or unknown; d2go divided by zero
  and returned the minimum integer. `MercenaryLifePercent` is also null without a mercenary (d2go: 0).
- Unit ids are `uint`, skill points `ushort`, the frame rate `uint`, gold `int` (the game's widths); d2go used Go's
  64-bit `int`.
- Monster stats are a `StatList` with their layers, like player and item stats, and `Monster.GetStat` returns null for a
  stat the monster does not have: a monster without a fire resistance stat is not one with 0%. d2go keyed them by id
  alone (a stat on two layers kept the last one read) and returned 0 on a miss. `IsImmune` and `IsEnemy` are false
  without the stat, as before.
- A missing player, corpse, hover or quest is `null` (or `QuestStatus.None`), not a zero value: `GameSnapshot.Player`
  is null out of a game, the party has no zero member for a missing main player, `Corpse` is null instead of
  `Found: false`, `Hover` is null instead of `IsHovered: false`.
- An item's tier is null for base items without tier codes and for unknown base items; d2go called them elite (all
  their codes are empty, so the code equals the elite code).
- Skill descriptions are linked by name through `GameDatabase` (`SkillInfo.Description`); d2go looked them up by
  skilldesc.txt row index with a skill id, which gave another skill's row for most skills.
- Enum names come from .NET: `ItemQuality.Crafted` prints `Crafted` (d2go: `UnknownItemQuality`).

**Reading memory**

- The pattern scan tries every position, the first and the last included; d2go returned 0 for "not found", so it
  missed a match at offset 0, and it never tried the last position.
- A pattern that is not found is an `InvalidDataException` naming it; d2go computed meaningless offsets from address 0.
  So d2go's re-scans (when the unit table or panel manager offset was 0) are gone: the offsets are found once.
- `rip`-relative displacements are sign-extended; d2go read them unsigned (the result is the same for the positive
  displacements of the supported build).
- Objects whose class is a shrine (objects.txt `OperateFn` 2) read their unit data byte as the shrine type, every other
  object as its interact type. d2go decided from the 8 bytes at unit `+0x0A`, which overlap the unit id, the mode and
  the unit data pointer: nearly every object read as a shrine and lost its interact type. The shrine name is
  `ShrineType.GetName()`.
- Each item stat (id and layer) is listed once; d2go added a stat listed twice in the `0x40` list twice. A modifier
  list's stat that is already listed keeps the listed value, as in d2go: the full list holds the item's total, a
  modifier list only its own part of it.
- An item in the inventory whose owner matches no shared stash unit is read even when there are fewer than three
  shared stash units; d2go panicked.
- Terror zones are read from the offline address when the Online tab is not selected; d2go always read the online one.
- Widgets are read once per snapshot (d2go: four times). A child widget without a name is skipped; d2go dropped every
  widget.
- Units, objects and items are sorted by exact distance from the player (ties keep the game's table order); d2go
  compared distances truncated to integers, so near units at different distances could tie.
- Strings of a fixed size end at their first NUL; d2go trimmed NULs from both ends and kept those inside. Strings read
  up to a NUL stop after `GameProcess.MaxStringLength` (4096) bytes. A read of 0 bytes is empty; d2go panicked.
- Linked lists (units, skills, party) stop after 16384 entries, and stat lists claiming more than 4096 entries are
  read as empty, so corrupted memory cannot hang the reader or allocate gigabytes. d2go also read 10 bytes per stat
  entry instead of 8.
- The waypoint list skips entries whose area is 0.
- Monsters and corpses come from one pass over the monster table.
- The game module is the module whose file name is `d2r.exe` (ignoring case); d2go took any path containing `d2r.exe`.
  The process and module lists grow as needed; d2go panicked past 1024 modules. When no process has loaded the game,
  `OpenGame` throws `InvalidOperationException` (d2go returned the error of opening process 0). Reading through a
  disposed `WindowsProcessMemory` throws `ObjectDisposedException`.
- `MapSeed.Reverse` computes the seed with the multiplier's inverse modulo 2^32 instead of d2go's search loop (which
  d2go credits to MapAssist): the same seed for every 32-bit hash, and no endless search for a hash of 2^32 or more.

**Model**

- `Inventory.Find` looks an item up by `ItemId` (d2go: by name, with Go's case folding), and `Inventory.InLocation`
  lists each item once (d2go listed an item once per matching location argument).
- `Belt.FindPotion` finds a potion by its item type (`hpot`, `mpot`, `rpot`) and returns the item; d2go matched a
  case-sensitive substring of the item name. The equipped belt is an `ItemId` (`Belt.Equipped`).
- `KeyBindings.FindSkill` skips skill slots without a key; d2go found `AttackSkill` (0) in the first unset slot.
- Monster types are an enum with `Normal`; finding a monster of any type is `FirstOrDefault` without a type filter.
  d2go matched any type only for `MonsterTypeNone`, and its zero value `""` matched nothing.
- Quest status is a `[Flags]` enum of the two bytes (first byte high) instead of a list of bit numbers.
- `OpenMenus.IsAnyPanelOpen` is d2go's `IsMenuOpen`: the loading screen and the automap still do not count, now
  documented as such.
- Widgets are `Widget` records (name, active, visible) instead of property bags.
- Not carried over, because d2go's reader never filled them: `AreaOrigin`, `CollisionGrid`, `NPCs`, `AdjacentLevels`,
  `Rooms` (and `Room`, `Level`), `PointOfInterest`, `Drop`; combine a snapshot with `D2Dotnet.Maps` instead. Also not
  carried over: d2go's Go compatibility (`GoStrings`/`EqualFold`, `ValueArray`, `GoInt`, string-typed enums), the
  generated tables (`GameTables`), the difficulty strings, the item and stat name lists.
- `LastGameName` and `LastGamePassword` read the same addresses, which d2go marks as outdated.

## Tests

`tests/D2Dotnet.Tests/Game` and `tests/D2Dotnet.Tests/Memory` run on any OS. The memory tests build synthetic memory
images in a fake `IProcessMemory` (a sparse 64-bit address space): a module whose code holds the seven patterns, and
the unit tables, units, paths, stat lists, inventories, party list, widgets, quest buffer and key binding blobs laid
out at d2go's offsets, with a hand-made `GameDatabase` (no game data). Against d2go's Go source (downloaded by
`tests/Fixtures.targets` into `.cache/d2go/`), they check the patterns and addresses, every key binding offset, the
ignored monsters, the stat rescaling of every stat id, the state bits, the quest byte ranges, that every hex constant
of each Go file is in the C# that reads it (except the two the redesign drops), and the hand-written lists and id
switches of the model. The changes above are tested wherever they can run off Windows. `WindowsProcessMemory` needs Windows and a running game; off
Windows the tests check that it refuses to run, its `MODULEINFO` layout and the module match.

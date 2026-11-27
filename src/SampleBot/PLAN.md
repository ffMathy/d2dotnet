# A bot that completes the game

The goal: a new character of any class, offline, played from level 1 through every quest of the five acts on Normal,
ending with Baal; the Sorceress first, the other classes after. This file is the plan and its state; it is updated
as parts land.

## What the library already gives

- **Reading the game** (`D2Dotnet.Memory`): the player (area, position, life, mana, level, stats, skills, the
  selected skills, waypoints), monsters with their class, kind and corpses, objects, items with the belt, quests
  with their status flags, key bindings, the open panels and the loading screen, the hovered unit, the map seed.
- **The maps** (`D2Dotnet.Maps`): every level of an act from the seed, with rooms, exits (warps and seams), preset
  objects and monsters (quest objects, bosses, waypoints), collision maps, and `MapGrid` paths in world subtiles.
- **The tables** (`D2Dotnet.Data`): skills with their mana cost, items, monsters, levels and the string tables.

## What the bot needs, in layers

1. **Input** (done, checked in a running game): clicks and keys sent to the game window, so the game can be in the
   background (`BackgroundInput`; the game aims at the real cursor, which is borrowed for about 80 ms per click or
   key), or `SendInput` with the game in front (`ForegroundInput`). Moving, clicking and casting standing still work
   in the background. The player's stats are in whole points (mana 35 for a new Sorceress).
2. **Survival** (done): drink from the belt; pick up potions and gold; with low life (or no mana for a caster) and
   nothing to drink, run back to town and heal at the act's healer; after a death, press Esc to come back in town and
   go back for the corpse.
3. **Travel across levels** (done for exits): `GeneratedAct.FindRoute` and `MapGrid` paths; seams are walked
   across into the next level's first reachable subtiles, warps clicked once the game says they are hovered, red
   portals taken where no exit leads; obstacles the map does not show are blocked and gone round. Waypoints: to do.
4. **Combat by class** (started): the best skill the character has, can pay for and has selected or bound to a key,
   from a table per class (the Sorceress so far); casts only along a clear line (`MapGrid.IsLineClear`).
5. **Town** (started): talking to NPCs heals; their dialog's options are clicked above them (the keyboard does not
   pick them; Akara's Trade is 166 pixels above her feet at a height of 720, other NPCs are measured the first time);
   potions are bought at Akara. To do: repair, sell, identify, the mercenary, the stash.
6. **The character** (started): stat and skill points are spent through the panels by a build per class (the
   Sorceress so far), each click checked through memory. To do: equipment.
7. **Quests**: one script per quest, driven by the quest's status flags, for the 27 quests of the five acts,
   moving to the next act when the act's last quest is done.

## Interface positions

The game's HD layout files (data/global/ui/layouts/*hd.json) give the panels' widgets: a 2160-pixel-high reference,
panels anchored to the window (left panels at (0, 0.397) plus (236, -651), right ones at (1, 0.397) plus (-1394,
-651)), widgets by offsets within them. The skill tree, the stat buttons and the shop's tabs and grid come from
there; every click is checked through memory, and the first one calibrates the scale (kept per window size). The
skill tree's tabs run from skilldesc page 3 to page 1. NPC dialogs float above the NPC and are found by clicking
upwards until the wanted panel opens.

## State of the quests

- Den of Evil: done in a running game.
- Sisters' Burial Grounds, The Search for Cain: written, not finished in a game yet.
- The others: to do.

## Order of work

1. Travel across levels and waypoints, checked with `--plan` against the act's generated map, then in the game.
2. The interface positions, measured together, starting with the ones Act I needs: NPC dialogs, shops, waypoints,
   quit menu, skill tree, stat points.
3. Town routines and the character's progression, for a Sorceress.
4. Act I quests: Den of Evil (done), Sisters' Burial Grounds (Blood Raven), The Search for Cain (the Inifuss scroll,
   Akara, the Cairn Stones, Tristram, Cain), The Forgotten Tower (the Countess), Tools of the Trade (the Horadric
   Malus, Charsi), Sisters to the Slaughter (Andariel), then Warriv to Act II.
5. Acts II to V, one quest at a time, each tested in the game before the next.

## Risks

- Every untested address (online flag, terror zones, frame rate, legacy graphics) and every interface position can
  be wrong on another build or window size; the bot checks what it can and stops when a read does not make sense.
- Quests with puzzles or several stages (the Horadric Staff and the Cube, Khalim's Will, the Hellforge, the
  Ancients) need their own handling; boss fights need the character to be strong enough, which depends on the
  build and the items found.
- The game must stay offline: the bot refuses to run online.

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
2. **Survival** (done for potions): drink from the belt; leave the game (save and exit) when life is low and
   nothing can be drunk, instead of dying.
3. **Travel across levels**: a route from the current area to any area of the act through exits (walk across
   seams, click warps) and waypoints; take the waypoint when the route through it is shorter.
4. **Combat by class**: choose skills by what the character has and can pay for, from a table per class (the
   Sorceress first: a bolt early, then the skills it learns; the other classes after); keep distance for casters;
   target the dangerous monsters first; follow the boss fights.
5. **Town**: walk to an NPC, talk, pick a dialog option; heal, buy potions and scrolls, repair, identify, sell,
   revive the mercenary; use the stash.
6. **The character**: spend stat and skill points by a build table per class; equip better items with simple rules.
7. **Quests**: one script per quest, driven by the quest's status flags, for the 27 quests of the five acts,
   moving to the next act when the act's last quest is done.

## Interface positions

The game does not tell where its panels' buttons are (widgets have names and a shown flag, no position), so every
click on a panel needs a position measured in the game window: NPC dialog options, shop tabs and items, the
waypoint panel's tabs and entries, the skill tree and its tabs, the character panel's stat buttons, inventory and
stash grids, the Horadric Cube and its Transmute button, the quit menu. They are measured once per window size, in
the same way as the world mapping (720 pixels of client height as the reference), with the player at the keyboard.

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

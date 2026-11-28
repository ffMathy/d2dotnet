// Memory layout from d2go pkg/memory/monsters.go and object.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Buffers.Binary;
using System.Collections.Frozen;
using D2Dotnet.Game;

namespace D2Dotnet.Memory;

public sealed partial class GameReader
{
    // Unit tables, after the player table (module-relative offsets from the player table).
    internal const nuint MonsterTable = 1024;
    internal const nuint ObjectTable = 2 * 1024;
    internal const nuint ItemTable = 4 * 1024;
    internal const nuint TileTable = 5 * 1024;

    // A monster unit's mode (+0x0C) while it dies and once it is dead.
    internal const uint MonsterModeDeath = 0;
    internal const uint MonsterModeDead = 12;

    // objects.txt OperateFn of shrines.
    internal const int ShrineOperateFn = 2;

    // Critters, town folk, traps, summons and other units that are not enemies (d2go shouldBeIgnored's list).
    internal static readonly FrozenSet<MonsterId> IgnoredMonsters = new[]
    {
        MonsterId.Chicken, MonsterId.Rat, MonsterId.Rogue, MonsterId.HellMeteor, MonsterId.Bird, MonsterId.Bird2, MonsterId.Bat,
        MonsterId.Act2Male, MonsterId.Act2Female, MonsterId.Act2Child, MonsterId.Cow, MonsterId.Camel, MonsterId.Act2Guard,
        MonsterId.Act2Vendor, MonsterId.Act2Vendor2, MonsterId.Maggot, MonsterId.Bug, MonsterId.Scorpion, MonsterId.Rogue2,
        MonsterId.Rogue3, MonsterId.Larva, MonsterId.Familiar, MonsterId.Act3Male, MonsterId.Act3Female, MonsterId.Snake,
        MonsterId.Parrot, MonsterId.Fish, MonsterId.EvilHole, MonsterId.EvilHole2, MonsterId.EvilHole3, MonsterId.EvilHole4,
        MonsterId.EvilHole5, MonsterId.FireboltTrap, MonsterId.HorzMissileTrap, MonsterId.VertMissileTrap,
        MonsterId.PoisonCloudTrap, MonsterId.LightningTrap, MonsterId.InvisoSpawner, MonsterId.Guard, MonsterId.MiniSper,
        MonsterId.BoneWall, MonsterId.Hydra, MonsterId.Hydra2, MonsterId.Hydra3, MonsterId.SevenTombs, MonsterId.IronWolf,
        MonsterId.CompellingOrbNpc, MonsterId.SpiritMummy, MonsterId.Act2Guard4, MonsterId.Act2Guard5, MonsterId.Window,
        MonsterId.Window2, MonsterId.MephistoSpirit, MonsterId.WakeOfDestruction, MonsterId.ChargedBoltSentry,
        MonsterId.LightningSentry, MonsterId.InvisiblePet, MonsterId.InfernoSentry, MonsterId.DeathSentry, MonsterId.BaalThrone,
        MonsterId.InjuredBarbarian, MonsterId.InjuredBarbarian2, MonsterId.InjuredBarbarian3, MonsterId.DemonHole,
    }.ToFrozenSet();

    /// <summary>
    /// The living monsters, NPCs, mercenaries and summons of the monster unit table, nearest to the player first.
    /// Critters, town folk, traps and similar units are left out unless they give experience.
    /// </summary>
    public IReadOnlyList<Monster> ReadMonsters() => ReadMonsterTable(PlayerPosition(), ReadHover()).Living;

    /// <summary>The dead monsters of the monster unit table, nearest to the player first, with the same exclusions as <see cref="ReadMonsters"/>.</summary>
    public IReadOnlyList<Monster> ReadCorpses() => ReadMonsterTable(PlayerPosition(), ReadHover()).Dead;

    /// <summary>The objects (unit type 2) of the object unit table, nearest to the player first.</summary>
    public IReadOnlyList<GameObject> ReadObjects() => ReadObjects(PlayerPosition(), ReadHover());

    /// <summary>
    /// The level warps near the player (stairs, ladders, cave mouths: the game's tile units), nearest first: where each
    /// is and the <c>lvlwarp</c> row it is (its class). Only those of the rooms the game has loaded round the player.
    /// </summary>
    public IReadOnlyList<LevelWarp> ReadWarps()
    {
        var origin = PlayerPosition();
        var warps = new List<LevelWarp>();
        foreach (var unit in WalkUnitTable(unchecked(Offsets.UnitTable + TileTable)))
        {
            unchecked
            {
                if (Process.ReadUInt32(unit + 0x00) != (uint)UnitType.Tile)
                    continue;
                var path = Process.ReadPointer(unit + 0x38);
                warps.Add(new LevelWarp(
                    new UnitId(Process.ReadUInt32(unit + 0x08)),
                    (int)Process.ReadUInt32(unit + 0x04),
                    new Point(Process.ReadUInt16(path + 0x10), Process.ReadUInt16(path + 0x14))));
            }
        }

        return NearestFirst(warps, w => w.Position, origin);
    }

    private Point PlayerPosition() => ReadPlayers(null).FirstOrDefault(p => p.IsMainPlayer)?.Position ?? default;

    internal (IReadOnlyList<Monster> Living, IReadOnlyList<Monster> Dead) ReadMonsterTable(Point origin, HoveredUnit? hover)
    {
        var living = new List<Monster>();
        var dead = new List<Monster>();
        foreach (var unit in WalkUnitTable(unchecked(Offsets.UnitTable + MonsterTable)))
        {
            unchecked
            {
                var data = Process.ReadBytes(unit, 144).AsSpan();
                var monsterClass = (MonsterId)BinaryPrimitives.ReadUInt32LittleEndian(data[0x04..]);
                var id = new UnitId(BinaryPrimitives.ReadUInt32LittleEndian(data[0x08..]));
                var unitData = (nuint)BinaryPrimitives.ReadUInt64LittleEndian(data[0x10..]);
                var typeFlag = Process.ReadByte(unitData + 0x1A);
                // The unique modifiers (monumod.txt ids, 0 ending the list) after the type flag, the last animation
                // mode and a word: +0x20 with D2R's 8-byte stats pointer (classic +0x1C). To be checked against known
                // super uniques (Bishibosh: 8 magic resistant, 9 fire enchanted).
                var modBytes = Process.ReadBytes(unitData + 0x20, 10);
                var mods = modBytes.TakeWhile(b => b != 0).Select(b => (int)b).ToArray();
                // Dying (mode 0) or dead (12). d2go read a flag at +0x1A6, which Reign of the Warlock no longer sets for
                // dead monsters (observed in a running game: every corpse in mode 12, the byte 0).
                var isCorpse = BinaryPrimitives.ReadUInt32LittleEndian(data[0x0C..]) is MonsterModeDeath or MonsterModeDead;

                var path = Process.ReadPointer(unit + 0x38);
                var position = new Point(Process.ReadUInt16(path + 0x02), Process.ReadUInt16(path + 0x06));

                var statsEx = (nuint)BinaryPrimitives.ReadUInt64LittleEndian(data[0x88..]);
                var stats = ReadMonsterStats(Process.ReadPointer(statsEx + 0x30), Process.ReadUInt64(statsEx + 0x38));

                // This excludes good NPCs but includes mercenaries.
                if (IgnoredMonsters.Contains(monsterClass) && (stats.GetValue(StatId.Experience) ?? 0) <= 0)
                    continue;

                (isCorpse ? dead : living).Add(new Monster
                {
                    Id = id,
                    Class = monsterClass,
                    Type = MonsterTypeOf(typeFlag),
                    Mods = mods,
                    Position = position,
                    IsHovered = hover is { } h && h.Is(UnitType.Monster, id),
                    Stats = stats,
                });
            }
        }

        return (NearestFirst(living, m => m.Position, origin), NearestFirst(dead, m => m.Position, origin));
    }

    // The unit data's type flag.
    internal static MonsterType MonsterTypeOf(byte typeFlag) => typeFlag switch
    {
        10 => MonsterType.SuperUnique,
        1 << 2 or 12 => MonsterType.Champion,
        1 << 3 => MonsterType.Unique,
        1 << 4 => MonsterType.Minion,
        _ => MonsterType.Normal,
    };

    // count 8-byte entries (layer u16, id u16, value u32) from `list`; d2go read them from list + 0x2 as (id u16, value
    // u32), dropping the layer. Values are signed and not rescaled; a later entry for the same stat (id and layer)
    // replaces the value of an earlier one.
    internal StatList ReadMonsterStats(nuint list, ulong count)
    {
        if (count == 0 || count > MaxStats)
            return StatList.Empty;

        var buffer = Process.ReadBytes(list, (int)count * 8).AsSpan();
        var stats = new List<Stat>((int)count);
        for (var i = 0; i < (int)count; i++)
        {
            var entry = buffer[(i * 8)..];
            var stat = new Stat(
                (StatId)BinaryPrimitives.ReadInt16LittleEndian(entry[0x2..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[0x4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(entry));
            var existing = stats.FindIndex(s => s.Id == stat.Id && s.Layer == stat.Layer);
            if (existing < 0)
                stats.Add(stat);
            else
                stats[existing] = stat;
        }

        return new StatList(stats);
    }

    internal IReadOnlyList<GameObject> ReadObjects(Point origin, HoveredUnit? hover)
    {
        var objects = new List<GameObject>();
        foreach (var unit in WalkUnitTable(unchecked(Offsets.UnitTable + ObjectTable)))
        {
            unchecked
            {
                if (Process.ReadUInt32(unit + 0x00) != (uint)UnitType.Object)
                    continue;

                var objectClass = (ObjectId)Process.ReadUInt32(unit + 0x04);
                var id = new UnitId(Process.ReadUInt32(unit + 0x08));
                var mode = Process.ReadUInt32(unit + 0x0c);
                var path = Process.ReadPointer(unit + 0x38);
                var position = new Point(Process.ReadUInt16(path + 0x10), Process.ReadUInt16(path + 0x14));

                // The same unit data byte is the shrine type of a shrine and the interact type of any other object.
                var unitData = Process.ReadPointer(unit + 0x10);
                var typeByte = Process.ReadByte(unitData + 0x08);
                var isShrine = Database.Objects.Find(objectClass)?.OperateFn == ShrineOperateFn;

                objects.Add(new GameObject
                {
                    Id = id,
                    Class = objectClass,
                    Position = position,
                    IsHovered = hover is { } h && h.Is(UnitType.Object, id),
                    IsSelectable = mode == 0,
                    InteractType = isShrine ? InteractType.None : (InteractType)typeByte,
                    Shrine = isShrine ? (ShrineType)typeByte : null,
                    Owner = Process.ReadString(unitData + 0x34, 32),
                });
            }
        }

        return NearestFirst(objects, o => o.Position, origin);
    }
}

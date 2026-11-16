// Port of d2go pkg/memory/monsters.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Collections.Frozen;
using D2Dotnet.D2Go.Data;
using D2Dotnet.D2Go.Data.Npc;
using D2Dotnet.D2Go.Data.Stat;
using DataMonsters = D2Dotnet.D2Go.Data.Monsters;

namespace D2Dotnet.D2Go.Memory;

public sealed partial class GameReader
{
    // Go shouldBeIgnored's list, in its order.
    private static readonly FrozenSet<NpcId> IgnoredNpcs = new[]
    {
        NpcId.Chicken, NpcId.Rat, NpcId.Rogue, NpcId.HellMeteor, NpcId.Bird, NpcId.Bird2, NpcId.Bat, NpcId.Act2Male,
        NpcId.Act2Female, NpcId.Act2Child, NpcId.Cow, NpcId.Camel, NpcId.Act2Guard, NpcId.Act2Vendor, NpcId.Act2Vendor2,
        NpcId.Maggot, NpcId.Bug, NpcId.Scorpion, NpcId.Rogue2, NpcId.Rogue3, NpcId.Larva, NpcId.Familiar, NpcId.Act3Male,
        NpcId.Act3Female, NpcId.Snake, NpcId.Parrot, NpcId.Fish, NpcId.EvilHole, NpcId.EvilHole2, NpcId.EvilHole3,
        NpcId.EvilHole4, NpcId.EvilHole5, NpcId.FireboltTrap, NpcId.HorzMissileTrap, NpcId.VertMissileTrap,
        NpcId.PoisonCloudTrap, NpcId.LightningTrap, NpcId.InvisoSpawner, NpcId.Guard, NpcId.MiniSper, NpcId.BoneWall,
        NpcId.Hydra, NpcId.Hydra2, NpcId.Hydra3, NpcId.SevenTombs, NpcId.IronWolf, NpcId.CompellingOrbNpc,
        NpcId.SpiritMummy, NpcId.Act2Guard4, NpcId.Act2Guard5, NpcId.Window, NpcId.Window2, NpcId.MephistoSpirit,
        NpcId.WakeOfDestruction, NpcId.ChargedBoltSentry, NpcId.LightningSentry, NpcId.InvisiblePet, NpcId.InfernoSentry,
        NpcId.DeathSentry, NpcId.BaalThrone, NpcId.InjuredBarbarian, NpcId.InjuredBarbarian2, NpcId.InjuredBarbarian3,
        NpcId.DemonHole,
    }.ToFrozenSet();

    /// <summary>
    /// The living monsters (and mercenaries) of the monster unit table, nearest to <paramref name="playerPosition"/>
    /// first (Go <c>Monsters</c>). Good NPCs, critters, traps and the like are left out unless they give experience.
    /// </summary>
    public DataMonsters Monsters(Position playerPosition, HoverData hover)
    {
        var baseAddr = unchecked(ModuleBaseAddress + _offset.UnitTable + 1024);
        var unitTableBuffer = ReadBytesFromMemory(baseAddr, UnitTableBuckets * 8);

        var monsters = new List<Monster>();
        for (var i = 0; i < UnitTableBuckets; i++)
        {
            var monsterOffset = 8 * i;
            var monsterUnitPtr = (nuint)ReadUIntFromBuffer(unitTableBuffer, (ulong)monsterOffset, IntType.Uint64);
            while (monsterUnitPtr > 0)
            {
                unchecked
                {
                    var monsterDataBuffer = ReadBytesFromMemory(monsterUnitPtr, 144);

                    var txtFileNo = ReadUIntFromBuffer(monsterDataBuffer, 0x04, IntType.Uint32);
                    var unitId = ReadUIntFromBuffer(monsterDataBuffer, 0x08, IntType.Uint32);

                    var unitDataPtr = (nuint)ReadUIntFromBuffer(monsterDataBuffer, 0x10, IntType.Uint64);
                    var flag = ReadBytesFromMemory(unitDataPtr + 0x1A, (ulong)IntType.Uint8)[0];
                    var isCorpse = ReadUInt(monsterUnitPtr + 0x1A6, IntType.Uint8);

                    // Coordinates (X, Y)
                    var pathPtr = (nuint)ReadUInt(monsterUnitPtr + 0x38, IntType.Uint64);
                    var posX = ReadUInt(pathPtr + 0x02, IntType.Uint16);
                    var posY = ReadUInt(pathPtr + 0x06, IntType.Uint16);

                    var id = new UnitId(GoInt.ToInt(unitId));
                    var hovered = hover.IsHovered && hover.UnitType == 1 && hover.UnitId == id;

                    var statsListExPtr = (nuint)ReadUIntFromBuffer(monsterDataBuffer, 0x88, IntType.Uint64);
                    var statPtr = (nuint)ReadUInt(statsListExPtr + 0x30, IntType.Uint64);
                    var statCount = ReadUInt(statsListExPtr + 0x38, IntType.Uint64);

                    var stats = GetMonsterStats(statCount, statPtr);

                    // This excludes good NPCs but includes Mercs
                    if (!ShouldBeIgnored(txtFileNo) || stats.GetValueOrDefault(StatId.Experience) > 0)
                    {
                        var m = new Monster
                        {
                            UnitId = id,
                            Name = (NpcId)GoInt.Narrow(txtFileNo),
                            IsHovered = hovered,
                            Position = new Position(GoInt.Narrow(posX), GoInt.Narrow(posY)),
                            Stats = stats,
                            Type = GetMonsterType(flag),
                        };

                        if (isCorpse == 0)
                            monsters.Add(m);
                    }

                    monsterUnitPtr = (nuint)ReadUInt(monsterUnitPtr + 0x150, IntType.Uint64);
                }
            }
        }

        return new DataMonsters(SortByDistance(monsters, m => m.Position, playerPosition));
    }

    // Go getMonsterType: the unit data's type flag.
    internal static MonsterType GetMonsterType(byte typeFlag) => typeFlag switch
    {
        10 => MonsterType.SuperUnique,
        1 << 2 or 12 => MonsterType.Champion,
        1 << 3 => MonsterType.Unique,
        1 << 4 => MonsterType.Minion,
        _ => MonsterType.None,
    };

    // Go getMonsterStats: statCount 8-byte entries (id u16, value u32) from statPtr + 2; a later entry for the same
    // stat replaces an earlier one.
    internal IReadOnlyDictionary<StatId, long> GetMonsterStats(ulong statCount, nuint statPtr)
    {
        var stats = new Dictionary<StatId, long>();

        if (statCount > 0)
        {
            var statBuffer = ReadBytesFromMemory(unchecked(statPtr + 0x2), unchecked(statCount * 8));
            for (long i = 0; i < unchecked((long)statCount); i++)
            {
                var offset = (ulong)(i * 8);
                var statEnum = ReadUIntFromBuffer(statBuffer, offset, IntType.Uint16);
                var statValue = ReadUIntFromBuffer(statBuffer, offset + 0x2, IntType.Uint32);
                stats[(StatId)unchecked((short)statEnum)] = GoInt.ToInt(statValue);
            }
        }

        return stats;
    }

    /// <summary>
    /// The dead monsters of the monster unit table, nearest to <paramref name="playerPosition"/> first (Go
    /// <c>Corpses</c>), with the same exclusions as <see cref="Monsters"/>.
    /// </summary>
    public DataMonsters Corpses(Position playerPosition, HoverData hover)
    {
        var baseAddr = unchecked(ModuleBaseAddress + _offset.UnitTable + 1024);
        var unitTableBuffer = ReadBytesFromMemory(baseAddr, UnitTableBuckets * 8);

        var corpses = new List<Monster>();

        for (var i = 0; i < UnitTableBuckets; i++)
        {
            var monsterOffset = 8 * i;
            var monsterUnitPtr = (nuint)ReadUIntFromBuffer(unitTableBuffer, (ulong)monsterOffset, IntType.Uint64);
            while (monsterUnitPtr > 0)
            {
                unchecked
                {
                    var monsterDataBuffer = ReadBytesFromMemory(monsterUnitPtr, 144);

                    var txtFileNo = ReadUIntFromBuffer(monsterDataBuffer, 0x04, IntType.Uint32);
                    var unitId = ReadUIntFromBuffer(monsterDataBuffer, 0x08, IntType.Uint32);

                    var unitDataPtr = (nuint)ReadUIntFromBuffer(monsterDataBuffer, 0x10, IntType.Uint64);
                    var flag = ReadBytesFromMemory(unitDataPtr + 0x1A, (ulong)IntType.Uint8)[0];
                    var isCorpse = ReadUInt(monsterUnitPtr + 0x1A6, IntType.Uint8);

                    if (isCorpse == 0)
                    {
                        monsterUnitPtr = (nuint)ReadUInt(monsterUnitPtr + 0x150, IntType.Uint64);
                        continue;
                    }

                    var pathPtr = (nuint)ReadUInt(monsterUnitPtr + 0x38, IntType.Uint64);
                    var posX = ReadUInt(pathPtr + 0x02, IntType.Uint16);
                    var posY = ReadUInt(pathPtr + 0x06, IntType.Uint16);

                    var statsListExPtr = (nuint)ReadUIntFromBuffer(monsterDataBuffer, 0x88, IntType.Uint64);
                    var statPtr = (nuint)ReadUInt(statsListExPtr + 0x30, IntType.Uint64);
                    var statCount = ReadUInt(statsListExPtr + 0x38, IntType.Uint64);

                    var stats = GetMonsterStats(statCount, statPtr);

                    var id = new UnitId(GoInt.ToInt(unitId));
                    var hovered = hover.IsHovered && hover.UnitType == 1 && hover.UnitId == id;

                    if ((!ShouldBeIgnored(txtFileNo) || stats.GetValueOrDefault(StatId.Experience) > 0) && isCorpse != 0)
                    {
                        corpses.Add(new Monster
                        {
                            UnitId = id,
                            Name = (NpcId)GoInt.Narrow(txtFileNo),
                            IsHovered = hovered,
                            Position = new Position(GoInt.Narrow(posX), GoInt.Narrow(posY)),
                            Stats = stats,
                            Type = GetMonsterType(flag),
                        });
                    }

                    monsterUnitPtr = (nuint)ReadUInt(monsterUnitPtr + 0x150, IntType.Uint64);
                }
            }
        }

        return new DataMonsters(SortByDistance(corpses, m => m.Position, playerPosition));
    }

    // Go shouldBeIgnored: critters, town folk, traps, summons and other units that are not enemies.
    internal static bool ShouldBeIgnored(ulong txtNo) => IgnoredNpcs.Contains((NpcId)GoInt.Narrow(txtNo));

    // Go's sort.SliceStable by utils.DistanceFromPoint from the player, ascending.
    private static IEnumerable<T> SortByDistance<T>(IEnumerable<T> items, Func<T, Position> position, Position from) =>
        items.OrderBy(i => Geometry.DistanceFromPoint(from, position(i)));
}

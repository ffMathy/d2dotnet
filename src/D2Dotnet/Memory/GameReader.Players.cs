// Memory layout from d2go pkg/memory/player.go and player_unit.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Buffers.Binary;
using D2Dotnet.Game;

namespace D2Dotnet.Memory;

public sealed partial class GameReader
{
    // player.go, relative to the module base: the waypoint list of the waypoint menu.
    internal const nuint WaypointsAddress = 0x1D705F0; // d2go 0x2140E20 before Reign of the Warlock

    /// <summary>
    /// Every player unit: players, the player's corpse and the shared stash units (the 128 lists of the player unit
    /// table, each linked at <c>+0x150</c>).
    /// </summary>
    public IReadOnlyList<PlayerUnit> ReadPlayers() => ReadPlayers(ReadHover());

    /// <summary>The player this game client controls, or null when not in a game.</summary>
    public MainPlayer? ReadMainPlayer() =>
        ReadPlayers(ReadHover()).FirstOrDefault(p => p.IsMainPlayer) is { } unit ? ReadMainPlayer(unit) : null;

    internal IReadOnlyList<PlayerUnit> ReadPlayers(HoveredUnit? hover)
    {
        var players = new List<PlayerUnit>();
        // Without the expansion offset, assume an expansion character (the D2R default).
        var isExpansion = Offsets.Expansion is not { } expansion
            || Process.ReadUInt16(unchecked(Process.ReadPointer(Module(expansion)) + 0x5C)) > 0;

        foreach (var unit in WalkUnitTable(Offsets.UnitTable))
        {
            unchecked
            {
                var id = new UnitId(Process.ReadUInt32(unit + 0x08));
                var inventory = Process.ReadPointer(unit + 0x90);

                var path = Process.ReadPointer(unit + 0x38);
                var room1 = Process.ReadPointer(path + 0x20);
                var room2 = Process.ReadPointer(room1 + 0x18);
                var level = Process.ReadPointer(room2 + 0x90);
                var area = (AreaId)Process.ReadUInt32(level + 0x1F8);
                var x = Process.ReadUInt16(path + 0x02);
                var y = Process.ReadUInt16(path + 0x06);

                var name = Process.ReadString(Process.ReadPointer(unit + 0x10));
                var isMain = Process.ReadUInt16(inventory + (isExpansion ? (nuint)0x70 : 0x30)) > 0;
                // The corpse flag: +0x1A5 since Reign of the Warlock (d2go +0x1A6), observed on a corpse in town.
                var corpseFlag = Process.ReadByte(unit + 0x1A5);

                var statsEx = Process.ReadPointer(unit + 0x88);
                players.Add(new PlayerUnit
                {
                    Address = unit,
                    Id = id,
                    Name = name,
                    Area = area,
                    Position = new Point(x, y),
                    IsMainPlayer = isMain,
                    IsCorpse = corpseFlag == 1 && inventory > 0 && x > 0 && y > 0,
                    IsHovered = hover is { } h && h.Is(UnitType.Player, id),
                    BaseStats = ReadStatList(statsEx + 0x30),
                    Stats = ReadStatList(statsEx + 0x88),
                    States = ReadStates(statsEx),
                });
            }
        }

        return players;
    }

    internal MainPlayer ReadMainPlayer(PlayerUnit unit)
    {
        unchecked
        {
            var skillList = Process.ReadPointer(unit.Address + 0x100);
            var left = Process.ReadPointer(Process.ReadPointer(skillList + 0x08));
            var right = Process.ReadPointer(Process.ReadPointer(skillList + 0x10));

            // Probably tied to the UI: only filled while the waypoint menu is open. Nine (area, active) pairs.
            // Reign of the Warlock: with the menu open in Act 1 it holds the act's nine waypoint areas (1, 3, 4, 5, 6, 27,
            // 29, 32, 35) with every second word 0, even for activated ones: the activation is kept elsewhere, so only
            // the current area counts here.
            var waypoints = new List<AreaId>();
            var list = Process.ReadBytes(Module(WaypointsAddress), 0x48).AsSpan();
            for (var i = 0; i < 0x48; i += 8)
            {
                var area = (AreaId)BinaryPrimitives.ReadUInt32LittleEndian(list[i..]);
                var active = BinaryPrimitives.ReadUInt32LittleEndian(list[(i + 4)..]);
                if (area != AreaId.None && (active == 1 || area == unit.Area))
                    waypoints.Add(area);
            }

            return new MainPlayer
            {
                Address = unit.Address,
                Id = unit.Id,
                Name = unit.Name,
                Area = unit.Area,
                Position = unit.Position,
                IsMainPlayer = unit.IsMainPlayer,
                IsCorpse = unit.IsCorpse,
                IsHovered = unit.IsHovered,
                Stats = unit.Stats,
                BaseStats = unit.BaseStats,
                States = unit.States,
                // The unit's class id (+0x04), as for objects; d2go's +0x174 reads 0 since Reign of the Warlock.
                Class = (CharacterClass)Process.ReadUInt32(unit.Address + 0x04),
                Skills = ReadSkills(skillList),
                LeftSkill = (SkillId)Process.ReadUInt16(left),
                RightSkill = (SkillId)Process.ReadUInt16(right),
                AvailableWaypoints = waypoints,
            };
        }
    }

    // The skill list linked at +0x08 from the list's first skill; a later entry for the same skill replaces an earlier one.
    internal IReadOnlyDictionary<SkillId, SkillPoints> ReadSkills(nuint skillList)
    {
        var skills = new Dictionary<SkillId, SkillPoints>();
        var skill = Process.ReadPointer(skillList);
        for (var n = 0; skill != 0 && n < MaxListLength; n++)
        {
            unchecked
            {
                var id = (SkillId)Process.ReadUInt16(Process.ReadPointer(skill));
                skills[id] = new SkillPoints(
                    Level: Process.ReadUInt16(skill + 0x38),
                    Quantity: Process.ReadUInt16(skill + 0x40),
                    Charges: Process.ReadUInt16(skill + 0x48));
                skill = Process.ReadPointer(skill + 0x08);
            }
        }

        return Freeze(skills);
    }

    // Six 32-bit flag words at +0xAD0: bit b of word i is state 32 * i + b.
    internal IReadOnlyList<StateId> ReadStates(nuint statsEx)
    {
        var words = Process.ReadBytes(unchecked(statsEx + 0xAD0), 6 * 4).AsSpan();
        var states = new List<StateId>();
        for (var i = 0; i < 6; i++)
            states.AddRange(StatesOfWord(BinaryPrimitives.ReadUInt32LittleEndian(words[(i * 4)..]), i));
        return states;
    }

    // The states of flag word `word`, lowest bit first.
    internal static IEnumerable<StateId> StatesOfWord(uint flags, int word)
    {
        for (var bit = 0; bit < 32; bit++)
        {
            if ((flags & (1u << bit)) != 0)
                yield return (StateId)(uint)((32 * word) + bit);
        }
    }

    /// <summary>
    /// The units of one unit type's table: <paramref name="tableOffset"/> (module-relative) holds 128 list heads; each
    /// list is linked at <c>+0x150</c>.
    /// </summary>
    internal IEnumerable<nuint> WalkUnitTable(nuint tableOffset)
    {
        var heads = Process.ReadBytes(Module(tableOffset), UnitTableSize);
        for (var i = 0; i < UnitTableBuckets; i++)
        {
            var unit = (nuint)BinaryPrimitives.ReadUInt64LittleEndian(heads.AsSpan(i * 8));
            for (var n = 0; unit != 0 && n < MaxListLength; n++)
            {
                yield return unit;
                unit = Process.ReadPointer(unchecked(unit + 0x150));
            }
        }
    }
}

// Port of d2go pkg/memory/player.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Buffers.Binary;
using D2Dotnet.D2Go.Data;
using D2Dotnet.D2Go.Data.Area;
using D2Dotnet.D2Go.Data.Skill;
using D2Dotnet.D2Go.Data.State;

namespace D2Dotnet.D2Go.Memory;

public sealed partial class GameReader
{
    // player.go, relative to the module base: the waypoint list of the waypoint menu.
    internal const nuint WaypointsAddress = 0x2140E20;

    private const int UnitTableBuckets = 128;

    /// <summary>
    /// Every player unit: the 128 hash buckets of the player unit table, each a list linked at <c>+0x150</c> (Go
    /// <c>GetRawPlayerUnits</c>).
    /// </summary>
    public RawPlayerUnits GetRawPlayerUnits()
    {
        var rawPlayerUnits = new List<RawPlayerUnit>();
        var hover = HoveredData();
        for (var i = 0; i < UnitTableBuckets; i++)
        {
            var unitOffset = unchecked(_offset.UnitTable + (nuint)(i * 8));
            var playerUnitAddr = unchecked(ModuleBaseAddress + unitOffset);
            var playerUnit = (nuint)ReadUInt(playerUnitAddr, IntType.Uint64);
            while (playerUnit > 0)
            {
                unchecked
                {
                    var unitId = ReadUInt(playerUnit + 0x08, IntType.Uint32);
                    var pInventory = playerUnit + 0x90;
                    var inventoryAddr = (nuint)ReadUInt(pInventory, IntType.Uint64);

                    var pPath = playerUnit + 0x38;
                    var pathAddress = (nuint)ReadUInt(pPath, IntType.Uint64);
                    var room1Ptr = (nuint)ReadUInt(pathAddress + 0x20, IntType.Uint64);
                    var room2Ptr = (nuint)ReadUInt(room1Ptr + 0x18, IntType.Uint64);
                    var levelPtr = (nuint)ReadUInt(room2Ptr + 0x90, IntType.Uint64);
                    var levelNo = ReadUInt(levelPtr + 0x1F8, IntType.Uint32);

                    var xPos = ReadUInt(pathAddress + 0x02, IntType.Uint16);
                    var yPos = ReadUInt(pathAddress + 0x06, IntType.Uint16);
                    var pUnitData = playerUnit + 0x10;
                    var playerNameAddr = (nuint)ReadUInt(pUnitData, IntType.Uint64);
                    var name = ReadStringFromMemory(playerNameAddr, 0);

                    var expCharPtr = (nuint)ReadUInt(ModuleBaseAddress + _offset.Expansion, IntType.Uint64);
                    var expChar = ReadUInt(expCharPtr + 0x5C, IntType.Uint16);
                    var isMainPlayer = ReadUInt(inventoryAddr + 0x30, IntType.Uint16);
                    if (expChar > 0)
                        isMainPlayer = ReadUInt(inventoryAddr + 0x70, IntType.Uint16);
                    var isCorpse = ReadUInt(playerUnit + 0x1A6, IntType.Uint8);

                    var statsListExPtr = (nuint)ReadUInt(playerUnit + 0x88, IntType.Uint64);
                    var baseStats = GetStatsList(statsListExPtr + 0x30);
                    var stats = GetStatsList(statsListExPtr + 0x88);
                    var states = GetStates(statsListExPtr);

                    var id = new UnitId(GoInt.ToInt(unitId));
                    rawPlayerUnits.Add(new RawPlayerUnit
                    {
                        UnitId = id,
                        Address = playerUnit,
                        Name = name,
                        IsMainPlayer = isMainPlayer > 0,
                        IsCorpse = isCorpse == 1 && inventoryAddr > 0 && xPos > 0 && yPos > 0,
                        Area = (AreaId)GoInt.Narrow(levelNo),
                        Position = new Position(GoInt.Narrow(xPos), GoInt.Narrow(yPos)),
                        IsHovered = hover.IsHovered && hover.UnitId == id && hover.UnitType == 0,
                        States = states,
                        Stats = stats,
                        BaseStats = baseStats,
                    });
                    playerUnit = (nuint)ReadUInt(playerUnit + 0x150, IntType.Uint64);
                }
            }
        }

        return new RawPlayerUnits(rawPlayerUnits);
    }

    /// <summary>
    /// The main player's character: skills, selected skills, class and, while the waypoint menu is open, the
    /// waypoints of its tab (Go <c>GetPlayerUnit</c>).
    /// </summary>
    public PlayerUnit GetPlayerUnit(RawPlayerUnit mainPlayerUnit)
    {
        ArgumentNullException.ThrowIfNull(mainPlayerUnit);
        unchecked
        {
            // Skills
            var skillListPtr = (nuint)ReadUInt(mainPlayerUnit.Address + 0x100, IntType.Uint64);
            var skills = GetSkills(skillListPtr);

            var leftSkillPtr = ReadUInt(skillListPtr + 0x08, IntType.Uint64);
            var leftSkillTxtPtr = (nuint)ReadUInt((nuint)leftSkillPtr, IntType.Uint64);
            var leftSkillId = ReadUInt(leftSkillTxtPtr, IntType.Uint16);

            var rightSkillPtr = ReadUInt(skillListPtr + 0x10, IntType.Uint64);
            var rightSkillTxtPtr = (nuint)ReadUInt((nuint)rightSkillPtr, IntType.Uint64);
            var rightSkillId = ReadUInt(rightSkillTxtPtr, IntType.Uint16);

            // Class
            var playerClass = (Class)(uint)ReadUInt(mainPlayerUnit.Address + 0x174, IntType.Uint32);

            var availableWPs = new List<AreaId>();
            // Probably there is a better place to pick up those values, since this seems to be very tied to the UI
            var wpList = ReadBytesFromMemory(ModuleBaseAddress + WaypointsAddress, 0x48).AsSpan();
            for (var i = 0; i < 0x48; i += 8)
            {
                var a = BinaryPrimitives.ReadUInt32LittleEndian(wpList[i..(i + 4)]);
                var available = BinaryPrimitives.ReadUInt32LittleEndian(wpList[(i + 4)..(i + 8)]);
                if (available == 1 || mainPlayerUnit.Area == (AreaId)GoInt.Narrow(a))
                    availableWPs.Add((AreaId)GoInt.Narrow(a));
            }

            return new PlayerUnit
            {
                Address = mainPlayerUnit.Address,
                Name = mainPlayerUnit.Name,
                Id = mainPlayerUnit.UnitId,
                Area = mainPlayerUnit.Area,
                Position = mainPlayerUnit.Position,
                Stats = mainPlayerUnit.Stats,
                BaseStats = mainPlayerUnit.BaseStats,
                Skills = skills,
                States = mainPlayerUnit.States,
                Class = playerClass,
                LeftSkill = (SkillId)(short)leftSkillId,
                RightSkill = (SkillId)(short)rightSkillId,
                AvailableWaypoints = availableWPs,
            };
        }
    }

    // Go getSkills: the skill list linked at +0x08; a later entry for the same skill replaces an earlier one.
    internal IReadOnlyDictionary<SkillId, SkillPoints> GetSkills(nuint skillListPtr)
    {
        var skills = new Dictionary<SkillId, SkillPoints>();

        var skillPtr = (nuint)ReadUInt(skillListPtr, IntType.Uint64);

        while (skillPtr != 0)
        {
            unchecked
            {
                var skillTxtPtr = (nuint)ReadUInt(skillPtr, IntType.Uint64);
                var skillTxt = ReadUInt(skillTxtPtr, IntType.Uint16);
                var lvl = ReadUInt(skillPtr + 0x38, IntType.Uint16);
                var quantity = ReadUInt(skillPtr + 0x40, IntType.Uint16);
                var charges = ReadUInt(skillPtr + 0x48, IntType.Uint16);

                skills[(SkillId)(short)skillTxt] = new SkillPoints(Level: (uint)lvl, Quantity: (uint)quantity, Charges: (uint)charges);

                skillPtr = (nuint)ReadUInt(skillPtr + 0x08, IntType.Uint64);
            }
        }

        return skills;
    }

    // Go getStates: six 32-bit flag words at +0xAD0; bit b of word i is state 32*i + b.
    internal States GetStates(nuint statsListExPtr)
    {
        var states = new List<StateId>();
        for (var i = 0; i < 6; i++)
        {
            var offset = i * 4;
            var stateByte = ReadUInt(unchecked(statsListExPtr + 0xAD0 + (nuint)offset), IntType.Uint32);

            states.AddRange(CalculateStates(stateByte, unchecked((ulong)((32 * i) - 1))));
        }

        return new States(states);
    }

    // Go calculateStates: for each set bit b (lowest first) of stateFlag, state b + 1 + offset, wrapping like Go's
    // uint (the offset of the first word is -1 as a uint).
    internal static IReadOnlyList<StateId> CalculateStates(ulong stateFlag, ulong offset)
    {
        var states = new List<StateId>();
        for (var bit = 0; bit < 32; bit++)
        {
            if (((1UL << bit) & stateFlag) != 0)
                states.Add((StateId)(uint)unchecked((ulong)(bit + 1) + offset));
        }

        return states;
    }
}

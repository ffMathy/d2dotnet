// Port of d2go pkg/memory/roster.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using D2Dotnet.D2Go.Data;
using D2Dotnet.D2Go.Data.Area;

namespace D2Dotnet.D2Go.Memory;

// Inside the namespace, so these win over the library's types of the same name in the enclosing D2Dotnet namespace.
using AreaId = D2Dotnet.D2Go.Data.Area.AreaId;

public sealed partial class GameReader
{
    /// <summary>
    /// The party: the main player first, then the party list linked at <c>+0x148</c> (Go <c>getRoster</c>). A member
    /// that is also a player unit with the same name takes its area and position from that unit.
    /// </summary>
    internal Roster GetRoster(RawPlayerUnits rawPlayerUnits)
    {
        var roster = new List<RosterMember>();
        unchecked
        {
            var partyStruct = (nuint)ReadUInt(ModuleBaseAddress + _offset.RosterOffset, IntType.Uint64);

            // We skip the first position because it's the main player, and we already have the information (+0x148 is the next party member)
            partyStruct = (nuint)ReadUInt(partyStruct + 0x148, IntType.Uint64);
            while (partyStruct > 0)
            {
                var name = ReadStringFromMemory(partyStruct, 16);
                var a = (AreaId)GoInt.Narrow(ReadUInt(partyStruct + 0x5C, IntType.Uint32));

                var xPos = GoInt.Narrow(ReadUInt(partyStruct + 0x60, IntType.Uint32));
                var yPos = GoInt.Narrow(ReadUInt(partyStruct + 0x64, IntType.Uint32));

                // When the player is in town, roster data is not updated, so we need to get the area from the player unit that match the same name
                foreach (var pu in rawPlayerUnits)
                {
                    if (pu.Name == name)
                    {
                        xPos = pu.Position.X;
                        yPos = pu.Position.Y;
                        a = pu.Area;
                        break;
                    }
                }

                roster.Add(new RosterMember { Name = name, Area = a, Position = new Position(xPos, yPos) });
                partyStruct = (nuint)ReadUInt(partyStruct + 0x148, IntType.Uint64);
            }
        }

        var mainPlayerUnit = rawPlayerUnits.GetMainPlayer();

        return [new RosterMember { Name = mainPlayerUnit.Name, Area = mainPlayerUnit.Area, Position = mainPlayerUnit.Position }, .. roster];
    }
}

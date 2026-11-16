// Port of d2go pkg/memory/terrorzones.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using D2Dotnet.D2Go.Data.Area;

namespace D2Dotnet.D2Go.Memory;

public sealed partial class GameReader
{
    /// <summary>
    /// The terrorized areas: the non-zero entries of the eight 32-bit area ids at the online terror zone address (Go
    /// <c>TerrorZones</c>; the offline address of addresses.go is not used, as in Go).
    /// </summary>
    public IReadOnlyList<AreaId> TerrorZones()
    {
        var areas = new List<AreaId>();
        var tz = unchecked(ModuleBaseAddress + Offsets.TzOnline);

        for (var i = 0; i < 8; i++)
        {
            var tzArea = ReadUInt(unchecked(tz + (nuint)(i * (int)IntType.Uint32)), IntType.Uint32);
            if (tzArea != 0)
                areas.Add((AreaId)GoInt.Narrow(tzArea));
        }

        return areas;
    }
}

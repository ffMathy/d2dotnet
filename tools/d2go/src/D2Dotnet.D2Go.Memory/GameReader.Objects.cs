// Port of d2go pkg/memory/object.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using D2Dotnet.D2Go.Data;
using D2Dotnet.D2Go.Data.Object;

namespace D2Dotnet.D2Go.Memory;

public sealed partial class GameReader
{
    /// <summary>
    /// The objects (unit type 2) of the object unit table, nearest to <paramref name="playerPosition"/> first (Go
    /// <c>Objects</c>).
    /// </summary>
    /// <remarks>
    /// As in Go, an object is read as a shrine when the 8 bytes at unit <c>+0x0A</c> are not all 0. Those bytes overlap
    /// the high half of the unit id, the mode and the low half of the unit data pointer, so in practice nearly every
    /// object takes the shrine branch, and <see cref="GameObject.InteractType"/> is then 0.
    /// </remarks>
    public GameObjects Objects(Position playerPosition, HoverData hover)
    {
        var baseAddr = unchecked(ModuleBaseAddress + _offset.UnitTable + (2 * 1024));
        var unitTableBuffer = ReadBytesFromMemory(baseAddr, UnitTableBuckets * 8);

        var objects = new List<GameObject>();
        for (var i = 0; i < UnitTableBuckets; i++)
        {
            var objectOffset = 8 * i;
            var objectUnitPtr = (nuint)ReadUIntFromBuffer(unitTableBuffer, (ulong)objectOffset, IntType.Uint64);
            while (objectUnitPtr > 0)
            {
                unchecked
                {
                    var objectType = ReadUInt(objectUnitPtr + 0x00, IntType.Uint32);
                    if (objectType == 2)
                    {
                        var txtFileNo = ReadUInt(objectUnitPtr + 0x04, IntType.Uint32);
                        var mode = ReadUInt(objectUnitPtr + 0x0c, IntType.Uint32);
                        var unitId = ReadUInt(objectUnitPtr + 0x08, IntType.Uint32);

                        // Coordinates (X, Y)
                        var pathPtr = (nuint)ReadUInt(objectUnitPtr + 0x38, IntType.Uint64);
                        var posX = ReadUInt(pathPtr + 0x10, IntType.Uint16);
                        var posY = ReadUInt(pathPtr + 0x14, IntType.Uint16);

                        var unitDataPtr = (nuint)ReadUInt(objectUnitPtr + 0x10, IntType.Uint64);
                        // checking if this is a shrine
                        var shrineTextPtr = (nuint)ReadUInt(objectUnitPtr + 0x0A, IntType.Uint64);
                        ulong interactType = 0;
                        var shrineData = new ShrineData();
                        if (shrineTextPtr > 0)
                        {
                            var shrineType = (ShrineType)(uint)ReadUInt(unitDataPtr + 0x08, IntType.Uint8);
                            shrineData = new ShrineData
                            {
                                ShrineName = ShrineTypes.ShrineTypeNames.GetValueOrDefault(shrineType, ""),
                                ShrineType = shrineType,
                            };
                        }
                        else
                        {
                            interactType = ReadUInt(unitDataPtr + 0x08, IntType.Uint8);
                        }

                        var owner = ReadStringFromMemory(unitDataPtr + 0x34, 32);

                        var id = new UnitId(GoInt.Narrow(unitId));
                        objects.Add(new GameObject
                        {
                            Id = id,
                            Name = (ObjectName)GoInt.Narrow(txtFileNo),
                            IsHovered = id == hover.UnitId && hover.UnitType == 2 && hover.IsHovered,
                            InteractType = (InteractType)(uint)interactType,
                            Shrine = shrineData,
                            Selectable = mode == 0,
                            Position = new Position(GoInt.Narrow(posX), GoInt.Narrow(posY)),
                            Owner = owner,
                        });
                    }

                    objectUnitPtr = (nuint)ReadUInt(objectUnitPtr + 0x150, IntType.Uint64);
                }
            }
        }

        return new GameObjects(SortByDistance(objects, o => o.Position, playerPosition));
    }
}

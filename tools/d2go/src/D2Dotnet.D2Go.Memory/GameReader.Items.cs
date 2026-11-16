// Port of d2go pkg/memory/item.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using D2Dotnet.D2Go.Data;
using D2Dotnet.D2Go.Data.Item;
using D2Dotnet.D2Go.Data.Stat;
using D2Dotnet.D2Go.Data.State;
using DataInventory = D2Dotnet.D2Go.Data.Inventory;

namespace D2Dotnet.D2Go.Memory;

public sealed partial class GameReader
{
    /// <summary>
    /// The items of the item unit table that are on the main player, in its stashes, in a vendor's or on the ground,
    /// plus the gold (Go <c>Inventory</c>). Belt items go to <see cref="DataInventory.Belt"/>; the others are sorted
    /// nearest to the main player first. Shared stash tabs are told apart by the order of the shared stash player
    /// units (those with <see cref="StateId.Sharedstash"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// An inventory item is owned by none of the known owners and there are fewer than three shared stash units: Go
    /// panics indexing the missing one.
    /// </exception>
    public DataInventory Inventory(RawPlayerUnits rawPlayerUnits, HoverData hover)
    {
        ArgumentNullException.ThrowIfNull(rawPlayerUnits);
        var mainPlayer = rawPlayerUnits.GetMainPlayer();
        var baseAddr = unchecked(ModuleBaseAddress + _offset.UnitTable + (4 * 1024));
        var unitTableBuffer = ReadBytesFromMemory(baseAddr, UnitTableBuckets * 8);

        var stashPlayerUnits = new Dictionary<ulong, RawPlayerUnit>();
        var stashPlayerUnitOrder = new List<ulong>();
        foreach (var pu in rawPlayerUnits)
        {
            if (pu.States.HasState(StateId.Sharedstash))
            {
                var order = ReadUInt(unchecked(pu.Address + 0xD8), IntType.Uint64);
                stashPlayerUnitOrder.Add(order);
                stashPlayerUnits[order] = pu;
            }
        }

        stashPlayerUnitOrder.Sort();

        // Go's map index: the zero value for a missing key.
        RawPlayerUnit StashUnit(int index) =>
            stashPlayerUnits.TryGetValue(stashPlayerUnitOrder[index], out var unit) ? unit : new RawPlayerUnit();

        // Gold
        mainPlayer.BaseStats.TryFindStat(StatId.Gold, 0, out var inventoryGold);
        mainPlayer.BaseStats.TryFindStat(StatId.StashGold, 0, out var mainPlayerStashedGold);
        var stashedGold = new long[4];
        stashedGold[0] = mainPlayerStashedGold.Value;
        for (var i = 0; i < stashPlayerUnitOrder.Count; i++)
        {
            if (i > 2)
                break;
            StashUnit(i).BaseStats.TryFindStat(StatId.StashGold, 0, out var stashGold);
            stashedGold[i + 1] = stashGold.Value;
        }

        var allItems = new List<GameItem>();
        var beltItems = new List<GameItem>();
        var beltName = default(ItemName);
        for (var i = 0; i < UnitTableBuckets; i++)
        {
            var itemOffset = 8 * i;
            var itemUnitPtr = (nuint)ReadUIntFromBuffer(unitTableBuffer, (ulong)itemOffset, IntType.Uint64);
            while (itemUnitPtr > 0)
            {
                unchecked
                {
                    var itemDataBuffer = ReadBytesFromMemory(itemUnitPtr, 144);
                    var itemType = ReadUIntFromBuffer(itemDataBuffer, 0x00, IntType.Uint32);
                    var txtFileNo = ReadUIntFromBuffer(itemDataBuffer, 0x04, IntType.Uint32);
                    var unitId = ReadUIntFromBuffer(itemDataBuffer, 0x08, IntType.Uint32);
                    // itemLoc = 0 in inventory, 1 equipped, 2 in belt, 3 on ground, 4 cursor, 5 dropping, 6 socketed
                    var itemLoc = ReadUIntFromBuffer(itemDataBuffer, 0x0C, IntType.Uint32);

                    if (itemType != 4)
                    {
                        itemUnitPtr = (nuint)ReadUInt(itemUnitPtr + 0x150, IntType.Uint64);
                        continue;
                    }

                    var unitDataPtr = (nuint)ReadUIntFromBuffer(itemDataBuffer, 0x10, IntType.Uint64);
                    var unitDataBuffer = ReadBytesFromMemory(unitDataPtr, 144);
                    var flags = ReadUIntFromBuffer(unitDataBuffer, 0x18, IntType.Uint32);
                    var invPage = ReadUIntFromBuffer(unitDataBuffer, 0x55, IntType.Uint8);
                    var itemQuality = ReadUIntFromBuffer(unitDataBuffer, 0x00, IntType.Uint32);
                    var itemOwnerNpc = ReadUIntFromBuffer(unitDataBuffer, 0x0C, IntType.Uint32);

                    // Item coordinates (X, Y)
                    var pathPtr = (nuint)ReadUIntFromBuffer(itemDataBuffer, 0x38, IntType.Uint64);
                    var pathBuffer = ReadBytesFromMemory(pathPtr, 144);
                    var itemX = ReadUIntFromBuffer(pathBuffer, 0x10, IntType.Uint16);
                    var itemY = ReadUIntFromBuffer(pathBuffer, 0x14, IntType.Uint16);

                    var name = ItemNames.GetNameByEnum((uint)txtFileNo);
                    var id = new UnitId(GoInt.ToInt(unitId));
                    var itemHovered = hover.IsHovered && hover.UnitType == 4 && hover.UnitId == id;

                    var itm = SetProperties(
                        new GameItem
                        {
                            Id = GoInt.Narrow(txtFileNo),
                            UnitId = id,
                            Name = name,
                            Quality = (ItemQuality)GoInt.Narrow(itemQuality),
                            Position = new Position(GoInt.Narrow(itemX), GoInt.Narrow(itemY)),
                            IsHovered = itemHovered,
                        },
                        (uint)flags);

                    var location = LocationType.Unknown;
                    switch (itemLoc)
                    {
                        case 0:
                            if (itemOwnerNpc == 2 || itemOwnerNpc == UnitIdAsUInt(StashUnit(0).UnitId))
                            {
                                location = LocationType.SharedStash;
                                invPage = 1;
                                break;
                            }

                            if (itemOwnerNpc == 3 || itemOwnerNpc == UnitIdAsUInt(StashUnit(1).UnitId))
                            {
                                location = LocationType.SharedStash;
                                invPage = 2;
                                break;
                            }

                            if (itemOwnerNpc == 4 || itemOwnerNpc == UnitIdAsUInt(StashUnit(2).UnitId))
                            {
                                location = LocationType.SharedStash;
                                invPage = 3;
                                break;
                            }

                            if ((0x00002000 & flags) != 0 && itemOwnerNpc == 4294967295)
                            {
                                location = LocationType.Vendor;
                                break;
                            }

                            if (new UnitId(GoInt.ToInt(itemOwnerNpc)) == mainPlayer.UnitId || itemOwnerNpc == 1)
                            {
                                if (invPage == 0)
                                {
                                    location = LocationType.Inventory;
                                    break;
                                }

                                location = LocationType.Stash;
                                invPage = 0;
                                break;
                            }

                            break;
                        case 1:
                            if (new UnitId(GoInt.ToInt(itemOwnerNpc)) == mainPlayer.UnitId || itemOwnerNpc == 1)
                            {
                                location = LocationType.Equipped;
                                if (itm.Type(Tables).Code == ItemTypeCodes.TypeBelt)
                                    beltName = itm.Name;
                            }

                            break;
                        case 2:
                            if (new UnitId(GoInt.ToInt(itemOwnerNpc)) == mainPlayer.UnitId || itemOwnerNpc == 1)
                                location = LocationType.Belt;
                            break;
                        case 3:
                        case 5:
                            location = LocationType.Ground;
                            break;
                        case 6:
                            location = LocationType.Socket;
                            break;
                        case 4:
                            location = LocationType.Cursor;
                            break;
                    }

                    itm = itm with { Location = new ItemLocation(location, GoInt.Narrow(invPage)) };

                    // We don't care about the inventory we don't know where they are, probably previous games or random crap
                    if (location != LocationType.Unknown)
                    {
                        // Item Stats
                        var statsListExPtr = (nuint)ReadUIntFromBuffer(itemDataBuffer, 0x88, IntType.Uint64);
                        var (baseStats, stats) = GetItemStats(statsListExPtr);
                        itm = itm with { Stats = stats, BaseStats = baseStats };

                        if (location == LocationType.Belt)
                            beltItems.Add(itm);
                        else
                            allItems.Add(itm);
                    }

                    itemUnitPtr = (nuint)ReadUInt(itemUnitPtr + 0x150, IntType.Uint64);
                }
            }
        }

        return new DataInventory
        {
            Gold = inventoryGold.Value,
            StashedGold = stashedGold,
            Belt = new Belt { Items = beltItems, Name = beltName },
            AllItems = [.. SortByDistance(allItems, it => it.Position, mainPlayer.Position)],
        };
    }

    // Go uint(UnitID): the 64-bit int reinterpreted as unsigned, which gives back the 32-bit id read.
    private static ulong UnitIdAsUInt(UnitId id) => unchecked((ulong)id.Value);

    /// <summary>
    /// The base and full stats of an item (Go <c>getItemStats</c>): the lists at <c>+0x30</c> and <c>+0x88</c> of its
    /// stats list, plus, when flag <c>0x80000000</c> is set, the stats of the modifier lists linked from <c>+0x70</c>.
    /// </summary>
    internal (Stats BaseStats, Stats FullStats) GetItemStats(nuint statsListExPtr)
    {
        unchecked
        {
            // Initial full and base stats extraction
            var fullStats = new List<StatData>(GetStatsList(statsListExPtr + 0x88));
            var baseStats = new List<StatData>(GetStatsList(statsListExPtr + 0x30));

            // Flags and last stat list pointers
            var flags = ReadUInt(statsListExPtr + 0x1C, IntType.Uint64);
            var lastStatsList = (nuint)ReadUInt(statsListExPtr + 0x70, IntType.Uint64);

            // If the special flag isn't set, return the current base and full stats
            if ((flags & 0x80000000) == 0)
                return (new Stats(baseStats), new Stats(fullStats));

            // Prepare for additional stats processing
            var additionalStats = new List<StatData>();
            var statListPtr = lastStatsList;

            // Traverse the stat lists to accumulate additional stats
            while (statListPtr != 0)
            {
                var statListFlags = ReadUInt(statListPtr + 0x1C, IntType.Uint64);

                // If we hit a condition where no further traversal is needed, break
                if ((0x40 & statListFlags & 0xFFFFDFFF) != 0)
                    break;

                // Move to the previous stat list
                statListPtr = (nuint)ReadUInt(statListPtr + 0x48, IntType.Uint64);
            }

            // If we found a valid previous stat list
            if (statListPtr != 0)
            {
                var additionalBaseStats = GetStatsList(statListPtr + 0x30);

                // Add only the additional stats that are not already present in fullStats
                foreach (var statM in additionalBaseStats)
                {
                    if (!Contains(fullStats, statM.Id, statM.Layer))
                        additionalStats.Add(statM);
                }
            }

            // Merge additional stats into fullStats (but not into baseStats)
            fullStats.AddRange(additionalStats);

            // Handle base stats from potential modifiers and ensure they don't pollute baseStats
            statListPtr = lastStatsList;

            while (statListPtr != 0)
            {
                var statListFlags = ReadUInt(statListPtr + 0x1C, IntType.Uint64);

                if (statListFlags != 0)
                {
                    var modifierBaseStats = GetStatsList(statListPtr + 0x30);

                    foreach (var mStat in modifierBaseStats)
                    {
                        // Update fullStats. In Go the update assigns to a copy returned by FindStat, so an existing
                        // stat keeps its value: only missing stats are added.
                        if (!Contains(fullStats, mStat.Id, mStat.Layer))
                            fullStats.Add(mStat);

                        // Add to baseStats only if this stat qualifies as a base stat
                        if ((flags & 0x80000000) != 0 && statListFlags == 0x80000000)
                        {
                            if (!Contains(baseStats, mStat.Id, mStat.Layer))
                                baseStats.Add(mStat);
                        }
                    }
                }

                // Move to the previous stat list
                statListPtr = (nuint)ReadUInt(statListPtr + 0x48, IntType.Uint64);
            }

            return (new Stats(baseStats), new Stats(fullStats));
        }
    }

    // Go Stats.FindStat's found result.
    private static bool Contains(List<StatData> stats, StatId id, int layer)
    {
        foreach (var s in stats)
        {
            if (s.Id == id && s.Layer == layer)
                return true;
        }

        return false;
    }

    // Go setProperties: ethereal 0x00400000, identified 0x00000010, runeword 0x04000000. Go only ever sets the flags
    // to true; the fields start false.
    internal static GameItem SetProperties(GameItem item, uint flags)
    {
        if ((0x00400000 & flags) != 0)
            item = item with { Ethereal = true };
        if ((0x00000010 & flags) != 0)
            item = item with { Identified = true };
        if ((0x04000000 & flags) != 0)
            item = item with { IsRuneword = true };

        return item;
    }
}

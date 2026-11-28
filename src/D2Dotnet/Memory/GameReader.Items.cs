// Memory layout from d2go pkg/memory/item.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Buffers.Binary;
using D2Dotnet.Game;

namespace D2Dotnet.Memory;

public sealed partial class GameReader
{
    // Owner ids of items in the three shared stash tabs, when the owner is not the tab's unit id.
    private const uint SharedStashOwner1 = 2, SharedStashOwner2 = 3, SharedStashOwner3 = 4;

    // Owner id of items on the main player, besides its unit id.
    private const uint PlayerOwner = 1;

    // Owner id of vendor items.
    private const uint VendorOwner = 4294967295;

    /// <summary>
    /// The items of the item unit table that are on the main player, in its stashes, at a vendor or on the ground, and the
    /// gold. Belt items are in <see cref="Inventory.Belt"/>; the others are sorted nearest to the main player first.
    /// </summary>
    public Inventory ReadInventory()
    {
        var hover = ReadHover();
        return ReadInventory(ReadPlayers(hover), hover);
    }

    /// <summary>
    /// Every item unit in the item table with its raw place fields, for finding out why an item is not listed by
    /// <see cref="ReadInventory()"/>: its class, id, where (0 stored, 1 equipped, 2 belt, 3 ground, 4 cursor, 5
    /// dropping, 6 socketed), owner, page, flags and position.
    /// </summary>
    public IReadOnlyList<(ItemId Class, UnitId Id, uint Where, uint Owner, byte Page, uint Flags, Point Position)> ReadItemUnits(int tables = 1)
    {
        var units = new List<(ItemId, UnitId, uint, uint, byte, uint, Point)>();
        var all = Enumerable.Range(0, tables).SelectMany(t => WalkUnitTable(unchecked(Offsets.UnitTable + ItemTable + (nuint)(t * UnitTableSize))));
        foreach (var unit in all)
        {
            unchecked
            {
                var data = Process.ReadBytes(unit, 144).AsSpan();
                if (BinaryPrimitives.ReadUInt32LittleEndian(data[0x00..]) != (uint)UnitType.Item)
                    continue;
                var unitData = Process.ReadBytes((nuint)BinaryPrimitives.ReadUInt64LittleEndian(data[0x10..]), 144).AsSpan();
                var path = Process.ReadBytes((nuint)BinaryPrimitives.ReadUInt64LittleEndian(data[0x38..]), 144).AsSpan();
                units.Add((
                    (ItemId)BinaryPrimitives.ReadUInt32LittleEndian(data[0x04..]),
                    new UnitId(BinaryPrimitives.ReadUInt32LittleEndian(data[0x08..])),
                    BinaryPrimitives.ReadUInt32LittleEndian(data[0x0C..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(unitData[0x0C..]),
                    unitData[0x55],
                    BinaryPrimitives.ReadUInt32LittleEndian(unitData[0x18..]),
                    new Point(BinaryPrimitives.ReadUInt16LittleEndian(path[0x10..]), BinaryPrimitives.ReadUInt16LittleEndian(path[0x14..]))));
            }
        }

        return units;
    }

    /// <summary>
    /// The item units of a player's inventory list: the inventory at unit <c>+0x90</c> (signature <c>0x01020304</c>)
    /// holds its first item at <c>+0x10</c>; each item's data (unit <c>+0x10</c>) links the next at <c>+0xB0</c>.
    /// Observed in Reign of the Warlock.
    /// </summary>
    internal IEnumerable<nuint> WalkInventoryList(nuint player)
    {
        var inventory = Process.ReadPointer(unchecked(player + 0x90));
        if (inventory == 0 || Process.ReadUInt32(inventory) != 0x01020304)
            yield break;
        var item = Process.ReadPointer(unchecked(inventory + 0x10));
        for (var n = 0; item != 0 && n < MaxListLength; n++)
        {
            if (Process.ReadUInt32(item) != (uint)UnitType.Item)
                yield break;
            yield return item;
            item = Process.ReadPointer(unchecked(Process.ReadPointer(unchecked(item + 0x10)) + 0xB0));
        }
    }

    internal Inventory ReadInventory(IReadOnlyList<PlayerUnit> players, HoveredUnit? hover)
    {
        var main = players.FirstOrDefault(p => p.IsMainPlayer);

        // The shared stash tabs are player units with the SharedStash state, in the order of the value at +0xD8.
        var stashes = players.Where(p => p.HasState(StateId.SharedStash))
            .Select(p => (Order: Process.ReadUInt64(unchecked(p.Address + 0xD8)), Unit: p))
            .OrderBy(s => s.Order)
            .Select(s => s.Unit)
            .Take(3)
            .ToArray();

        var stashGold = new int[4];
        stashGold[0] = main?.BaseStats.GetValue(StatId.StashGold) ?? 0;
        for (var i = 0; i < stashes.Length; i++)
            stashGold[i + 1] = stashes[i].BaseStats.GetValue(StatId.StashGold) ?? 0;

        bool OwnedByMain(uint owner) => owner == PlayerOwner || (main is not null && owner == main.Id.Value);
        bool InStash(uint owner, int tab, uint fallbackOwner) =>
            owner == fallbackOwner || (tab < stashes.Length && owner == stashes[tab].Id.Value);

        var items = new List<Item>();
        var belt = new List<Item>();
        ItemId? beltClass = null;
        // The item table misses some of the player's items in Reign of the Warlock (observed: a full inventory listed
        // half full), so the main player's inventory list is walked too.
        var units = WalkUnitTable(unchecked(Offsets.UnitTable + ItemTable)).ToList();
        if (main is not null)
            units.AddRange(WalkInventoryList(main.Address));
        foreach (var unit in units.Distinct())
        {
            unchecked
            {
                var data = Process.ReadBytes(unit, 144).AsSpan();
                if (BinaryPrimitives.ReadUInt32LittleEndian(data[0x00..]) != (uint)UnitType.Item)
                    continue;

                var itemClass = (ItemId)BinaryPrimitives.ReadUInt32LittleEndian(data[0x04..]);
                var id = new UnitId(BinaryPrimitives.ReadUInt32LittleEndian(data[0x08..]));
                // 0 in inventory, 1 equipped, 2 in belt, 3 on ground, 4 cursor, 5 dropping, 6 socketed
                var where = BinaryPrimitives.ReadUInt32LittleEndian(data[0x0C..]);

                var unitData = Process.ReadBytes((nuint)BinaryPrimitives.ReadUInt64LittleEndian(data[0x10..]), 144).AsSpan();
                var flags = BinaryPrimitives.ReadUInt32LittleEndian(unitData[0x18..]);
                var page = unitData[0x55];
                var quality = (ItemQuality)BinaryPrimitives.ReadUInt32LittleEndian(unitData[0x00..]);
                var owner = BinaryPrimitives.ReadUInt32LittleEndian(unitData[0x0C..]);

                var path = Process.ReadBytes((nuint)BinaryPrimitives.ReadUInt64LittleEndian(data[0x38..]), 144).AsSpan();
                var position = new Point(BinaryPrimitives.ReadUInt16LittleEndian(path[0x10..]), BinaryPrimitives.ReadUInt16LittleEndian(path[0x14..]));

                ItemLocation? location = null;
                switch (where)
                {
                    case 0:
                        if (InStash(owner, 0, SharedStashOwner1))
                            (location, page) = (ItemLocation.SharedStash, 1);
                        else if (InStash(owner, 1, SharedStashOwner2))
                            (location, page) = (ItemLocation.SharedStash, 2);
                        else if (InStash(owner, 2, SharedStashOwner3))
                            (location, page) = (ItemLocation.SharedStash, 3);
                        else if ((0x00002000 & flags) != 0 && owner == VendorOwner)
                            location = ItemLocation.Vendor;
                        else if (OwnedByMain(owner))
                            (location, page) = page == 0 ? (ItemLocation.Inventory, (byte)0) : (ItemLocation.Stash, (byte)0);
                        break;
                    case 1:
                        if (OwnedByMain(owner))
                            location = ItemLocation.Equipped;
                        break;
                    case 2:
                        if (OwnedByMain(owner))
                            location = ItemLocation.Belt;
                        break;
                    case 3:
                    case 5:
                        location = ItemLocation.Ground;
                        break;
                    case 4:
                        location = ItemLocation.Cursor;
                        break;
                    case 6:
                        location = ItemLocation.Socket;
                        break;
                }

                // Items whose place is not known are left out: probably previous games' or random leftovers.
                if (location is not { } known)
                    continue;

                var info = Database.Items.Find(itemClass);
                if (known == ItemLocation.Equipped && info?.Type == ItemTypeCode.Belt)
                    beltClass = itemClass;

                var (baseStats, stats) = ReadItemStats((nuint)BinaryPrimitives.ReadUInt64LittleEndian(data[0x88..]));
                var item = new Item
                {
                    Id = id,
                    Class = itemClass,
                    Info = info,
                    Quality = quality,
                    Position = position,
                    Location = known,
                    Page = page,
                    IsEthereal = (0x00400000 & flags) != 0,
                    IsIdentified = (0x00000010 & flags) != 0,
                    IsRuneword = (0x04000000 & flags) != 0,
                    IsHovered = hover is { } h && h.Is(UnitType.Item, id),
                    Stats = stats,
                    BaseStats = baseStats,
                };
                (known == ItemLocation.Belt ? belt : items).Add(item);
            }
        }

        return new Inventory
        {
            Items = NearestFirst(items, i => i.Position, main?.Position ?? default),
            Belt = new Belt { Items = belt, Equipped = beltClass },
            Gold = main?.BaseStats.GetValue(StatId.Gold) ?? 0,
            StashGold = stashGold,
        };
    }

    /// <summary>
    /// The base and full stats of an item: the lists at <c>+0x30</c> and <c>+0x88</c> of its stats, plus, when flag
    /// <c>0x80000000</c> is set, the stats of the modifier lists linked from <c>+0x70</c> (each previous list at <c>+0x48</c>).
    /// A stat (id and layer) is listed once, with the first value found: the full list's (the item's total) wins over a
    /// modifier list's (one part of it).
    /// </summary>
    internal (StatList BaseStats, StatList Stats) ReadItemStats(nuint statsEx)
    {
        unchecked
        {
            var full = new List<Stat>(ReadStatList(statsEx + 0x88));
            var baseStats = new List<Stat>(ReadStatList(statsEx + 0x30));

            var flags = Process.ReadUInt64(statsEx + 0x1C);
            var last = Process.ReadPointer(statsEx + 0x70);
            if ((flags & 0x80000000) == 0)
                return (new StatList(baseStats), new StatList(full));

            // The first previous list with flag 0x40 (and not 0x2000): its base stats that are not listed yet are added.
            var list = last;
            for (var n = 0; list != 0 && n < MaxListLength; n++)
            {
                if ((0x40 & Process.ReadUInt64(list + 0x1C) & 0xFFFFDFFF) != 0)
                    break;
                list = Process.ReadPointer(list + 0x48);
            }

            if (list != 0)
            {
                foreach (var stat in ReadStatList(list + 0x30))
                    AddMissing(full, stat);
            }

            // Then every modifier list with flags: its stats that are not listed yet are added to the full stats, and to
            // the base stats when its flags are exactly 0x80000000. A stat already listed keeps its value: the full list
            // holds the item's total, a modifier list only its own part of it.
            list = last;
            for (var n = 0; list != 0 && n < MaxListLength; n++)
            {
                var listFlags = Process.ReadUInt64(list + 0x1C);
                if (listFlags != 0)
                {
                    foreach (var stat in ReadStatList(list + 0x30))
                    {
                        AddMissing(full, stat);
                        if (listFlags == 0x80000000)
                            AddMissing(baseStats, stat);
                    }
                }

                list = Process.ReadPointer(list + 0x48);
            }

            return (new StatList(baseStats), new StatList(full));
        }

        static int IndexOf(List<Stat> stats, Stat stat) => stats.FindIndex(s => s.Id == stat.Id && s.Layer == stat.Layer);

        static void AddMissing(List<Stat> stats, Stat stat)
        {
            if (IndexOf(stats, stat) < 0)
                stats.Add(stat);
        }
    }
}

using D2Dotnet.Game;

namespace D2Dotnet.Tests.Memory;

/// <summary>item.go.</summary>
public sealed class InventoryTests
{
    private const uint MainId = 500;
    private const uint Stash1 = 602, Stash2 = 603, Stash3 = 601;
    private const uint SharedStashWord5 = 1u << 26; // state 186

    /// <summary>A main player at (100, 100) and four shared stash units whose orders sort them 602, 603, 601, 604.</summary>
    private static GameImage WithPlayers()
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec
        {
            UnitId = MainId, Name = "Main", X = 100, Y = 100, IsMain = true,
            BaseStats = [new RawStat(StatId.Gold, 5000), new RawStat(StatId.StashGold, 100)],
        });
        void Stash(uint id, ulong order, uint gold) => image.AddPlayer(new PlayerSpec
        {
            UnitId = id, Name = "Stash", StashOrder = order, StateWords = [0, 0, 0, 0, 0, SharedStashWord5],
            BaseStats = [new RawStat(StatId.StashGold, gold)], Bucket = 1,
        });
        Stash(Stash3, 30, 400);
        Stash(Stash1, 10, 200);
        Stash(Stash2, 20, 300);
        Stash(604, 40, 999);
        return image;
    }

    private static Inventory Read(GameImage image) => image.Reader().ReadInventory();

    [Fact]
    public void Gold_ComesFromTheMainPlayerAndTheFirstThreeStashUnitsInOrder()
    {
        var inv = Read(WithPlayers());
        Assert.Equal(5000, inv.Gold);
        Assert.Equal([100, 200, 300, 400], inv.StashGold);
        Assert.Empty(inv.Items);
        Assert.Empty(inv.Belt.Items);
        Assert.Null(inv.Belt.Equipped);
    }

    [Fact]
    public void Gold_WithoutStashUnits_OrWithoutAMainPlayer()
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = MainId, Name = "Main", IsMain = true, BaseStats = [new RawStat(StatId.StashGold, 7)] });
        var inv = Read(image);
        Assert.Equal(0, inv.Gold);
        Assert.Equal([7, 0, 0, 0], inv.StashGold);

        Assert.Equal([0, 0, 0, 0], Read(new GameImage()).StashGold);
    }

    [Fact]
    public void Items_InTheInventoryListButNotTheTable_AreListedOnce()
    {
        var image = new GameImage();
        var main = image.AddPlayer(new PlayerSpec { UnitId = MainId, Name = "Main", IsMain = true });
        var both = image.AddItem(10, 1, 0, MainId, x: 1);
        var listOnly = image.AddItem(11, 2, 0, MainId, x: 2, inTable: false);
        image.SetInventoryList(main, both, listOnly);

        var items = Read(image).Items;
        Assert.Equal([1u, 2u], items.Select(i => i.Id.Value).Order());
    }

    public static TheoryData<uint, uint, byte, uint, ItemLocation, byte> Locations => new()
    {
        // where, owner, inventory page, flags => location, page
        { 0, 2, 0, 0, ItemLocation.SharedStash, 1 },
        { 0, Stash1, 5, 0, ItemLocation.SharedStash, 1 },
        { 0, 3, 0, 0, ItemLocation.SharedStash, 2 },
        { 0, Stash2, 0, 0, ItemLocation.SharedStash, 2 },
        { 0, 4, 9, 0, ItemLocation.SharedStash, 3 },
        { 0, Stash3, 0, 0, ItemLocation.SharedStash, 3 },
        { 0, 0xFFFF_FFFF, 7, 0x2000, ItemLocation.Vendor, 7 },
        { 0, MainId, 0, 0, ItemLocation.Inventory, 0 },
        { 0, 1, 0, 0, ItemLocation.Inventory, 0 },
        { 0, MainId, 4, 0, ItemLocation.Stash, 0 },
        { 1, MainId, 3, 0, ItemLocation.Equipped, 3 },
        { 1, 1, 0, 0, ItemLocation.Equipped, 0 },
        { 2, MainId, 0, 0, ItemLocation.Belt, 0 },
        { 2, 1, 0, 0, ItemLocation.Belt, 0 },
        { 3, 77, 2, 0, ItemLocation.Ground, 2 },
        { 5, 77, 0, 0, ItemLocation.Ground, 0 },
        { 4, 77, 0, 0, ItemLocation.Cursor, 0 },
        { 6, 77, 0, 0, ItemLocation.Socket, 0 },
    };

    [Theory]
    [MemberData(nameof(Locations))]
    public void Location_FollowsTheGameRules(uint where, uint owner, byte page, uint flags, ItemLocation expected, byte expectedPage)
    {
        var image = WithPlayers();
        image.AddItem(10, 1, where, owner, invPage: page, flags: flags);
        var inv = Read(image);

        var item = expected == ItemLocation.Belt ? Assert.Single(inv.Belt.Items) : Assert.Single(inv.Items);
        Assert.Equal(expected, item.Location);
        Assert.Equal(expectedPage, item.Page);
        if (expected == ItemLocation.Belt)
            Assert.Empty(inv.Items);
    }

    [Theory]
    [InlineData(0u, 77u, 0u)]           // no owner matches
    [InlineData(0u, 0xFFFF_FFFFu, 0u)]  // vendor owner without the vendor flag
    [InlineData(0u, 77u, 0x2000u)]      // vendor flag without the vendor owner
    [InlineData(1u, 77u, 0u)]           // equipped on someone else
    [InlineData(2u, 77u, 0u)]           // someone else's belt
    [InlineData(7u, MainId, 0u)]        // unknown location
    public void UnknownLocations_AreLeftOut(uint where, uint owner, uint flags)
    {
        var image = WithPlayers();
        image.AddItem(10, 1, where, owner, flags: flags);
        var inv = Read(image);
        Assert.Empty(inv.Items);
        Assert.Empty(inv.Belt.Items);
    }

    [Fact]
    public void InventoryItem_WithFewerThanThreeStashUnits_IsRead()
    {
        // d2go panicked indexing the missing stash unit.
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = MainId, Name = "Main", IsMain = true });
        image.AddItem(10, 1, 0, MainId);
        image.AddItem(10, 2, 0, 3);
        var inv = Read(image);
        Assert.Equal([ItemLocation.Inventory, ItemLocation.SharedStash], inv.Items.OrderBy(i => i.Id.Value).Select(i => i.Location));
    }

    [Fact]
    public void ItemsOfAMissingMainPlayer_AreOnlyThoseOfOwner1()
    {
        var image = new GameImage();
        image.AddItem(10, 1, 0, 0);
        image.AddItem(10, 2, 0, 1);
        Assert.Equal(2u, Assert.Single(Read(image).Items).Id.Value);
    }

    [Fact]
    public void EquippedBelt_IsTheBelt()
    {
        var image = WithPlayers();
        image.AddItem((uint)ItemId.Buckler, 1, 1, MainId);
        image.AddItem((uint)ItemId.HeavyBelt, 2, 1, MainId);
        image.AddItem((uint)ItemId.Sash, 3, 1, 77); // not ours: unknown, not our belt
        var inv = Read(image);

        Assert.Equal(ItemId.HeavyBelt, inv.Belt.Equipped);
        Assert.Equal(3, inv.Belt.Rows);
        Assert.Equal([1u, 2u], inv.Items.Select(i => i.Id.Value).Order());
    }

    [Fact]
    public void Items_HaveTheirFieldsFlagsAndBaseItem()
    {
        var image = WithPlayers();
        image.AddItem(0, 0x8000_0001, 3, 0, quality: 7, flags: 0x00400000 | 0x00000010 | 0x04000000, x: 300, y: 400);
        image.AddItem((uint)ItemId.HealingPotion, 2, 3, 0, quality: 4, flags: 0x00000010, x: 101, y: 100);
        image.AddItem(2, 3, 4, 0, unitType: 3); // not an item unit
        image.AddItem(9999, 4, 4, 0, x: 1000); // an id the game data does not have
        var inv = Read(image);

        Assert.Equal(3, inv.Items.Count);
        var near = inv.Items[0];
        Assert.Equal(ItemId.HealingPotion, near.Class);
        Assert.Equal($"Item{(int)ItemId.HealingPotion}", near.Info?.Name);
        Assert.True(near.IsHealingPotion);
        Assert.Equal(ItemQuality.Magic, near.Quality);
        Assert.Equal(new Point(101, 100), near.Position);
        Assert.True(near.IsIdentified);
        Assert.False(near.IsEthereal);
        Assert.False(near.IsRuneword);

        var far = inv.Items[1];
        Assert.Equal(new UnitId(0x8000_0001), far.Id);
        Assert.Equal(ItemQuality.Unique, far.Quality);
        Assert.True(far.IsEthereal && far.IsIdentified && far.IsRuneword);

        Assert.Null(inv.Items[2].Info);
    }

    [Fact]
    public void Items_AreSortedByDistanceFromTheMainPlayer_BeltItemsKeepTheirOrder()
    {
        var image = WithPlayers();
        image.AddItem(1, 1, 3, 0, x: 200, y: 100, bucket: 0);
        image.AddItem(1, 2, 3, 0, x: 100, y: 100, bucket: 5);
        image.AddItem(1, 3, 3, 0, x: 100, y: 200, bucket: 5); // ties with unit 1: stays after it
        image.AddItem(1, 4, 2, MainId, x: 3, y: 0, bucket: 0);
        image.AddItem(1, 5, 2, MainId, x: 0, y: 0, bucket: 9);
        var inv = Read(image);

        Assert.Equal([2u, 1u, 3u], inv.Items.Select(i => i.Id.Value));
        Assert.Equal([4u, 5u], inv.Belt.Items.Select(i => i.Id.Value));
    }

    [Fact]
    public void Items_Hover_NeedsUnitType4()
    {
        var image = WithPlayers();
        image.AddItem(1, 9, 3, 0);
        image.SetHover(1, 4, 9);
        Assert.True(Assert.Single(Read(image).Items).IsHovered);

        image.SetHover(1, 2, 9);
        Assert.False(Assert.Single(Read(image).Items).IsHovered);
    }

    [Fact]
    public void Items_ReadTheirStats()
    {
        var image = WithPlayers();
        var stats = image.ItemStats(baseStats: [new RawStat(StatId.Defense, 50)], fullStats: [new RawStat(StatId.Defense, 75), new RawStat(StatId.MaxLife, 20 << 8)]);
        image.AddItem(1, 1, 3, 0, statsEx: stats);
        var item = Assert.Single(Read(image).Items);

        Assert.Equal([new Stat(StatId.Defense, 50)], item.BaseStats);
        Assert.Equal([new Stat(StatId.Defense, 75), new Stat(StatId.MaxLife, 20)], item.Stats);
        Assert.Equal(75, item.GetStat(StatId.Defense));
    }

    [Fact]
    public void ItemStats_WithoutTheModifierFlag_AreTheTwoLists()
    {
        var image = new GameImage();
        var node = image.StatListNode(0x80000000, [new RawStat(StatId.Dexterity, 1)]);
        var stats = image.ItemStats([new RawStat(StatId.Strength, 5)], [new RawStat(StatId.Strength, 10)], flags: 0x7FFF_FFFF, lastStatsList: node);

        var (b, f) = image.Reader().ReadItemStats((nuint)stats);
        Assert.Equal([new Stat(StatId.Strength, 5)], b);
        Assert.Equal([new Stat(StatId.Strength, 10)], f);
    }

    [Fact]
    public void ItemStats_WithModifiers_ListEachStatOnce_AndTheFullValueWins()
    {
        // The full list holds the item's total, a modifier list one part of it: a stat already listed keeps its value,
        // as in d2go (which assigned a modifier's value to a copy). d2go also added a stat the 0x40 list repeats twice.
        var image = new GameImage();
        var n3 = image.StatListNode(0, [new RawStat(StatId.Energy, 4)]);
        var n2 = image.StatListNode(0x2040, [new RawStat(StatId.Vitality, 3), new RawStat(StatId.Strength, 1), new RawStat(StatId.Vitality, 3)], n3);
        var n1 = image.StatListNode(0x80000000, [new RawStat(StatId.Dexterity, 7), new RawStat(StatId.Strength, 99), new RawStat(StatId.Strength, 8, Layer: 1)], n2);
        var stats = image.ItemStats([new RawStat(StatId.Strength, 5)], [new RawStat(StatId.Strength, 10)], flags: 0x8000_0000, lastStatsList: n1);

        var (b, f) = image.Reader().ReadItemStats((nuint)stats);

        // Full: the full list, then the missing stats of the first list with flag 0x40, then the missing stats of every
        // modifier list (n1, then n2). Neither n1's Strength 99 nor n2's Strength 1 replaces the full list's 10.
        Assert.Equal(
            [
                new Stat(StatId.Strength, 10),
                new Stat(StatId.Vitality, 3),
                new Stat(StatId.Dexterity, 7),
                new Stat(StatId.Strength, 8, 1),
            ],
            f);
        // Base: only the stats of lists whose flags are exactly 0x80000000, when missing.
        Assert.Equal([new Stat(StatId.Strength, 5), new Stat(StatId.Dexterity, 7), new Stat(StatId.Strength, 8, 1)], b);
    }

    [Fact]
    public void ItemStats_AStatInTwoModifierLists_KeepsTheFullValue()
    {
        // A weapon with +20 from an affix and +15 from a socketed jewel: the full list has the total, 35.
        var image = new GameImage();
        var jewel = image.StatListNode(0x80000000, [new RawStat(StatId.IncreasedAttackSpeed, 15)]);
        var affix = image.StatListNode(0x80000000, [new RawStat(StatId.IncreasedAttackSpeed, 20), new RawStat(StatId.Dexterity, 2)], jewel);
        var stats = image.ItemStats([], [new RawStat(StatId.IncreasedAttackSpeed, 35)], flags: 0x8000_0000, lastStatsList: affix);

        var (b, f) = image.Reader().ReadItemStats((nuint)stats);
        Assert.Equal([new Stat(StatId.IncreasedAttackSpeed, 35), new Stat(StatId.Dexterity, 2)], f);
        // Base: the first modifier list's value of a stat missing from the base list.
        Assert.Equal([new Stat(StatId.IncreasedAttackSpeed, 20), new Stat(StatId.Dexterity, 2)], b);
    }

    [Fact]
    public void ItemStats_WithoutA0x40List_AddOnlyModifiers()
    {
        var image = new GameImage();
        var n2 = image.StatListNode(0x1, [new RawStat(StatId.Energy, 4)]);
        var n1 = image.StatListNode(0x8000_0000_0000_0000, [new RawStat(StatId.Dexterity, 7)], n2);
        var stats = image.ItemStats([], [], flags: 0x8000_0000, lastStatsList: n1);

        var (b, f) = image.Reader().ReadItemStats((nuint)stats);
        Assert.Equal([new Stat(StatId.Dexterity, 7), new Stat(StatId.Energy, 4)], f);
        // 0x8000000000000000 is not exactly 0x80000000: the flags are read as 64 bits.
        Assert.Empty(b);
    }
}

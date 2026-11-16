using D2Dotnet.D2Go.Data.Item;
using D2Dotnet.D2Go.Data.Stat;
using DataInventory = D2Dotnet.D2Go.Data.Inventory;

namespace D2Dotnet.D2Go.Memory.Tests;

// Inside the namespace, so these win over the library's types of the same name in the enclosing D2Dotnet namespace.
using StatId = D2Dotnet.D2Go.Data.Stat.StatId;

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

    private static DataInventory Read(GameImage image, HoverData hover = default)
    {
        using var reader = image.Reader();
        return reader.Inventory(reader.GetRawPlayerUnits(), hover);
    }

    [Fact]
    public void Gold_ComesFromTheMainPlayerAndTheFirstThreeStashUnitsInOrder()
    {
        var inv = Read(WithPlayers());
        Assert.Equal(5000, inv.Gold);
        Assert.Equal([100, 200, 300, 400], inv.StashedGold);
        Assert.Empty(inv.AllItems);
        Assert.Empty(inv.Belt.Items);
        Assert.Equal(default, inv.Belt.Name);
    }

    [Fact]
    public void Gold_WithoutStashUnits()
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = MainId, Name = "Main", IsMain = true, BaseStats = [new RawStat(StatId.StashGold, 7)] });
        var inv = Read(image);
        Assert.Equal(0, inv.Gold);
        Assert.Equal([7, 0, 0, 0], inv.StashedGold);
    }

    public static TheoryData<uint, uint, byte, uint, string, int> Locations => new()
    {
        // location, owner, inventory page, flags => location type, page
        { 0, 2, 0, 0, "shared_stash", 1 },
        { 0, Stash1, 5, 0, "shared_stash", 1 },
        { 0, 3, 0, 0, "shared_stash", 2 },
        { 0, Stash2, 0, 0, "shared_stash", 2 },
        { 0, 4, 9, 0, "shared_stash", 3 },
        { 0, Stash3, 0, 0, "shared_stash", 3 },
        { 0, 0xFFFF_FFFF, 7, 0x2000, "vendor", 7 },
        { 0, MainId, 0, 0, "inventory", 0 },
        { 0, 1, 0, 0, "inventory", 0 },
        { 0, MainId, 4, 0, "stash", 0 },
        { 1, MainId, 3, 0, "equipped", 3 },
        { 1, 1, 0, 0, "equipped", 0 },
        { 2, MainId, 0, 0, "belt", 0 },
        { 2, 1, 0, 0, "belt", 0 },
        { 3, 77, 2, 0, "ground", 2 },
        { 5, 77, 0, 0, "ground", 0 },
        { 4, 77, 0, 0, "cursor", 0 },
        { 6, 77, 0, 0, "socket", 0 },
    };

    [Theory]
    [MemberData(nameof(Locations))]
    public void Location_FollowsGoRules(uint location, uint owner, byte page, uint flags, string expected, int expectedPage)
    {
        var image = WithPlayers();
        image.AddItem(10, 1, location, owner, invPage: page, flags: flags);
        var inv = Read(image);

        var item = expected == "belt" ? Assert.Single(inv.Belt.Items) : Assert.Single(inv.AllItems);
        Assert.Equal(new ItemLocation(expected, expectedPage), item.Location);
        if (expected == "belt")
            Assert.Empty(inv.AllItems);
    }

    [Theory]
    [InlineData(0u, 77u, 0u)]           // no owner matches
    [InlineData(0u, 0xFFFF_FFFFu, 0u)]  // vendor owner without the vendor flag
    [InlineData(0u, 77u, 0x2000u)]      // vendor flag without the vendor owner
    [InlineData(1u, 77u, 0u)]           // equipped on someone else
    [InlineData(2u, 77u, 0u)]           // someone else's belt
    [InlineData(7u, MainId, 0u)]        // unknown location
    public void UnknownLocations_AreLeftOut(uint location, uint owner, uint flags)
    {
        var image = WithPlayers();
        image.AddItem(10, 1, location, owner, flags: flags);
        var inv = Read(image);
        Assert.Empty(inv.AllItems);
        Assert.Empty(inv.Belt.Items);
    }

    [Fact]
    public void InventoryItem_WithoutThreeStashUnits_PanicsLikeGo()
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = MainId, Name = "Main", IsMain = true });
        image.AddItem(10, 1, 0, MainId);
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(image));

        // Owner 2 is decided before any stash unit is looked at.
        var ok = new GameImage();
        ok.AddPlayer(new PlayerSpec { UnitId = MainId, Name = "Main", IsMain = true });
        ok.AddItem(10, 1, 0, 2);
        Assert.Equal("shared_stash", Assert.Single(Read(ok).AllItems).Location.LocationType.Value);
    }

    [Fact]
    public void EquippedBelt_NamesTheBelt()
    {
        var image = WithPlayers();
        image.AddItem((uint)TestTables.BeltLikeNameId, 1, 1, MainId);
        image.AddItem((uint)TestTables.BeltId, 2, 1, MainId);
        image.AddItem((uint)TestTables.BeltId, 3, 1, 77); // not ours: unknown, does not name the belt
        var inv = Read(image);

        Assert.Equal(new ItemName("Belt"), inv.Belt.Name);
        Assert.Equal([1, 2], inv.AllItems.Select(i => i.UnitId.Value).Order());
    }

    [Fact]
    public void Items_HaveTheirFieldsAndFlags()
    {
        var image = WithPlayers();
        image.AddItem(0, 0x8000_0001, 3, 0, quality: 7, flags: 0x00400000 | 0x00000010 | 0x04000000, x: 300, y: 400);
        image.AddItem(1, 2, 3, 0, quality: 4, flags: 0x00000010, x: 101, y: 100);
        image.AddItem(2, 3, 4, 0, unitType: 3); // not an item unit
        var inv = Read(image);

        Assert.Equal(2, inv.AllItems.Count);
        var near = inv.AllItems[0];
        Assert.Equal(1, near.Id);
        Assert.Equal(ItemNames.GetNameByEnum(1), near.Name);
        Assert.Equal(ItemQuality.Magic, near.Quality);
        Assert.Equal(new Position(101, 100), near.Position);
        Assert.True(near.Identified);
        Assert.False(near.Ethereal);
        Assert.False(near.IsRuneword);

        var far = inv.AllItems[1];
        Assert.Equal(new UnitId(0x8000_0001L), far.UnitId);
        Assert.Equal(ItemQuality.Unique, far.Quality);
        Assert.True(far.Ethereal && far.Identified && far.IsRuneword);
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

        Assert.Equal([2, 1, 3], inv.AllItems.Select(i => i.UnitId.Value));
        Assert.Equal([4, 5], inv.Belt.Items.Select(i => i.UnitId.Value));
    }

    [Fact]
    public void Items_Hover_NeedsUnitType4()
    {
        var image = WithPlayers();
        image.AddItem(1, 9, 3, 0);
        image.SetHover(1, 4, 9);
        using (var reader = image.Reader())
            Assert.True(Assert.Single(reader.Inventory(reader.GetRawPlayerUnits(), reader.HoveredData()).AllItems).IsHovered);

        image.SetHover(1, 2, 9);
        using (var reader = image.Reader())
            Assert.False(Assert.Single(reader.Inventory(reader.GetRawPlayerUnits(), reader.HoveredData()).AllItems).IsHovered);
    }

    [Fact]
    public void Items_ReadTheirStats()
    {
        var image = WithPlayers();
        var stats = image.ItemStats(baseStats: [new RawStat(StatId.Defense, 50)], fullStats: [new RawStat(StatId.Defense, 75), new RawStat(StatId.MaxLife, 20 << 8)]);
        image.AddItem(1, 1, 3, 0, statsEx: stats);
        var item = Assert.Single(Read(image).AllItems);

        Assert.Equal([new StatData(StatId.Defense, 50, 0)], item.BaseStats);
        Assert.Equal([new StatData(StatId.Defense, 75, 0), new StatData(StatId.MaxLife, 20, 0)], item.Stats);
    }

    [Fact]
    public void GetItemStats_WithoutTheModifierFlag_IsTheTwoLists()
    {
        var image = new GameImage();
        var node = image.StatListNode(0x80000000, [new RawStat(StatId.Dexterity, 1)]);
        var stats = image.ItemStats([new RawStat(StatId.Strength, 5)], [new RawStat(StatId.Strength, 10)], flags: 0x7FFF_FFFF, lastStatsList: node);
        using var reader = image.Reader();

        var (b, f) = reader.GetItemStats((nuint)stats);
        Assert.Equal([new StatData(StatId.Strength, 5, 0)], b);
        Assert.Equal([new StatData(StatId.Strength, 10, 0)], f);
    }

    [Fact]
    public void GetItemStats_WithModifiers_FollowsGo()
    {
        var image = new GameImage();
        var n3 = image.StatListNode(0, [new RawStat(StatId.Energy, 4)]);
        var n2 = image.StatListNode(0x2040, [new RawStat(StatId.Vitality, 3), new RawStat(StatId.Strength, 1), new RawStat(StatId.Vitality, 3)], n3);
        var n1 = image.StatListNode(0x80000000, [new RawStat(StatId.Dexterity, 7), new RawStat(StatId.Strength, 99), new RawStat(StatId.Strength, 8, Layer: 1)], n2);
        var stats = image.ItemStats([new RawStat(StatId.Strength, 5)], [new RawStat(StatId.Strength, 10)], flags: 0x8000_0000, lastStatsList: n1);
        using var reader = image.Reader();

        var (b, f) = reader.GetItemStats((nuint)stats);

        // Full: the stats of the first list with flag 0x40 that are not in full yet (checked against full before any
        // is added, so the duplicate Vitality is added twice), then the modifier stats that are still missing; an
        // existing stat keeps its value (Go assigns to a copy).
        Assert.Equal(
            [
                new StatData(StatId.Strength, 10, 0),
                new StatData(StatId.Vitality, 3, 0),
                new StatData(StatId.Vitality, 3, 0),
                new StatData(StatId.Dexterity, 7, 0),
                new StatData(StatId.Strength, 8, 1),
            ],
            f);
        // Base: only the stats of lists whose flags are exactly 0x80000000.
        Assert.Equal([new StatData(StatId.Strength, 5, 0), new StatData(StatId.Dexterity, 7, 0), new StatData(StatId.Strength, 8, 1)], b);
    }

    [Fact]
    public void GetItemStats_WithoutA0x40List_AddsOnlyModifiers()
    {
        var image = new GameImage();
        var n2 = image.StatListNode(0x1, [new RawStat(StatId.Energy, 4)]);
        var n1 = image.StatListNode(0x8000_0000_0000_0000, [new RawStat(StatId.Dexterity, 7)], n2);
        var stats = image.ItemStats([], [], flags: 0x8000_0000, lastStatsList: n1);
        using var reader = image.Reader();

        var (b, f) = reader.GetItemStats((nuint)stats);
        Assert.Equal([new StatData(StatId.Dexterity, 7, 0), new StatData(StatId.Energy, 4, 0)], f);
        // 0x8000000000000000 is not exactly 0x80000000: the flags are read as 64 bits.
        Assert.Empty(b);
    }

    [Fact]
    public void SetProperties_SetsOnlyTheFlaggedProperties()
    {
        var none = GameReader.SetProperties(new GameItem(), 0xFBBF_FFEF);
        Assert.False(none.Ethereal || none.Identified || none.IsRuneword);
        Assert.True(GameReader.SetProperties(new GameItem(), 0x0040_0000).Ethereal);
        Assert.True(GameReader.SetProperties(new GameItem(), 0x0000_0010).Identified);
        Assert.True(GameReader.SetProperties(new GameItem(), 0x0400_0000).IsRuneword);
    }

    [Fact]
    public void Inventory_RejectsNull()
    {
        using var reader = new GameImage().Reader();
        Assert.Throws<ArgumentNullException>(() => reader.Inventory(null!, default));
    }
}

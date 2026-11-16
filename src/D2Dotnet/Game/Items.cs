using D2Dotnet.Data;

namespace D2Dotnet.Game;

/// <summary>An item's quality, numbered as the game numbers them.</summary>
public enum ItemQuality
{
    /// <summary>Low quality (cracked, damaged...).</summary>
    LowQuality = 0x01,

    /// <summary>Normal.</summary>
    Normal = 0x02,

    /// <summary>Superior.</summary>
    Superior = 0x03,

    /// <summary>Magic.</summary>
    Magic = 0x04,

    /// <summary>Set item.</summary>
    Set = 0x05,

    /// <summary>Rare.</summary>
    Rare = 0x06,

    /// <summary>Unique.</summary>
    Unique = 0x07,

    /// <summary>Crafted.</summary>
    Crafted = 0x08,
}

/// <summary>Normal, exceptional or elite: which of its base item's three versions an item is.</summary>
public enum ItemTier
{
    /// <summary>The normal version.</summary>
    Normal = 0,

    /// <summary>The exceptional version.</summary>
    Exceptional = 1,

    /// <summary>The elite version.</summary>
    Elite = 2,
}

/// <summary>Where an item is.</summary>
public enum ItemLocation
{
    /// <summary>In the main player's inventory.</summary>
    Inventory,

    /// <summary>Equipped by the main player.</summary>
    Equipped,

    /// <summary>In the main player's belt (see <see cref="Inventory.Belt"/>).</summary>
    Belt,

    /// <summary>On the ground (or dropping to it).</summary>
    Ground,

    /// <summary>On the mouse cursor.</summary>
    Cursor,

    /// <summary>Socketed into another item.</summary>
    Socket,

    /// <summary>In the main player's personal stash tab.</summary>
    Stash,

    /// <summary>In one of the shared stash tabs (<see cref="Item.Page"/> 1 to 3).</summary>
    SharedStash,

    /// <summary>For sale at a vendor.</summary>
    Vendor,
}

/// <summary>A kind of potion that goes in the belt.</summary>
public enum PotionType
{
    /// <summary>Healing potions (item type <c>hpot</c>).</summary>
    Healing,

    /// <summary>Mana potions (item type <c>mpot</c>).</summary>
    Mana,

    /// <summary>Rejuvenation potions (item type <c>rpot</c>).</summary>
    Rejuvenation,
}

/// <summary>An item unit.</summary>
public sealed record Item
{
    /// <summary>The unit id.</summary>
    public UnitId Id { get; init; }

    /// <summary>The base item (its row across weapons.txt, armor.txt and misc.txt).</summary>
    public ItemId Class { get; init; }

    /// <summary>The base item's row from the game data; null when the game data has no such row.</summary>
    public ItemInfo? Info { get; init; }

    /// <summary>The quality.</summary>
    public ItemQuality Quality { get; init; }

    /// <summary>
    /// The position: the cell of the inventory, stash or belt (in the belt, X is the column and Y the row, 0 at the
    /// bottom), or the world position on the ground.
    /// </summary>
    public Point Position { get; init; }

    /// <summary>Where the item is.</summary>
    public ItemLocation Location { get; init; }

    /// <summary>
    /// For <see cref="ItemLocation.SharedStash"/> the shared stash tab, 1 to 3; for <see cref="ItemLocation.Stash"/> and
    /// <see cref="ItemLocation.Inventory"/> 0; otherwise the inventory page byte the game stores.
    /// </summary>
    public byte Page { get; init; }

    /// <summary>True for an ethereal item.</summary>
    public bool IsEthereal { get; init; }

    /// <summary>True for an identified item.</summary>
    public bool IsIdentified { get; init; }

    /// <summary>True for a runeword.</summary>
    public bool IsRuneword { get; init; }

    /// <summary>True when the mouse is over the item.</summary>
    public bool IsHovered { get; init; }

    /// <summary>The item's stats, modifiers included.</summary>
    public StatList Stats { get; init; } = StatList.Empty;

    /// <summary>The item's base stats.</summary>
    public StatList BaseStats { get; init; } = StatList.Empty;

    /// <summary>The value of a stat from <see cref="Stats"/>, else from <see cref="BaseStats"/>; null when neither has it.</summary>
    public int? GetStat(StatId id, ushort layer = 0) => Stats.GetValue(id, layer) ?? BaseStats.GetValue(id, layer);

    /// <summary>The base item's type code (see <see cref="ItemTypeCode"/>), or null when <see cref="Info"/> is unknown.</summary>
    public string? TypeCode => Info?.Type;

    /// <summary>
    /// Normal, exceptional or elite, from the base item's tier codes; null for base items without tier codes (misc.txt
    /// items) or when <see cref="Info"/> is unknown.
    /// </summary>
    public ItemTier? Tier => Info is not { NormalCode.Length: > 0 } info ? null
        : info.Code == info.UltraCode ? ItemTier.Elite
        : info.Code == info.UberCode ? ItemTier.Exceptional
        : ItemTier.Normal;

    /// <summary>True when the base item's type is exactly <paramref name="typeCode"/> (parent types are not considered).</summary>
    public bool IsType(string typeCode) => string.Equals(TypeCode, typeCode, StringComparison.Ordinal);

    /// <summary>A healing potion.</summary>
    public bool IsHealingPotion => IsType(ItemTypeCode.HealingPotion);

    /// <summary>A mana potion.</summary>
    public bool IsManaPotion => IsType(ItemTypeCode.ManaPotion);

    /// <summary>A rejuvenation potion.</summary>
    public bool IsRejuvenationPotion => IsType(ItemTypeCode.RejuvPotion);

    /// <summary>A healing, mana or rejuvenation potion.</summary>
    public bool IsPotion => IsHealingPotion || IsManaPotion || IsRejuvenationPotion;

    /// <summary>A quest item (item type <c>ques</c>).</summary>
    public bool IsQuestItem => IsType(ItemTypeCode.Quest);

    /// <summary>True when the item is a potion of <paramref name="type"/>.</summary>
    public bool IsPotionOf(PotionType type) => type switch
    {
        PotionType.Healing => IsHealingPotion,
        PotionType.Mana => IsManaPotion,
        PotionType.Rejuvenation => IsRejuvenationPotion,
        _ => false,
    };
}

/// <summary>The equipped belt and the items in it.</summary>
public sealed record Belt
{
    /// <summary>The items in the belt.</summary>
    public IReadOnlyList<Item> Items { get; init; } = [];

    /// <summary>The equipped belt's base item, or null without a belt.</summary>
    public ItemId? Equipped { get; init; }

    /// <summary>The belt's rows: 1 without a belt, 2 for a sash or light belt, 3 for a belt or heavy belt, else 4.</summary>
    public int Rows => Equipped switch
    {
        null => 1,
        ItemId.Sash or ItemId.LightBelt => 2,
        ItemId.Belt or ItemId.HeavyBelt => 3,
        _ => 4,
    };

    /// <summary>The first potion of <paramref name="type"/> in the bottom row (row 0, columns 0 to 3), or null.</summary>
    public Item? FindPotion(PotionType type) =>
        Items.FirstOrDefault(i => i.IsPotionOf(type) && i.Position.Y == 0 && i.Position.X is >= 0 and <= 3);
}

/// <summary>The main player's items, the items around it, and its gold.</summary>
public sealed record Inventory
{
    /// <summary>
    /// Every known item except the belt's: equipped, inventory, stashes, cursor, sockets, vendors and the ground; nearest
    /// to the main player first.
    /// </summary>
    public IReadOnlyList<Item> Items { get; init; } = [];

    /// <summary>The belt.</summary>
    public Belt Belt { get; init; } = new();

    /// <summary>The gold in the inventory.</summary>
    public int Gold { get; init; }

    /// <summary>The gold in each stash tab: the personal tab, then the three shared tabs.</summary>
    public IReadOnlyList<int> StashGold { get; init; } = [0, 0, 0, 0];

    /// <summary>
    /// The first item of base item <paramref name="item"/> in one of <paramref name="locations"/>, or anywhere when none are
    /// given; null when there is none.
    /// </summary>
    public Item? Find(ItemId item, params ReadOnlySpan<ItemLocation> locations)
    {
        foreach (var it in Items)
        {
            if (it.Class == item && (locations.IsEmpty || locations.Contains(it.Location)))
                return it;
        }

        return null;
    }

    /// <summary>The items in any of <paramref name="locations"/>, in <see cref="Items"/> order, each once.</summary>
    public IReadOnlyList<Item> InLocation(params ReadOnlySpan<ItemLocation> locations)
    {
        var result = new List<Item>();
        foreach (var it in Items)
        {
            if (locations.Contains(it.Location))
                result.Add(it);
        }

        return result;
    }
}

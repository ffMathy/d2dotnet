// Port of d2go pkg/data/items.go and belt.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using D2Dotnet.D2Go.Data.Item;
using D2Dotnet.D2Go.Data.Stat;

namespace D2Dotnet.D2Go.Data;

/// <summary>The player's items and gold (Go <c>data.Inventory</c>).</summary>
public sealed record Inventory
{
    public Belt Belt { get; init; } = new();

    public IReadOnlyList<GameItem> AllItems { get; init; } = [];

    public int Gold { get; init; }

    /// <summary>Gold in each of the four stash tabs (Go <c>[4]int</c>).</summary>
    public IReadOnlyList<int> StashedGold { get; init; } = [0, 0, 0, 0];

    /// <summary>
    /// The first item whose name equals <paramref name="name"/> ignoring case and that is in one of
    /// <paramref name="locations"/>, or anywhere when none are given (Go <c>Find</c>). When there is none, returns
    /// false and <paramref name="item"/> is the zero value.
    /// </summary>
    public bool TryFind(ItemName name, out GameItem item, params ReadOnlySpan<LocationType> locations)
    {
        foreach (var it in AllItems)
        {
            if (GoStrings.EqualFold(it.Name.Value, name.Value))
            {
                // If no locations are specified, return the first item found
                if (locations.Length == 0)
                {
                    item = it;
                    return true;
                }

                foreach (var l in locations)
                {
                    if (it.Location.LocationType == l)
                    {
                        item = it;
                        return true;
                    }
                }
            }
        }

        item = new GameItem();
        return false;
    }

    /// <summary>
    /// The items in any of <paramref name="locations"/>, in inventory order (Go <c>ByLocation</c>). As in Go, an item
    /// is listed once per matching entry of <paramref name="locations"/>, so a repeated location repeats its items.
    /// </summary>
    public IReadOnlyList<GameItem> ByLocation(params ReadOnlySpan<LocationType> locations)
    {
        var items = new List<GameItem>();

        foreach (var it in AllItems)
        {
            foreach (var l in locations)
            {
                if (it.Location.LocationType == l)
                    items.Add(it);
            }
        }

        return items;
    }
}

/// <summary>An item unit (Go <c>data.Item</c>).</summary>
public sealed record GameItem
{
    /// <summary>The item id (the txt file number): a key of <see cref="GameTables.ItemDescriptions"/>.</summary>
    public int Id { get; init; }

    /// <summary>The unit id (Go's embedded <c>UnitID</c>).</summary>
    public UnitId UnitId { get; init; }

    public ItemName Name { get; init; }

    public ItemQuality Quality { get; init; }

    public Position Position { get; init; }

    public ItemLocation Location { get; init; }

    public bool Ethereal { get; init; }

    public bool IsHovered { get; init; }

    public Stats BaseStats { get; init; } = Stats.Empty;

    public Stats Stats { get; init; } = Stats.Empty;

    public bool Identified { get; init; }

    public bool IsRuneword { get; init; }

    /// <summary>The base item (Go <c>item.Desc[i.ID]</c>: the zero value when the id is unknown).</summary>
    public ItemDescription Desc(GameTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return tables.ItemDesc(Id);
    }

    /// <summary>The item type of the base item.</summary>
    public ItemType Type(GameTables tables) => Desc(tables).GetItemType(tables);

    /// <summary>A healing, mana or rejuvenation potion.</summary>
    public bool IsPotion(GameTables tables) => IsHealingPotion(tables) || IsManaPotion(tables) || IsRejuvPotion(tables);

    public bool IsHealingPotion(GameTables tables) => Type(tables).IsType(ItemTypeCodes.TypeHealingPotion);

    public bool IsManaPotion(GameTables tables) => Type(tables).IsType(ItemTypeCodes.TypeManaPotion);

    public bool IsRejuvPotion(GameTables tables) => Type(tables).IsType(ItemTypeCodes.TypeRejuvPotion);

    /// <summary>The item type is exactly <see cref="ItemTypeCodes.TypeQuest"/>.</summary>
    public bool IsFromQuest(GameTables tables) => Type(tables).IsType(ItemTypeCodes.TypeQuest);

    /// <summary>
    /// The stat from <see cref="Stats"/>, else from <see cref="BaseStats"/> (Go <c>FindStat</c>). When neither has
    /// it, returns false and <paramref name="stat"/> is the zero value.
    /// </summary>
    public bool TryFindStat(StatId id, int layer, out StatData stat) =>
        Stats.TryFindStat(id, layer, out stat) || BaseStats.TryFindStat(id, layer, out stat);
}

/// <summary>An item that matched a pickit rule (Go <c>data.Drop</c>).</summary>
public sealed record Drop
{
    public GameItem Item { get; init; } = new();

    public string Rule { get; init; } = "";

    public string RuleFile { get; init; } = "";

    public string DropLocation { get; init; } = "";
}

/// <summary>The equipped belt and the items in it (Go <c>data.Belt</c>).</summary>
public sealed record Belt
{
    public IReadOnlyList<GameItem> Items { get; init; } = [];

    /// <summary>The belt's item name; empty without a belt.</summary>
    public ItemName Name { get; init; }

    /// <summary>
    /// The position of the first potion of the bottom row (Y 0, X 0 to 3) whose name contains
    /// <paramref name="potionType"/> (Go <c>GetFirstPotion</c>). When there is none, returns false and
    /// <paramref name="position"/> is (0, 0).
    /// </summary>
    public bool TryGetFirstPotion(PotionType potionType, out Position position)
    {
        foreach (var i in Items)
        {
            // Ensure potion is in row 0 and one of the four columns
            if (i.Name.Value.Contains(potionType.Value, StringComparison.Ordinal) && i.Position.Y == 0
                && (i.Position.X == 0 || i.Position.X == 1 || i.Position.X == 2 || i.Position.X == 3))
            {
                position = i.Position;
                return true;
            }
        }

        position = default;
        return false;
    }

    /// <summary>The belt's rows: 1 without a belt, 2 for a sash or light belt, 3 for a belt or heavy belt, else 4.</summary>
    public int Rows()
    {
        switch (Name.Value)
        {
            case "":
                return 1;
            case "Sash":
            case "LightBelt":
                return 2;
            case "Belt":
            case "HeavyBelt":
                return 3;
            default:
                return 4;
        }
    }
}

// Port of d2go pkg/data/item/item.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data.Item;

/// <summary>
/// The base item of an item id: a row of weapons.txt, armor.txt or misc.txt (Go <c>item.Description</c>). Instances
/// come from <see cref="GameTables.ItemDescriptions"/>; the default instance is Go's zero value.
/// </summary>
public sealed record ItemDescription
{
    /// <summary>The item id: the row index across weapons.txt, armor.txt and misc.txt, in that order.</summary>
    public int Id { get; init; }

    /// <summary>The <c>name</c> column, with d2go's spelling fixes (see <see cref="GameTables"/>).</summary>
    public string Name { get; init; } = "";

    /// <summary>The <c>code</c> column.</summary>
    public string Code { get; init; } = "";

    /// <summary>The <c>normcode</c> column: the code of the normal version.</summary>
    public string NormalCode { get; init; } = "";

    /// <summary>The <c>ubercode</c> column: the code of the exceptional version.</summary>
    public string UberCode { get; init; } = "";

    /// <summary>The <c>ultracode</c> column: the code of the elite version.</summary>
    public string UltraCode { get; init; } = "";

    /// <summary>The <c>invwidth</c> column.</summary>
    public int InventoryWidth { get; init; }

    /// <summary>The <c>invheight</c> column.</summary>
    public int InventoryHeight { get; init; }

    /// <summary>The <c>minac</c> column, 0 when the file has none.</summary>
    public int MinDefense { get; init; }

    /// <summary>The <c>maxac</c> column, 0 when the file has none.</summary>
    public int MaxDefense { get; init; }

    /// <summary>The <c>type</c> column: an item type code, see <see cref="ItemTypeCodes"/>.</summary>
    public string Type { get; init; } = "";

    /// <summary>
    /// Elite when <see cref="Code"/> equals <see cref="UltraCode"/>, else exceptional when it equals
    /// <see cref="UberCode"/>, else normal. Items without tier codes (misc.txt) are normal; the zero value, whose
    /// codes are all empty, is elite, as in Go.
    /// </summary>
    public ItemTier Tier()
    {
        if (Code == UltraCode)
            return ItemTier.Elite;

        if (Code == UberCode)
            return ItemTier.Exceptional;

        return ItemTier.Normal;
    }

    /// <summary>The item type of <see cref="Type"/> (Go <c>GetType</c>: <c>ItemTypes[d.Type]</c>, the zero value when missing).</summary>
    public ItemType GetItemType(GameTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return tables.ItemType(Type);
    }
}

/// <summary>
/// An item type: a row of itemtypes.txt (Go <c>item.Type</c>). Instances come from <see cref="GameTables.ItemTypes"/>;
/// the default instance is Go's zero value.
/// </summary>
public sealed record ItemType
{
    /// <summary>The row index in itemtypes.txt (rows with an empty second column are not counted).</summary>
    public int Id { get; init; }

    /// <summary>The <c>ItemType</c> column.</summary>
    public string Name { get; init; } = "";

    /// <summary>The <c>Code</c> column, see <see cref="ItemTypeCodes"/>.</summary>
    public string Code { get; init; } = "";

    /// <summary>The <c>Throwable</c> column is <c>1</c>.</summary>
    public bool Throwable { get; init; }

    /// <summary>The <c>Beltable</c> column is <c>1</c>.</summary>
    public bool Beltable { get; init; }

    /// <summary>True when <see cref="Code"/> is <paramref name="typeName"/>. Parent types are not considered (a TODO in Go).</summary>
    public bool IsType(string typeName) => Code == typeName;
}

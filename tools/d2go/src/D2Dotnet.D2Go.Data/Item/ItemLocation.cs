// Port of d2go pkg/data/item/location.go, quality.go and tier.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data.Item;

/// <summary>Where an item is, and the stash page for stash locations (Go <c>item.Location</c>).</summary>
public readonly record struct ItemLocation(LocationType LocationType, int Page);

/// <summary>Item quality (Go <c>item.Quality</c>); the values are the game's.</summary>
public enum ItemQuality
{
    LowQuality = 0x01,
    Normal = 0x02,
    Superior = 0x03,
    Magic = 0x04,
    Set = 0x05,
    Rare = 0x06,
    Unique = 0x07,
    Crafted = 0x08,
}

/// <summary>The methods of Go's <c>item.Quality</c>.</summary>
public static class ItemQualityExtensions
{
    /// <summary>
    /// The quality's name (Go <c>Quality.ToString</c>). As in Go, <see cref="ItemQuality.Crafted"/> and any value
    /// that is not a member give <c>"UnknownItemQuality"</c>.
    /// </summary>
    public static string ToQualityString(this ItemQuality quality) => quality switch
    {
        ItemQuality.LowQuality => "LowQuality",
        ItemQuality.Normal => "Normal",
        ItemQuality.Superior => "Superior",
        ItemQuality.Magic => "Magic",
        ItemQuality.Set => "Set",
        ItemQuality.Rare => "Rare",
        ItemQuality.Unique => "Unique",
        _ => "UnknownItemQuality",
    };
}

/// <summary>Normal, exceptional or elite (Go <c>item.Tier</c>).</summary>
public enum ItemTier
{
    Normal = 0,
    Exceptional = 1,
    Elite = 2,
}

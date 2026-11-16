// Port of d2go pkg/data/item/location.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data.Item;

/// <summary>
/// Where an item is (Go <c>item.LocationType</c>).
/// </summary>
/// <remarks>
/// A Go named string type: any string converts to it implicitly, <see cref="Value"/> is never null (the default value is
/// the empty string, Go's zero value), and equality is ordinal on <see cref="Value"/>.
/// </remarks>
public readonly struct LocationType : IEquatable<LocationType>
{
    // Go LocationEquipped, LocationStash, ... (the Location prefix is dropped).
    public static readonly LocationType Equipped = new("equipped");
    public static readonly LocationType Stash = new("stash");
    public static readonly LocationType SharedStash = new("shared_stash");
    public static readonly LocationType Belt = new("belt");
    public static readonly LocationType Inventory = new("inventory");
    public static readonly LocationType Cube = new("cube");
    public static readonly LocationType Vendor = new("vendor");
    public static readonly LocationType Ground = new("ground");
    public static readonly LocationType Socket = new("socket");
    public static readonly LocationType Unknown = new("unknown");
    public static readonly LocationType Cursor = new("cursor");

    private readonly string? _value;

    public LocationType(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The string value; empty for the default value.</summary>
    public string Value => _value ?? "";

    public static implicit operator LocationType(string value) => new(value);

    public static bool operator ==(LocationType left, LocationType right) => left.Equals(right);

    public static bool operator !=(LocationType left, LocationType right) => !left.Equals(right);

    public bool Equals(LocationType other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is LocationType other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

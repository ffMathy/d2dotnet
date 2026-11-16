// Port of d2go pkg/data/item/name.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data.Item;

/// <summary>
/// An item name such as <c>"HealingPotion"</c> (Go <c>item.Name</c>); see <see cref="ItemNames"/>.
/// </summary>
/// <remarks>
/// A Go named string type: any string converts to it implicitly, <see cref="Value"/> is never null (the default value is
/// the empty string, Go's zero value), and equality is ordinal on <see cref="Value"/>.
/// </remarks>
public readonly struct ItemName : IEquatable<ItemName>
{
    private readonly string? _value;

    public ItemName(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The string value; empty for the default value.</summary>
    public string Value => _value ?? "";

    public static implicit operator ItemName(string value) => new(value);

    public static bool operator ==(ItemName left, ItemName right) => left.Equals(right);

    public static bool operator !=(ItemName left, ItemName right) => !left.Equals(right);

    public bool Equals(ItemName other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ItemName other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

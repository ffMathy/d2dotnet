// Port of d2go pkg/data/belt.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data;

/// <summary>
/// A kind of belt potion, matched against item names (Go <c>data.PotionType</c>).
/// </summary>
/// <remarks>
/// A Go named string type: any string converts to it implicitly, <see cref="Value"/> is never null (the default value is
/// the empty string, Go's zero value), and equality is ordinal on <see cref="Value"/>.
/// </remarks>
public readonly struct PotionType : IEquatable<PotionType>
{
    public static readonly PotionType HealingPotion = new("HealingPotion");
    public static readonly PotionType ManaPotion = new("ManaPotion");
    public static readonly PotionType RejuvenationPotion = new("RejuvenationPotion");

    private readonly string? _value;

    public PotionType(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The string value; empty for the default value.</summary>
    public string Value => _value ?? "";

    public static implicit operator PotionType(string value) => new(value);

    public static bool operator ==(PotionType left, PotionType right) => left.Equals(right);

    public static bool operator !=(PotionType left, PotionType right) => !left.Equals(right);

    public bool Equals(PotionType other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is PotionType other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

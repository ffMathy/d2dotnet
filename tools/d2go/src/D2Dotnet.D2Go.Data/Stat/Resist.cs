// Port of d2go pkg/data/stat/immune.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data.Stat;

/// <summary>
/// An immunity kind (Go <c>stat.Resist</c>).
/// </summary>
/// <remarks>
/// A Go named string type: any string converts to it implicitly, <see cref="Value"/> is never null (the default value is
/// the empty string, Go's zero value), and equality is ordinal on <see cref="Value"/>.
/// </remarks>
public readonly struct Resist : IEquatable<Resist>
{
    public static readonly Resist ColdImmune = new("cold");
    public static readonly Resist FireImmune = new("fire");
    public static readonly Resist LightImmune = new("light");
    public static readonly Resist PoisonImmune = new("poison");
    public static readonly Resist MagicImmune = new("magic");

    private readonly string? _value;

    public Resist(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The string value; empty for the default value.</summary>
    public string Value => _value ?? "";

    public static implicit operator Resist(string value) => new(value);

    public static bool operator ==(Resist left, Resist right) => left.Equals(right);

    public static bool operator !=(Resist left, Resist right) => !left.Equals(right);

    public bool Equals(Resist other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Resist other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

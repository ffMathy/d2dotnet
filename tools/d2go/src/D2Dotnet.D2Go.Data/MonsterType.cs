// Port of d2go pkg/data/data.go and npc.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data;

/// <summary>
/// The kind of a monster (Go <c>data.MonsterType</c>).
/// </summary>
/// <remarks>
/// A Go named string type: any string converts to it implicitly, <see cref="Value"/> is never null (the default value is
/// the empty string, Go's zero value), and equality is ordinal on <see cref="Value"/>.
/// </remarks>
public readonly struct MonsterType : IEquatable<MonsterType>
{
    // Go MonsterTypeNone, MonsterTypeChampion, MonsterTypeMinion, MonsterTypeUnique, MonsterTypeSuperUnique.
    public static readonly MonsterType None = new("None");
    public static readonly MonsterType Champion = new("Champion");
    public static readonly MonsterType Minion = new("Minion");
    public static readonly MonsterType Unique = new("Unique");
    public static readonly MonsterType SuperUnique = new("SuperUnique");

    private readonly string? _value;

    public MonsterType(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The string value; empty for the default value.</summary>
    public string Value => _value ?? "";

    public static implicit operator MonsterType(string value) => new(value);

    public static bool operator ==(MonsterType left, MonsterType right) => left.Equals(right);

    public static bool operator !=(MonsterType left, MonsterType right) => !left.Equals(right);

    public bool Equals(MonsterType other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is MonsterType other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

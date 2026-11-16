// Port of d2go pkg/data/difficulty/difficulty.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data.Difficulty;

/// <summary>
/// A game difficulty (Go <c>difficulty.Difficulty</c>).
/// </summary>
/// <remarks>
/// A Go named string type: any string converts to it implicitly, <see cref="Value"/> is never null (the default value is
/// the empty string, Go's zero value), and equality is ordinal on <see cref="Value"/>.
/// </remarks>
public readonly struct GameDifficulty : IEquatable<GameDifficulty>
{
    // Go untyped constants difficulty.Normal, Nightmare and Hell: usable as a string or, implicitly, as a GameDifficulty.
    public const string Normal = "normal";
    public const string Nightmare = "nightmare";
    public const string Hell = "hell";

    private readonly string? _value;

    public GameDifficulty(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The string value; empty for the default value.</summary>
    public string Value => _value ?? "";

    public static implicit operator GameDifficulty(string value) => new(value);

    public static bool operator ==(GameDifficulty left, GameDifficulty right) => left.Equals(right);

    public static bool operator !=(GameDifficulty left, GameDifficulty right) => !left.Equals(right);

    public bool Equals(GameDifficulty other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is GameDifficulty other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

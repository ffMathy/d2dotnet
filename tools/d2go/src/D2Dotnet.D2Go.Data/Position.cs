// Port of d2go pkg/data/data.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data;

/// <summary>A point in game coordinates (Go <c>data.Position</c>).</summary>
public readonly record struct Position(int X, int Y);

/// <summary>A unit id (Go <c>data.UnitID</c>, a 64-bit <c>int</c>; the game's 32-bit unsigned ids are never negative).</summary>
public readonly record struct UnitId(long Value)
{
    public static explicit operator UnitId(long value) => new(value);

    public static explicit operator long(UnitId id) => id.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

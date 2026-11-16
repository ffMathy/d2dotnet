// Port of d2go pkg/data/data.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data;

/// <summary>A point in game coordinates (Go <c>data.Position</c>).</summary>
public readonly record struct Position(int X, int Y);

/// <summary>A unit id (Go <c>data.UnitID</c>, an <c>int</c>).</summary>
public readonly record struct UnitId(int Value)
{
    public static explicit operator UnitId(int value) => new(value);

    public static explicit operator int(UnitId id) => id.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

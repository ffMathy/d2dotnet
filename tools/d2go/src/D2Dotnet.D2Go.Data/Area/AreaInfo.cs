// Port of d2go pkg/data/area/area.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data.Area;

/// <summary>
/// A level: its id and its <c>LevelName</c> from levels.txt (Go <c>area.Area</c>). Instances come from
/// <see cref="GameTables.Areas"/>; the default instance is Go's zero value.
/// </summary>
public sealed record AreaInfo
{
    /// <summary>The level id (Go's embedded <c>ID</c>).</summary>
    public AreaId Id { get; init; }

    /// <summary>The <c>LevelName</c> column of levels.txt.</summary>
    public string Name { get; init; } = "";

    // Methods promoted from the embedded area.ID in Go.

    /// <inheritdoc cref="AreaIdExtensions.IsTown"/>
    public bool IsTown() => Id.IsTown();

    /// <inheritdoc cref="AreaIdExtensions.CanBeTerrorized"/>
    public bool CanBeTerrorized() => Id.CanBeTerrorized();

    /// <inheritdoc cref="AreaIdExtensions.Act"/>
    public int Act() => Id.Act();

    /// <inheritdoc cref="AreaIdExtensions.Area"/>
    public AreaInfo Area(GameTables tables) => Id.Area(tables);
}

/// <summary>The methods of Go's <c>area.ID</c>.</summary>
public static class AreaIdExtensions
{
    /// <summary>True for the five towns.</summary>
    public static bool IsTown(this AreaId area) =>
        area is AreaId.RogueEncampment or AreaId.LutGholein or AreaId.KurastDocks or AreaId.ThePandemoniumFortress or AreaId.Harrogath;

    /// <summary>True when the area is in <see cref="TerrorZones.CanBeTerrorized"/>.</summary>
    public static bool CanBeTerrorized(this AreaId area) => TerrorZones.CanBeTerrorized.Contains(area);

    /// <summary>The act (1 to 5) by id range; ids below 40, including negative ones, are act 1 and ids from 109 up are act 5.</summary>
    public static int Act(this AreaId area)
    {
        var a = (int)area;
        if (a < 40)
            return 1;
        if (a >= 40 && a < 75)
            return 2;
        if (a >= 75 && a < 103)
            return 3;
        if (a >= 103 && a < 109)
            return 4;

        return 5;
    }

    /// <summary>The level of this id in <paramref name="tables"/> (Go <c>Areas[a]</c>: the zero value when missing).</summary>
    public static AreaInfo Area(this AreaId area, GameTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return tables.Area(area);
    }
}

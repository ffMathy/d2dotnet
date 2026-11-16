using System.Globalization;

namespace D2Dotnet.Game;

/// <summary>The id the game gives a unit (player, monster, object, item) for as long as it exists: 32 bits, unsigned.</summary>
/// <param name="Value">The id.</param>
public readonly record struct UnitId(uint Value)
{
    /// <inheritdoc/>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The kind of a unit, as the game numbers its unit tables.</summary>
public enum UnitType : uint
{
    /// <summary>A player (or a player's corpse, or a shared stash unit).</summary>
    Player = 0,

    /// <summary>A monster, NPC or mercenary.</summary>
    Monster = 1,

    /// <summary>An object: chest, shrine, door, waypoint, portal...</summary>
    Object = 2,

    /// <summary>A missile.</summary>
    Missile = 3,

    /// <summary>An item.</summary>
    Item = 4,

    /// <summary>A room tile (level exit).</summary>
    Tile = 5,
}

/// <summary>The unit under the mouse cursor.</summary>
/// <param name="Type">Its kind.</param>
/// <param name="Id">Its id.</param>
public readonly record struct HoveredUnit(UnitType Type, UnitId Id)
{
    /// <summary>True when this is the unit of kind <paramref name="type"/> with id <paramref name="id"/>.</summary>
    public bool Is(UnitType type, UnitId id) => Type == type && Id == id;
}

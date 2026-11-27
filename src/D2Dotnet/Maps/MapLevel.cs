namespace D2Dotnet.Maps;

/// <summary>How the game lays a level out: the <c>DrlgType</c> column of levels.txt.</summary>
public enum LevelLayout
{
    /// <summary>No layout (the unused rows).</summary>
    None = 0,

    /// <summary>A maze of rooms (dungeons, caves, catacombs).</summary>
    Maze = 1,

    /// <summary>One or more fixed preset maps (towns, most quest levels).</summary>
    Preset = 2,

    /// <summary>An outdoor level (wilderness) built from borders, fillers and sub-presets.</summary>
    Wilderness = 3,
}

/// <summary>What a <see cref="PresetUnit"/> is.</summary>
public enum PresetKind
{
    /// <summary>A monster or NPC; see <see cref="PresetUnit.ClassId"/>.</summary>
    Npc = 1,

    /// <summary>An object (an objects.txt row).</summary>
    Object = 2,
}

/// <summary>How a <see cref="LevelExit"/> is crossed.</summary>
public enum ExitKind
{
    /// <summary>A warp: a door, stairs or a cave entrance in one room.</summary>
    Warp,

    /// <summary>An open border between two outdoor levels that touch.</summary>
    Seam,
}

/// <summary>One room of a level.</summary>
/// <param name="Bounds">Where the room is, in world tiles.</param>
/// <param name="Type">The engine's room type (<c>D2RoomExStrc.nType</c>).</param>
/// <param name="PresetType">The engine's room preset type (<c>D2RoomExStrc.nPresetType</c>): 1 for an outdoor or
/// maze room, 2 for a room of a preset (DS1) map.</param>
/// <param name="PickedFile">For a preset room the DS1 file variant picked; for an outdoor room the sub-theme variant
/// picked; -1 otherwise.</param>
public readonly record struct MapRoom(Rect Bounds, int Type, int PresetType, int PickedFile);

/// <summary>A unit the level places: an object or a monster/NPC.</summary>
/// <param name="Kind">An object or a monster/NPC.</param>
/// <param name="ClassId">For an object, its objects.txt row (an <see cref="ObjectId"/>). For a monster, its monstats.txt
/// game row (a <see cref="MonsterId"/>); a monster a DS1 map names through monpreset.txt is reported the way libd2 does,
/// in one id space of the monstats.txt game rows, then the superuniques.txt game rows (a super unique is
/// <c>monster count + its index</c>), then the monplace.txt rows.</param>
/// <param name="Position">Where it is, in level-local subtiles.</param>
public readonly record struct PresetUnit(PresetKind Kind, int ClassId, Point Position);

/// <summary>A way from one level to another.</summary>
/// <param name="Destination">The level it leads to.</param>
/// <param name="Position">The centre of the room that holds it, in level-local subtiles.</param>
/// <param name="Kind">A warp or an open border.</param>
public readonly record struct LevelExit(AreaId Destination, Point Position, ExitKind Kind);

/// <summary>A shrine or well the game seeds into an Act I outdoor room.</summary>
/// <param name="Object">The object class.</param>
/// <param name="Position">Where it is, in level-local subtiles.</param>
public readonly record struct Shrine(ObjectId Object, Point Position);

/// <summary>One generated level of a <see cref="GeneratedAct"/>.</summary>
/// <remarks>
/// <para>Room and level bounds are world tiles. Everything inside a level (presets, exits, shrines, the collision map)
/// is in level-local subtiles: a tile is 5 by 5 subtiles, and (0, 0) is the level's top-left corner.</para>
/// <para>Names and other descriptive data are in the game's tables: see <see cref="Data.GameDatabase.Levels"/> and
/// <see cref="Data.Localization.GetLevelName"/>.</para>
/// <para><see cref="MapGenerator"/> fills every property; the type can be built directly for tests.</para>
/// </remarks>
public sealed class MapLevel
{
    /// <summary>The number of subtiles along a tile's side.</summary>
    public const int SubtilesPerTile = 5;

    /// <summary>The level.</summary>
    public AreaId Id { get; init; }

    /// <summary>How the level is laid out.</summary>
    public LevelLayout Layout { get; init; }

    /// <summary>True when the act's placement graph positioned the level (the connected overworld); false for
    /// interiors laid out from the <c>Depend</c> offset chain. Either way <see cref="Bounds"/> is where the game puts the
    /// level (checked in a running game for the Den of Evil, an interior).</summary>
    public bool IsPlaced { get; init; }

    /// <summary>Where the level is, in world tiles.</summary>
    public Rect Bounds { get; init; }

    /// <summary>The level's top-left corner in world subtiles, the unit positions read from a running game use.</summary>
    public Point Origin => new(Bounds.X * SubtilesPerTile, Bounds.Y * SubtilesPerTile);

    /// <summary>A world subtile (as a running game reports positions) in level-local subtiles.</summary>
    public Point ToLocal(Point world) => new(world.X - Origin.X, world.Y - Origin.Y);

    /// <summary>A level-local subtile (as <see cref="Presets"/>, <see cref="Exits"/> and the collision map use) in world subtiles.</summary>
    public Point ToWorld(Point local) => new(local.X + Origin.X, local.Y + Origin.Y);

    /// <summary>The rooms, in the engine's order.</summary>
    public IReadOnlyList<MapRoom> Rooms { get; init; } = [];

    /// <summary>The units the level places (objects, monsters and NPCs), in the engine's room order.</summary>
    public IReadOnlyList<PresetUnit> Presets { get; init; } = [];

    /// <summary>The warps and open borders to other levels.</summary>
    public IReadOnlyList<LevelExit> Exits { get; init; } = [];

    /// <summary>The seeded outdoor shrines and wells (Act I wilderness).</summary>
    public IReadOnlyList<Shrine> Shrines { get; init; } = [];

    /// <summary>The waypoint objects among <see cref="Presets"/>.</summary>
    public IReadOnlyList<PresetUnit> Waypoints { get; init; } = [];

    /// <summary>The collision map, the size of <see cref="Bounds"/> in subtiles; null when the level built no room.</summary>
    public CollisionMap? Collision { get; init; }

    /// <inheritdoc/>
    public override string ToString() => $"{Id} {Bounds}";
}

/// <summary>Every level of one act, generated for a seed and a difficulty.</summary>
/// <remarks><see cref="MapGenerator"/> fills every property; the type can be built directly for tests.</remarks>
public sealed class GeneratedAct
{
    /// <summary>The act.</summary>
    public ActId Act { get; init; }

    /// <summary>The map seed.</summary>
    public uint Seed { get; init; }

    /// <summary>The difficulty.</summary>
    public Difficulty Difficulty { get; init; }

    /// <summary>Act II only: the Tal Rasha's Tomb that holds the Horadric orifice (one of the seven tombs,
    /// <see cref="AreaId.TalRashasTomb1"/> to <see cref="AreaId.TalRashasTomb7"/>); null in the other acts.</summary>
    public AreaId? StaffTomb { get; init; }

    /// <summary>The act's levels, in levels.txt order.</summary>
    public IReadOnlyList<MapLevel> Levels { get; init; } = [];

    /// <summary>The level with this id, or null when the act has none.</summary>
    public MapLevel? Find(AreaId id)
    {
        foreach (var level in Levels)
        {
            if (level.Id == id)
                return level;
        }

        return null;
    }

    /// <summary>The level with this id.</summary>
    /// <exception cref="KeyNotFoundException">The act has no such level.</exception>
    public MapLevel this[AreaId id] => Find(id) ?? throw new KeyNotFoundException($"{Act} has no level {id}.");

    /// <inheritdoc/>
    public override string ToString() => $"{Act} {Difficulty} seed {Seed} ({Levels.Count} levels)";
}

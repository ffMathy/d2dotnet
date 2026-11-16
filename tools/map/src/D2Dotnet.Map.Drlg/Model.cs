namespace D2Dotnet.Map.Drlg;

/// <summary>Game difficulty (Levels.txt / LvlMaze.txt use separate columns per difficulty).</summary>
public enum Difficulty
{
    Normal = 0,
    Nightmare = 1,
    Hell = 2,
}

/// <summary>Levels.txt DrlgType.</summary>
public enum DrlgType
{
    None = 0,
    Maze = 1,
    Preset = 2,
    Wilderness = 3,
}

/// <summary>Kind of a preset unit.</summary>
public enum PresetUnitType
{
    /// <summary>A monster or NPC (MonStats row, or a DBM monster code; see <see cref="PresetUnit.ClassId"/>).</summary>
    Npc = 1,
    /// <summary>An object (Objects.txt row).</summary>
    Object = 2,
}

/// <summary>How an exit is reached.</summary>
public enum ExitKind
{
    /// <summary>A per-room warp door (dungeon entrance, stairs).</summary>
    Warp,
    /// <summary>A cross-level border bridge between two edge-to-edge outdoor levels.</summary>
    Seam,
}

/// <summary>One room of a level. X/Y are world tiles, Width/Height tiles. Type is
/// D2RoomExStrc.nType, PresetType D2RoomExStrc.nPresetType (1 outdoor/maze shell, 2 preset/DS1),
/// PickedFile the room's DS1/theme selector (preset rooms: the picked DS1 file variant; outdoor
/// rooms: the picked sub-theme variant; -1 otherwise).</summary>
public readonly record struct Room(int X, int Y, int Width, int Height, int Type, int PresetType, int PickedFile);

/// <summary>A preset unit at level-local subtile (X, Y). ClassId is, for objects, the Objects.txt
/// row; for NPCs the MonStats row, or for a DS1 monster the DBM monster code when the engine
/// resolves one (as libd2 reports it).</summary>
public readonly record struct PresetUnit(PresetUnitType Type, int ClassId, int X, int Y);

/// <summary>An exit/adjacency: the destination level and the bridge position in level-local
/// subtiles (the room centre of the warp room, or of the seam room).</summary>
public readonly record struct LevelExit(int DestinationLevelId, int X, int Y, ExitKind Kind);

/// <summary>A seeded outdoor shrine or well (Act I wilderness) at level-local subtile (X, Y);
/// ClassId is its Objects.txt row.</summary>
public readonly record struct Shrine(int ClassId, int X, int Y);

/// <summary>A level's raw subtile collision map (the engine's per-room CollMaps composited at
/// the level's WorldSize*5). Each cell is the raw COLLIDE_* flag byte; <see cref="Uncovered"/>
/// (0xFFFF) marks subtiles no room covers.</summary>
public sealed class CollisionGrid
{
    /// <summary>Value of a subtile no room covers.</summary>
    public const ushort Uncovered = 0xFFFF;

    public CollisionGrid(int width, int height, ushort[] cells)
    {
        if (cells.Length != width * height) throw new ArgumentException("cells must be width*height", nameof(cells));
        Width = width;
        Height = height;
        Cells = cells;
    }

    /// <summary>Width in subtiles.</summary>
    public int Width { get; }

    /// <summary>Height in subtiles.</summary>
    public int Height { get; }

    /// <summary>Row-major cells (Width*Height).</summary>
    public ushort[] Cells { get; }

    /// <summary>The raw flags at level-local subtile (x, y).</summary>
    public ushort this[int x, int y] => Cells[y * Width + x];

    /// <summary>Convenience view: the subtile is covered and COLLIDE_BLOCK_PLAYER (0x01) is clear.
    /// Read the raw flags for anything finer.</summary>
    public bool IsWalkable(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;
        ushort c = Cells[y * Width + x];
        return c != Uncovered && (c & 0x01) == 0;
    }
}

/// <summary>One generated level.</summary>
public sealed class Level
{
    internal Level() { }

    /// <summary>Levels.txt Id.</summary>
    public int Id { get; init; }

    /// <summary>Levels.txt LevelName (display name).</summary>
    public string Name { get; init; } = "";

    /// <summary>Levels.txt Name (internal name).</summary>
    public string InternalName { get; init; } = "";

    public DrlgType DrlgType { get; init; }

    /// <summary>True when the act placement graph positioned the level (the connected overworld);
    /// false for interiors laid out from the Depend offset chain.</summary>
    public bool Placed { get; init; }

    /// <summary>World tile origin of the level.</summary>
    public int OriginX { get; init; }

    public int OriginY { get; init; }

    /// <summary>Size in tiles (multiply by 5 for subtiles).</summary>
    public int Width { get; init; }

    public int Height { get; init; }

    public IReadOnlyList<Room> Rooms { get; init; } = Array.Empty<Room>();

    public IReadOnlyList<PresetUnit> Presets { get; init; } = Array.Empty<PresetUnit>();

    /// <summary>Warp and seam exits (the libd2 "adjacents").</summary>
    public IReadOnlyList<LevelExit> Exits { get; init; } = Array.Empty<LevelExit>();

    /// <summary>Seeded outdoor shrines/wells (Act I wilderness LvlSub type 5).</summary>
    public IReadOnlyList<Shrine> Shrines { get; init; } = Array.Empty<Shrine>();

    /// <summary>The waypoint objects among <see cref="Presets"/>.</summary>
    public IReadOnlyList<PresetUnit> Waypoints { get; init; } = Array.Empty<PresetUnit>();

    /// <summary>Raw collision, or null when the level materialized no collision.</summary>
    public CollisionGrid? Collision { get; init; }
}

/// <summary>Every level of one act for one seed and difficulty.</summary>
public sealed class Act
{
    internal Act() { }

    /// <summary>0-based act number (Act I = 0 … Act V = 4).</summary>
    public int Number { get; init; }

    public uint Seed { get; init; }

    public Difficulty Difficulty { get; init; }

    /// <summary>The act's levels in Levels.txt id order.</summary>
    public IReadOnlyList<Level> Levels { get; init; } = Array.Empty<Level>();

    /// <summary>The level with this id, or null.</summary>
    public Level? GetLevel(int id)
    {
        foreach (var l in Levels)
            if (l.Id == id) return l;
        return null;
    }
}

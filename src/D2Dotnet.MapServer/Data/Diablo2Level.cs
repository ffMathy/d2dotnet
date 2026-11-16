using System.Text.Json.Serialization;

namespace D2Dotnet.MapServer.Data;

/// <summary>Offset of a level from the top left of the game world.</summary>
public sealed record LevelOffset(int X, int Y);

/// <summary>Reported width and height of a level.</summary>
public sealed record LevelSize(int Width, int Height);

/// <summary>
/// Specific diablo 2 level @see levels.txt (upstream <c>Diablo2Level</c>).
/// Property order follows the JSON the upstream native generator emitted (see <c>Generation/LevelConverter.cs</c>).
/// </summary>
public sealed class Diablo2Level
{
    /// <summary>
    /// Always <c>"map"</c>: upstream returned the generator's raw <c>map</c> message, so the field is part of the
    /// HTTP output.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    /// <summary>Level id @see levels.txt</summary>
    public int Id { get; set; }

    /// <summary>Name of the level @example "Harrogath"</summary>
    public string Name { get; set; } = "";

    /// <summary>Offset from the game world.</summary>
    public LevelOffset Offset { get; set; } = new(0, 0);

    /// <summary>Reported width and height of the level.</summary>
    public LevelSize Size { get; set; } = new(0, 0);

    /// <summary>Preset objects, npcs and exits.</summary>
    public List<Diablo2LevelObject> Objects { get; set; } = [];

    /// <summary>Collision map for the level, one run length encoded row per y offset.</summary>
    public List<List<int>> Map { get; set; } = [];
}

/// <summary>Values of <see cref="Diablo2LevelObject.Type"/>.</summary>
public static class Diablo2LevelObjectType
{
    public const string Object = "object";
    public const string Npc = "npc";
    public const string Exit = "exit";
}

/// <summary>
/// Preset object in a level: an object, npc, super unique npc or exit
/// (upstream union <c>Diablo2LevelObject</c> = <c>Diablo2LevelObjectObject | Diablo2LevelNpc | Diablo2LevelExit | Diablo2LevelNpcSuper</c>).
/// </summary>
public sealed class Diablo2LevelObject
{
    /// <summary>
    /// Id of object. For NPCs see monstats.txt, for Objects see objects.txt, for Exits see levels.txt
    /// </summary>
    public int Id { get; set; }

    /// <summary><c>"object"</c>, <c>"npc"</c> or <c>"exit"</c>.</summary>
    public string Type { get; set; } = "";

    /// <summary>X offset relative to the level.</summary>
    public int X { get; set; }

    /// <summary>Y offset relative to the level.</summary>
    public int Y { get; set; }

    /// <summary>Name of the object/npc/exit if it exists.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>Operate function of an object (objects.txt OperateFn).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Op { get; set; }

    /// <summary>Is this the correct exit for quests (the correct tomb, the ruined temple).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsGoodExit { get; set; }

    /// <summary>Object class, see <see cref="Diablo2ItemClasses"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Class { get; set; }

    /// <summary>Is this npc a super unique monster.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsSuperUnique { get; set; }

    /// <summary>Super unique id (Diablo2LevelNpcSuper).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SuperId { get; set; }
}

/// <summary>Object classes (upstream type <c>Diablo2ItemClasses</c>).</summary>
public static class Diablo2ItemClasses
{
    /// <summary>OpCode 1</summary>
    public const string Casket = "casket";
    /// <summary>Chests dead barb etc.. but not super chests @see "chest-super" (OpCode 4)</summary>
    public const string Chest = "chest";
    /// <summary>ItemCode: 580 or 581</summary>
    public const string ChestSuper = "chest-super";
    public const string ChestExploding = "chest-exploding";
    public const string Door = "door";
    /// <summary>OpCode 2</summary>
    public const string Shrine = "shrine";
    /// <summary>OpCode 22</summary>
    public const string Well = "well";
    /// <summary>Useful quest items like hordiric orifice</summary>
    public const string Quest = "quest";
    /// <summary>Small items like ajar or basket (OpCode 3)</summary>
    public const string Urn = "urn";
    /// <summary>OpCode 22</summary>
    public const string Waypoint = "waypoint";
    /// <summary>OpCode 68</summary>
    public const string UrnEvil = "urn-evil";
    /// <summary>OpCode 5</summary>
    public const string Barrel = "barrel";
    /// <summary>OpCode 7</summary>
    public const string BarrelExploding = "barrel-exploding";
    /// <summary>OpCode 20</summary>
    public const string RackWeapon = "rack-weapon";
    /// <summary>OpCode 19</summary>
    public const string RackArmor = "rack-armor";
}

/// <summary>Collection of maps to make up an act (upstream <c>Diablo2Map</c>).</summary>
public sealed class Diablo2Map
{
    /// <summary>Id of map generation.</summary>
    public string Id { get; set; } = "";

    /// <summary>Diablo2 Map Seed.</summary>
    public double Seed { get; set; }

    /// <summary>Game Difficulty.</summary>
    public Difficulty Difficulty { get; set; }

    /// <summary>All the levels generated.</summary>
    public IReadOnlyList<Diablo2Level> Levels { get; set; } = [];

    /// <summary>Game Act, only exists if the map generation was for a specific act.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Act? Act { get; set; }
}

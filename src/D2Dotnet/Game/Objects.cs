using System.Collections.Frozen;

namespace D2Dotnet.Game;

/// <summary>How an object can be interacted with (its unit data's interact byte, for objects that are not shrines).</summary>
public enum InteractType : byte
{
    /// <summary>Nothing special.</summary>
    None = 0x00,

    /// <summary>Trapped.</summary>
    Trap = 0x04,

    /// <summary>Locked.</summary>
    Locked = 0x80,
}

/// <summary>The kind of a shrine, numbered as the game numbers them (shrines.txt).</summary>
public enum ShrineType : byte
{
    /// <summary>Refill shrine.</summary>
    RefillShrine = 0x01,

    /// <summary>Health shrine.</summary>
    HealthShrine = 0x02,

    /// <summary>Mana shrine.</summary>
    ManaShrine = 0x03,

    /// <summary>Health exchange shrine.</summary>
    HPXChangeShrine = 0x04,

    /// <summary>Mana exchange shrine.</summary>
    ManaXChangeShrine = 0x05,

    /// <summary>Armor shrine.</summary>
    ArmorShrine = 0x06,

    /// <summary>Combat shrine.</summary>
    CombatShrine = 0x07,

    /// <summary>Resist fire shrine.</summary>
    ResistFireShrine = 0x08,

    /// <summary>Resist cold shrine.</summary>
    ResistColdShrine = 0x09,

    /// <summary>Resist lightning shrine.</summary>
    ResistLightningShrine = 0x0A,

    /// <summary>Resist poison shrine.</summary>
    ResistPoisonShrine = 0x0B,

    /// <summary>Skill shrine.</summary>
    SkillShrine = 0x0C,

    /// <summary>Mana recharge shrine.</summary>
    ManaRegenShrine = 0x0D,

    /// <summary>Stamina shrine.</summary>
    StaminaShrine = 0x0E,

    /// <summary>Experience shrine.</summary>
    ExperienceShrine = 0x0F,

    /// <summary>Enirhs (unknown) shrine.</summary>
    UnknownShrine = 0x10,

    /// <summary>Portal shrine.</summary>
    PortalShrine = 0x11,

    /// <summary>Gem shrine.</summary>
    GemShrine = 0x12,

    /// <summary>Fire shrine.</summary>
    FireShrine = 0x13,

    /// <summary>Monster shrine.</summary>
    MonsterShrine = 0x14,

    /// <summary>Explosive shrine.</summary>
    ExplosiveShrine = 0x15,

    /// <summary>Poison shrine.</summary>
    PoisonShrine = 0x16,
}

/// <summary>Display names of <see cref="ShrineType"/>.</summary>
public static class ShrineTypes
{
    private static readonly FrozenDictionary<ShrineType, string> Names = new Dictionary<ShrineType, string>
    {
        [ShrineType.RefillShrine] = "Refill Shrine",
        [ShrineType.HealthShrine] = "Health Shrine",
        [ShrineType.ManaShrine] = "Mana Shrine",
        [ShrineType.HPXChangeShrine] = "HP XChange Shrine",
        [ShrineType.ManaXChangeShrine] = "Mana XChange Shrine",
        [ShrineType.ArmorShrine] = "Armor Shrine",
        [ShrineType.CombatShrine] = "Combat Shrine",
        [ShrineType.ResistFireShrine] = "Resist Fire Shrine",
        [ShrineType.ResistColdShrine] = "Resist Cold Shrine",
        [ShrineType.ResistLightningShrine] = "Resist Lightning Shrine",
        [ShrineType.ResistPoisonShrine] = "Resist Poison Shrine",
        [ShrineType.SkillShrine] = "Skill Shrine",
        [ShrineType.ManaRegenShrine] = "Mana Regen Shrine",
        [ShrineType.StaminaShrine] = "Stamina Shrine",
        [ShrineType.ExperienceShrine] = "Experience Shrine",
        [ShrineType.UnknownShrine] = "Unknown Shrine",
        [ShrineType.PortalShrine] = "Portal Shrine",
        [ShrineType.GemShrine] = "Gem Shrine",
        [ShrineType.FireShrine] = "Fire Shrine",
        [ShrineType.MonsterShrine] = "Monster Shrine",
        [ShrineType.ExplosiveShrine] = "Explosive Shrine",
        [ShrineType.PoisonShrine] = "Poison Shrine",
    }.ToFrozenDictionary();

    /// <summary>The English name of a shrine kind (such as <c>Experience Shrine</c>), or null for a value that is not one.</summary>
    public static string? GetName(this ShrineType type) => Names.GetValueOrDefault(type);

    /// <summary>True for the 22 shrine kinds.</summary>
    public static bool IsDefined(this ShrineType type) => Names.ContainsKey(type);
}

/// <summary>An object unit: chest, shrine, door, waypoint, portal...</summary>
public sealed record GameObject
{
    // Chests, caskets, urns, bodies and other containers (d2go IsChest's list, by id).
    private static readonly FrozenSet<ObjectId> Chests = new[]
    {
        1, 3, 5, 6, 50, 51, 53, 54, 55, 56, 57, 58, 79, 87, 88, 89, 104, 105, 106, 107,
        125, 126, 127, 128, 139, 140, 141, 144, 146, 147, 148, 154, 155, 158, 159, 169, 171, 174, 175, 176,
        177, 178, 181, 182, 183, 185, 186, 187, 188, 198, 203, 204, 205, 223, 224, 225, 240, 241, 242, 243,
        244, 247, 248, 266, 268, 270, 271, 272, 274, 284, 314, 315, 316, 317, 326, 329, 330, 331, 332, 333,
        334, 335, 336, 354, 355, 356, 360, 371, 372, 380, 381, 383, 384, 387, 388, 389, 390, 391, 397, 405,
        406, 407, 413, 416, 420, 424, 425, 430, 431, 432, 433, 454, 455, 463, 466, 485, 486, 487, 501, 502,
        504, 505, 518, 524, 525, 526, 529, 530, 531, 532, 533, 534, 535, 540, 541, 544, 545, 556, 580, 581,
    }.Select(id => (ObjectId)id).ToFrozenSet();

    // Sparkly chests and the like (d2go IsSuperChest's list, by id).
    private static readonly FrozenSet<ObjectId> SuperChests =
        new[] { 104, 105, 106, 107, 181, 183, 580, 397, 387, 389, 390, 391, 455 }.Select(id => (ObjectId)id).ToFrozenSet();

    /// <summary>The unit id.</summary>
    public UnitId Id { get; init; }

    /// <summary>The object class (objects.txt row).</summary>
    public ObjectId Class { get; init; }

    /// <summary>The position, in world subtiles.</summary>
    public Point Position { get; init; }

    /// <summary>True when the mouse is over the object.</summary>
    public bool IsHovered { get; init; }

    /// <summary>True when the object is in its idle mode (mode 0) and so can be clicked (an opened chest is not).</summary>
    public bool IsSelectable { get; init; }

    /// <summary>How the object can be interacted with; <see cref="InteractType.None"/> for shrines.</summary>
    public InteractType InteractType { get; init; }

    /// <summary>The kind of shrine, for objects whose class is a shrine (objects.txt <c>OperateFn</c> 2); null otherwise.</summary>
    public ShrineType? Shrine { get; init; }

    /// <summary>The owner's name (of a town portal, for example); empty when none.</summary>
    public string Owner { get; init; } = "";

    /// <summary>True for a shrine of one of the 22 shrine kinds.</summary>
    public bool IsShrine => Shrine is { } shrine && shrine.IsDefined();

    /// <summary>A waypoint.</summary>
    public bool IsWaypoint => Class is ObjectId.WaypointPortal or ObjectId.Act1WildernessWaypoint or ObjectId.Act2Waypoint
        or ObjectId.Act3TownWaypoint or ObjectId.PandamoniumFortressWaypoint or ObjectId.Act2CellerWaypoint
        or ObjectId.Act2SewerWaypoint or ObjectId.Act3TravincalWaypoint or ObjectId.ValleyWaypoint or ObjectId.WorldstoneWaypoint
        or ObjectId.ExpansionWildernessWaypoint or ObjectId.IceCaveWaypoint or ObjectId.TempleWaypoint
        or ObjectId.InnerHellWaypoint or ObjectId.WaypointH or ObjectId.ExpansionWaypoint;

    /// <summary>A (blue) town portal.</summary>
    public bool IsPortal => Class == ObjectId.TownPortal;

    /// <summary>A permanent (red) portal.</summary>
    public bool IsRedPortal => Class == ObjectId.PermanentTownPortal;

    /// <summary>A chest, casket, urn, body or other container.</summary>
    public bool IsChest => Chests.Contains(Class);

    /// <summary>A sparkly chest and the like.</summary>
    public bool IsSuperChest => SuperChests.Contains(Class);

    /// <summary>A door or gate.</summary>
    public bool IsDoor => Class is ObjectId.DoorCathedralLeft or ObjectId.DoorCathedralRight or ObjectId.DoorCathedralDouble
        or ObjectId.DoorCourtyardLeft or ObjectId.DoorCourtyardRight or ObjectId.DoorGateLeft or ObjectId.DoorGateRight
        or ObjectId.DoorMonasteryDoubleRight or ObjectId.DoorWoodenLeft or ObjectId.DoorWoodenLeft2 or ObjectId.DoorWoodenRight
        or ObjectId.IronGrateDoorLeft or ObjectId.IronGrateDoorRight or ObjectId.SlimeDoor1 or ObjectId.SlimeDoor2
        or ObjectId.TombDoorLeft or ObjectId.TombDoorLeft2 or ObjectId.TombDoorRight or ObjectId.TombDoorRight2
        or ObjectId.WoodenDoorLeft or ObjectId.WoodenDoorRight or ObjectId.WoodenGrateDoorLeft or ObjectId.WoodenGrateDoorRight
        or ObjectId.AndarielDoor or ObjectId.PenBreakableDoor or ObjectId.ArreatSummitDoorToWorldstone or ObjectId.SecretDoor1
        or ObjectId.ExpansionTownGate;
}

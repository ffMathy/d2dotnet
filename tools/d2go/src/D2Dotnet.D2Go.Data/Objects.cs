// Port of d2go pkg/data/objects.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using D2Dotnet.D2Go.Data.Object;

namespace D2Dotnet.D2Go.Data;

/// <summary>An object unit: chest, shrine, door, waypoint, portal... (Go <c>data.Object</c>).</summary>
public sealed record GameObject
{
    // IsChest's and IsSuperChest's ids, as numbers in Go.
    private static readonly FrozenSet<int> ChestIds = new[]
    {
        1, 3, 5, 6, 50, 51, 53, 54, 55, 56, 57, 58, 79, 87, 88, 89, 104, 105, 106, 107,
        125, 126, 127, 128, 139, 140, 141, 144, 146, 147, 148, 154, 155, 158, 159, 169, 171, 174, 175, 176,
        177, 178, 181, 182, 183, 185, 186, 187, 188, 198, 203, 204, 205, 223, 224, 225, 240, 241, 242, 243,
        244, 247, 248, 266, 268, 270, 271, 272, 274, 284, 314, 315, 316, 317, 326, 329, 330, 331, 332, 333,
        334, 335, 336, 354, 355, 356, 360, 371, 372, 380, 381, 383, 384, 387, 388, 389, 390, 391, 397, 405,
        406, 407, 413, 416, 420, 424, 425, 430, 431, 432, 433, 454, 455, 463, 466, 485, 486, 487, 501, 502,
        504, 505, 518, 524, 525, 526, 529, 530, 531, 532, 533, 534, 535, 540, 541, 544, 545, 556, 580, 581,
    }.ToFrozenSet();

    private static readonly FrozenSet<int> SuperChestIds = new[]
    {
        104, 105, 106, 107, 181, 183, 580, 397, 387, 389, 390, 391, 455,
    }.ToFrozenSet();

    public UnitId Id { get; init; }

    /// <summary>The object class (Go's embedded <c>object.Name</c>).</summary>
    public ObjectName Name { get; init; }

    public bool IsHovered { get; init; }

    public bool Selectable { get; init; }

    public InteractType InteractType { get; init; }

    public ShrineData Shrine { get; init; } = new();

    public Position Position { get; init; }

    public string Owner { get; init; } = "";

    /// <inheritdoc cref="ObjectNameExtensions.Desc"/>
    /// <remarks>Promoted from the embedded <c>object.Name</c> in Go.</remarks>
    public ObjectDescription Desc(GameTables tables) => Name.Desc(tables);

    /// <summary>True when <see cref="ShrineData.ShrineType"/> is one of the 22 shrine kinds.</summary>
    public bool IsShrine() => Shrine.ShrineType switch
    {
        ShrineType.RefillShrine or ShrineType.HealthShrine or ShrineType.ManaShrine or ShrineType.HPXChangeShrine
            or ShrineType.ManaXChangeShrine or ShrineType.ArmorShrine or ShrineType.CombatShrine or ShrineType.ResistFireShrine
            or ShrineType.ResistColdShrine or ShrineType.ResistLightningShrine or ShrineType.ResistPoisonShrine
            or ShrineType.SkillShrine or ShrineType.ManaRegenShrine or ShrineType.StaminaShrine or ShrineType.ExperienceShrine
            or ShrineType.UnknownShrine or ShrineType.PortalShrine or ShrineType.GemShrine or ShrineType.FireShrine
            or ShrineType.MonsterShrine or ShrineType.ExplosiveShrine or ShrineType.PoisonShrine => true,
        _ => false,
    };

    public bool IsWaypoint() => Name switch
    {
        ObjectName.WaypointPortal or ObjectName.Act1WildernessWaypoint or ObjectName.Act2Waypoint or ObjectName.Act3TownWaypoint
            or ObjectName.PandamoniumFortressWaypoint or ObjectName.Act2CellerWaypoint or ObjectName.Act2SewerWaypoint
            or ObjectName.Act3TravincalWaypoint or ObjectName.ValleyWaypoint or ObjectName.WorldstoneWaypoint
            or ObjectName.ExpansionWildernessWaypoint or ObjectName.IceCaveWaypoint or ObjectName.TempleWaypoint
            or ObjectName.InnerHellWaypoint or ObjectName.WaypointH or ObjectName.ExpansionWaypoint => true,
        _ => false,
    };

    /// <summary>A (blue) town portal.</summary>
    public bool IsPortal() => Name == ObjectName.TownPortal;

    /// <summary>A permanent (red) portal.</summary>
    public bool IsRedPortal() => Name == ObjectName.PermanentTownPortal;

    /// <summary>A chest, casket, urn, body or other container (d2go lists them by number).</summary>
    public bool IsChest() => ChestIds.Contains((int)Name);

    public bool IsDoor() => Name switch
    {
        ObjectName.DoorCathedralLeft or ObjectName.DoorCathedralRight or ObjectName.DoorCathedralDouble
            or ObjectName.DoorCourtyardLeft or ObjectName.DoorCourtyardRight or ObjectName.DoorGateLeft
            or ObjectName.DoorGateRight or ObjectName.DoorMonasteryDoubleRight or ObjectName.DoorWoodenLeft
            or ObjectName.DoorWoodenLeft2 or ObjectName.DoorWoodenRight or ObjectName.IronGrateDoorLeft
            or ObjectName.IronGrateDoorRight or ObjectName.SlimeDoor1 or ObjectName.SlimeDoor2 or ObjectName.TombDoorLeft
            or ObjectName.TombDoorLeft2 or ObjectName.TombDoorRight or ObjectName.TombDoorRight2 or ObjectName.WoodenDoorLeft
            or ObjectName.WoodenDoorRight or ObjectName.WoodenGrateDoorLeft or ObjectName.WoodenGrateDoorRight
            or ObjectName.AndarielDoor or ObjectName.PenBreakableDoor or ObjectName.ArreatSummitDoorToWorldstone
            or ObjectName.SecretDoor1 or ObjectName.ExpansionTownGate => true,
        _ => false,
    };

    /// <summary>A sparkly chest and the like (d2go lists them by number).</summary>
    public bool IsSuperChest() => SuperChestIds.Contains((int)Name);
}

/// <summary>Objects (Go <c>data.Objects</c>).</summary>
[CollectionBuilder(typeof(GameObjects), nameof(Create))]
public sealed class GameObjects : ReadOnlyCollection<GameObject>
{
    public GameObjects()
        : base([])
    {
    }

    public GameObjects(IEnumerable<GameObject> objects)
        : base([.. objects])
    {
    }

    /// <summary>No objects (Go's nil slice).</summary>
    public static new GameObjects Empty { get; } = new();

    /// <summary>Collection expression builder.</summary>
    public static GameObjects Create(ReadOnlySpan<GameObject> objects) => new(objects.ToArray());

    /// <summary>
    /// The first object of class <paramref name="name"/> (Go <c>FindOne</c>). When there is none, returns false and
    /// <paramref name="obj"/> is the zero value.
    /// </summary>
    public bool TryFindOne(ObjectName name, out GameObject obj)
    {
        foreach (var o in this)
        {
            if (o.Name == name)
            {
                obj = o;
                return true;
            }
        }

        obj = new GameObject();
        return false;
    }

    /// <summary>
    /// The object with this unit id (Go <c>FindByID</c>). When there is none, returns false and <paramref name="obj"/>
    /// is the zero value.
    /// </summary>
    public bool TryFindById(UnitId id, out GameObject obj)
    {
        foreach (var o in this)
        {
            if (o.Id == id)
            {
                obj = o;
                return true;
            }
        }

        obj = new GameObject();
        return false;
    }
}

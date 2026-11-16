using System.Collections.Frozen;

namespace D2Dotnet.Game;

/// <summary>What kind of monster a unit is: a normal one, or one of the elite kinds.</summary>
public enum MonsterType
{
    /// <summary>A normal monster (also NPCs, mercenaries and summons).</summary>
    Normal,

    /// <summary>A champion.</summary>
    Champion,

    /// <summary>A minion of a unique or super unique.</summary>
    Minion,

    /// <summary>A unique (random boss).</summary>
    Unique,

    /// <summary>A super unique (named boss such as Bishibosh).</summary>
    SuperUnique,
}

/// <summary>A damage element a monster can be immune to.</summary>
public enum Element
{
    /// <summary>Fire (fire resistance).</summary>
    Fire,

    /// <summary>Cold (cold resistance).</summary>
    Cold,

    /// <summary>Lightning (lightning resistance).</summary>
    Lightning,

    /// <summary>Poison (poison resistance).</summary>
    Poison,

    /// <summary>Magic (magic resistance).</summary>
    Magic,
}

/// <summary>A monster unit: a monster, an NPC, a mercenary or a summon, alive or dead.</summary>
public sealed record Monster
{
    // Friendly NPCs (d2go IsGoodNPC's list, by id).
    private static readonly FrozenSet<MonsterId> FriendlyNpcs = new[]
    {
        146, 154, 147, 150, 155, 148, 244, 210, 175, 199, 198, 177, 178, 201, 202, 200, 331, 245, 264, 255,
        176, 252, 254, 253, 297, 246, 251, 367, 521, 257, 405, 265, 520, 512, 527, 515, 513, 511, 514, 266,
        408, 406,
    }.Select(id => (MonsterId)id).ToFrozenSet();

    /// <summary>The unit id.</summary>
    public UnitId Id { get; init; }

    /// <summary>The monster class (monstats.txt row).</summary>
    public MonsterId Class { get; init; }

    /// <summary>Normal or one of the elite kinds.</summary>
    public MonsterType Type { get; init; }

    /// <summary>The position, in world subtiles.</summary>
    public Point Position { get; init; }

    /// <summary>True when the mouse is over the monster.</summary>
    public bool IsHovered { get; init; }

    /// <summary>
    /// The monster's stats, as the game stores them (signed 32-bit values, not rescaled: life and max life are in
    /// 1/256 points), in the game's order. A stat (id and layer) the game lists twice is listed once, with its last value.
    /// </summary>
    public StatList Stats { get; init; } = StatList.Empty;

    /// <summary>The value of a stat from <see cref="Stats"/>; null when the monster does not have it.</summary>
    public int? GetStat(StatId id, ushort layer = 0) => Stats.GetValue(id, layer);

    /// <summary>
    /// True when the monster's resistance to <paramref name="element"/> is 100 or more; false when it has no such
    /// resistance stat.
    /// </summary>
    public bool IsImmune(Element element) => GetStat(element switch
    {
        Element.Fire => StatId.FireResist,
        Element.Cold => StatId.ColdResist,
        Element.Lightning => StatId.LightningResist,
        Element.Poison => StatId.PoisonResist,
        Element.Magic => StatId.MagicResist,
        _ => throw new ArgumentOutOfRangeException(nameof(element), element, null),
    }) >= 100;

    /// <summary>A hireling: act 1 rogue, act 2 guard, act 3 iron wolf or act 5 barbarian.</summary>
    public bool IsMercenary => Class is MonsterId.Guard or MonsterId.Act5Hireling1Hand or MonsterId.Act5Hireling2Hand
        or MonsterId.IronWolf or MonsterId.Rogue2;

    /// <summary>A player summon: druid summons and spirits, necromancer golems and skeletons, valkyrie, shadows.</summary>
    public bool IsPet => Class is MonsterId.DruHawk or MonsterId.DruSpiritWolf or MonsterId.DruFenris or MonsterId.HeartOfWolverine
        or MonsterId.OakSage or MonsterId.DruBear or MonsterId.DruPlaguePoppy or MonsterId.VineCreature
        or MonsterId.DruCycleOfLife or MonsterId.ClayGolem or MonsterId.BloodGolem or MonsterId.IronGolem
        or MonsterId.FireGolem or MonsterId.NecroSkeleton or MonsterId.NecroMage or MonsterId.Valkyrie
        or MonsterId.ShadowWarrior or MonsterId.ShadowMaster;

    /// <summary>A friendly NPC: town NPCs and the like.</summary>
    public bool IsFriendlyNpc => FriendlyNpcs.Contains(Class);

    /// <summary>A champion, minion, unique or super unique.</summary>
    public bool IsElite => Type != MonsterType.Normal;

    /// <summary>A monster that raises or summons other monsters (the fallen shamans).</summary>
    public bool IsMonsterRaiser => Class is MonsterId.FallenShaman or MonsterId.CarverShaman or MonsterId.DevilkinShaman
        or MonsterId.DarkShaman or MonsterId.WarpedShaman;

    /// <summary>A monster that cannot be fought as a normal enemy (it cannot be targeted, or is part of another monster).</summary>
    public bool IsUntargetable => Class is MonsterId.WaterWatcherLimb or MonsterId.WaterWatcherHead or MonsterId.BaalTaunt
        or MonsterId.Act5Combatant or MonsterId.Act5Combatant2 or MonsterId.BarricadeTower;

    /// <summary>
    /// A living hostile monster: not a mercenary, pet, friendly NPC or untargetable, and with a life stat above 0 (a monster
    /// without a life stat is not an enemy).
    /// </summary>
    public bool IsEnemy => !IsMercenary && !IsUntargetable && !IsFriendlyNpc && !IsPet && GetStat(StatId.Life) > 0;
}

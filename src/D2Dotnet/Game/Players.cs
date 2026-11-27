using System.Collections.Frozen;

namespace D2Dotnet.Game;

/// <summary>A character class, numbered as the game numbers them.</summary>
public enum CharacterClass : uint
{
    /// <summary>Amazon.</summary>
    Amazon = 0,

    /// <summary>Sorceress.</summary>
    Sorceress = 1,

    /// <summary>Necromancer.</summary>
    Necromancer = 2,

    /// <summary>Paladin.</summary>
    Paladin = 3,

    /// <summary>Barbarian.</summary>
    Barbarian = 4,

    /// <summary>Druid.</summary>
    Druid = 5,

    /// <summary>Assassin.</summary>
    Assassin = 6,

    /// <summary>Warlock (Reign of the Warlock; not in d2go).</summary>
    Warlock = 7,
}

/// <summary>A skill's points on a unit, 16 bits each as the game stores them.</summary>
/// <param name="Level">The skill level (hard points plus bonuses).</param>
/// <param name="Quantity">The quantity (for skills that use one).</param>
/// <param name="Charges">The charges left (for skills given by an item with charges).</param>
public readonly record struct SkillPoints(ushort Level, ushort Quantity, ushort Charges);

/// <summary>
/// A player unit from the game's player unit table: the main player, other players in the game, the player's corpse
/// and the shared stash tabs (which the game keeps as player units).
/// </summary>
public record PlayerUnit
{
    /// <summary>The unit's address in the game process.</summary>
    public nuint Address { get; init; }

    /// <summary>The unit id.</summary>
    public UnitId Id { get; init; }

    /// <summary>The character name.</summary>
    public string Name { get; init; } = "";

    /// <summary>The area the unit is in.</summary>
    public AreaId Area { get; init; }

    /// <summary>The position, in world subtiles.</summary>
    public Point Position { get; init; }

    /// <summary>True for the player this game client controls.</summary>
    public bool IsMainPlayer { get; init; }

    /// <summary>True for a player's corpse.</summary>
    public bool IsCorpse { get; init; }

    /// <summary>True when the mouse is over the unit.</summary>
    public bool IsHovered { get; init; }

    /// <summary>The unit's current stats (with equipment and buffs).</summary>
    public StatList Stats { get; init; } = StatList.Empty;

    /// <summary>The unit's base stats (without equipment and buffs).</summary>
    public StatList BaseStats { get; init; } = StatList.Empty;

    /// <summary>The unit's states (buffs, curses, auras...), lowest id first.</summary>
    public IReadOnlyList<StateId> States { get; init; } = [];

    /// <summary>The value of a stat from <see cref="Stats"/>, else from <see cref="BaseStats"/>; null when neither has it.</summary>
    public int? GetStat(StatId id, ushort layer = 0) => Stats.GetValue(id, layer) ?? BaseStats.GetValue(id, layer);

    /// <summary>True when the unit has <paramref name="state"/>.</summary>
    public bool HasState(StateId state) => States.Contains(state);
}

/// <summary>The player this game client controls: a <see cref="PlayerUnit"/> plus its class, skills and waypoints.</summary>
public sealed record MainPlayer : PlayerUnit
{
    private const long GoldPerLevel = 10_000;

    // Curses and other debuffs (d2go HasDebuff's list).
    private static readonly StateId[] Debuffs =
    [
        StateId.AmplifyDamage, StateId.Attract, StateId.Confuse, StateId.Conversion, StateId.Decrepify, StateId.DimVision,
        StateId.IronMaiden, StateId.LifeTap, StateId.LowerResist, StateId.Terror, StateId.Weaken, StateId.Convicted,
        StateId.Poison, StateId.Cold, StateId.Slowed, StateId.BloodMana, StateId.DefenseCurse,
    ];

    /// <summary>The character class.</summary>
    public CharacterClass Class { get; init; }

    /// <summary>The skills the character has, by skill (items' skills and charges included).</summary>
    public IReadOnlyDictionary<SkillId, SkillPoints> Skills { get; init; } = FrozenDictionary<SkillId, SkillPoints>.Empty;

    /// <summary>The skill on the left mouse button.</summary>
    public SkillId LeftSkill { get; init; }

    /// <summary>The skill on the right mouse button.</summary>
    public SkillId RightSkill { get; init; }

    /// <summary>
    /// The waypoints of the waypoint menu's selected tab that are active, plus the current area's. Only filled while
    /// the waypoint menu is open.
    /// </summary>
    public IReadOnlyList<AreaId> AvailableWaypoints { get; init; } = [];

    /// <summary>The character level, 0 when unknown.</summary>
    public int Level => GetStat(StatId.Level) ?? 0;

    /// <summary>The experience (unsigned: level 99 is 3,520,485,254).</summary>
    public uint Experience => unchecked((uint)(GetStat(StatId.Experience) ?? 0));

    /// <summary>The most gold the character can carry: 10,000 per level.</summary>
    public long MaxGold => GoldPerLevel * Level;

    /// <summary>The gold in the inventory plus the character's own stash (not the shared stash).</summary>
    public long TotalGold => (long)(GetStat(StatId.Gold) ?? 0) + (GetStat(StatId.StashGold) ?? 0);

    /// <summary>Life in percent of max life, truncated; null when the max life is unknown or 0.</summary>
    public int? LifePercent => Percent(GetStat(StatId.Life), GetStat(StatId.MaxLife));

    /// <summary>Mana in percent of max mana, truncated; null when the max mana is unknown or 0.</summary>
    public int? ManaPercent => Percent(GetStat(StatId.Mana), GetStat(StatId.MaxMana));

    /// <summary>True when one of the character's states is a curse or another debuff.</summary>
    public bool HasDebuff => States.Any(s => Debuffs.Contains(s));

    /// <summary>
    /// The frames a cast takes with the character's faster cast rate
    /// (<see href="https://diablo.fandom.com/wiki/Faster_Cast_Rate"/>): effective FCR capped at 75, and the class's base
    /// cast animation.
    /// </summary>
    public int CastingFrames
    {
        get
        {
            long fcr = GetStat(StatId.FasterCastRate) ?? 0;
            var effective = Math.Min(Math.Floor((double)(fcr * 120) / (fcr + 120)), 75);
            return (int)(Math.Ceiling(256 * CastingBaseFrames / Math.Floor(256 * (100 + effective) / 100.0)) - 1);
        }
    }

    private double CastingBaseFrames => Class switch
    {
        CharacterClass.Amazon => 20,
        CharacterClass.Assassin => 17,
        CharacterClass.Barbarian or CharacterClass.Sorceress => 14,
        CharacterClass.Druid => 15,
        _ => 16, // necromancer, paladin
    };

    private static int? Percent(int? value, int? max) =>
        max is > 0 ? (int)((double)(value ?? 0) / max.Value * 100) : null;
}

/// <summary>The main player's corpse.</summary>
/// <param name="Position">Where it lies.</param>
/// <param name="IsHovered">True when the mouse is over it.</param>
public readonly record struct PlayerCorpse(Point Position, bool IsHovered);

/// <summary>A party member.</summary>
/// <param name="Name">The character name.</param>
/// <param name="Area">The area they are in.</param>
/// <param name="Position">Their position.</param>
public sealed record PartyMember(string Name, AreaId Area, Point Position);

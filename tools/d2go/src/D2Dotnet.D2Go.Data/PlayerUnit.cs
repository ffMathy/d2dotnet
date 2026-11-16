// Port of d2go pkg/data/data.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using D2Dotnet.D2Go.Data.Area;
using D2Dotnet.D2Go.Data.Skill;
using D2Dotnet.D2Go.Data.Stat;
using D2Dotnet.D2Go.Data.State;

namespace D2Dotnet.D2Go.Data;

/// <summary>The player's character (Go <c>data.PlayerUnit</c>).</summary>
public sealed record PlayerUnit
{
    private const int GoldPerLevel = 10000;

    // Go HasDebuff's list, in its order.
    private static readonly StateId[] Debuffs =
    [
        StateId.Amplifydamage,
        StateId.Attract,
        StateId.Confuse,
        StateId.Conversion,
        StateId.Decrepify,
        StateId.Dimvision,
        StateId.Ironmaiden,
        StateId.Lifetap,
        StateId.Lowerresist,
        StateId.Terror,
        StateId.Weaken,
        StateId.Convicted,
        StateId.Poison,
        StateId.Cold,
        StateId.Slowed,
        StateId.BloodMana,
        StateId.DefenseCurse,
    ];

    /// <summary>The unit's address in the game process (Go <c>uintptr</c>).</summary>
    public nuint Address { get; init; }

    public string Name { get; init; } = "";

    public UnitId Id { get; init; }

    public AreaId Area { get; init; }

    public Position Position { get; init; }

    public Stats Stats { get; init; } = Stats.Empty;

    public Stats BaseStats { get; init; } = Stats.Empty;

    public IReadOnlyDictionary<SkillId, SkillPoints> Skills { get; init; } = new Dictionary<SkillId, SkillPoints>();

    public States States { get; init; } = States.Empty;

    public Class Class { get; init; }

    public SkillId LeftSkill { get; init; }

    public SkillId RightSkill { get; init; }

    /// <summary>Only filled while the waypoint menu is open, and only for its selected tab.</summary>
    public IReadOnlyList<AreaId> AvailableWaypoints { get; init; } = [];

    /// <summary>
    /// The stat from <see cref="Stats"/>, else from <see cref="BaseStats"/> (Go <c>FindStat</c>). When neither has
    /// it, returns false and <paramref name="stat"/> is the zero value.
    /// </summary>
    public bool TryFindStat(StatId id, int layer, out StatData stat) =>
        Stats.TryFindStat(id, layer, out stat) || BaseStats.TryFindStat(id, layer, out stat);

    /// <summary>The most gold the character can carry: 10000 per level.</summary>
    public long MaxGold()
    {
        TryFindStat(StatId.Level, 0, out var lvl);
        return GoldPerLevel * lvl.Value;
    }

    /// <summary>The gold in the inventory plus the character's own stash (not the shared stash).</summary>
    public long TotalPlayerGold()
    {
        TryFindStat(StatId.Gold, 0, out var gold);
        TryFindStat(StatId.StashGold, 0, out var stashGold);

        return gold.Value + stashGold.Value;
    }

    /// <summary>Life in percent of max life, truncated (see the README for a zero max life).</summary>
    public int HPPercent()
    {
        TryFindStat(StatId.Life, 0, out var life);
        TryFindStat(StatId.MaxLife, 0, out var maxLife);

        return GoConvert.ToInt((double)life.Value / maxLife.Value * 100);
    }

    /// <summary>Mana in percent of max mana, truncated (see the README for a zero max mana).</summary>
    public int MPPercent()
    {
        TryFindStat(StatId.Mana, 0, out var mana);
        TryFindStat(StatId.MaxMana, 0, out var maxMana);

        return GoConvert.ToInt((double)mana.Value / maxMana.Value * 100);
    }

    /// <summary>
    /// The frames a cast takes with the character's faster cast rate
    /// (<see href="https://diablo.fandom.com/wiki/Faster_Cast_Rate"/>): effective FCR capped at 75, and the class's
    /// base cast animation.
    /// </summary>
    public int CastingFrames()
    {
        TryFindStat(StatId.FasterCastRate, 0, out var fcr);
        var baseAnimation = GetCastingBaseSpeed();

        // Go's int is 64-bit, as is StatData.Value.
        var ecfr = Math.Floor((double)(fcr.Value * 120) / (fcr.Value + 120));
        if (ecfr > 75)
            ecfr = 75;

        // Animation speed for druids is different
        var cf = Math.Ceiling(256 * baseAnimation / Math.Floor(256 * (100 + ecfr) / 100.0)) - 1;

        return GoConvert.ToInt(cf);
    }

    private double GetCastingBaseSpeed()
    {
        // TODO (upstream): Implement logic for Lightning?
        switch (Class)
        {
            case Class.Amazon:
                return 20;
            case Class.Assassin:
                return 17;
            case Class.Barbarian:
                return 14;
            case Class.Necromancer:
            case Class.Paladin:
                return 16;
            case Class.Druid:
                return 15;
            case Class.Sorceress:
                return 14;
        }

        return 16;
    }

    /// <summary>True when one of the character's states is a curse or another debuff.</summary>
    public bool HasDebuff()
    {
        foreach (var s in States)
        {
            foreach (var d in Debuffs)
            {
                if (s == d)
                    return true;
            }
        }

        return false;
    }
}

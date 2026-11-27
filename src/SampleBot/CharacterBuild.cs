using D2Dotnet;
using D2Dotnet.Game;

/// <summary>
/// Where a class spends its points: stats by targets, skills by a plan of (skill, level) steps taken in order. A step
/// whose skill needs a level the character does not have yet waits; one whose skill needs other skills first spends
/// on those (skills.txt <c>reqskill</c>), so the plan only names the skills it wants.
/// </summary>
internal sealed record CharacterBuild(
    IReadOnlyList<(StatId Stat, int UpTo)> Stats,
    StatId RestOfTheStats,
    IReadOnlyList<(SkillId Skill, int Level)> Skills)
{
    /// <summary>The builds per class; the other classes come when they are played.</summary>
    public static IReadOnlyDictionary<CharacterClass, CharacterBuild> ByClass { get; } = new Dictionary<CharacterClass, CharacterBuild>
    {
        // A fire Sorceress with Teleport: Fire Bolt early, Fire Ball from level 12, Meteor from 24, then Fire Mastery.
        [CharacterClass.Sorceress] = new(
            Stats: [(StatId.Strength, 35)],
            RestOfTheStats: StatId.Vitality,
            Skills:
            [
                (SkillId.FireBolt, 4), (SkillId.Warmth, 1), (SkillId.FrozenArmor, 1), (SkillId.FireBolt, 7),
                (SkillId.Inferno, 1), (SkillId.Telekinesis, 1), (SkillId.FireBolt, 10),
                (SkillId.FireBall, 1), (SkillId.Blaze, 1), (SkillId.FireBall, 8),
                (SkillId.Teleport, 1), (SkillId.FireWall, 1), (SkillId.FireBall, 16),
                (SkillId.Meteor, 1), (SkillId.FireBall, 20), (SkillId.Meteor, 20), (SkillId.FireMastery, 20),
                (SkillId.Warmth, 20),
            ]),
    };

    /// <summary>Spends every unspent stat and skill point by the build of the player's class.</summary>
    public static void SpendPoints(Bot bot, GameUi ui)
    {
        var player = bot.Player();
        if (!ByClass.TryGetValue(player.Class, out var build))
            return;
        if ((player.GetStat(StatId.StatPoints) ?? 0) == 0 && (player.GetStat(StatId.SkillPoints) ?? 0) == 0)
            return;

        try
        {
            while ((bot.Player().GetStat(StatId.StatPoints) ?? 0) > 0)
            {
                if (!ui.SpendStatPoint(build.NextStat(bot.Player())))
                    break;
            }

            while ((bot.Player().GetStat(StatId.SkillPoints) ?? 0) > 0)
            {
                if (build.NextSkill(bot, bot.Player()) is not { } skill || !ui.SpendSkillPoint(skill))
                    break;
            }
        }
        finally
        {
            ui.ClosePanels();
        }
    }

    private StatId NextStat(MainPlayer player)
    {
        foreach (var (stat, upTo) in Stats)
        {
            if ((player.BaseStats.GetValue(stat) ?? 0) < upTo)
                return stat;
        }

        return RestOfTheStats;
    }

    // The first plan step not reached yet that can take a point now (or a skill it needs first).
    private SkillId? NextSkill(Bot bot, MainPlayer player)
    {
        foreach (var (skill, level) in Skills)
        {
            if (Level(player, skill) >= level)
                continue;
            if (Spendable(bot, player, skill) is { } now)
                return now;
        }

        return null;
    }

    // The skill itself when a point can go into it now, else the first skill it needs that can; null when none can.
    private static SkillId? Spendable(Bot bot, MainPlayer player, SkillId skill, int depth = 0)
    {
        if (depth > 5 || bot.Data.Skills.Find(skill) is not { } info || player.Level < info.RequiredLevel)
            return null;
        foreach (var required in info.RequiredSkills)
        {
            if (Level(player, required) < 1)
                return Spendable(bot, player, required, depth + 1);
        }

        return skill;
    }

    // Hard points: a skill the character has at 0 points reads 0 (a new Sorceress's Fire Bolt).
    private static int Level(MainPlayer player, SkillId skill) => player.Skills.TryGetValue(skill, out var p) ? p.Level : 0;
}

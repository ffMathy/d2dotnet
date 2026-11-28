using D2Dotnet;
using D2Dotnet.Game;

/// <summary>How a character attacks one monster, once per call.</summary>
internal interface ICombat
{
    void Attack(Bot bot, MainPlayer player, Monster target);

    /// <summary>
    /// True when the attack it would make now stops at the first monster it meets (a bolt, a fireball, a melee swing),
    /// so a monster in between takes the hit; false for area and piercing skills (Blizzard, Meteor, Lightning...).
    /// </summary>
    bool StopsAtFirstHit(Bot bot, MainPlayer player);
}

/// <summary>
/// Attacks with the best skill the character has and can pay for, from a table per class (strongest first): the skill
/// must be the selected right skill already, or be bound to a skill key, which selects it. Without one it uses the
/// right skill when it can pay for it, else the left skill (walking up to melee).
/// </summary>
/// <remarks>
/// Pressing a skill key in D2R puts the skill on the right mouse button. Skills are cast standing still (shift), so a
/// miss does not walk the character into the pack.
/// </remarks>
internal sealed class ClassCombat : ICombat
{
    // The attack skills of each class, strongest first. The other classes' tables come when they are played.
    private static readonly Dictionary<CharacterClass, SkillId[]> AttackSkills = new()
    {
        [CharacterClass.Sorceress] =
        [
            SkillId.FrozenOrb, SkillId.Blizzard, SkillId.Meteor, SkillId.ChainLightning, SkillId.GlacialSpike,
            SkillId.FireBall, SkillId.Lightning, SkillId.IceBlast, SkillId.ChargedBolt, SkillId.IceBolt, SkillId.FireBolt,
        ],
    };

    private SkillId? _selected;

    // Skills that land where they are aimed, go through monsters or jump between them.
    private static readonly HashSet<SkillId> PassOver =
    [
        SkillId.FrozenOrb, SkillId.Blizzard, SkillId.Meteor, SkillId.ChainLightning, SkillId.Lightning,
    ];

    public bool StopsAtFirstHit(Bot bot, MainPlayer player) => Choose(bot, player) is not { } skill || !PassOver.Contains(skill);

    public void Attack(Bot bot, MainPlayer player, Monster target)
    {
        var skill = Choose(bot, player);
        bot.Aim(player.Position, target.Position, bot.Window.AimHeight);
        if (skill is { } chosen)
        {
            if (player.RightSkill != chosen && KeyFor(bot, chosen) is { } key)
            {
                bot.Input.PressKey(key);
                Thread.Sleep(80);
            }

            _selected = chosen;
            bot.Input.RightClickStandingStill();
        }
        else
        {
            bot.Input.LeftClick();
        }

        Thread.Sleep(250);
    }

    // The best attack skill that can be cast now, or null for the left skill.
    private SkillId? Choose(Bot bot, MainPlayer player)
    {
        if (AttackSkills.TryGetValue(player.Class, out var table))
        {
            foreach (var skill in table)
            {
                if (bot.CanCast(player, skill) && (player.RightSkill == skill || KeyFor(bot, skill) is not null))
                    return skill;
            }
        }

        // No table for the class, or nothing of it usable: the right skill when it is an attack that can be paid for.
        return player.RightSkill is not (SkillId.AttackSkill or SkillId.None) && bot.CanCast(player, player.RightSkill)
            ? player.RightSkill
            : null;
    }

    private static int? KeyFor(Bot bot, SkillId skill) =>
        bot.Keys.Skills.FirstOrDefault(b => b.Skill == skill) is { Skill: not SkillId.None } binding ? Bot.KeyOf(binding.Keys) : null;

    /// <summary>The skill selected last, for the log.</summary>
    public SkillId? Selected => _selected;
}

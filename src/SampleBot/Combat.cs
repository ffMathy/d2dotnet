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

    /// <summary>
    /// The resistance stat against the attack it would make now (fire for Fire Bolt...), or null for a physical
    /// attack. A monster with 100 or more there is immune to it.
    /// </summary>
    StatId? ResistanceAgainst(Bot bot, MainPlayer player);

    /// <summary>True when one of the character's attacks can hurt the monster.</summary>
    bool CanHurt(Bot bot, MainPlayer player, Monster target);
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

    /// <summary>The attack skills of the class, strongest first (none for a class without a table yet).</summary>
    public static IReadOnlyList<SkillId> AttackSkillsOf(CharacterClass characterClass) =>
        AttackSkills.TryGetValue(characterClass, out var table) ? table : [];

    // Skills that land where they are aimed, go through monsters or jump between them.
    private static readonly HashSet<SkillId> PassOver =
    [
        SkillId.FrozenOrb, SkillId.Blizzard, SkillId.Meteor, SkillId.ChainLightning, SkillId.Lightning,
    ];

    // The element of each attack skill, by the resistance that stops it.
    private static readonly Dictionary<SkillId, StatId> Elements = new()
    {
        [SkillId.FireBolt] = StatId.FireResist, [SkillId.FireBall] = StatId.FireResist, [SkillId.Meteor] = StatId.FireResist,
        [SkillId.IceBolt] = StatId.ColdResist, [SkillId.IceBlast] = StatId.ColdResist, [SkillId.GlacialSpike] = StatId.ColdResist,
        [SkillId.Blizzard] = StatId.ColdResist, [SkillId.FrozenOrb] = StatId.ColdResist,
        [SkillId.ChargedBolt] = StatId.LightningResist, [SkillId.Lightning] = StatId.LightningResist, [SkillId.ChainLightning] = StatId.LightningResist,
    };

    public StatId? ResistanceAgainst(Bot bot, MainPlayer player) =>
        Choose(bot, player) is { } skill && Elements.TryGetValue(skill, out var element) ? element : null;

    public bool StopsAtFirstHit(Bot bot, MainPlayer player) => Choose(bot, player) is not { } skill || !PassOver.Contains(skill);

    public void Attack(Bot bot, MainPlayer player, Monster target)
    {
        var skill = Choose(bot, player, target);
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

    // The attack skill to use now: of the class's skills that can be cast and selected (the right skill already, or
    // on a key), those the target does not resist almost fully; the strongest (the table's order) with at least 4
    // points (or as many as the best has). Null for the left skill.
    private SkillId? Choose(Bot bot, MainPlayer player, Monster? target = null)
    {
        if (AttackSkills.TryGetValue(player.Class, out var table))
        {
            var usable = table
                .Select((skill, order) => (Skill: skill, Order: order))
                .Where(s => bot.CanCast(player, s.Skill) && (player.RightSkill == s.Skill || KeyFor(bot, s.Skill) is not null))
                .Where(s => target is null || !Elements.TryGetValue(s.Skill, out var element) || bot.Resistance(target, element) < 95)
                .ToList();
            // The strongest skill (the table's order) once it has a few points: 4, or as many as the best has.
            int Points(SkillId skill) => player.Skills.TryGetValue(skill, out var points) ? points.Level : 0;
            var most = usable.Select(s => Points(s.Skill)).DefaultIfEmpty(0).Max();
            var ready = usable.Where(s => Points(s.Skill) >= Math.Min(4, most)).OrderBy(s => s.Order).ToList();
            if (ready.Count > 0)
                return ready[0].Skill;
            if (target is not null && table.Any(s => player.Skills.ContainsKey(s)))
                return null; // every attack skill the character has is resisted: the left skill
        }

        // No table for the class, or nothing of it usable: the right skill when it is an attack that can be paid for.
        return player.RightSkill is not (SkillId.AttackSkill or SkillId.None) && bot.CanCast(player, player.RightSkill)
            ? player.RightSkill
            : null;
    }

    /// <summary>True when one of the class's attack skills can hurt the monster (it does not resist it almost fully).</summary>
    public bool CanHurt(Bot bot, MainPlayer player, Monster target) =>
        !AttackSkills.TryGetValue(player.Class, out var table)
        || table.Where(s => player.Skills.ContainsKey(s)).Any(s => !Elements.TryGetValue(s, out var element) || bot.Resistance(target, element) < 95);

    private static int? KeyFor(Bot bot, SkillId skill) =>
        bot.Keys.Skills.FirstOrDefault(b => b.Skill == skill) is { Skill: not SkillId.None } binding ? Bot.KeyOf(binding.Keys) : null;

    /// <summary>The skill selected last, for the log.</summary>
    public SkillId? Selected => _selected;
}

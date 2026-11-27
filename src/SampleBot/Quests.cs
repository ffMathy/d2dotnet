using D2Dotnet;
using D2Dotnet.Game;

/// <summary>One quest the bot can do.</summary>
internal interface IQuest
{
    QuestId Id { get; }

    /// <summary>The character level the bot wants before trying the quest; below it, it levels in the act's wilderness first.</summary>
    int MinimumLevel { get; }

    /// <summary>True when there is nothing left for the bot to do: the quest is done, or its goal is (and only the reward is left).</summary>
    bool IsDone(Bot bot);

    void Run(Bot bot);
}

/// <summary>
/// A quest whose goal is to clear a level (the Den of Evil) or to kill one monster in it (Blood Raven in the Burial
/// Grounds): travel there, and explore the level until the goal is met.
/// </summary>
internal sealed class ClearAreaQuest(QuestId id, AreaId area, int minimumLevel, MonsterId? boss = null) : IQuest
{
    public QuestId Id => id;

    public int MinimumLevel => minimumLevel;

    public bool IsDone(Bot bot) => GoalMet(bot.Reader.ReadQuests().GetValueOrDefault(id));

    // The goal is met once the quest log has moved on to the reward (or the quest is done).
    private static bool GoalMet(QuestStatus status) =>
        status.IsCompleted() || (status & (QuestStatus.RewardGranted | QuestStatus.RewardPending | QuestStatus.PrimaryGoalCompleted)) != 0;

    public void Run(Bot bot)
    {
        Bot.Log($"{id}: to {area}{(boss is { } b ? $" for {b}" : "")}.");
        bot.TravelTo(area);
        if (boss is { } target)
        {
            // Explore until the boss is in sight, then fight it.
            bot.ExploreLevel(until: () => bot.Reader.ReadMonsters().Any(m => m.Class == target && m.IsEnemy));
            if (bot.Reader.ReadMonsters().FirstOrDefault(m => m.Class == target && m.IsEnemy) is { } found)
            {
                Bot.Log($"{target} found.");
                bot.Fight(found.Id, seconds: 120);
            }
        }
        else
        {
            bot.ExploreLevel();
        }

        Bot.Log(IsDone(bot) ? $"{id}: the goal is met." : $"{id}: explored {area}, but the quest log has not moved on.");
    }
}

/// <summary>The quests in the order the bot does them; it runs the first one that is not done.</summary>
internal sealed class QuestRunner(Bot bot)
{
    private readonly GameUi ui = new(bot);

    public static IReadOnlyList<IQuest> Quests { get; } =
    [
        new ClearAreaQuest(QuestId.Act1DenOfEvil, AreaId.DenOfEvil, minimumLevel: 3),
        new ClearAreaQuest(QuestId.Act1SistersBurialGrounds, AreaId.BurialGrounds, minimumLevel: 7, MonsterId.BloodRaven),
    ];

    // The wilderness each act levels in, nearest the town first.
    private static readonly Dictionary<ActId, AreaId[]> LevellingAreas = new()
    {
        [ActId.Act1] = [AreaId.BloodMoor, AreaId.ColdPlains],
    };

    // Explores the act's levelling areas, one after the other, until the character reaches the level; points are
    // spent as it levels.
    private void LevelUp(int level)
    {
        var areas = LevellingAreas.GetValueOrDefault(bot.Act.Act) ?? throw new BotStopped($"No levelling areas for {bot.Act.Act}.");
        Bot.Log($"Level {bot.Player().Level}: levelling to {level} in {string.Join(" and ", areas)}.");
        foreach (var area in areas)
        {
            if (bot.Player().Level >= level)
                return;
            bot.TravelTo(area);
            bot.ExploreLevel(until: () => bot.Player().Level >= level);
            CharacterBuild.SpendPoints(bot, ui);
        }
    }

    /// <summary>The first quest that is not done, or null when every quest the bot knows is.</summary>
    public IQuest? Next() => Quests.FirstOrDefault(q => !q.IsDone(bot));

    public void Run()
    {
        var tries = 0;
        IQuest? last = null;
        while (Next() is { } quest)
        {
            tries = quest == last ? tries + 1 : 1;
            last = quest;
            if (tries > 5)
                throw new BotStopped($"{quest.Id} is still not done after five tries.");

            bot.HealInTown();
            CharacterBuild.SpendPoints(bot, ui);
            try
            {
                if (bot.Player().Level < quest.MinimumLevel)
                    LevelUp(quest.MinimumLevel);
                else
                    quest.Run(bot);
            }
            catch (LowLife low)
            {
                // Back to town, healed there, and the quest is tried again.
                bot.Retreat(low.Message);
            }
        }

        Bot.Log($"Every quest the bot knows is done ({string.Join(", ", Quests.Select(q => q.Id))}).");
    }
}

using D2Dotnet;
using D2Dotnet.Game;

/// <summary>One quest the bot can do.</summary>
internal interface IQuest
{
    QuestId Id { get; }

    /// <summary>True when there is nothing left for the bot to do: the quest is done, or its goal is (and only the reward is left).</summary>
    bool IsDone(Bot bot);

    void Run(Bot bot);
}

/// <summary>
/// A quest whose goal is to clear a level (the Den of Evil) or to kill one monster in it (Blood Raven in the Burial
/// Grounds): travel there, and explore the level until the goal is met.
/// </summary>
internal sealed class ClearAreaQuest(QuestId id, AreaId area, MonsterId? boss = null) : IQuest
{
    public QuestId Id => id;

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
    public static IReadOnlyList<IQuest> Quests { get; } =
    [
        new ClearAreaQuest(QuestId.Act1DenOfEvil, AreaId.DenOfEvil),
        new ClearAreaQuest(QuestId.Act1SistersBurialGrounds, AreaId.BurialGrounds, MonsterId.BloodRaven),
    ];

    /// <summary>The first quest that is not done, or null when every quest the bot knows is.</summary>
    public IQuest? Next() => Quests.FirstOrDefault(q => !q.IsDone(bot));

    public void Run()
    {
        while (Next() is { } quest)
        {
            try
            {
                quest.Run(bot);
            }
            catch (LowLife low)
            {
                bot.Retreat(low.Message);
            }

            if (!quest.IsDone(bot))
                throw new BotStopped($"{quest.Id} is not done after running it.");
        }

        Bot.Log($"Every quest the bot knows is done ({string.Join(", ", Quests.Select(q => q.Id))}).");
    }
}

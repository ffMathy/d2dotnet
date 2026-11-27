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

/// <summary>
/// The Search for Cain: the Scroll of Inifuss from the tree in the Dark Wood, Akara reads it (it becomes the Key to the
/// Cairn Stones), the five stones in the Stony Field open a portal to Tristram, and Cain's cage there is opened.
/// </summary>
internal sealed class SearchForCainQuest : IQuest
{
    private static readonly ObjectId[] Stones =
    [
        ObjectId.CairnStoneAlpha, ObjectId.CairnStoneBeta, ObjectId.CairnStoneGamma,
        ObjectId.CairnStoneDelta, ObjectId.CairnStoneLambda, ObjectId.CairnStoneTheta,
    ];

    public QuestId Id => QuestId.Act1TheSearchForCain;

    public int MinimumLevel => 9;

    // Done once Cain is free: the quest log moves on to the reward (Cain is then in the camp).
    public bool IsDone(Bot bot) =>
        bot.Reader.ReadQuests().GetValueOrDefault(Id) is var status
        && (status.IsCompleted() || (status & (QuestStatus.RewardGranted | QuestStatus.RewardPending)) != 0);

    public void Run(Bot bot)
    {
        bot.QuestItems.Add(ItemId.ScrollOfInifuss);
        if (bot.Player().Area != AreaId.Tristram)
        {
            if (!bot.HasItem(ItemId.ScrollOfInifuss) && !bot.HasItem(ItemId.KeyToTheCairnStones))
                FetchTheScroll(bot);
            if (bot.HasItem(ItemId.ScrollOfInifuss))
            {
                Bot.Log("Taking the Scroll of Inifuss to Akara.");
                bot.TravelTo(AreaId.RogueEncampment);
                bot.TalkTo(MonsterId.Akara, speech: 4);
            }

            OpenThePortal(bot);
        }

        FreeCain(bot);
    }

    private static void FetchTheScroll(Bot bot)
    {
        Bot.Log("Fetching the Scroll of Inifuss from the tree in the Dark Wood.");
        bot.TravelTo(AreaId.DarkWood);
        var tree = bot.PresetPosition(ObjectId.InifussTree) ?? throw new BotStopped("The Dark Wood has no Tree of Inifuss.");
        bot.WalkTo(bot.GridHere(), tree, arriveWithin: 8, fight: true);
        bot.FightNearby(Bot.FightRange);
        if (!bot.UseObject(ObjectId.InifussTree, until: () => bot.Reader.ReadInventory().Items.Any(i => i.Class == ItemId.ScrollOfInifuss)))
            throw new BotStopped("Could not use the Tree of Inifuss.");
        Thread.Sleep(500);
        bot.PickUpNearby();
        if (!bot.HasItem(ItemId.ScrollOfInifuss))
            throw new BotStopped("The Scroll of Inifuss is not in the inventory.");
    }

    private static void OpenThePortal(Bot bot)
    {
        Bot.Log("Opening the portal at the Cairn Stones.");
        bot.TravelTo(AreaId.StonyField);
        var stones = bot.PresetPosition(ObjectId.CairnStoneAlpha) ?? throw new BotStopped("The Stony Field has no Cairn Stones.");
        bot.WalkTo(bot.GridHere(), stones, arriveWithin: 8, fight: true);
        bot.FightNearby(25); // Rakanishu and his pack guard the stones

        // Touch every stone, in rounds, until the red portal stands among them.
        for (var round = 0; round < 3 && !bot.Reader.ReadObjects().Any(o => o.IsRedPortal); round++)
        {
            foreach (var stone in Stones)
            {
                bot.UseObject(stone);
                Thread.Sleep(300);
            }

            Thread.Sleep(1500);
        }

        if (!bot.Reader.ReadObjects().Any(o => o.IsRedPortal))
            throw new BotStopped("Touched the Cairn Stones, but no portal opened.");
        Bot.Log("Entering the portal to Tristram.");
        bot.UseObject(ObjectId.PermanentTownPortal, until: () => bot.Reader.ReadMainPlayer()?.Area == AreaId.Tristram);
        if (!bot.WaitForArea(AreaId.Tristram, seconds: 10))
            throw new BotStopped("The portal did not lead to Tristram.");
    }

    private void FreeCain(Bot bot)
    {
        Bot.Log("Freeing Cain in Tristram.");
        var cage = bot.PresetPosition(ObjectId.CainGibbet) ?? throw new BotStopped("Tristram has no cage for Cain.");
        bot.WalkTo(bot.GridHere(), cage, arriveWithin: 8, fight: true);
        bot.FightNearby(25);
        if (!bot.UseObject(ObjectId.CainGibbet, until: () => IsDone(bot)))
            Bot.Log("Clicked Cain's cage, but the quest log has not moved on yet.");
        bot.TravelTo(AreaId.RogueEncampment);
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
        new SearchForCainQuest(),
    ];

    // The wilderness each act levels in, nearest the town first.
    private static readonly Dictionary<ActId, AreaId[]> LevellingAreas = new()
    {
        [ActId.Act1] = [AreaId.BloodMoor, AreaId.ColdPlains, AreaId.StonyField],
    };

    // The level a character needs before it levels in each area (the Blood Moor's monsters are the weakest).
    private static readonly Dictionary<AreaId, int> AreaLevels = new()
    {
        [AreaId.BloodMoor] = 1,
        [AreaId.ColdPlains] = 6,
        [AreaId.StonyField] = 9,
    };

    // Explores the act's levelling areas, one after the other, until the character reaches the level; points are
    // spent as it levels.
    private void LevelUp(int level)
    {
        var areas = LevellingAreas.GetValueOrDefault(bot.Act.Act) ?? throw new BotStopped($"No levelling areas for {bot.Act.Act}.");
        Bot.Log($"Level {bot.Player().Level}: levelling to {level} in {string.Join(" and ", areas)}.");
        // The hardest area the character is ready for, again and again until the level is reached.
        for (var round = 0; round < 10 && bot.Player().Level < level; round++)
        {
            var characterLevel = bot.Player().Level;
            var area = areas.Where(x => characterLevel >= AreaLevels.GetValueOrDefault(x)).DefaultIfEmpty(areas[0]).Last();
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
        var deaths = 0;
        var calibrated = false;
        Died? died = null;
        IQuest? last = null;
        while (Next() is { } quest)
        {
            tries = quest == last ? tries + 1 : 1;
            last = quest;
            if (tries > 5)
                throw new BotStopped($"{quest.Id} is still not done after five tries.");

            try
            {
                if (!calibrated)
                {
                    bot.CloseDialog(); // a dialog or shop left open by an earlier run
                    bot.Calibrate();
                    calibrated = true;
                }

                // Hurt out in the field (a stopped run, or one that ran out of potions): back to town first.
                if (bot.Player() is { LifePercent: < 40 } hurt && hurt.Area != bot.Town)
                    bot.Retreat($"Life is at {hurt.LifePercent}%.");
                bot.HealInTown();
                CharacterBuild.SpendPoints(bot, ui);
                Shop.BuyPotions(bot, ui);
                if (bot.Player().Level < quest.MinimumLevel)
                    LevelUp(quest.MinimumLevel);
                else
                    quest.Run(bot);
            }
            catch (LowLife low)
            {
                // Back to town, healed there, and the quest is tried again.
                try
                {
                    bot.Retreat(low.Message);
                }
                catch (Died retreatDeath)
                {
                    died = retreatDeath;
                }
            }
            catch (Died death)
            {
                died = death;
            }
            catch (Stuck stuck)
            {
                // Counted as a try: the next one starts from where the character is.
                Bot.Log($"{stuck.Message} Trying {quest.Id} again.");
            }

            // Back to life in town and to the corpse; dying on the way there starts that again.
            while (died is { } death)
            {
                died = null;
                if (++deaths > 5)
                    throw new BotStopped("The character died five times; stopping.");
                try
                {
                    bot.Respawn(death);
                }
                catch (Died again)
                {
                    died = again;
                }
                catch (LowLife low)
                {
                    // Hurt again on the way to the corpse: back to town; the corpse waits for the next try.
                    try
                    {
                        bot.Retreat(low.Message);
                    }
                    catch (Died again)
                    {
                        died = again;
                    }
                }
            }
        }

        Bot.Log($"Every quest the bot knows is done ({string.Join(", ", Quests.Select(q => q.Id))}).");
    }
}

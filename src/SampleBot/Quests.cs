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

    /// <summary>Who gives the reward once the goal is met (talked to while the log says it is pending), or null.</summary>
    MonsterId[] RewardGivers => [];

    void Run(Bot bot);
}

/// <summary>
/// A quest whose goal is to clear a level (the Den of Evil) or to kill one monster in it (Blood Raven in the Burial
/// Grounds): travel there, explore the level until the goal is met, and go back to the quest giver, whose talk moves the
/// quest log on (the game shows a kill in the log only after that, or after a new game).
/// </summary>
internal sealed class ClearAreaQuest(QuestId id, AreaId area, int minimumLevel, MonsterId giver, MonsterId? boss = null, string? superUnique = null) : IQuest
{
    public QuestId Id => id;

    public int MinimumLevel => minimumLevel;

    public bool IsDone(Bot bot) => GoalMet(bot.Reader.ReadQuests().GetValueOrDefault(id));

    // The goal is met once the quest log has moved on to the reward (or the quest is done).
    private static bool GoalMet(QuestStatus status) =>
        status.IsCompleted() || (status & (QuestStatus.RewardGranted | QuestStatus.RewardPending | QuestStatus.PrimaryGoalCompleted)) != 0;

    public void Run(Bot bot)
    {
        var name = boss?.ToString() ?? superUnique;
        Bot.Log($"{id}: to {area}{(name is not null ? $" for {name}" : "")}.");
        bot.TravelTo(area);
        if (name is not null)
        {
            // Explore until the boss is in sight, then fight it. A super unique of a common class (the Countess, a Dark
            // Stalker) is the level's only super unique.
            bool IsBoss(Monster m) => m.IsEnemy && (boss is { } target ? m.Class == target : m.Type == MonsterType.SuperUnique);
            bot.ExploreLevel(until: () => bot.Reader.ReadMonsters().Any(IsBoss));
            if (bot.Reader.ReadMonsters().FirstOrDefault(IsBoss) is { } found)
            {
                Bot.Log($"{name} found.");
                bot.Proof($"{name} found");
                bot.Fight(found.Id, seconds: 120);
            }
        }
        else
        {
            bot.ExploreLevel();
        }

        if (!IsDone(bot))
        {
            Bot.Log($"{id}: explored {area}; back to {giver} for the quest log.");
            bot.TravelTo(bot.Town, fight: true);
            bot.TalkTo(giver, speech: 6);
            bot.Proof($"talked to {giver}");
        }

        Bot.Log(IsDone(bot) ? $"{id}: the goal is met." : $"{id}: the quest log has not moved on.");
        bot.Proof(IsDone(bot) ? $"{id} goal met" : $"{id} not done");
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

    // "Visit Cain and Akara in the Rogue Encampment": Akara gives the reward.
    public MonsterId[] RewardGivers => [MonsterId.Akara];

    // Rakanishu, lightning enchanted, guards the Cairn Stones: the bot leaves him alone below Bot.DeadlyBelowLevel. The
    // levelling guides send characters to Tristram from about level 5, so the quest comes as early as that allows.
    public int MinimumLevel => Bot.DeadlyBelowLevel;

    // Done once Cain is free: the quest log moves on to the reward (Cain is then in the camp).
    public bool IsDone(Bot bot) =>
        bot.Reader.ReadQuests().GetValueOrDefault(Id) is var status
        && (status.IsCompleted() || (status & (QuestStatus.RewardGranted | QuestStatus.RewardPending)) != 0);

    public void Run(Bot bot)
    {
        bot.QuestItems.Add(ItemId.ScrollOfInifuss);
        Bot.Log($"{Id}: status {bot.Reader.ReadQuests().GetValueOrDefault(Id)} ({(ushort)bot.Reader.ReadQuests().GetValueOrDefault(Id):X4}).");
        if (bot.Player().Area != AreaId.Tristram)
        {
            // Once the stones opened, the key is gone and the stones open again without it (in any later game).
            if (!bot.HasItem(ItemId.ScrollOfInifuss) && !bot.HasItem(ItemId.KeyToTheCairnStones) && !StonesOpened(bot))
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

    // The character opened the Cairn Stones once: a file per character, as the quest status bits for it are not known.
    private static string StonesFile(Bot bot) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Dotnet", $"cairn-stones-{bot.Player().Name}.txt");

    private static bool StonesOpened(Bot bot) => File.Exists(StonesFile(bot));

    private static void MarkStonesOpened(Bot bot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StonesFile(bot))!);
        File.WriteAllText(StonesFile(bot), $"{bot.Reader.ReadQuests().GetValueOrDefault(QuestId.Act1TheSearchForCain)}");
    }

    /// <summary>Goes to Tristram through the Cairn Stones' portal (opening it when it is not open): for Tristram runs.</summary>
    public static void EnterTristram(Bot bot)
    {
        if (bot.Player().Area != AreaId.Tristram)
            OpenThePortal(bot);
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
        MarkStonesOpened(bot);
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

/// <summary>
/// Tools of the Trade: the Horadric Malus lies on its stand in the Barracks (a click on the stand drops it); Charsi takes
/// it back.
/// </summary>
internal sealed class ToolsOfTheTradeQuest : IQuest
{
    public QuestId Id => QuestId.Act1ToolsOfTheTrade;

    public int MinimumLevel => 1;

    public bool IsDone(Bot bot) =>
        bot.Reader.ReadQuests().GetValueOrDefault(Id) is var status
        && (status.IsCompleted() || (status & (QuestStatus.RewardGranted | QuestStatus.RewardPending)) != 0);

    public void Run(Bot bot)
    {
        bot.QuestItems.Add(ItemId.HoradricMalus);
        if (!bot.HasItem(ItemId.HoradricMalus))
        {
            Bot.Log("Fetching the Horadric Malus from the Barracks.");
            bot.TravelTo(AreaId.Barracks);
            var stand = bot.PresetPosition(ObjectId.Malus) ?? throw new BotStopped("The Barracks have no Malus.");
            bot.WalkTo(bot.GridHere(), stand, arriveWithin: 8, fight: true);
            bot.FightNearby(25); // the Smith guards it
            bot.Proof("at the Malus");
            bot.UseObject(ObjectId.Malus, until: () => bot.Reader.ReadInventory().Items.Any(i => i.Class == ItemId.HoradricMalus));
            Thread.Sleep(500);
            bot.PickUpNearby();
            if (!bot.HasItem(ItemId.HoradricMalus))
                throw new BotStopped("The Horadric Malus is not in the inventory.");
        }

        Bot.Log("Taking the Horadric Malus to Charsi.");
        bot.TravelTo(bot.Town);
        bot.TalkTo(MonsterId.Charsi, speech: 5);
    }
}

/// <summary>The quests in the order the bot does them; it runs the first one that is not done.</summary>
internal sealed class QuestRunner(Bot bot)
{
    private readonly GameUi ui = new(bot);

    public static IReadOnlyList<IQuest> Quests { get; } =
    [
        new ClearAreaQuest(QuestId.Act1DenOfEvil, AreaId.DenOfEvil, minimumLevel: 3, MonsterId.Akara),
        new ClearAreaQuest(QuestId.Act1SistersBurialGrounds, AreaId.BurialGrounds, minimumLevel: 7, MonsterId.Kashya, MonsterId.BloodRaven),
        new SearchForCainQuest(),
        new ClearAreaQuest(QuestId.Act1TheForgottenTower, AreaId.TowerCellarLevel5, minimumLevel: 10, MonsterId.Akara, superUnique: "TheCountess"),
        new ToolsOfTheTradeQuest(),
        new ClearAreaQuest(QuestId.Act1SistersToTheSlaughter, AreaId.CatacombsLevel4, minimumLevel: 18, MonsterId.Akara, MonsterId.Andariel),
    ];

    // Rewards already asked for in this run: each is asked for once.
    private readonly HashSet<QuestId> _rewardsAsked = [];

    /// <summary>Talks to the givers of the rewards the quest log says are waiting (in town only).</summary>
    private void CollectRewards()
    {
        if (bot.Player().Area != bot.Town)
            return;
        foreach (var quest in Quests)
        {
            var status = bot.Reader.ReadQuests().GetValueOrDefault(quest.Id);
            if (quest.RewardGivers.Length == 0 || (status & QuestStatus.RewardPending) == 0 || !_rewardsAsked.Add(quest.Id))
                continue;
            Bot.Log($"{quest.Id}: the reward is waiting ({status}); talking to {string.Join(", ", quest.RewardGivers)}.");
            foreach (var giver in quest.RewardGivers)
                bot.TalkTo(giver, speech: 5);
            bot.ProofQuestLog($"{quest.Id} reward");
        }
    }

    // The wilderness each act levels in, nearest the town first.
    /// <summary>
    /// Where the levelling guides send a character of the level (Maxroll, d2db: Normal), best last: the Den of Evil to
    /// about 5; Tristram runs (once Cain is free) or the Countess's tower to 15; then the Act 2 tombs, Travincal,
    /// Baal runs, which the bot cannot reach yet: there, and in Nightmare and Hell, the act's areas with the highest
    /// monster level for now (see <see cref="BestLevellingAreas"/>).
    /// </summary>
    internal List<AreaId> GuideAreas(int characterLevel)
    {
        if (bot.Act.Difficulty == Difficulty.Normal && bot.Act.Act == ActId.Act1)
        {
            if (characterLevel < 6)
                return [AreaId.DenOfEvil];
            if (characterLevel < 15)
            {
                return Quests.OfType<SearchForCainQuest>().First().IsDone(bot)
                    ? [AreaId.Tristram]
                    : [AreaId.TowerCellarLevel1, AreaId.TowerCellarLevel2, AreaId.TowerCellarLevel3, AreaId.TowerCellarLevel4, AreaId.TowerCellarLevel5];
            }
        }

        return BestLevellingAreas(characterLevel);
    }

    // Areas left out of levelling: a portal leads to Tristram; Andariel's lair and the Den of Evil are quest places.
    private static readonly HashSet<AreaId> NotForLevelling = [AreaId.Tristram, AreaId.CatacombsLevel4, AreaId.DenOfEvil];

    /// <summary>
    /// The act's areas to level in, best last: reachable on foot from town, with monsters at most a level above the
    /// character (experience falls off for monsters far below it, and much higher ones kill), the four with the
    /// highest monster level (levels.txt MonLvlEx: the Act 1 Catacombs 11-12, the Tamoe Highland 8...).
    /// </summary>
    internal List<AreaId> BestLevellingAreas(int characterLevel)
    {
        var difficulty = bot.Act.Difficulty;
        return bot.Act.Levels
            .Select(l => (l.Id, Monsters: bot.Data.Levels.Find(l.Id)?.MonsterLevelIn(difficulty) ?? 0))
            .Where(l => l.Monsters > 0 && l.Monsters <= characterLevel + 1 && !NotForLevelling.Contains(l.Id)
                        && l.Id != bot.Town && bot.Act.FindRoute(bot.Town, l.Id) is not null)
            .OrderBy(l => l.Monsters)
            .TakeLast(4)
            .Select(l => l.Id)
            .ToList();
    }

    // The areas explored in this game: their monsters stay dead until a new game. (The map seed does not tell games
    // apart: a single-player character keeps its map from game to game.)
    private readonly HashSet<AreaId> _explored = [];

    // Explores the act's levelling areas until the character reaches the level: the hardest one it is ready for that
    // this game has not been explored yet, else the next one up; points are spent as it levels.
    private void LevelUp(int level)
    {
        Bot.Log($"Level {bot.Player().Level}: levelling to {level} in {string.Join(", ", GuideAreas(bot.Player().Level))}.");
        while (bot.Player().Level < level)
        {
            var characterLevel = bot.Player().Level;
            // The areas whose monsters give the most experience for the level; when they are cleared, a new game
            // brings them back (quicker than clearing easier ones, which give next to nothing).
            var ready = GuideAreas(characterLevel);
            var fresh = ready.Where(a => !_explored.Contains(a) && !bot.IsAvoided(a)).ToList();
            if (fresh.Count == 0)
                fresh = ready.Where(a => !_explored.Contains(a)).ToList(); // only avoided ones left: better than new games
            if (fresh.Count == 0)
            {
                // The monsters of every levelling area are dead in this game: a new one brings them back.
                if (bot.Player().Area != bot.Town)
                    bot.TravelTo(bot.Town, fight: false);
                GameSession.NewGame(bot);
                _explored.Clear();
                continue;
            }
            var area = fresh[^1];
            try
            {
                if (area == AreaId.Tristram)
                    SearchForCainQuest.EnterTristram(bot); // through the Cairn Stones' portal, not on foot
                else
                    bot.TravelTo(area);
                bot.ExploreLevel(until: () => bot.Player().Level >= level);
            }
            catch (TooDangerous danger)
            {
                // Out by portal, and the area is done for this game.
                bot.Retreat(danger.Message);
                bot.ForgetPortal(); // its portal leads back to the danger
                _explored.Add(area);
                continue;
            }

            if (bot.Player().Level < level)
                _explored.Add(area);
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
            // No levelling first: the quest is tried again and again, leaving the game when it is too much; every try is
            // experience. (A cap only against a quest the bot cannot do at all.)
            tries = quest == last ? tries + 1 : 1;
            last = quest;
            if (tries > 300)
                throw new BotStopped($"{quest.Id} is still not done after {tries - 1} tries.");
            Bot.Log($"{quest.Id}: try {tries} at level {bot.Player().Level}.");

            try
            {
                if (!calibrated)
                {
                    bot.CloseDialog(); // a dialog or shop left open by an earlier run
                    bot.Calibrate();
                    calibrated = true;
                }

                // Hurt out in the field (a stopped run): a new game.
                if (bot.Player() is { LifePercent: < 40 } hurt && hurt.Area != bot.Town)
                    throw new LowLife($"Life is at {hurt.LifePercent}%.");
                if (bot.Player().Area == bot.Town)
                    bot.PickUpCorpse(); // a corpse a new game brought to town
                bot.HealInTown();
                CharacterBuild.SpendPoints(bot, ui);
                SkillBinder.Bind(bot, ClassCombat.AttackSkillsOf(bot.Player().Class));
                CollectRewards();
                if (bot.Player().Area == bot.Town)
                    new TownChores(bot, ui, bot.Items).Run();
                bot.Proof($"{quest.Id} start");
                quest.Run(bot);
                if (quest.IsDone(bot))
                    bot.ProofQuestLog($"{quest.Id} done");
            }
            catch (Exception danger) when (danger is LowLife or TooDangerous)
            {
                // Too much for now: out of the game, and the quest again in a new one.
                try
                {
                    bot.ExitAndRetry(danger.Message);
                }
                catch (Died exitDeath)
                {
                    died = exitDeath;
                }
            }
            catch (NeedTown errand)
            {
                // An errand (selling, repairs): to town by portal, and back to the quest.
                try
                {
                    bot.Retreat(errand.Message);
                }
                catch (Died retreatDeath)
                {
                    died = retreatDeath;
                }
                catch (Stuck stuck)
                {
                    Bot.Log($"{stuck.Message} (going to town) Trying again.");
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
                if (++deaths > 30)
                    throw new BotStopped("The character died 30 times; stopping.");
                try
                {
                    bot.Respawn(death);
                }
                catch (Died again)
                {
                    died = again;
                }
                catch (Stuck stuck)
                {
                    Bot.Log($"{stuck.Message} (going for the corpse) Trying again later.");
                }
                catch (Exception danger) when (danger is LowLife or TooDangerous)
                {
                    // Hurt again after coming back: out of the game; the corpse waits in town.
                    bot.ExitAndRetry(danger.Message);
                }
            }
        }

        CollectRewards();
        Bot.Log($"Every quest the bot knows is done ({string.Join(", ", Quests.Select(q => q.Id))}).");
    }
}

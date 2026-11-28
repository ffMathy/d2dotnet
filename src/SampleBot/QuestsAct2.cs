using D2Dotnet;
using D2Dotnet.Game;
using D2Dotnet.Maps;
using D2Dotnet.Screen;

/// <summary>
/// A quest item from a container at the end of a level: the Horadric Cube's chest (Halls of the Dead 3), the Staff of
/// Kings' chest (Maggot Lair 3), the Tainted Sun altar's Amulet of the Viper (Claw Viper Temple 2). Travel there, walk
/// up to where the level places the container, fight what guards it, click it and pick up what it drops.
/// </summary>
internal sealed class FetchQuestItem(QuestId id, AreaId area, ObjectId container, ItemId item, Func<Bot, bool> done) : IQuest
{
    public QuestId Id => id;

    public int MinimumLevel => 1;

    public bool IsDone(Bot bot) => done(bot);

    public void Run(Bot bot)
    {
        bot.QuestItems.Add(item);
        Bot.Log($"{id}: the {item} from the {container} in {area}.");
        bot.TravelTo(area);
        var spot = bot.PresetPosition(container) ?? throw new Stuck($"{area} has no {container}.");
        bot.WalkTo(bot.GridHere(), spot, arriveWithin: 8, fight: true);
        bot.FightNearby(25);
        bot.Proof($"at the {container}");
        bool Dropped() => bot.Reader.ReadInventory().Items.Any(i => i.Class == item);
        if (!Dropped())
            bot.UseObject(container, until: Dropped);
        Thread.Sleep(500);
        bot.FightNearby(15);
        bot.PickUpNearby();
        if (!bot.HasItem(item))
            throw new Stuck($"The {item} is not in the inventory.");
        Bot.Log($"{id}: got the {item}.");
        bot.Proof($"got the {item}");
    }
}

/// <summary>
/// The Horadric Staff: the Staff of Kings and the Amulet of the Viper put into the Horadric Cube and transmuted
/// (horadriccubelayouthd.json, a left panel: the 3 by 4 grid from (431, 534), cells of 98; the Transmute button at
/// (520, 969)). The staff comes out where the shaft was, and goes back to the inventory.
/// </summary>
internal sealed class TransmuteStaffQuest : IQuest
{
    private const int GridX = 431, GridY = 534, Cell = 98, ButtonX = 520, ButtonY = 969;

    public QuestId Id => QuestId.Act2TheHoradricStaff;

    public int MinimumLevel => 1;

    public bool IsDone(Bot bot) => Act2.StaffDone(bot);

    public void Run(Bot bot)
    {
        Bot.Log("The Horadric Staff: the shaft and the amulet into the cube.");
        bot.TravelTo(bot.Town);
        var ui = bot.Ui;
        bot.ClosePanels();
        InventoryPanel.Open(bot);
        var cube = Carried(bot).FirstOrDefault(i => i.Class == ItemId.HoradricCube) ?? throw new Stuck("No Horadric Cube in the inventory.");
        ui.ClickRightPanel(InventoryPanel.CentreOf(cube), right: true);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && !bot.Reader.ReadMenus().Cube)
            Thread.Sleep(100);
        if (!bot.Reader.ReadMenus().Cube)
            throw new Stuck("The Horadric Cube did not open.");

        // The shaft (1 by 3) in the first column, the amulet in the last.
        Put(bot, ui, ItemId.StaffOfKings, (GridX + Cell / 2, GridY + Cell * 3 / 2));
        Put(bot, ui, ItemId.AmuletOfTheViper, (GridX + Cell * 2 + Cell / 2, GridY + Cell / 2));
        ui.ClickLeftPanel((ButtonX + 60, ButtonY + 40));
        Thread.Sleep(1200);

        // The Horadric Staff (1 by 4) out of the first column, into the first free column of the inventory.
        ui.ClickLeftPanel((GridX + Cell / 2, GridY + Cell * 2));
        Thread.Sleep(400);
        if (InventoryPanel.FreePlace(Carried(bot), 1, 4) is { } place)
        {
            ui.ClickRightPanel(InventoryPanel.CentreOfCells(place, 1, 4));
            Thread.Sleep(400);
        }

        bot.ClosePanels();
        bot.ClearCursor();
        if (!Act2.HasStaff(bot))
            throw new Stuck("No Horadric Staff after the transmute.");
        Bot.Log("The Horadric Staff is made.");
        bot.Proof("the Horadric Staff");
    }

    // An item of the inventory onto the cursor, and down into the cube at the point.
    private static void Put(Bot bot, GameUi ui, ItemId item, (int X, int Y) at)
    {
        var carried = Carried(bot).FirstOrDefault(i => i.Class == item) ?? throw new Stuck($"No {item} in the inventory.");
        ui.ClickRightPanel(InventoryPanel.CentreOf(carried));
        Thread.Sleep(400);
        ui.ClickLeftPanel(at);
        Thread.Sleep(400);
    }

    private static List<Item> Carried(Bot bot) =>
        bot.Reader.ReadInventory().Items.Where(i => i.Location == ItemLocation.Inventory).ToList();
}

/// <summary>
/// The Arcane Sanctuary and the Summoner: through the palace cellars to the sanctuary's portal, to where the level
/// places Horazon's Journal (the Summoner stands by it), the Summoner killed, the journal read; its red portal leads to
/// the Canyon of the Magi, whose waypoint is then taken (activated on arrival).
/// </summary>
internal sealed class SummonerQuest : IQuest
{
    public QuestId Id => QuestId.Act2TheSummoner;

    public int MinimumLevel => 1;

    public bool IsDone(Bot bot) =>
        bot.Reader.ReadQuests().GetValueOrDefault(QuestId.Act2TheSummoner) is var status
        && (status.IsCompleted() || (status & (QuestStatus.RewardGranted | QuestStatus.RewardPending)) != 0)
        && bot.HasWaypoint(bot.Player(), AreaId.CanyonOfTheMagi);

    public void Run(Bot bot)
    {
        Act2.ToCanyon(bot, killSummoner: true);
        bot.Proof("in the Canyon of the Magi");
    }
}

/// <summary>
/// The Seven Tombs: the true tomb is the one whose level places the Horadric Orifice; the staff goes into the orifice,
/// the way down to Duriel opens beside it; Duriel killed, Tyrael talked to in his lair, then Jerhyn in town.
/// </summary>
internal sealed class SevenTombsQuest : IQuest
{
    public QuestId Id => QuestId.Act2TheSevenTombs;

    public int MinimumLevel => 1;

    public bool IsDone(Bot bot) =>
        bot.Reader.ReadQuests().GetValueOrDefault(QuestId.Act2TheSevenTombs) is var status
        && (status.IsCompleted() || (status & (QuestStatus.RewardGranted | QuestStatus.RewardPending)) != 0);

    public void Run(Bot bot)
    {
        var tomb = Act2.TrueTomb(bot) ?? throw new BotStopped("No tomb of the act places the Horadric Orifice.");
        Bot.Log($"The Seven Tombs: the true tomb is {tomb}.");
        if (bot.Player().Area != AreaId.DurielsLair)
        {
            if (!bot.HasWaypoint(bot.Player(), AreaId.CanyonOfTheMagi))
                Act2.ToCanyon(bot, killSummoner: false);
            bot.TravelTo(tomb);
            if (!Act2.StaffPlaced(bot))
                PlaceStaff(bot);
            EnterLair(bot);
        }

        if (bot.Reader.ReadMonsters().FirstOrDefault(m => m.Class == MonsterId.Duriel && m.IsEnemy) is { } duriel)
        {
            Bot.Log("Duriel found.");
            bot.Proof("Duriel found");
            bot.Fight(duriel.Id, seconds: 180);
        }

        // Tyrael waits behind the lair's door.
        if (bot.Reader.ReadMonsters().All(m => m.Class != MonsterId.Duriel || !m.IsEnemy))
        {
            bot.Proof("Duriel dead");
            bot.TalkTo(MonsterId.Tyrael, speech: 8);
            bot.TravelTo(bot.Town);
            bot.TalkTo(MonsterId.Jerhyn, speech: 6);
            bot.ProofQuestLog("after Duriel");
        }
    }

    // The staff into the orifice: the orifice clicked opens a small panel with one slot; the staff (on the cursor) is
    // dropped into the middle of what appeared (found in pictures from before and after, as the skill icons are).
    private static void PlaceStaff(Bot bot)
    {
        var orifice = bot.PresetPosition(ObjectId.HoradricOrifice) ?? throw new Stuck("This tomb has no orifice.");
        bot.WalkTo(bot.GridHere(), orifice, arriveWithin: 5, fight: true);
        bot.FightNearby(20);
        var before = GameScreen.Capture(bot.Window.Handle);
        bot.UseObject(ObjectId.HoradricOrifice, until: () => bot.Reader.ReadMenus().IsAnyPanelOpen);
        Thread.Sleep(600);
        var after = GameScreen.Capture(bot.Window.Handle);
        var panel = SkillBinder.FindIcons(before, after, bot.Window.ClientArea());
        var ui = bot.Ui;
        InventoryPanel.Open(bot);
        var staff = bot.Reader.ReadInventory().Items.FirstOrDefault(i => i.Location == ItemLocation.Inventory && i.Class == ItemId.HoradricStaff)
            ?? throw new Stuck("No Horadric Staff in the inventory.");
        ui.ClickRightPanel(InventoryPanel.CentreOf(staff));
        Thread.Sleep(400);
        // The slot: the middle of the changed squares on the left half (the inventory opened on the right).
        var (cx, _, cw, _) = bot.Window.ClientArea();
        var left = panel.Where(p => p.X < cx + cw / 2).ToList();
        if (left.Count > 0)
        {
            bot.Input.AimAt(((int)left.Average(p => p.X), (int)left.Average(p => p.Y)));
            bot.Input.LeftClick();
            Thread.Sleep(800);
        }

        bot.ClosePanels();
        bot.ClearCursor();
        if (!Act2.StaffPlaced(bot))
            throw new Stuck("The staff did not go into the orifice.");
        Bot.Log("The Horadric Staff is in the orifice.");
        bot.Proof("the staff in the orifice");
    }

    // Down the way to Duriel that opens by the orifice.
    private static void EnterLair(Bot bot)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !bot.Reader.ReadObjects().Any(o => o.Class == ObjectId.DurielsLairPortal))
            Thread.Sleep(500);
        if (!bot.UseObject(ObjectId.DurielsLairPortal, until: () => bot.Reader.ReadMainPlayer()?.Area == AreaId.DurielsLair))
            throw new Stuck("Could not go down to Duriel.");
        bot.WaitForArea(AreaId.DurielsLair, seconds: 10);
    }
}

/// <summary>What the Act 2 quests share.</summary>
internal static class Act2
{
    public static bool HasStaff(Bot bot) => bot.HasItem(ItemId.HoradricStaff);

    /// <summary>True once the staff is in the orifice (the Horadric Staff quest is done and the staff is gone).</summary>
    public static bool StaffPlaced(Bot bot) =>
        !bot.HasItem(ItemId.HoradricStaff)
        && bot.Reader.ReadQuests().GetValueOrDefault(QuestId.Act2TheHoradricStaff).IsCompleted();

    /// <summary>The Horadric Staff made or used: its pieces are no longer wanted.</summary>
    public static bool StaffDone(Bot bot) => HasStaff(bot) || StaffPlaced(bot);

    /// <summary>The tomb whose level places the Horadric Orifice.</summary>
    public static AreaId? TrueTomb(Bot bot) =>
        Enumerable.Range((int)AreaId.TalRashasTomb1, 7).Select(i => (AreaId)i)
            .Where(a => bot.Act.Find(a) is { } level
                        && level.Presets.Any(p => p.Kind == PresetKind.Object && p.ClassId == (int)ObjectId.HoradricOrifice))
            .Select(a => (AreaId?)a).FirstOrDefault();

    /// <summary>
    /// To the Canyon of the Magi through the Arcane Sanctuary: the palace cellars, the sanctuary's portal, Horazon's
    /// Journal (the Summoner killed on the way when asked), its red portal.
    /// </summary>
    public static void ToCanyon(Bot bot, bool killSummoner)
    {
        if (bot.Player().Area != AreaId.ArcaneSanctuary)
        {
            bot.TravelTo(AreaId.PalaceCellarLevel3);
            var portal = bot.PresetPosition(ObjectId.ArcaneSanctuaryPortal);
            if (portal is { } spot)
                bot.WalkTo(bot.GridHere(), spot, arriveWithin: 6, fight: true);
            if (!bot.UseObject(ObjectId.ArcaneSanctuaryPortal, until: () => bot.Reader.ReadMainPlayer()?.Area == AreaId.ArcaneSanctuary))
                throw new Stuck("Could not take the portal to the Arcane Sanctuary.");
            bot.WaitForArea(AreaId.ArcaneSanctuary, seconds: 10);
        }

        var journal = bot.PresetPosition(ObjectId.YetAnotherTome) ?? throw new Stuck("The Arcane Sanctuary has no journal.");
        Bot.Log($"To Horazon's Journal at {journal}.");
        bot.WalkTo(bot.GridHere(), journal, arriveWithin: 12, fight: true);
        if (killSummoner && bot.Reader.ReadMonsters().FirstOrDefault(m => m.Class == MonsterId.Summoner && m.IsEnemy) is { } summoner)
        {
            Bot.Log("The Summoner found.");
            bot.Proof("the Summoner found");
            bot.Fight(summoner.Id, seconds: 120);
        }

        bot.FightNearby(20);
        bot.UseObject(ObjectId.YetAnotherTome, until: () => bot.Reader.ReadObjects().Any(o => o.IsRedPortal));
        Thread.Sleep(1500);
        if (!bot.UseObject(ObjectId.PermanentTownPortal, until: () => bot.Reader.ReadMainPlayer()?.Area == AreaId.CanyonOfTheMagi))
            throw new Stuck("Could not take the journal's portal to the Canyon of the Magi.");
        bot.WaitForArea(AreaId.CanyonOfTheMagi, seconds: 10);
        bot.ActivateWaypointHere();
    }
}

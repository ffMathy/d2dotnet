using D2Dotnet;
using D2Dotnet.Game;

/// <summary>
/// What the bot does with items in town: stashes its gold, identifies with the Tome of Identify, wears upgrades, and
/// sells what it does not need (while total gold is below <see cref="ItemRules.SellingBelowGold"/>, or when the
/// inventory is nearly full). Every step is checked through memory.
/// </summary>
/// <remarks>
/// Positions from the HD layouts (reference pixels from the panel's corner): the inventory (playerinventory*layouthd,
/// a right panel) has a 10 by 4 grid from (93, 897) with 98-pixel cells, the gold button at (429, 1347), and slots
/// head (482, 185, 196x196), neck (718, 353, 98x98), torso (482, 428, 196x294), weapon (108, 232, 196x392), shield
/// (860, 232, 196x392), rings (344, 770) and (718, 770) (98x98), belt (483, 769, 196x98), feet (859, 672, 196x196),
/// gloves (107, 672, 196x196). A shop's goods (vendorpanellayouthd, a left panel) are from (91, 233).
/// </remarks>
internal sealed class TownChores(Bot bot, GameUi ui, ItemRules rules)
{
    private const int GridX = 93, GridY = 897, Cell = 98, Columns = 10, Rows = 4;

    // Potions kept in the inventory besides the belt; more are sold.
    private const int PotionReserve = 8;
    private static readonly (int X, int Y) GoldButton = (429 + 30, 1347 + 30);
    private static readonly (int X, int Y) ShopDropPoint = (91 + 5 * Cell, 233 + 4 * Cell);

    private static readonly Dictionary<Slot, (int X, int Y, int Width, int Height)> SlotRects = new()
    {
        [Slot.Head] = (482, 185, 196, 196), [Slot.Neck] = (718, 353, 98, 98), [Slot.Torso] = (482, 428, 196, 294),
        [Slot.Weapon] = (108, 232, 196, 392), [Slot.Shield] = (860, 232, 196, 392), [Slot.Belt] = (483, 769, 196, 98),
        [Slot.Feet] = (859, 672, 196, 196), [Slot.Gloves] = (107, 672, 196, 196),
    };

    private static readonly (int X, int Y, int Width, int Height)[] RingRects = [(344, 770, 98, 98), (718, 770, 98, 98)];

    /// <summary>Everything, in order: identify, wear upgrades, sell, buy potions, potions from the inventory to the belt, the gold left to the stash.</summary>
    public void Run()
    {
        if (bot.Player().Area != bot.Town)
            return;
        try
        {
            // Gold is stashed last: what selling brings in pays for the potions first.
            Identify();
            WearUpgrades();
            FreeBeltForHealing();
            WithdrawForPotions();
            Repair();
            InventoryPanel.RefillBelt(bot, ui);
            Sell();
            bot.CloseDialog();
            Shop.BuyPotions(bot, ui);
            InventoryPanel.RefillBelt(bot, ui);
            StashGold();
        }
        finally
        {
            bot.CloseDialog();
            ClosePanels();
            bot.ClearCursor();
        }
    }

    // ---- The inventory ----

    private IReadOnlyList<Item> Carried() => bot.Reader.ReadInventory().Items.Where(i => i.Location == ItemLocation.Inventory).ToList();

    private IReadOnlyList<Item> Equipped() => bot.Reader.ReadInventory().Items.Where(i => i.Location == ItemLocation.Equipped).ToList();

    /// <summary>The free cells of the 10 by 4 inventory.</summary>
    public int FreeCells() => Columns * Rows - Used(Carried()).Cast<bool>().Count(u => u);

    /// <summary>The free cells of the 10 by 4 inventory with these items carried.</summary>
    public static int FreeCellsOf(IReadOnlyList<Item> carried) => Columns * Rows - Used(carried).Cast<bool>().Count(u => u);

    /// <summary>True when an item of the size fits somewhere among the carried items.</summary>
    public static bool Fits(IReadOnlyList<Item> carried, int width, int height)
    {
        var used = Used(carried);
        for (var x = 0; x + width <= Columns; x++)
        {
            for (var y = 0; y + height <= Rows; y++)
            {
                var free = true;
                for (var dx = 0; dx < width && free; dx++)
                {
                    for (var dy = 0; dy < height && free; dy++)
                        free = !used[x + dx, y + dy];
                }

                if (free)
                    return true;
            }
        }

        return false;
    }

    private static bool[,] Used(IReadOnlyList<Item> carried)
    {
        var used = new bool[Columns, Rows];
        foreach (var item in carried.Where(i => i.Location == ItemLocation.Inventory))
        {
            for (var dx = 0; dx < (item.Info?.InventoryWidth ?? 1); dx++)
            {
                for (var dy = 0; dy < (item.Info?.InventoryHeight ?? 1); dy++)
                {
                    int x = item.Position.X + dx, y = item.Position.Y + dy;
                    if (x is >= 0 and < Columns && y is >= 0 and < Rows)
                        used[x, y] = true;
                }
            }
        }

        return used;
    }

    private static (int X, int Y) CentreOf(Item item) => InventoryPanel.CentreOf(item);

    private void OpenInventory() => InventoryPanel.Open(bot);

    private void ClosePanels() => bot.ClosePanels();

    private Item? OnCursor() => bot.Reader.ReadInventory().Items.FirstOrDefault(i => i.Location == ItemLocation.Cursor);

    // ---- Gold ----

    /// <summary>Puts the carried gold in the stash: opens the stash, clicks the inventory's gold button, and confirms with Enter.</summary>
    public void StashGold()
    {
        if (bot.Reader.ReadInventory().Gold <= 0)
            return;
        if (!bot.UseObject(ObjectId.Bank, until: () => bot.Reader.ReadMenus().Stash))
        {
            bot.Failure("Could not open the stash.");
            return;
        }

        Thread.Sleep(400);
        OpenInventory();
        var gold = bot.Reader.ReadInventory().Gold;
        ui.ClickRightPanel(GoldButton);
        Thread.Sleep(500);
        bot.Input.PressKey(Native.VkReturn);
        if (Wait(() => bot.Reader.ReadInventory().Gold < gold, 2))
            Bot.Log($"Stashed {gold - bot.Reader.ReadInventory().Gold} gold (stash: {bot.Reader.ReadInventory().StashGold.Sum()}).");
        else
            bot.Failure("Could not stash the gold.");
        ClosePanels();
    }

    // The stash panel's (bankexpansionlayouthd, a left panel) gold withdraw button.
    private static readonly (int X, int Y) WithdrawButton = (424 + 30, 1356 + 30);

    /// <summary>
    /// Takes gold out of the stash when potions, tomes or a repair are needed and little is carried: the withdraw button offers all of
    /// it (as much as can be carried), confirmed with Enter. The rest goes back with <see cref="StashGold"/>.
    /// </summary>
    public void WithdrawForPotions()
    {
        var inventory = bot.Reader.ReadInventory();
        var (room, spare) = Shop.PotionsWanted(bot);
        var tomes = !bot.HasItem(ItemId.TomeOfIdentify) || !bot.HasItem(ItemId.TomeOfTownPortal);
        if ((room + spare <= 0 && !tomes && LowestDurability() >= 0.5) || inventory.Gold >= 500 || inventory.StashGold.Sum() <= 0)
            return;
        if (!bot.UseObject(ObjectId.Bank, until: () => bot.Reader.ReadMenus().Stash))
        {
            bot.Failure("Could not open the stash to take gold out.");
            return;
        }

        Thread.Sleep(400);
        var gold = inventory.Gold;
        ui.ClickLeftPanel(WithdrawButton);
        Thread.Sleep(500);
        bot.Input.PressKey(Native.VkReturn);
        if (Wait(() => bot.Reader.ReadInventory().Gold > gold, 2))
            Bot.Log($"Took {bot.Reader.ReadInventory().Gold - gold} gold out of the stash for potions.");
        else
            bot.Failure("Could not take gold out of the stash.");
        ClosePanels();
    }

    /// <summary>
    /// A belt with more than two columns of mana potions has too little room for healing ones: in town, the extra mana
    /// columns are drunk empty (mana potions are cheap), so healing potions get columns.
    /// </summary>
    public void FreeBeltForHealing()
    {
        for (var round = 0; round < 8; round++)
        {
            var belt = bot.Reader.ReadInventory().Belt;
            var manaColumns = belt.Items.Where(i => i.IsManaPotion).Select(i => i.Position.X % 4).Distinct().ToList();
            if (manaColumns.Count <= 2)
                return;
            // The mana column with the fewest potions goes first; its bottom potion is drunk with its belt key.
            var column = manaColumns.OrderBy(c => belt.Items.Count(i => i.Position.X % 4 == c)).First();
            if (round == 0)
                Bot.Log($"The belt has {manaColumns.Count} columns of mana potions: drinking one empty for healing potions.");
            bot.Press(bot.Keys.UseBelt[column]);
            Thread.Sleep(700);
        }
    }

    // ---- Identifying ----

    /// <summary>Identifies the carried magic items with the Tome of Identify: right click on the tome, left click on the item.</summary>
    public void Identify()
    {
        var unknown = Carried().Where(i => !i.IsIdentified && i.Quality is ItemQuality.Magic or ItemQuality.Rare or ItemQuality.Set or ItemQuality.Unique).ToList();
        if (unknown.Count == 0)
            return;
        OpenInventory();
        foreach (var item in unknown)
        {
            // The tome, else a scroll.
            var tome = Carried().FirstOrDefault(i => i.Class == ItemId.TomeOfIdentify) ?? Carried().FirstOrDefault(i => i.Class == ItemId.ScrollOfIdentify);
            if (tome is null)
            {
                Bot.Log("Nothing to identify with (no Tome or Scroll of Identify).");
                return;
            }

            ui.ClickRightPanel(CentreOf(tome), right: true);
            Thread.Sleep(300);
            ui.ClickRightPanel(CentreOf(item));
            if (Wait(() => Carried().FirstOrDefault(i => i.Id == item.Id) is { IsIdentified: true }, 2))
            {
                Bot.Log($"Identified {item.Quality} {item.Class}.");
                continue;
            }

            // The click picked the item up instead (identifying had not started): put it back.
            bot.Failure($"Could not identify {item.Class}.");
            if (OnCursor()?.Id == item.Id)
            {
                ui.ClickRightPanel(CentreOf(item));
                Wait(() => OnCursor() is null, 2);
            }

            return;
        }
    }

    // ---- Wearing ----

    /// <summary>Wears the carried items that score better than what is worn: picks the item up, clicks its slot, and puts what comes off back in the inventory.</summary>
    public void WearUpgrades()
    {
        for (var round = 0; round < 6; round++)
        {
            var player = bot.Player();
            var equipped = Equipped();
            var upgrade = Carried().Where(i => rules.IsUpgrade(i, player, equipped)).MaxBy(i => rules.Score(i, player.Class));
            if (upgrade is null)
                return;

            var slot = rules.SlotOf(upgrade);
            var replaced = rules.Replaces(upgrade, player, equipped);
            var rect = slot == Slot.Ring ? RingRects[RingIndex(replaced, equipped)] : SlotRects[slot];
            OpenInventory();
            ui.ClickRightPanel(CentreOf(upgrade));
            if (!Wait(() => OnCursor()?.Id == upgrade.Id, 2))
            {
                bot.Failure($"Could not pick up {upgrade.Class} to wear it.");
                return;
            }

            ui.ClickRightPanel((rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
            if (!Wait(() => Equipped().Any(e => e.Id == upgrade.Id), 2))
            {
                bot.Failure($"Could not wear {upgrade.Class}; putting it back.");
                ui.ClickRightPanel(CentreOf(upgrade));
                return;
            }

            // What came off is on the cursor: back into the inventory, where the upgrade was.
            Thread.Sleep(200);
            if (OnCursor() is { } off)
            {
                ui.ClickRightPanel(CentreOf(upgrade));
                Wait(() => OnCursor() is null, 2);
                Bot.Log($"Wearing {upgrade.Quality} {upgrade.Class} instead of {off.Class}.");
            }
            else
            {
                Bot.Log($"Wearing {upgrade.Quality} {upgrade.Class}.");
            }
        }
    }

    // Which ring slot holds the ring to replace: 0 (right hand) or 1 (left hand). The equipped item's position is taken
    // as its body location (7 for the left hand), else the first slot.
    private static int RingIndex(Item? replaced, IReadOnlyList<Item> equipped) =>
        replaced is null ? (equipped.Count(e => e.Info?.Type == "ring") == 0 ? 0 : 1) : replaced.Position.X == 7 ? 1 : 0;

    // ---- Repairing ----

    // The shop's (vendorpanellayouthd, a left panel) repair-all button (button_repair_all; button_repair at x 169 repairs one item at a time).
    private static readonly (int X, int Y) RepairButton = (877 + 40, 1277 + 40);

    /// <summary>The worn items' lowest durability, as a share of their maximum (1 when nothing wears out).</summary>
    public double LowestDurability() => bot.LowestDurability();

    /// <summary>Repairs everything at the town's smith (Charsi, Fara, Hratli, Halbu, Larzuk) once something worn is below half its durability.</summary>
    public void Repair()
    {
        var lowest = LowestDurability();
        if (lowest >= 0.5 || bot.Reader.ReadInventory().Gold + bot.Reader.ReadInventory().StashGold.Sum() < 100)
            return;
        var smith = bot.Town switch
        {
            AreaId.RogueEncampment => MonsterId.Charsi,
            AreaId.LutGholein => MonsterId.Fara,
            AreaId.KurastDocks => MonsterId.Hratli,
            AreaId.PandemoniumFortress => MonsterId.Halbu,
            _ => MonsterId.Larzuk,
        };
        if (!Shop.OpenTrade(bot, smith))
        {
            bot.Failure($"Could not open {smith}'s shop to repair.");
            return;
        }

        Thread.Sleep(400);
        Bot.Log("Durability: " + string.Join(", ", Equipped().Select(i => $"{i.Class} {i.GetStat(StatId.Durability)}/{i.GetStat(StatId.MaxDurability)}")));
        ui.ClickLeftPanel(RepairButton);
        Thread.Sleep(300);
        bot.Proof("after the repair click");
        if (Wait(() => LowestDurability() > lowest, 2))
            Bot.Log($"Repaired at {smith} (lowest durability was {lowest:P0}).");
        else
            bot.Failure($"Could not repair at {smith}.");
        bot.CloseDialog();
    }

    // ---- Selling ----

    /// <summary>
    /// Sells the carried items that are not kept, worn, supplies or upgrades, at the town's vendor: picks each up and
    /// drops it on the shop. Only while total gold is below <see cref="ItemRules.SellingBelowGold"/>, unless the
    /// inventory is nearly full.
    /// </summary>
    public void Sell()
    {
        var player = bot.Player();
        var inventory = bot.Reader.ReadInventory();
        var total = inventory.Gold + inventory.StashGold.Sum();
        var equipped = Equipped();
        var toSell = Carried().Where(i => rules.IsToSell(i, player, equipped)).ToList();
        // Potions beyond the reserve (after the belt is filled from them): the weakest go.
        // Rejuvenation potions are never sold; of the others, the strongest (the highest class of each kind) are kept.
        var potions = Carried().Where(i => i.IsPotion && !i.IsRejuvenationPotion).OrderByDescending(i => i.Class).ToList();
        var rejuvenations = Carried().Count(i => i.IsRejuvenationPotion);
        toSell.AddRange(potions.Skip(Math.Max(0, PotionReserve - rejuvenations)));
        if (toSell.Count == 0 || (total >= ItemRules.SellingBelowGold && FreeCells() > 8))
            return;

        var vendor = bot.Town switch
        {
            AreaId.RogueEncampment => MonsterId.Charsi,
            AreaId.LutGholein => MonsterId.Fara,
            AreaId.KurastDocks => MonsterId.Ormus,
            AreaId.PandemoniumFortress => MonsterId.Jamella,
            _ => MonsterId.Larzuk,
        };
        if (!Shop.OpenTrade(bot, vendor))
        {
            bot.Failure($"Could not open {vendor}'s shop to sell.");
            return;
        }

        Thread.Sleep(400);
        foreach (var item in toSell)
        {
            var gold = bot.Reader.ReadInventory().Gold;
            // Where the item is now (selling moves nothing, but the list read before may be stale).
            var now = Carried().FirstOrDefault(i => i.Id == item.Id) ?? item;
            ui.ClickRightPanel(CentreOf(now));
            if (!Wait(() => OnCursor()?.Id == item.Id, 2))
            {
                bot.Failure($"Could not pick up {item.Quality} {item.Class} at {now.Position} to sell it (on the cursor: {OnCursor()?.Class.ToString() ?? "nothing"}).");
                bot.ClearCursor();
                continue;
            }

            ui.ClickLeftPanel(ShopDropPoint);
            if (Wait(() => OnCursor() is null && bot.Reader.ReadInventory().Gold > gold, 2))
            {
                Bot.Log($"Sold {item.Quality} {item.Class} for {bot.Reader.ReadInventory().Gold - gold} gold.");
            }
            else
            {
                bot.Failure($"Could not sell {item.Class}; putting it back.");
                ui.ClickRightPanel(CentreOf(item));
                Wait(() => OnCursor() is null, 2);
            }
        }
    }

    private static bool Wait(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(100);
        }

        return condition();
    }
}

/// <summary>The inventory panel: where its cells are, opening it, and moving potions with it.</summary>
internal static class InventoryPanel
{
    private const int GridX = 93, GridY = 897, Cell = 98;

    /// <summary>Where an inventory item's centre is, in the right panel's reference pixels.</summary>
    public static (int X, int Y) CentreOf(Item item) =>
        (GridX + Cell * item.Position.X + Cell * (item.Info?.InventoryWidth ?? 1) / 2,
         GridY + Cell * item.Position.Y + Cell * (item.Info?.InventoryHeight ?? 1) / 2);

    public static void Open(Bot bot)
    {
        if (bot.Reader.ReadMenus().Inventory)
            return;
        bot.Press(bot.Keys.Inventory);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!bot.Reader.ReadMenus().Inventory && DateTime.UtcNow < deadline)
            Thread.Sleep(100);
        Thread.Sleep(200);
    }

    public static void Close(Bot bot)
    {
        if (!bot.Reader.ReadMenus().Inventory)
            return;
        bot.Press(bot.Keys.Inventory);
        Thread.Sleep(200);
    }

    /// <summary>Where an item of the size spanning <paramref name="cell"/> (its top-left cell) is centred.</summary>
    public static (int X, int Y) CentreOfCells((int X, int Y) cell, int width, int height) =>
        (GridX + Cell * cell.X + Cell * width / 2, GridY + Cell * cell.Y + Cell * height / 2);

    /// <summary>The top-left cell of the first free place an item of the size fits in among the carried items, or null.</summary>
    public static (int X, int Y)? FreePlace(IReadOnlyList<Item> items, int width, int height)
    {
        var used = new bool[10, 4];
        foreach (var item in items.Where(i => i.Location == ItemLocation.Inventory))
        {
            for (var dx = 0; dx < (item.Info?.InventoryWidth ?? 1); dx++)
            {
                for (var dy = 0; dy < (item.Info?.InventoryHeight ?? 1); dy++)
                {
                    int x = item.Position.X + dx, y = item.Position.Y + dy;
                    if (x is >= 0 and < 10 && y is >= 0 and < 4)
                        used[x, y] = true;
                }
            }
        }

        for (var y = 0; y + height <= 4; y++)
        {
            for (var x = 0; x + width <= 10; x++)
            {
                var free = true;
                for (var dx = 0; dx < width && free; dx++)
                {
                    for (var dy = 0; dy < height && free; dy++)
                        free = !used[x + dx, y + dy];
                }

                if (free)
                    return (x, y);
            }
        }

        return null;
    }

    /// <summary>Drinks a potion of the kind from the inventory (a right click on it); false when there is none.</summary>
    public static bool Drink(Bot bot, GameUi ui, PotionType type)
    {
        var potion = bot.Reader.ReadInventory().Items.FirstOrDefault(i => i.Location == ItemLocation.Inventory && i.IsPotionOf(type));
        if (potion is null)
            return false;
        Open(bot);
        ui.ClickRightPanel(CentreOf(potion), right: true);
        Thread.Sleep(250);
        Close(bot);
        return true;
    }

    /// <summary>
    /// Moves potions from the inventory into the belt's free slots (a shift click on each): healing potions first, a
    /// third of the room for mana potions. Returns how many moved.
    /// </summary>
    public static int RefillBelt(Bot bot, GameUi ui)
    {
        var inventory = bot.Reader.ReadInventory();
        var room = inventory.Belt.Rows * 4 - inventory.Belt.Items.Count;
        var potions = inventory.Items.Where(i => i.Location == ItemLocation.Inventory && i.IsPotion).ToList();
        if (room <= 0 || potions.Count == 0)
            return 0;

        var mana = potions.Where(p => p.IsManaPotion).Take(Math.Max(1, room / 3));
        var order = potions.Where(p => p.IsHealingPotion || p.IsRejuvenationPotion).Concat(mana).Concat(potions.Where(p => p.IsManaPotion)).Distinct().Take(room).ToList();
        Open(bot);
        var moved = 0;
        foreach (var potion in order)
        {
            // A potion goes to a column of its kind with room, else to an empty column: mana may take two columns at
            // most, so the healing potions bought later have room (a belt all mana left nothing to heal with).
            var belt = bot.Reader.ReadInventory().Belt;
            if (potion.IsManaPotion)
            {
                var manaColumns = belt.Items.Where(i => i.IsManaPotion).Select(i => i.Position.X % 4).Distinct().ToList();
                var roomInMana = manaColumns.Any(c => belt.Items.Count(i => i.Position.X % 4 == c) < belt.Rows);
                if (!roomInMana && manaColumns.Count >= 2)
                    continue;
            }

            var before = belt.Items.Count;
            ui.ClickRightPanel(CentreOf(potion), shift: true);
            var deadline = DateTime.UtcNow.AddSeconds(1);
            while (bot.Reader.ReadInventory().Belt.Items.Count == before && DateTime.UtcNow < deadline)
                Thread.Sleep(80);
            if (bot.Reader.ReadInventory().Belt.Items.Count == before)
                break;
            moved++;
        }

        Close(bot);
        if (moved > 0)
            Bot.Log($"Moved {moved} potions from the inventory to the belt.");
        return moved;
    }
}

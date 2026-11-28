using D2Dotnet;
using D2Dotnet.Game;

/// <summary>Buying from the act's vendors.</summary>
internal static class Shop
{
    // How far above each NPC's feet their dialog's Trade is (pixels at a height of 720), found by clicking upwards
    // from 130 until the shop opens; measured for Akara: 166 (Cancel or Akara below 130, Talk at 142 to 154).
    private static readonly Dictionary<MonsterId, int> TradeHeights = LoadHeights();

    // The heights learned in earlier runs (%LOCALAPPDATA%\D2Dotnet	rade-heights.txt, "npc=height" lines).
    private static readonly string HeightsFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Dotnet", "trade-heights.txt");

    private static Dictionary<MonsterId, int> LoadHeights()
    {
        var heights = new Dictionary<MonsterId, int> { [MonsterId.Akara] = 166 };
        try
        {
            foreach (var line in File.Exists(HeightsFile) ? File.ReadAllLines(HeightsFile) : [])
            {
                var parts = line.Split('=');
                if (parts.Length == 2 && Enum.TryParse<MonsterId>(parts[0], out var npc) && int.TryParse(parts[1], out var height))
                    heights[npc] = height;
            }
        }
        catch (IOException)
        {
        }

        return heights;
    }

    private static void SaveHeights()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HeightsFile)!);
            File.WriteAllLines(HeightsFile, TradeHeights.Select(h => $"{h.Key}={h.Value}"));
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Opens the NPC's shop: their dialog, then Trade (found the first time for an NPC not measured yet).</summary>
    public static bool OpenTrade(Bot bot, MonsterId npc) => ChooseOption(bot, npc, () => IsOpen(bot), settle: 1.5);

    /// <summary>
    /// Picks an option of the NPC's dialog menu (the trade, Warriv's "Go East"): the menu floats above the NPC, so its
    /// options are clicked at heights up the NPC until <paramref name="done"/> holds; the height that worked is kept
    /// (per NPC: only one option is wanted of each) and tried first next time.
    /// </summary>
    public static bool ChooseOption(Bot bot, MonsterId npc, Func<bool> done, double settle, int option = 1)
    {
        if (ChooseOptionByKeys(bot, npc, done, settle, option))
            return true;

        // The learned height and round it first (the dialog grows with its options: Akara gains "Reset Stat/Skill
        // Points" after the Den of Evil), then the whole range again.
        var scan = Enumerable.Range(0, 20).Select(i => 130 + 12 * i);
        IEnumerable<int> heights = TradeHeights.TryGetValue(npc, out var known)
            ? new[] { 0, 8, -8, 16, -16, 24, -24 }.Select(d => known + d).Concat(scan).Distinct()
            : scan;
        foreach (var height in heights)
        {
            if (!bot.OpenDialog(npc))
                return false;
            Thread.Sleep(600);
            if (bot.Reader.ReadMonsters().FirstOrDefault(m => m.Class == npc) is not { } who)
                return false;
            bot.Aim(bot.Player().Position, who.Position, height);
            bot.Input.LeftClick();
            var deadline = DateTime.UtcNow.AddSeconds(settle);
            while (DateTime.UtcNow < deadline && !done())
                Thread.Sleep(150);
            if (done())
            {
                if (!TradeHeights.TryGetValue(npc, out var before) || before != height)
                {
                    TradeHeights[npc] = height;
                    SaveHeights();
                }

                return true;
            }

            bot.CloseDialog();
            Thread.Sleep(500);
        }

        return false;
    }

    /// <summary>
    /// Picks the menu's option at <paramref name="option"/> (0 is the first) with the keys, as on a controller: Home to
    /// the first, Down to the option, Enter. The menu (npcdialogpanelhd.json) is a list sized by its texts and placed
    /// over the NPC, so its rows have no fixed place on screen; the keys need none. The options the bot wants are the
    /// second of each menu: Trade (or Trade/Repair), Warriv's Go East, Meshif's Sail East.
    /// </summary>
    private static bool ChooseOptionByKeys(Bot bot, MonsterId npc, Func<bool> done, double settle, int option)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!bot.OpenDialog(npc))
                return false;
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline && !bot.Reader.ReadMenus().NpcInteract)
                Thread.Sleep(100);
            if (!bot.Reader.ReadMenus().NpcInteract)
            {
                bot.CloseDialog();
                continue;
            }

            Thread.Sleep(300);
            // The cursor away from the menu (a hovered row would take the selection) while the keys pick.
            var me = bot.Player();
            bot.Aim(me.Position, me.Position, 0);
            bot.Input.PressKey(VkHome);
            for (var i = 0; i < option; i++)
            {
                Thread.Sleep(120);
                bot.Input.PressKey(Native.VkDown);
            }

            Thread.Sleep(120);
            bot.Input.PressKey(Native.VkReturn);
            deadline = DateTime.UtcNow.AddSeconds(settle);
            while (DateTime.UtcNow < deadline && !done())
                Thread.Sleep(150);
            if (done())
                return true;
            bot.CloseDialog();
            Thread.Sleep(400);
        }

        Bot.Log($"Could not pick option {option} of {npc}'s menu with the keys; trying clicks.");
        return false;
    }

    private const int VkHome = 0x24;

    /// <summary>Opens Akara's shop and lists her goods with their grid cells and pages, leaving the shop open.</summary>
    public static void ListGoods(Bot bot)
    {
        if (!OpenTrade(bot, MonsterId.Akara))
            throw new BotStopped("Akara's shop did not open.");
        foreach (var item in bot.Reader.ReadInventory().Items.Where(i => i.Location == ItemLocation.Vendor))
            Bot.Log($"  {item.Class} at {item.Position} page {item.Page}, {item.Info?.InventoryWidth}x{item.Info?.InventoryHeight}");
    }

    // The shop's tabs and goods (vendorpanellayouthd.json, a left panel): four tabs from (82, 162), 249 by 75; the grid
    // from (91, 233), 98-pixel cells. A vendor item's page is its tab (Akara: weapons on 1 and 2, the rest on 3).
    private const int TabX = 82, TabY = 162, TabWidth = 249, TabHeight = 75, GridX = 91, GridY = 233, Cell = 98;

    /// <summary>
    /// Buys up to <paramref name="count"/> of an item from the open shop (a right click on it), while there is gold for it;
    /// returns how many were bought, checked through the gold.
    /// </summary>
    public static int Buy(Bot bot, GameUi ui, ItemId item, int count)
    {
        var bought = 0;
        for (var i = 0; i < count; i++)
        {
            if (bot.Reader.ReadInventory().Items.FirstOrDefault(v => v.Location == ItemLocation.Vendor && v.Class == item) is not { } goods)
                break;
            var gold = bot.Reader.ReadInventory().Gold;
            ui.ClickLeftPanel((TabX + TabWidth * goods.Page + TabWidth / 2, TabY + TabHeight / 2));
            Thread.Sleep(250);
            var width = goods.Info?.InventoryWidth ?? 1;
            var height = goods.Info?.InventoryHeight ?? 1;
            ui.ClickLeftPanel((GridX + Cell * goods.Position.X + Cell * width / 2, GridY + Cell * goods.Position.Y + Cell * height / 2), right: true);
            var deadline = DateTime.UtcNow.AddSeconds(1.5);
            while (DateTime.UtcNow < deadline && bot.Reader.ReadInventory().Gold == gold)
                Thread.Sleep(100);
            if (bot.Reader.ReadInventory().Gold == gold)
                break; // not enough gold, or the click missed
            bought++;
        }

        if (bought > 0)
            Bot.Log($"Bought {bought} {item}.");
        return bought;
    }

    /// <summary>
    /// In the Rogue Encampment, fills the belt at Akara: healing potions in the first columns, mana potions in the last,
    /// while the gold lasts. Bought potions that the belt has no room for go to the inventory.
    /// </summary>
    public static void BuyPotions(Bot bot, GameUi ui)
    {
        var inventory = bot.Reader.ReadInventory();
        var (room, spare) = PotionsWanted(bot);
        var tomes = !bot.HasItem(ItemId.TomeOfIdentify) || !bot.HasItem(ItemId.TomeOfTownPortal);
        if ((room + spare <= 0 && !tomes) || inventory.Gold < 50 || bot.Player().Area != AreaId.RogueEncampment)
            return;
        if (!OpenTrade(bot, MonsterId.Akara))
        {
            bot.Failure("Could not open Akara's shop.");
            return;
        }

        try
        {
            var goods = bot.Reader.ReadInventory().Items.Where(i => i.Location == ItemLocation.Vendor).ToList();
            var healing = goods.Where(g => g.IsPotionOf(PotionType.Healing)).Select(g => g.Class).DefaultIfEmpty(ItemId.MinorHealingPotion).Max();
            var mana = goods.Where(g => g.IsPotionOf(PotionType.Mana)).Select(g => g.Class).DefaultIfEmpty(ItemId.MinorManaPotion).Max();
            // Casters live on mana: half the belt for it; the others a third.
            var caster = bot.Player().Class is CharacterClass.Sorceress or CharacterClass.Necromancer or CharacterClass.Warlock;
            var manaCount = caster ? room / 2 : room / 3;
            // The belt first, then the tomes (a way back to town, identifying), then spares with what gold is left.
            Buy(bot, ui, healing, room - manaCount);
            Buy(bot, ui, mana, manaCount);
            foreach (var tome in new[] { ItemId.TomeOfTownPortal, ItemId.TomeOfIdentify })
            {
                if (!bot.HasItem(tome) && TownChores.FreeCellsOf(Carried(bot)) >= 4)
                    Buy(bot, ui, tome, 1);
            }

            // Scrolls for the tomes (bought scrolls go into the tome): town portals to get home, identifying.
            if (bot.HasItem(ItemId.TomeOfTownPortal) && bot.TownPortals() < 8)
                Buy(bot, ui, ItemId.ScrollOfTownPortal, 8 - bot.TownPortals());
            if (Carried(bot).FirstOrDefault(i => i.Class == ItemId.TomeOfIdentify) is { } idTome && (idTome.GetStat(StatId.Quantity) ?? 0) < 5)
                Buy(bot, ui, ItemId.ScrollOfIdentify, 5 - (idTome.GetStat(StatId.Quantity) ?? 0));

            Buy(bot, ui, healing, spare);
            if (caster)
                Buy(bot, ui, mana, Math.Max(0, SpareHealing - Carried(bot).Count(i => i.IsManaPotion)));
        }
        finally
        {
            bot.CloseDialog();
        }
    }

    /// <summary>
    /// The potions to buy: the belt's free slots, and the spare healing potions to carry in the inventory, for when a
    /// fight empties the belt (a champion pack can); no spares when the inventory is nearly full.
    /// </summary>
    public static (int Belt, int Spare) PotionsWanted(Bot bot)
    {
        var inventory = bot.Reader.ReadInventory();
        var room = inventory.Belt.Rows * 4 - inventory.Belt.Items.Count;
        var carried = inventory.Items.Where(i => i.Location == ItemLocation.Inventory).ToList();
        // With 1000 gold (gold and stash together; a potion costs tens), twice the spares: a fight in the Countess's
        // cellars empties the belt within minutes.
        var target = inventory.Gold + inventory.StashGold.Sum() >= 1000 ? 2 * SpareHealing : SpareHealing;
        var spare = TownChores.FreeCellsOf(carried) < 6 ? 0 : Math.Max(0, Math.Min(target, TownChores.FreeCellsOf(carried) - 4) - carried.Count(i => i.IsHealingPotion));
        return (Math.Max(0, room), spare);
    }

    // How many healing potions to carry in the inventory besides the belt.
    private const int SpareHealing = 6;

    private static List<Item> Carried(Bot bot) => bot.Reader.ReadInventory().Items.Where(i => i.Location == ItemLocation.Inventory).ToList();

    /// <summary>True when a vendor's goods are in memory: the shop is open.</summary>
    public static bool IsOpen(Bot bot) => bot.Reader.ReadInventory().Items.Any(i => i.Location == ItemLocation.Vendor);

    /// <summary>
    /// Finds where the NPC dialog's Trade is: opens Akara's dialog and clicks above her, higher each time, until the shop
    /// opens. The keyboard does not pick dialog options (Up, Down and Enter were tried). Prints what worked.
    /// </summary>
    public static void ProbeTrade(Bot bot)
    {
        try
        {
            if (bot.Player().Area != AreaId.RogueEncampment)
                bot.Retreat("Probing the shop.");
        }
        catch (Died died)
        {
            bot.Respawn(died);
            if (bot.Player().Area != AreaId.RogueEncampment)
                bot.Retreat("Probing the shop.");
        }
        // The dialog floats above the NPC: click straight above them, a little higher each time, until the shop's
        // items appear in memory (wrong options are harmless: Talk starts a speech, Cancel closes the dialog).
        for (var height = 70; height <= 330; height += 12)
        {
            if (!bot.OpenDialog(MonsterId.Akara))
                throw new BotStopped("Akara's dialog did not open.");
            Thread.Sleep(600);
            var akara = bot.Reader.ReadMonsters().First(m => m.Class == MonsterId.Akara);
            bot.Aim(bot.Player().Position, akara.Position, height);
            bot.Input.LeftClick();
            Thread.Sleep(900);
            var open = IsOpen(bot);
            Bot.Log($"{height} pixels above Akara's feet: shop {(open ? "open" : "closed")}, dialog {(bot.Reader.ReadMenus().NpcInteract ? "open" : "closed")}, shop flag {bot.Reader.ReadMenus().NpcShop}.");
            bot.CloseDialog();
            Thread.Sleep(600);
            if (open)
            {
                Bot.Log($"Trade is {height} pixels above Akara's feet (at a height of 720).");
                return;
            }
        }

        Bot.Log("No height above Akara opened the shop.");
    }
}

using D2Dotnet;
using D2Dotnet.Game;

/// <summary>Buying from the act's vendors.</summary>
internal static class Shop
{
    // How far above each NPC's feet their dialog's Trade is (pixels at a height of 720), found by clicking upwards
    // from 130 until the shop opens; measured for Akara: 166 (Cancel or Akara below 130, Talk at 142 to 154).
    private static readonly Dictionary<MonsterId, int> TradeHeights = new() { [MonsterId.Akara] = 166 };

    /// <summary>Opens the NPC's shop: their dialog, then Trade (found the first time for an NPC not measured yet).</summary>
    public static bool OpenTrade(Bot bot, MonsterId npc)
    {
        IEnumerable<int> heights = TradeHeights.TryGetValue(npc, out var known) ? [known, known + 8, known - 8] : Enumerable.Range(0, 20).Select(i => 130 + 12 * i);
        foreach (var height in heights)
        {
            if (!bot.OpenDialog(npc))
                return false;
            Thread.Sleep(600);
            if (bot.Reader.ReadMonsters().FirstOrDefault(m => m.Class == npc) is not { } who)
                return false;
            bot.Aim(bot.Player().Position, who.Position, height);
            bot.Input.LeftClick();
            var deadline = DateTime.UtcNow.AddSeconds(1.5);
            while (DateTime.UtcNow < deadline && !IsOpen(bot))
                Thread.Sleep(150);
            if (IsOpen(bot))
            {
                TradeHeights[npc] = height;
                return true;
            }

            bot.CloseDialog();
            Thread.Sleep(500);
        }

        return false;
    }

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
            var manaCount = room / 3;
            Buy(bot, ui, healing, room - manaCount + spare);
            Buy(bot, ui, mana, manaCount);

            // Tomes once: identifying in the field-free way (in town), and a way back to town.
            foreach (var tome in new[] { ItemId.TomeOfIdentify, ItemId.TomeOfTownPortal })
            {
                if (!bot.HasItem(tome) && TownChores.FreeCellsOf(Carried(bot)) >= 4)
                    Buy(bot, ui, tome, 1);
            }
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
        var spare = TownChores.FreeCellsOf(carried) < 8 ? 0 : Math.Max(0, SpareHealing - carried.Count(i => i.IsHealingPotion));
        return (Math.Max(0, room), spare);
    }

    // How many healing potions to carry in the inventory besides the belt.
    private const int SpareHealing = 4;

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

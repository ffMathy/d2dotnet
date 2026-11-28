// A bot that plays the game's quests, offline, from the character's current state (see PLAN.md; it knows Den of Evil
// and Sisters' Burial Grounds so far). Usage: dotnet run [-- [--plan | --check-input] [--foreground] [<D2R folder>]].
//   --plan         prints the quests the bot knows and their state, and sends no input.
//   --check-input  checks moving, clicking and casting from the Rogue Encampment or the Blood Moor.
//   --spend-points spends the unspent stat and skill points by the class's build, and stops.
//   --new-game     saves and exits, and starts a new game on Normal with the same character.
//   --town         goes to town and does the town chores: stash gold, identify, wear upgrades, sell, buy potions.
//   --screenshot   saves a picture of the game window (also taken when the bot is stuck, gives up, dies or stops).
//   --foreground   synthesizes input (SendInput), which needs the game in front. By default the clicks and keys are
//                  sent to the game window, which works with the game in the background; the game aims at the real
//                  cursor, so the bot borrows it for about 80 ms per click or key and puts it back.
// Press End to stop the bot.
using System.Runtime.Versioning;
using D2Dotnet;
using D2Dotnet.Files;
using D2Dotnet.Game;
using D2Dotnet.Maps;
using D2Dotnet.Memory;

[assembly: SupportedOSPlatform("windows")]

// Two bots at once would fight over the cursor and the keys: one at a time.
using var single = new Mutex(true, @"Local\D2Dotnet.SampleBot", out var first);
if (!first)
{
    Console.Error.WriteLine("Another SampleBot is running; stop it first.");
    return 1;
}

var planOnly = args.Contains("--plan");
var foreground = args.Contains("--foreground");
var checkInput = args.Contains("--check-input");
var spendPoints = args.Contains("--spend-points");
var probeTrade = args.Contains("--probe-trade");
var newGame = args.Contains("--new-game");
var screenshot = args.Contains("--screenshot");
var town = args.Contains("--town");
var walls = args.Contains("--walls");
var trail = args.Contains("--trail");
var questLog = args.Contains("--quest-log");
args = [.. args.Where(a => a is not ("--plan" or "--foreground" or "--check-input" or "--spend-points" or "--probe-trade" or "--new-game" or "--screenshot" or "--town" or "--walls" or "--trail" or "--quest-log"))];
var installPath = args.Length > 0 ? args[0] : @"C:\Program Files (x86)\Diablo II Resurrected";
using var game = GameInstall.Open(installPath);

if (Environment.GetEnvironmentVariable("D2_LAYOUT") is { Length: > 0 } layoutName)
{
    // Prints the game's UI layout files whose name holds the text (data/global/ui/layouts), to find where a button is.
    foreach (var file in game.Files.EnumerateFiles("data/global/ui/layouts").Where(f => f.Contains(layoutName, StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine($"==== {file}\n{System.Text.Encoding.UTF8.GetString(game.Files.ReadFile(file)!)}");
    return 0;
}

if (Environment.GetEnvironmentVariable("D2_COLUMNS") is { Length: > 0 } tableName)
{
    Console.WriteLine(string.Join(", ", game.Data.GetRequiredTable(tableName).Columns));
    return 0;
}

if (Environment.GetEnvironmentVariable("D2_TYPES") == "1")
{
    Console.WriteLine(string.Join(", ", game.Data.ItemTypes.Where(t => t.Class.Length > 0).Select(t => $"{t.Code}={t.Class}")));
    return 0;
}

using var process = GameProcess.Attach();
var reader = new GameReader(process, game.Data, OffsetCache.LoadOrScan(process));
foreach (var missing in reader.Offsets.Missing)
    Console.Error.WriteLine($"warning: the {missing} offset was not found in this game build; what depends on it is unavailable.");

// At the character selection (a run stopped between Save and Exit and Play): into a game first.
if (reader.ReadMapContext() is null && reader.IsInCharacterSelection() && !reader.IsOnline())
{
    var startWindow = GameWindow.Of(process.ProcessId);
    var starter = new Bot(reader, game.Data, game.Maps, startWindow, foreground ? new ForegroundInput() : new BackgroundInput(startWindow));
    GameSession.EnterFromCharacterSelection(starter);
}

if (reader.ReadMapContext() is not { } context)
{
    // A picture of what the game shows instead (a menu, an error), for seeing why.
    var outside = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Dotnet", "screenshots",
        $"{DateTime.Now:yyyyMMdd-HHmmss}-not-in-a-game.png");
    try
    {
        D2Dotnet.Screen.GameScreen.Capture(process.ProcessId).SavePng(outside);
        Console.WriteLine($"Screenshot: {outside}");
    }
    catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
    {
    }

    Console.WriteLine("Not in a game: start one with the character first.");
    return 1;
}

if (reader.IsOnline())
{
    Console.WriteLine("This bot is for offline games only.");
    return 1;
}

var window = GameWindow.Of(process.ProcessId);
IGameInput input = foreground ? new ForegroundInput() : new BackgroundInput(window);
var bot = new Bot(reader, game.Data, game.Maps, window, input);
var quests = new QuestRunner(bot);
bot.StartWatchdog();
if (trail)
    bot.StartTrail(seconds: 3);

if (walls)
{
    // The collision flags around the player, a character per subtile (hex of the low nibble, '.' for 0, ' ' uncovered;
    // '@' the player, 'm' monsters), the counts of each flag value in the level, and a screenshot to compare with.
    var player = reader.ReadMainPlayer()!;
    var level = bot.Act[player.Area];
    var map = level.Collision!;
    var local = level.ToLocal(player.Position);
    var monsters = reader.ReadMonsters().Where(m => m.IsEnemy).Select(m => level.ToLocal(m.Position)).ToHashSet();
    Console.WriteLine($"{player.Area} at {player.Position} (local {local}); flag values in the level:");
    foreach (var group in map.Cells.ToArray().GroupBy(c => c).OrderBy(g => g.Key))
        Console.WriteLine($"  0x{group.Key:X4}: {group.Count()}");
    for (var y = local.Y - 25; y <= local.Y + 25; y++)
    {
        var line = new System.Text.StringBuilder();
        for (var x = local.X - 40; x <= local.X + 40; x++)
        {
            var p = new Point(x, y);
            line.Append(p == local ? '@' : monsters.Contains(p) ? 'm'
                : x < 0 || y < 0 || x >= map.Width || y >= map.Height || map[x, y] == CollisionMap.Uncovered ? ' '
                : map[x, y] == 0 ? '.' : "0123456789ABCDEF"[map[x, y] & 0xF]);
        }

        Console.WriteLine(line);
    }

    bot.Screenshot("walls");
    return 0;
}

if (args.Contains("--ui-flags") || Environment.GetEnvironmentVariable("D2_UI_FLAGS") == "1")
{
    // The non-zero bytes of the panel flags (from 0x14 before the UI offset), to find a panel's flag: open it, run this,
    // close it, run it again.
    var flags = process.ReadBytes(unchecked(process.ModuleBaseAddress + reader.Offsets.UI - 0x14), 0x40);
    Console.WriteLine(string.Join(" ", flags.Select((b, i) => (b, i)).Where(x => x.b != 0).Select(x => $"0x{x.i:X2}={x.b}")));
    return 0;
}

if (Environment.GetEnvironmentVariable("D2_PATH") is { Length: > 0 } pathTo)
{
    // Times a path search from the player to "x,y" (world subtiles) on the player's level.
    var parts = pathTo.Split(',');
    var to = new Point(int.Parse(parts[0]), int.Parse(parts[1]));
    var me = reader.ReadMainPlayer()!;
    var grid = bot.GridHere();
    var level = bot.Act[me.Area];
    Console.WriteLine($"{me.Area} {level.Bounds} from {me.Position} (walkable {grid.IsWalkable(me.Position)}) to {to} (walkable {grid.IsWalkable(to)}, level {grid.LevelAt(to)?.Id})");
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var path = grid.FindPath(me.Position, to);
    Console.WriteLine($"FindPath: {(path is null ? "none" : path.Count + " steps")} in {watch.ElapsedMilliseconds} ms");
    return 0;
}

if (Environment.GetEnvironmentVariable("D2_PLAYERS") == "1")
{
    if (bot.PresetPosition(ObjectId.Bank) is { } bank && Bot.Distance(bot.Player().Position, bank) > 8)
        bot.WalkTo(bot.GridHere(), bank, arriveWithin: 5, fight: false);
    foreach (var p in reader.ReadPlayers())
        Console.WriteLine($"  {p.Name} id {p.Id.Value} main {p.IsMainPlayer} corpse {p.IsCorpse} at {p.Position} in {p.Area}, bytes 0x1A0..0x1AF: " +
            string.Join(" ", process.ReadBytes(p.Address + 0x1A0, 16).Select(b => b.ToString("X2"))));
    bot.Screenshot("players");
    return 0;
}

if (Environment.GetEnvironmentVariable("D2_INV") == "1")
{
    var inv = reader.ReadInventory();
    var carried = inv.Items.Where(i => i.Location == ItemLocation.Inventory).ToList();
    Console.WriteLine($"Gold {inv.Gold}, stash {inv.StashGold.Sum()}, free cells {TownChores.FreeCellsOf(carried)}, belt {inv.Belt.Items.Count}/{inv.Belt.Rows * 4}.");
    foreach (var item in carried)
        Console.WriteLine($"  {item.Quality} {item.Class} at {item.Position} {item.Info?.InventoryWidth}x{item.Info?.InventoryHeight}");
    Console.WriteLine($"Main player id {reader.ReadMainPlayer()!.Id.Value}. Item units:");
    foreach (var u in reader.ReadItemUnits(int.Parse(Environment.GetEnvironmentVariable("D2_TABLES") ?? "1")).Where(u => u.Where != 3))
        Console.WriteLine($"  {u.Class} {u.Id.Value} where {u.Where} owner {u.Owner} page {u.Page} flags 0x{u.Flags:X8} at {u.Position}");
    foreach (var group in inv.Items.GroupBy(i => i.Location))
        Console.WriteLine($"  {group.Key}: {group.Count()}");
    InventoryPanel.Open(bot);
    bot.Screenshot("inventory");
    InventoryPanel.Close(bot);
    return 0;
}

if (Environment.GetEnvironmentVariable("D2_BIND") == "1")
{
    SkillBinder.Bind(bot, ClassCombat.AttackSkillsOf(reader.ReadMainPlayer()!.Class));
}

if (Environment.GetEnvironmentVariable("D2_KEYS") == "1" || Environment.GetEnvironmentVariable("D2_BIND") == "1")
{
    // The skills bound to keys, and the skills the character has.
    foreach (var binding in bot.Keys.Skills.Where(b => b.Skill != SkillId.None))
        Console.WriteLine($"  {binding.Skill} on {binding.Keys.Primary}/{binding.Keys.Secondary}");
    Console.WriteLine("Skills: " + string.Join(", ", reader.ReadMainPlayer()!.Skills.Select(s => $"{s.Key} {s.Value.Level}")));
    Console.WriteLine("Waypoints: " + string.Join(", ", reader.ReadMainPlayer()!.AvailableWaypoints));
    return 0;
}

if (questLog)
{
    bot.ProofQuestLog("quest log");
    return 0;
}

if (screenshot)
{
    return bot.Screenshot("requested") is null ? 1 : 0;
}

if (planOnly)
{
    var player = reader.ReadMainPlayer()!;
    Console.WriteLine($"{player.Name} ({player.Class}, level {player.Level}) in {player.Area}; seed 0x{context.Seed:X8}, {context.Difficulty}.");
    foreach (var quest in QuestRunner.Quests)
        Console.WriteLine($"  {quest.Id}: {(quest.IsDone(bot) ? "done" : "to do")} ({reader.ReadQuests().GetValueOrDefault(quest.Id)})");
    Console.WriteLine("  Quest log (every quest with a status): " + string.Join(", ", reader.ReadQuests().Where(q => q.Value != 0).Select(q => $"{q.Key}={q.Value}")));
    Console.WriteLine(quests.Next() is { } next ? $"Next: {next.Id}." : "Every quest the bot knows is done.");
    Console.WriteLine($"Levelling areas for level {player.Level}: {string.Join(", ", quests.GuideAreas(player.Level).Select(a => $"{a} ({game.Data.Levels.Find(a)?.MonsterLevelIn(context.Difficulty)})"))}.");
    return 0;
}

if (foreground)
{
    Console.WriteLine($"Seed 0x{context.Seed:X8}, {context.Difficulty}. Starting in 3 seconds; the game window comes to the front.");
    window.BringToFront();
}
else
{
    Console.WriteLine($"Seed 0x{context.Seed:X8}, {context.Difficulty}. Starting in 3 seconds; the game can stay in the background (the mouse pointer is borrowed for a moment per click).");
}

Thread.Sleep(3000);

try
{
    if (checkInput)
    {
        InputCheck.Run(bot);
    }
    else if (town)
    {
        var ui = new GameUi(bot);
        if (bot.Player().Area != bot.Town)
            bot.TravelTo(bot.Town);
        bot.HealInTown();
        new TownChores(bot, ui, bot.Items).Run();
    }
    else if (newGame)
    {
        GameSession.NewGame(bot);
    }
    else if (probeTrade)
    {
        Shop.ListGoods(bot);
    }
    else if (spendPoints)
    {
        CharacterBuild.SpendPoints(bot, new GameUi(bot));
    }
    else
    {
        quests.Run();
    }

    return 0;
}
catch (BotStopped stopped)
{
    bot.Screenshot("stopped");
    Console.WriteLine($"Stopped: {stopped.Message}");
    return 2;
}
catch (LowLife low)
{
    Console.WriteLine($"Stopped: {low.Message}");
    return 2;
}

// A bot that plays the game's quests, offline, from the character's current state (see PLAN.md; it knows Den of Evil
// and Sisters' Burial Grounds so far). Usage: dotnet run [-- [--plan | --check-input] [--foreground] [<D2R folder>]].
//   --plan         prints the quests the bot knows and their state, and sends no input.
//   --check-input  checks moving, clicking and casting from the Rogue Encampment or the Blood Moor.
//   --spend-points spends the unspent stat and skill points by the class's build, and stops.
//   --new-game     saves and exits, and starts a new game on Normal with the same character.
//   --foreground   synthesizes input (SendInput), which needs the game in front. By default the clicks and keys are
//                  sent to the game window, which works with the game in the background; the game aims at the real
//                  cursor, so the bot borrows it for about 80 ms per click or key and puts it back.
// Press End to stop the bot.
using System.Runtime.Versioning;
using D2Dotnet.Files;
using D2Dotnet.Memory;

[assembly: SupportedOSPlatform("windows")]

var planOnly = args.Contains("--plan");
var foreground = args.Contains("--foreground");
var checkInput = args.Contains("--check-input");
var spendPoints = args.Contains("--spend-points");
var probeTrade = args.Contains("--probe-trade");
var newGame = args.Contains("--new-game");
args = [.. args.Where(a => a is not ("--plan" or "--foreground" or "--check-input" or "--spend-points" or "--probe-trade" or "--new-game"))];
var installPath = args.Length > 0 ? args[0] : @"C:\Program Files (x86)\Diablo II Resurrected";
using var game = GameInstall.Open(installPath);

using var process = GameProcess.Attach();
var reader = new GameReader(process, game.Data, OffsetCache.LoadOrScan(process));
foreach (var missing in reader.Offsets.Missing)
    Console.Error.WriteLine($"warning: the {missing} offset was not found in this game build; what depends on it is unavailable.");

if (reader.ReadMapContext() is not { } context)
{
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

if (planOnly)
{
    var player = reader.ReadMainPlayer()!;
    Console.WriteLine($"{player.Name} ({player.Class}, level {player.Level}) in {player.Area}; seed 0x{context.Seed:X8}, {context.Difficulty}.");
    foreach (var quest in QuestRunner.Quests)
        Console.WriteLine($"  {quest.Id}: {(quest.IsDone(bot) ? "done" : "to do")} ({reader.ReadQuests().GetValueOrDefault(quest.Id)})");
    Console.WriteLine(quests.Next() is { } next ? $"Next: {next.Id}." : "Every quest the bot knows is done.");
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
    Console.WriteLine($"Stopped: {stopped.Message}");
    return 2;
}
catch (LowLife low)
{
    Console.WriteLine($"Stopped: {low.Message}");
    return 2;
}

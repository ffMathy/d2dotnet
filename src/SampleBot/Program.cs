// A bot that clears the Den of Evil, offline, for a character standing in the Rogue Encampment (or already in the Blood
// Moor or the Den). Usage: dotnet run [-- [--plan | --check-input] [--foreground] [<D2R install folder>]].
//   --plan         prints the route and sends no input.
//   --check-input  checks moving, clicking and casting from the Rogue Encampment instead of clearing the Den.
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
args = [.. args.Where(a => a is not ("--plan" or "--foreground" or "--check-input"))];
var installPath = args.Length > 0 ? args[0] : @"C:\Program Files (x86)\Diablo II Resurrected";
using var game = GameInstall.Open(installPath);

using var process = GameProcess.Attach();
var reader = new GameReader(process, game.Data, OffsetCache.LoadOrScan(process));
foreach (var missing in reader.Offsets.Missing)
    Console.Error.WriteLine($"warning: the {missing} offset was not found in this game build; what depends on it is unavailable.");

if (reader.ReadMapContext() is not { } context)
{
    Console.WriteLine("Not in a game: start one with the character in the Rogue Encampment.");
    return 1;
}

if (reader.IsOnline())
{
    Console.WriteLine("This bot is for offline games only.");
    return 1;
}

var act = game.Maps.Generate(context);
var window = GameWindow.Of(process.ProcessId);
var keys = reader.ReadKeyBindings();
if (planOnly)
{
    new DenBot(reader, game.Data, act, window, keys, new BackgroundInput(window)).Plan();
    return 0;
}

IGameInput input = foreground ? new ForegroundInput() : new BackgroundInput(window);
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
    var bot = new DenBot(reader, game.Data, act, window, keys, input);
    if (checkInput)
        bot.CheckInput();
    else
        bot.Run();
    return 0;
}
catch (BotStopped stopped)
{
    Console.WriteLine($"Stopped: {stopped.Message}");
    return 2;
}

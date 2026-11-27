// A bot that clears the Den of Evil, offline, for a character standing in the Rogue Encampment (or already in the Blood
// Moor or the Den). Usage: dotnet run [-- [--plan] [<D2R install folder>]]; --plan prints the route and sends no
// input. Press End to stop the bot.
using System.Runtime.Versioning;
using D2Dotnet.Files;
using D2Dotnet.Memory;

[assembly: SupportedOSPlatform("windows")]

var planOnly = args.Contains("--plan");
args = [.. args.Where(a => a != "--plan")];
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
    new DenBot(reader, game.Data, act, window, keys).Plan();
    return 0;
}

Console.WriteLine($"Seed 0x{context.Seed:X8}, {context.Difficulty}. Starting in 3 seconds; the game window comes to the front.");
window.BringToFront();
Thread.Sleep(3000);

try
{
    new DenBot(reader, game.Data, act, window, keys).Run();
    return 0;
}
catch (BotStopped stopped)
{
    Console.WriteLine($"Stopped: {stopped.Message}");
    return 2;
}

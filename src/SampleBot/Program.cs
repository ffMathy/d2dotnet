// Usage: dotnet run [-- <D2R install folder>]
using D2Dotnet.Files;
using D2Dotnet.Game;
using D2Dotnet.Maps;
using D2Dotnet.Memory;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("Reading a running game needs Windows.");

var installPath = args.Length > 0 ? args[0] : @"C:\Program Files (x86)\Diablo II Resurrected";
using var game = GameInstall.Open(installPath);

using var process = GameProcess.Attach();
var reader = new GameReader(process, game.Data, OffsetCache.LoadOrScan(process));
foreach (var missing in reader.Offsets.Missing)
    Console.Error.WriteLine($"warning: the {missing} offset was not found in this game build; what depends on it is unavailable.");
GameSnapshot snapshot = reader.ReadSnapshot();

if (snapshot.Player is not { } player)
{
    Console.WriteLine("Attached, but no player is in a game.");
    return;
}
Console.WriteLine($"{player.Name} ({player.Class}, level {player.Level}) in {player.Area}, life {player.LifePercent}%");
Console.WriteLine($"Enemies nearby: {snapshot.Enemies.Count()}");

if (snapshot.Map is not { } context)
{
    Console.WriteLine("No map context (seed/difficulty) could be read.");
    return;
}
GeneratedAct act = game.Maps.Generate(context);
MapLevel here = act[context.Area];
Console.WriteLine($"Seed 0x{context.Seed:X8}, {context.Difficulty}; {here.Exits.Count} exits from {context.Area}:");
foreach (LevelExit exit in here.Exits)
    Console.WriteLine($"  {exit.Destination} at {exit.Position}");

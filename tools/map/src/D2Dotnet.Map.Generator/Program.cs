// d2-map: C# port of the native C map generator from blacha/diablo2 packages/map/map
// (MIT, (c) 2021 Blayne Chard). See README.md.
using System.Reflection;
using D2Dotnet.Map.Generator.Cli;
using D2Dotnet.Map.Generator.Interop;
using D2Dotnet.Map.Generator.Output;

namespace D2Dotnet.Map.Generator;

internal static class Program
{
    private static int Main(string[] args)
    {
        var metadata = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
        var platform = new WindowsGamePlatform();
        var stderr = Console.OpenStandardError();
        Callbacks.Stderr = stderr;

        var ctx = new GeneratorContext(
            Console.OpenStandardOutput(),
            stderr,
            new SystemClock(),
            platform,
            new NativeGameMemory(),
            platform,
            new FileProbe(),
            metadata.GetValueOrDefault("GitVersion") ?? "unknown",
            metadata.GetValueOrDefault("GitHash") ?? "unknown");

        try
        {
            return new MapGeneratorProgram(ctx).Main(args, Console.OpenStandardInput());
        }
        catch (MissingOptionValueException)
        {
            // The C program dereferences argv[argc] (NULL) here and dies with an access violation.
            return MissingOptionValueException.AccessViolationExitCode;
        }
    }
}

using D2Dotnet.Map.Generator.Native;
using D2Dotnet.Map.Generator.Output;

namespace D2Dotnet.Map.Generator.Game;

/// <summary>Everything the ported program needs from its environment (the C globals and libc/Win32).</summary>
public sealed class GeneratorContext
{
    public GeneratorContext(
        Stream stdout,
        Stream stderr,
        IClock clock,
        IGamePlatform platform,
        IGameMemory memory,
        IModuleLoader modules,
        IFileProbe files,
        string gitVersion,
        string gitHash)
    {
        Stdout = new CStream(stdout);
        Stderr = new CStream(stderr, unbuffered: true);
        Json = new JsonWriter(Stdout);
        Log = new Logger(Stdout, clock);
        Platform = platform;
        Memory = memory;
        Modules = modules;
        Files = files;
        GitVersion = gitVersion;
        GitHash = gitHash;
    }

    /// <summary>C stdout (fully buffered, flushed by json_end / log_process / exit).</summary>
    public CStream Stdout { get; }

    /// <summary>C stderr (unbuffered).</summary>
    public CStream Stderr { get; }

    public JsonWriter Json { get; }

    public Logger Log { get; }

    public IGamePlatform Platform { get; }

    public IGameMemory Memory { get; }

    public IModuleLoader Modules { get; }

    public IFileProbe Files { get; }

    /// <summary><c>GIT_VERSION</c> compile-time define.</summary>
    public string GitVersion { get; }

    /// <summary><c>GIT_HASH</c> compile-time define.</summary>
    public string GitHash { get; }

    /// <summary>C <c>exit(code)</c>: flush stdio, then terminate.</summary>
    public void Exit(int code)
    {
        Stdout.Flush();
        Stderr.Flush();
        Platform.Exit(code);
    }
}

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
        string gitHash,
        bool textMode = false)
    {
        TextMode = textMode;
        Stdout = new CStream(stdout, textMode: textMode);
        Stderr = new CStream(stderr, unbuffered: true, textMode: textMode);
        Json = new JsonWriter(Stdout);
        Log = new Logger(Stdout, clock);
        Platform = platform;
        Memory = memory;
        Modules = modules;
        Files = files;
        GitVersion = gitVersion;
        GitHash = gitHash;
    }

    /// <summary>
    /// msvcrt text mode for stdin/stdout/stderr ("\n" written as "\r\n", "\r\n" read as "\n",
    /// Ctrl-Z ends stdin), as in the upstream Windows build. On in the exe, off in the golden tests.
    /// </summary>
    public bool TextMode { get; }

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

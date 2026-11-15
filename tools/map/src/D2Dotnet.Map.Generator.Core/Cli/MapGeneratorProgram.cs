using D2Dotnet.Map.Generator.Game;
using D2Dotnet.Map.Generator.Output;

namespace D2Dotnet.Map.Generator.Cli;

/// <summary>
/// Thrown where the C program would dereference <c>argv[argc]</c> (a NULL pointer) because an
/// option such as <c>--seed</c> is the last argument. The executable turns it into an
/// access-violation exit code without further output, which is what the C program does.
/// </summary>
public sealed class MissingOptionValueException(string option) : Exception($"option {option} has no value (argv[argc] is NULL)")
{
    /// <summary>Exit code of a Windows access violation (STATUS_ACCESS_VIOLATION).</summary>
    public const int AccessViolationExitCode = unchecked((int)0xC0000005);

    public string Option { get; } = option;
}

/// <summary>Port of upstream map/main.c.</summary>
public sealed class MapGeneratorProgram
{
    public const int INPUT_BUFFER = 1024;

    public const string COMMAND_EXIT = "$exit";
    public const string COMMAND_MAP = "$map";
    public const string COMMAND_DIFF = "$difficulty";
    public const string COMMAND_ACT = "$act";
    public const string COMMAND_SEED = "$seed";

    public const string CharGray = "\x1b[90m";

    /// <summary><c>CliUsage</c>, printed verbatim (it contains no <c>%</c>, so printf leaves it unchanged).</summary>
    public const string CliUsage = "\nUsage:\n" +
        "    d2-map.exe [D2 Game Path] [options]\n" +
        "\nOptions:\n" +
        "    --seed [-s]          Map Seed\n" +
        "    --difficulty [-d]    Game Difficulty [0: Normal, 1: Nightmare, 2:Hell]\n" +
        "    --act [-a]           Dump a specific act [0: ActI, 1:ActII, 2: ActIII, 3: ActIV, 4: Act5]\n" +
        "    --map [-m]           Dump a specific Map [0: Rogue Encampent ...]\n" +
        "    --verbose [-v]       Increase logging level\n" +

        "\nExamples:\n" +
        "\n    \x1b[90m# Dump ActI from Normal mode for seed 1122334 \x1b[0m\n" +
        "    d2-map.exe /home/diablo2 --seed 1122334 --difficulty 0 --act 0\n" +
        "\n    \x1b[90m# Dump all acts from Hell mode for seed 1122334 \x1b[0m\n" +
        "    d2-map.exe /home/diablo2 --seed 1122334 --difficulty 2\n";

    private const string F = "map/main.c";

    private readonly GeneratorContext _ctx;
    private readonly D2Client _client;

    public MapGeneratorProgram(GeneratorContext ctx)
    {
        _ctx = ctx;
        _client = new D2Client(ctx);
    }

    /// <summary>The ported d2_client.c instance (exposed for tests).</summary>
    public D2Client Client => _client;

    private Logger Log => _ctx.Log;
    private JsonWriter Json => _ctx.Json;

    private static LogSource At(int line) => new(F, line);

    private void Printf(string s) => _ctx.Stdout.Write(s);

    /// <summary><c>dump_info</c>.</summary>
    public void DumpInfo(uint seed, int difficulty, int actId, int mapId)
    {
        Json.Start();
        Json.KeyValue("type", "info");
        Json.KeyValue("seed", seed);
        Json.KeyValue("difficulty", difficulty);
        if (actId > -1) Json.KeyValue("act", actId);
        if (mapId > -1) Json.KeyValue("map", mapId);
        Json.End();
    }

    /// <summary><c>dump_maps</c>.</summary>
    public void DumpMaps(uint seed, int difficulty, int actId, int mapId)
    {
        var totalTime = Log.CurrentTimeMillis();
        var mapCount = 0;
        if (mapId > -1)
        {
            var startTime = Log.CurrentTimeMillis();
            var res = _client.DumpMap(unchecked((int)seed), difficulty, mapId);
            if (res == 0) mapCount++;
            var duration = Log.CurrentTimeMillis() - startTime;
            Log.Debug(At(63), "Map:Generation", LogField.UI("seed", seed), LogField.I("difficulty", difficulty), LogField.I("mapId", mapId), LogField.I("duration", unchecked((int)duration)));
        }
        else
        {
            for (var id = 0; id < 200; id++)
            {
                // Skip map if its not part of the current act
                if (actId > -1 && D2Client.GetAct(id) != actId) continue;

                var startTime = Log.CurrentTimeMillis();
                var res = _client.DumpMap(unchecked((int)seed), difficulty, id);
                if (res == 0) mapCount++;
                if (res == 1) continue; // Failed to generate the map

                var currentTime = Log.CurrentTimeMillis();
                var duration = currentTime - startTime;
                Log.Debug(At(77), "Map:Generation", LogField.UI("seed", seed), LogField.I("difficulty", difficulty), LogField.I("actId", D2Client.GetAct(id)), LogField.I("mapId", id), LogField.I("duration", unchecked((int)duration)));
            }
        }
        var total = Log.CurrentTimeMillis() - totalTime;
        Log.Info(At(81), "Map:Generation:Done", LogField.UI("seed", seed), LogField.I("difficulty", difficulty), LogField.I("count", mapCount), LogField.I("duration", unchecked((int)total)));
        Printf("\n");
    }

    /// <summary>
    /// <c>main</c>. <paramref name="args"/> are the arguments after the program name
    /// (<c>argv[1..]</c>). Returns the exit code; stdout is flushed before returning, like
    /// returning from C main. Paths that call <c>ExitProcess</c> never return.
    /// </summary>
    public int Main(string[] args, Stream stdin)
    {
        var code = MainCore(args, stdin);
        _ctx.Stdout.Flush();
        return code;
    }

    private int MainCore(string[] args, Stream stdin)
    {
        // if (argc < 1) is never true: argv[0] (the program) always exists.
        Log.Info(At(91), "Cli:Start", LogField.S("version", _ctx.GitVersion), LogField.S("hash", _ctx.GitHash));

        string? gameFolder = null;
        uint argSeed = 0xff00ff00;
        var argMapId = -1;
        var argDifficulty = 0;
        var argActId = -1;
        var foundArgs = 0;

        string NextArg(ref int i, string option)
        {
            ++i;
            if (i >= args.Length) throw new MissingOptionValueException(option);
            return args[i];
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (CParse.StartsWith(arg, "--seed") || CParse.StartsWith(arg, "-s"))
            {
                CParse.SscanfUnsigned(NextArg(ref i, arg), ref argSeed);
                Log.Debug(At(105), "Cli:Arg", LogField.UI("seed", argSeed));
                foundArgs++;
            }
            else if (CParse.StartsWith(arg, "--difficulty") || CParse.StartsWith(arg, "-d"))
            {
                argDifficulty = CParse.Atoi(NextArg(ref i, arg));
                Log.Debug(At(109), "Cli:Arg", LogField.I("difficulty", argDifficulty));
                foundArgs++;
            }
            else if (CParse.StartsWith(arg, "--map") || CParse.StartsWith(arg, "-m"))
            {
                argMapId = CParse.Atoi(NextArg(ref i, arg));
                Log.Debug(At(113), "Cli:Arg", LogField.I("mapId", argMapId));
                foundArgs++;
            }
            else if (CParse.StartsWith(arg, "--act") || CParse.StartsWith(arg, "-a"))
            {
                argActId = CParse.Atoi(NextArg(ref i, arg));
                Log.Debug(At(117), "Cli:Arg", LogField.I("actId", argActId));
                foundArgs++;
            }
            else if (CParse.StartsWith(arg, "--verbose") || CParse.StartsWith(arg, "-v"))
            {
                Log.Debug(At(120), "Cli:Arg", LogField.B("verbose", true));
                Log.SetLevel(LogLevel.Trace);
            }
            else
            {
                gameFolder = arg;
                Log.Debug(At(124), "Cli:Arg", LogField.S("game", gameFolder));
            }
        }

        if (argActId > 0 && argMapId > 0)
        {
            Printf("--act and --map cannot be used together\n");
            Printf(CliUsage);
            return 1;
        }
        if (gameFolder == null)
        {
            Printf(CliUsage);
            return 1;
        }

        var initStartTime = Log.CurrentTimeMillis();
        _client.GameInit(gameFolder);
        var duration = Log.CurrentTimeMillis() - initStartTime;
        Log.Info(At(143), "Map:Init:Done", LogField.S("version", _ctx.GitVersion), LogField.S("hash", _ctx.GitHash), LogField.I("duration", unchecked((int)duration)));

        /* Seed/Diff has been passed in just generate the map that is required */
        if (foundArgs > 0)
        {
            if (argMapId > -1) DumpMaps(argSeed, argDifficulty, -1, argMapId);
            else if (argActId > -1) DumpMaps(argSeed, argDifficulty, argActId, -1);
            else DumpMaps(argSeed, argDifficulty, -1, -1);
            return 0;
        }

        /* Init the D2 client using the provided path */
        Json.Start();
        Json.KeyValue("type", "init");
        Json.End();

        var reader = new CLineReader(stdin, INPUT_BUFFER);
        /* Read in seed/Difficulty then generate all the maps */
        while (reader.Fgets() is { } buffer)
        {
            if (CParse.StartsWith(buffer, COMMAND_EXIT)) return 0;

            if (CParse.StartsWith(buffer, COMMAND_MAP))
            {
                DumpMaps(argSeed, argDifficulty, argActId, argMapId);
                argActId = -1;
                argMapId = -1;
                Json.Start();
                Json.KeyValue("type", "done");
                Json.End();
            }
            else if (CParse.StartsWith(buffer, COMMAND_SEED))
            {
                CParse.SscanfTokenUnsigned(buffer, ref argSeed);
                DumpInfo(argSeed, argDifficulty, argActId, argMapId);
            }
            else if (CParse.StartsWith(buffer, COMMAND_DIFF))
            {
                CParse.SscanfTokenSigned(buffer, ref argDifficulty);
                DumpInfo(argSeed, argDifficulty, argActId, argMapId);
            }
            else if (CParse.StartsWith(buffer, COMMAND_ACT))
            {
                CParse.SscanfTokenSigned(buffer, ref argActId);
                DumpInfo(argSeed, argDifficulty, argActId, argMapId);
            }
            Printf("\n");
        }

        return 0;
    }
}

using System.Text;
using D2Dotnet.Map.Generator.Cli;
using D2Dotnet.Map.Generator.Game;
using D2Dotnet.Map.Generator.Native;
using D2Dotnet.Map.Generator.Output;
using D2Dotnet.Map.Generator.Tests.Fakes;

namespace D2Dotnet.Map.Generator.Tests;

/// <summary>A small hand-built act I world in fake memory (see the comments for the expected output).</summary>
internal sealed class TestWorld
{
    public const string Folder = "C:\\games\\d2";

    public uint Act;
    public uint Misc;
    public uint Level1;
    public uint Level2;
    public uint RoomA;
    public uint RoomB;

    public TestWorld(FakeGame g)
    {
        var m = g.Memory;
        g.AddLevelTxt(1, "Rogue Encampment");
        g.AddLevelTxt(2, "Blood Moor");
        g.AddObjectTxt(2, "Shrine", selectable: 1, operateFn: 2);
        g.AddObjectTxt(3, "casket6", selectable: 0, operateFn: 1);

        Misc = m.New(new ActMisc());
        Act = m.New(new Act { pMisc = Misc });
        Level2 = m.New(new Level { dwLevelNo = 2, dwPosX = 12, dwPosY = 20, dwSizeX = 5, dwSizeY = 5, pMisc = Misc });
        Level1 = m.New(new Level { dwLevelNo = 1, dwPosX = 10, dwPosY = 20, dwSizeX = 2, dwSizeY = 1, pMisc = Misc, pNextLevel = Level2 });
        m.Update<ActMisc>(Misc, x => { x.pLevelFirst = Level1; x.pAct = Act; return x; });
        g.Acts[0] = Act;

        // Collision: room A covers x 0..4, room B x 5..9 (origin = (50, 100)).
        ushort[][] collA =
        [
            [0, 0, 1024, 1024, 0],
            [1, 1, 1, 1, 1],
            [1, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [1, 1, 1, 1, 1],
        ];
        ushort[][] collB =
        [
            [0, 0, 0, 0, 0],
            [1024, 1024, 1024, 1024, 1024],
            [0, 0, 0, 0, 3],
            [0, 0, 0, 0, 0],
            [5, 5, 5, 5, 5],
        ];
        var room1A = g.AddRoom1(g.AddCollMap(50, 100, collA));
        var room1B = g.AddRoom1(g.AddCollMap(55, 100, collB));

        var exitRoom = m.New(new Room2 { pLevel = Level2, dwPosX = 60, dwPosY = 20 });
        var num9 = m.New(9u);
        var num5 = m.New(5u);
        var tile2 = m.New(new RoomTile { nNum = num5, pRoom2 = exitRoom });
        var tile1 = m.New(new RoomTile { nNum = num9, pRoom2 = exitRoom, pNext = tile2 });

        var presetsB = g.AddPresets(
            (D2Client.UNIT_TYPE_OBJECT, 7, 0, 0),  // Barrel: useless
            (D2Client.UNIT_TYPE_OBJECT, 3, 1, 2)); // not selectable: no op, no class
        RoomB = m.New(new Room2 { dwPosX = 11, dwPosY = 20, pRoom1 = room1B, pLevel = Level1, pPreset = presetsB });

        var presetsA = g.AddPresets(
            (D2Client.UNIT_TYPE_NPC, 19, 3, 4),      // Fallen
            (D2Client.UNIT_TYPE_NPC, 149, 0, 0),     // Chicken: useless
            (D2Client.UNIT_TYPE_OBJECT, 2, 1, 1),    // Shrine
            (D2Client.UNIT_TYPE_OBJECT, 580, 2, 2),  // no txt lookup, chest-super
            (D2Client.UNIT_TYPE_TILE, 5, 4, 0),      // exit to level 2
            (D2Client.UNIT_TYPE_ITEM, 1, 0, 0));     // ignored
        RoomA = m.New(new Room2 { dwPosX = 10, dwPosY = 20, pRoom2Next = RoomB, pLevel = Level1, pPreset = presetsA, pRoomTiles = tile1 });
        g.PendingRoom1[RoomA] = room1A;
        g.PendingRooms[Level1] = RoomA;
    }

    public const string Level1Json =
        "\n{\"type\":\"map\", \"id\":1, \"name\":\"Rogue Encampment\", \"offset\":{\"x\":50, \"y\":100}, \"size\":{\"width\":10, \"height\":5}, " +
        "\"objects\":[{\"id\":19, \"type\":\"npc\", \"x\":3, \"y\":4}, " +
        "{\"id\":2, \"type\":\"object\", \"x\":1, \"y\":1, \"name\":\"Shrine\", \"op\":2, \"class\":\"shrine\"}, " +
        "{\"id\":580, \"type\":\"object\", \"x\":2, \"y\":2, \"class\":\"chest-super\"}, " +
        "{\"id\":2, \"type\":\"exit\", \"x\":4, \"y\":0}, " +
        "{\"id\":3, \"type\":\"object\", \"x\":6, \"y\":2, \"name\":\"casket6\"}], " +
        "\"map\":[[0, 2, 2], [], [1, 8], [0]]}\n";
}

public class ProgramTests
{
    private static string L(int level, string src, string msg, string fields = "") =>
        "{\"level\":" + level + ",\"time\":1000,\"source\":\"" + src + "\",\"msg\":\"" + msg + "\"" + fields + "}\n";

    private static readonly string InitLogsD2 =
        L(20, "map/d2_client.c:147", "Init:Dll", ",\"path\":\"C:/games/d2\"") +
        L(30, "map/d2_client.c:176", "Registry:InstallPath", ",\"value\":\"C:/games/d2\"") +
        L(30, "map/d2_client.c:180", "Init:Game", ",\"version\":\"\",\"path\":\"C:/games/d2/\"") +
        L(20, "map/d2_client.c:185", "Init:Offsets:Defined") +
        L(20, "map/d2_client.c:127", "Init:Dll:Done", ",\"dll\":\"Fog.dll\"") +
        L(20, "map/d2_client.c:134", "Init:Dll:Done", ",\"dll\":\"D2Win.dll\"") +
        L(20, "map/d2_client.c:138", "Init:Dll:Done", ",\"dll\":\"D2Lang.dll\"") +
        L(20, "map/d2_client.c:143", "Init:Dll:Done", ",\"dll\":\"D2Client.dll\"") +
        L(30, "map/main.c:143", "Map:Init:Done", ",\"version\":\"v-test\",\"hash\":\"abc123\",\"duration\":0");

    private static int Run(Harness h, string stdin, params string[] args) =>
        new MapGeneratorProgram(h.Context).Main(args, new MemoryStream(Encoding.Latin1.GetBytes(stdin)));

    [Fact]
    public void NoGameFolderPrintsUsage()
    {
        var h = new Harness();
        Assert.Equal(1, Run(h, ""));
        Assert.Equal(L(30, "map/main.c:91", "Cli:Start", ",\"version\":\"v-test\",\"hash\":\"abc123\"") + MapGeneratorProgram.CliUsage, h.Out);
    }

    [Fact]
    public void UsageTextIsVerbatim()
    {
        Assert.Equal(
            "\nUsage:\n    d2-map.exe [D2 Game Path] [options]\n\nOptions:\n" +
            "    --seed [-s]          Map Seed\n" +
            "    --difficulty [-d]    Game Difficulty [0: Normal, 1: Nightmare, 2:Hell]\n" +
            "    --act [-a]           Dump a specific act [0: ActI, 1:ActII, 2: ActIII, 3: ActIV, 4: Act5]\n" +
            "    --map [-m]           Dump a specific Map [0: Rogue Encampent ...]\n" +
            "    --verbose [-v]       Increase logging level\n" +
            "\nExamples:\n" +
            "\n    \u001b[90m# Dump ActI from Normal mode for seed 1122334 \u001b[0m\n" +
            "    d2-map.exe /home/diablo2 --seed 1122334 --difficulty 0 --act 0\n" +
            "\n    \u001b[90m# Dump all acts from Hell mode for seed 1122334 \u001b[0m\n" +
            "    d2-map.exe /home/diablo2 --seed 1122334 --difficulty 2\n",
            MapGeneratorProgram.CliUsage);
    }

    [Fact]
    public void ActAndMapTogetherIsRejected()
    {
        var h = new Harness();
        Assert.Equal(1, Run(h, "", "C:\\d2", "--act", "1", "-m", "2"));
        Assert.EndsWith("--act and --map cannot be used together\n" + MapGeneratorProgram.CliUsage, h.Out);
    }

    [Fact]
    public void ActZeroAndMapAreNotRejectedBecauseTheCheckIsGreaterThanZero()
    {
        var h = new Harness();
        Assert.Throws<FakeExit>(() => Run(h, "", "C:\\d2", "--act", "0", "-m", "2"));
        Assert.DoesNotContain("cannot be used together", h.Out);
    }

    [Fact]
    public void ArgumentLogsAndVerbose()
    {
        var h = new Harness();
        Run(h, "", "-s", "1122334", "-d", "2", "-a", "3", "-v");
        Assert.Equal(
            L(30, "map/main.c:91", "Cli:Start", ",\"version\":\"v-test\",\"hash\":\"abc123\"") +
            L(20, "map/main.c:105", "Cli:Arg", ",\"seed\": \"0x11201e\"") +
            L(20, "map/main.c:109", "Cli:Arg", ",\"difficulty\":2") +
            L(20, "map/main.c:117", "Cli:Arg", ",\"actId\":3") +
            L(20, "map/main.c:120", "Cli:Arg", ",\"verbose\":\"true\"") +
            MapGeneratorProgram.CliUsage, h.Out);
        Assert.Equal(LogLevel.Trace, h.Log.Level);
    }

    [Fact]
    public void OptionWithoutValueIsTheNullArgvCrash()
    {
        var h = new Harness();
        Assert.Throws<MissingOptionValueException>(() => Run(h, "", "C:\\d2", "--seed"));
    }

    [Fact]
    public void UnknownGameVersionExits1()
    {
        var h = new Harness();
        var ex = Assert.Throws<FakeExit>(() => Run(h, "", "C:\\nothing"));
        Assert.Equal((1, "ExitProcess"), (ex.Code, ex.How));
        Assert.EndsWith(L(50, "map/d2_client.c:151", "Init:Failed:UnknownGameVersion", ",\"path\":\"C:/nothing\""), h.Out);
        Assert.Equal(["C:\\nothing\\Path of Diablo/Game.exe", "C:\\nothing\\ProjectD2/Game.exe", "C:\\nothing\\Game.exe"], h.Files.Probed);
    }

    [Fact]
    public void VersionDetectionTraceLogsUseForwardSlashes()
    {
        var h = new Harness("C:\\d2\\ProjectD2/Game.exe");
        h.Log.SetLevel(LogLevel.Trace);
        var v = new GameVersions(h.Files, h.Log).GameVersion("C:\\d2");
        h.Context.Stdout.Flush();
        Assert.Equal(D2Version.VersionProjectDiablo2, v);
        Assert.Equal(
            "{\"level\":10,\"time\":1000,\"source\":\"map/d2_version.h:38\",\"msg\":\"Init:GamePath\",\"exists\":\"false\",\"game\":\"C:/d2/Path of Diablo/Game.exe\"}\n" +
            "{\"level\":10,\"time\":1000,\"source\":\"map/d2_version.h:38\",\"msg\":\"Init:GamePath\",\"exists\":\"true\",\"game\":\"C:/d2/ProjectD2/Game.exe\"}\n", h.Out);
    }

    [Theory]
    [InlineData("C:\\d2\\Path of Diablo/Game.exe", D2Version.VersionPathOfDiablo)]
    [InlineData("C:\\d2\\ProjectD2/Game.exe", D2Version.VersionProjectDiablo2)]
    [InlineData("C:\\d2\\Game.exe", D2Version.VersionDiablo2)]
    public void VersionDetectionOrder(string exe, D2Version expected)
    {
        var h = new Harness(exe, "C:\\d2\\Game.exe");
        Assert.Equal(expected, new GameVersions(h.Files, h.Log).GameVersion("C:\\d2"));
    }

    [Fact]
    public void GameVersionPaths()
    {
        Assert.Equal("Path of Diablo/", GameVersions.GameVersionPath(D2Version.VersionPathOfDiablo));
        Assert.Equal("ProjectD2/", GameVersions.GameVersionPath(D2Version.VersionProjectDiablo2));
        Assert.Equal("", GameVersions.GameVersionPath(D2Version.VersionDiablo2));
        Assert.Null(GameVersions.GameVersionPath(D2Version.VersionUnknown));
    }

    [Fact]
    public void RegistryOpenFailureExits1()
    {
        var h = new Harness(TestWorld.Folder + "\\Game.exe");
        h.Game.RegistryKeyExists = false;
        var ex = Assert.Throws<FakeExit>(() => Run(h, "", TestWorld.Folder));
        Assert.Equal(1, ex.Code);
        Assert.EndsWith(L(50, "map/d2_client.c:169", "Registry:Failed:Open"), h.Out);
    }

    [Fact]
    public void Fog10218FailureExits1()
    {
        var h = new Harness(TestWorld.Folder + "\\Game.exe");
        h.Game.Fog10218Result = 0;
        var ex = Assert.Throws<FakeExit>(() => Run(h, "", TestWorld.Folder));
        Assert.Equal((1, "ExitProcess"), (ex.Code, ex.How));
        Assert.EndsWith(L(50, "map/d2_client.c:124", "Init:Dll:Failed", ",\"dll\":\"Fog.dll\""), h.Out);
    }

    [Fact]
    public void D2WinFailureUsesUpstreamMessages()
    {
        var pd2 = new Harness(TestWorld.Folder + "\\Game.exe");
        pd2.Game.D2WinInitResult = 0;
        Assert.Throws<FakeExit>(() => Run(pd2, "", TestWorld.Folder));
        Assert.EndsWith(L(50, "map/d2_client.c:131", "InitFailed", ",\"dll\":\"D2Win.dll\""), pd2.Out);

        var pod = new Harness(TestWorld.Folder + "\\Path of Diablo/Game.exe");
        pod.Game.D2WinInitResult = 0;
        Assert.Throws<FakeExit>(() => Run(pod, "", TestWorld.Folder));
        Assert.EndsWith(L(50, "map/d2_client.c:82", "Init:Dll:Failed", ",\"dll\":\"D2Win.dll\""), pod.Out);
    }

    [Fact]
    public void MissingOrdinalExitsViaCExit()
    {
        var h = new Harness(TestWorld.Folder + "\\Game.exe");
        h.Game.MissingOrdinals.Add(10218);
        var ex = Assert.Throws<FakeExit>(() => Run(h, "", TestWorld.Folder));
        Assert.Equal((1, "exit"), (ex.Code, ex.How));
        Assert.EndsWith(L(50, "map/offset.c:43", "Dll:Init:Failed", ",\"dll\":\"Fog.DLL\",\"offset\":0"), h.Out);
    }

    [Fact]
    public void Pd2InitSequenceMatchesC()
    {
        var h = new Harness(TestWorld.Folder + "\\Game.exe");
        Run(h, "$exit\n", TestWorld.Folder);
        var a = h.Game.BoundAddresses!;
        Assert.Equal(
        [
            "RegOpenKeyEx(HKCU, SOFTWARE\\Blizzard Entertainment\\Diablo II)",
            "RegSetValueEx(InstallPath=C:\\games\\d2)",
            "RegCloseKey",
            "SetCurrentDirectory(C:\\games\\d2/)",
            "FOG_10021(\"D2\")",
            $"FOG_10019(\"Diablo II\", 0x{FakeGame.ExceptionHandlerAddress:x}, \"v1.xy\", 1)",
            "FOG_10101(1, 0)",
            "FOG_10089(1)",
            "FOG_10218()",
            "D2WIN_10086()",
            "D2WIN_10005(0, 0, 0, client)",
            "D2LANG_10008(0, \"ENG\", 0)",
            "D2COMMON_Pd2_InitDataTables(0, 0, 0)",
            "D2CLIENT_Pd2_InitGameMisc()",
            "SetCurrentDirectory(C:\\games\\d2)",
        ], h.Game.Calls);
        // *p_STORM_Pd2_MPQHashTable = NULL, Pod table untouched; D2Client.dwInit = 1, fpInit = D2ClientInterface
        Assert.Equal(0u, h.Game.Memory.Read<uint>(a[D2Ptr.STORM_Pd2_MPQHashTable]));
        Assert.Equal(0xDEADBEEFu, h.Game.Memory.Read<uint>(a[D2Ptr.STORM_Pod_MPQHashTable]));
        Assert.Equal(L(30, "map/main.c:91", "Cli:Start", ",\"version\":\"v-test\",\"hash\":\"abc123\"") +
                     L(20, "map/main.c:124", "Cli:Arg", ",\"game\":\"C:/games/d2\"") +
                     InitLogsD2 + "\n{\"type\":\"init\"}\n", h.Out);
    }

    [Fact]
    public void PodInitSequenceMatchesC()
    {
        var h = new Harness(TestWorld.Folder + "\\Path of Diablo/Game.exe");
        var program = new MapGeneratorProgram(h.Context);
        program.Main([TestWorld.Folder], new MemoryStream(Encoding.Latin1.GetBytes("$exit")));
        var a = h.Game.BoundAddresses!;
        Assert.Equal(
        [
            "RegOpenKeyEx(HKCU, SOFTWARE\\Blizzard Entertainment\\Diablo II)",
            "RegSetValueEx(InstallPath=C:\\games\\d2)",
            "RegCloseKey",
            "SetCurrentDirectory(C:\\games\\d2/Path of Diablo/)",
            "FOG_10021(\"D2\")",
            $"FOG_10019(\"Diablo II\", 0x{FakeGame.ExceptionHandlerAddress:x}, \"v1.xy\", 1)",
            "FOG_10101(1, 0)",
            "FOG_10089(1)",
            "FOG_10218()",
            "D2WIN_10174()",
            "D2WIN_10072(0, 0, 0, client)",
            "D2LANG_10009(0, \"ENG\", 0)",
            "D2COMMON_Pod_InitDataTables(0, 0, 0)",
            "D2CLIENT_Pod_InitGameMisc()",
            "SetCurrentDirectory(C:\\games\\d2)",
        ], h.Game.Calls);
        Assert.Equal(0u, h.Game.Memory.Read<uint>(a[D2Ptr.STORM_Pod_MPQHashTable]));
        var client = program.Client.D2ClientStruct;
        Assert.Equal(1u, h.Game.Memory.Read<uint>(client));
        Assert.Equal(FakeGame.D2ClientInterfaceAddress, h.Game.Memory.Read<uint>(client + 0x20D));
        Assert.Equal("C:\\games\\d2/Path of Diablo/", program.Client.D2Dir);
        Assert.Equal("C:\\games\\d2", h.Game.Registry["InstallPath"]);
    }

    [Fact]
    public void DumpSingleMapFromCommandLine()
    {
        var h = new Harness(TestWorld.Folder + "\\Game.exe");
        _ = new TestWorld(h.Game);
        Assert.Equal(0, Run(h, "", TestWorld.Folder, "--seed", "1", "--map", "1"));
        Assert.Equal(
            L(30, "map/main.c:91", "Cli:Start", ",\"version\":\"v-test\",\"hash\":\"abc123\"") +
            L(20, "map/main.c:124", "Cli:Arg", ",\"game\":\"C:/games/d2\"") +
            L(20, "map/main.c:105", "Cli:Arg", ",\"seed\": \"0x000001\"") +
            L(20, "map/main.c:113", "Cli:Arg", ",\"mapId\":1") +
            InitLogsD2 +
            TestWorld.Level1Json +
            L(20, "map/main.c:63", "Map:Generation", ",\"seed\": \"0x000001\",\"difficulty\":0,\"mapId\":1,\"duration\":0") +
            L(30, "map/main.c:81", "Map:Generation:Done", ",\"seed\": \"0x000001\",\"difficulty\":0,\"count\":1,\"duration\":0") +
            "\n", h.Out);

        var dumpCalls = h.Game.Calls.SkipWhile(c => !c.Contains("LoadAct", StringComparison.Ordinal)).ToList();
        Assert.Equal(
        [
            "D2COMMON_Pd2_LoadAct(0, 1, 1, 0, 0, 0, 1, 0x6fb12aa0, 0x6fb12760)",
            "D2COMMON_Pd2_InitLevel(level 1)",
            "D2COMMON_Pd2_AddRoomData(0x" + h.Game.Acts[0].ToString("x") + ", 1, 10, 20, 0)",
            // upstream's d2common_remove_room_data calls AddRoomData again
            "D2COMMON_Pd2_AddRoomData(0x" + h.Game.Acts[0].ToString("x") + ", 1, 10, 20, 0)",
        ], dumpCalls);
        Assert.Equal("", h.Err);
    }

    [Fact]
    public void TextModeUsageIsCrLf()
    {
        var bin = new Harness();
        Run(bin, "");
        var text = new Harness(true);
        Assert.Equal(1, Run(text, ""));
        Assert.Contains('\n', bin.Out);
        Assert.Equal(bin.Out.Replace("\n", "\r\n", StringComparison.Ordinal), text.Out);
    }

    [Fact]
    public void TextModeStdinProtocolMatchesBinaryWithCrLf()
    {
        // The session as the upstream Windows exe sees it: CRLF commands in, CRLF lines out, and a
        // Ctrl-Z byte ends the command loop (the commands after it are never read).
        var bin = new Harness(TestWorld.Folder + "\\Game.exe");
        _ = new TestWorld(bin.Game);
        var binCode = Run(bin, "$seed 10\n$difficulty 2\n$act 0\n$map\n", TestWorld.Folder);

        var text = new Harness(true, TestWorld.Folder + "\\Game.exe");
        _ = new TestWorld(text.Game);
        var textCode = Run(text, "$seed 10\r\n$difficulty 2\r\n$act 0\r\n$map\r\n\u001a$seed 99\r\n$map\r\n", TestWorld.Folder);

        Assert.Equal(binCode, textCode);
        Assert.Equal(bin.Out.Replace("\n", "\r\n", StringComparison.Ordinal), text.Out);
        Assert.DoesNotContain("\"seed\":99", text.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void StdinProtocol()
    {
        var h = new Harness(TestWorld.Folder + "\\Game.exe");
        _ = new TestWorld(h.Game);
        var stdin = "$seed 10\n$difficulty 2\n$act 0\nunknown\n$map\n$exit\n$seed 99\n";
        Assert.Equal(0, Run(h, stdin, TestWorld.Folder));

        var level1 = TestWorld.Level1Json;
        Assert.Equal(
            L(30, "map/main.c:91", "Cli:Start", ",\"version\":\"v-test\",\"hash\":\"abc123\"") +
            L(20, "map/main.c:124", "Cli:Arg", ",\"game\":\"C:/games/d2\"") +
            InitLogsD2 +
            "\n{\"type\":\"init\"}\n" +
            "\n{\"type\":\"info\", \"seed\":10, \"difficulty\":0}\n" + "\n" +
            "\n{\"type\":\"info\", \"seed\":10, \"difficulty\":2}\n" + "\n" +
            "\n{\"type\":\"info\", \"seed\":10, \"difficulty\":2, \"act\":0}\n" + "\n" +
            "\n" + // unknown command: only the trailing printf("\n")
            level1 +
            L(20, "map/main.c:77", "Map:Generation", ",\"seed\": \"0x00000a\",\"difficulty\":2,\"actId\":0,\"mapId\":1,\"duration\":0") +
            L(40, "map/d2_client.c:460", "Map:SkippingLevel:FailedRoomLoading", ",\"mapId\":2,\"mapName\":\"Blood Moor\"") +
            L(30, "map/main.c:81", "Map:Generation:Done", ",\"seed\": \"0x00000a\",\"difficulty\":2,\"count\":1,\"duration\":0") +
            "\n" +
            "\n{\"type\":\"done\"}\n" + "\n",
            h.Out);
        // The act is loaded once for both levels (cache), with seed 10 / difficulty 2.
        Assert.Single(h.Game.Calls, c => c.StartsWith("D2COMMON_Pd2_LoadAct(0, 10, 1, 0, 2, 0, 1,", StringComparison.Ordinal));
    }

    [Fact]
    public void ActCacheUnloadsWhenSeedChanges()
    {
        var h = new Harness(TestWorld.Folder + "\\Game.exe");
        var w = new TestWorld(h.Game);
        Run(h, "$seed 1\n$act 0\n$map\n$seed 2\n$act 0\n$map\n", TestWorld.Folder);
        var acts = h.Game.Calls.Where(c => c.Contains("LoadAct", StringComparison.Ordinal) || c.Contains("UnloadAct", StringComparison.Ordinal)).ToList();
        Assert.Equal(
        [
            "D2COMMON_Pd2_LoadAct(0, 1, 1, 0, 0, 0, 1, 0x6fb12aa0, 0x6fb12760)",
            $"D2COMMON_Pd2_UnloadAct(0x{w.Act:x})",
            "D2COMMON_Pd2_LoadAct(0, 2, 1, 0, 0, 0, 1, 0x6fb12aa0, 0x6fb12760)",
        ], acts);
    }

    [Fact]
    public void MapResetsActAndMapSelectionAfterwards()
    {
        var h = new Harness(TestWorld.Folder + "\\Game.exe");
        _ = new TestWorld(h.Game);
        Run(h, "$act 3\n$map\n$seed 4\n", TestWorld.Folder);
        Assert.Contains("\n{\"type\":\"info\", \"seed\":4278255360, \"difficulty\":0, \"act\":3}\n", h.Out);
        Assert.EndsWith("\n{\"type\":\"info\", \"seed\":4, \"difficulty\":0}\n\n", h.Out);
    }
}

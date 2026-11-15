using D2Dotnet.Map.Generator.Native;
using D2Dotnet.Map.Generator.Output;
using D2Dotnet.Map.Generator.Tests.Fakes;

namespace D2Dotnet.Map.Generator.Tests;

public class D2PointersTests
{
    [Fact]
    public void TableHasEveryDeclarationInOrder()
    {
        Assert.Equal(40, D2Pointers.All.Count);
        for (var i = 0; i < D2Pointers.All.Count; i++) Assert.Equal((D2Ptr)i, D2Pointers.All[i].Ptr);
        Assert.Equal(D2Ptr.FOG_10021, D2Pointers.All[0].Ptr);      // _D2PTRS_START
        Assert.Equal(D2Ptr.D2COMMON_10943, D2Pointers.All[^1].Ptr); // _D2PTRS_END
    }

    [Theory]
    [InlineData(D2Ptr.FOG_10021, D2Dll.FOG, -10021, D2CallingConvention.Fastcall, 1)]
    [InlineData(D2Ptr.D2COMMON_LoadAct, D2Dll.D2COMMON, 0x24810, D2CallingConvention.Stdcall, 9)]
    [InlineData(D2Ptr.FOG_10019, D2Dll.FOG, -10019, D2CallingConvention.Fastcall, 4)]
    [InlineData(D2Ptr.FOG_10218, D2Dll.FOG, -10218, D2CallingConvention.Fastcall, 0)]
    [InlineData(D2Ptr.D2CLIENT_Pod_InitGameMisc_I, D2Dll.D2CLIENT, 0x4454B, D2CallingConvention.Stdcall, 3)]
    [InlineData(D2Ptr.STORM_Pod_MPQHashTable, D2Dll.STORM, 0x52A60, D2CallingConvention.None, 0)]
    [InlineData(D2Ptr.D2CLIENT_Pod_LoadAct_1, D2Dll.D2CLIENT, 0x737F0, D2CallingConvention.None, 0)]
    [InlineData(D2Ptr.D2CLIENT_Pod_LoadAct_2, D2Dll.D2CLIENT, 0x2B420, D2CallingConvention.None, 0)]
    [InlineData(D2Ptr.D2COMMON_Pod_GetLevel, D2Dll.D2COMMON, 0x6D440, D2CallingConvention.Fastcall, 2)]
    [InlineData(D2Ptr.D2COMMON_Pod_InitDataTables, D2Dll.D2COMMON, -10081, D2CallingConvention.Stdcall, 3)]
    [InlineData(D2Ptr.D2COMMON_Pod_GetObjectTxt, D2Dll.D2COMMON, 0x1ADC0, D2CallingConvention.Stdcall, 1)]
    [InlineData(D2Ptr.D2WIN_10072, D2Dll.D2WIN, -10072, D2CallingConvention.Fastcall, 4)]
    [InlineData(D2Ptr.STORM_Pd2_MPQHashTable, D2Dll.STORM, 0x53120, D2CallingConvention.None, 0)]
    [InlineData(D2Ptr.D2CLIENT_Pd2_LoadAct_1, D2Dll.D2CLIENT, 0x62AA0, D2CallingConvention.None, 0)]
    [InlineData(D2Ptr.D2CLIENT_Pd2_LoadAct_2, D2Dll.D2CLIENT, 0x62760, D2CallingConvention.None, 0)]
    [InlineData(D2Ptr.D2COMMON_Pd2_AddRoomData, D2Dll.D2COMMON, -10401, D2CallingConvention.Stdcall, 5)]
    [InlineData(D2Ptr.D2COMMON_Pd2_RemoveRoomData, D2Dll.D2COMMON, -11099, D2CallingConvention.Stdcall, 5)]
    [InlineData(D2Ptr.D2COMMON_Pd2_GetLevel, D2Dll.D2COMMON, -10207, D2CallingConvention.Fastcall, 2)]
    [InlineData(D2Ptr.D2COMMON_Pd2_InitLevel, D2Dll.D2COMMON, -10322, D2CallingConvention.Stdcall, 1)]
    [InlineData(D2Ptr.D2COMMON_Pd2_LoadAct, D2Dll.D2COMMON, 0x3CB30, D2CallingConvention.Stdcall, 9)]
    [InlineData(D2Ptr.D2COMMON_Pd2_UnloadAct, D2Dll.D2COMMON, -10868, D2CallingConvention.Stdcall, 1)]
    [InlineData(D2Ptr.D2COMMON_Pd2_GetLevelText, D2Dll.D2COMMON, -10014, D2CallingConvention.Stdcall, 1)]
    [InlineData(D2Ptr.D2COMMON_Pd2_GetObjectTxt, D2Dll.D2COMMON, -10688, D2CallingConvention.Stdcall, 1)]
    [InlineData(D2Ptr.D2COMMON_Pd2_InitDataTables, D2Dll.D2COMMON, -10943, D2CallingConvention.Stdcall, 3)]
    [InlineData(D2Ptr.D2WIN_10086, D2Dll.D2WIN, -10086, D2CallingConvention.Fastcall, 0)]
    [InlineData(D2Ptr.D2WIN_10005, D2Dll.D2WIN, -10005, D2CallingConvention.Fastcall, 4)]
    [InlineData(D2Ptr.D2LANG_10008, D2Dll.D2LANG, -10008, D2CallingConvention.Fastcall, 3)]
    [InlineData(D2Ptr.D2LANG_10009, D2Dll.D2LANG, -10009, D2CallingConvention.Fastcall, 3)]
    [InlineData(D2Ptr.D2COMMON_10943, D2Dll.D2COMMON, -10943, D2CallingConvention.Stdcall, 3)]
    public void EntriesMatchD2PtrsH(D2Ptr ptr, D2Dll dll, int offset, D2CallingConvention cc, int args)
    {
        var def = D2Pointers.Get(ptr);
        Assert.Equal(dll, def.Dll);
        Assert.Equal(offset, def.Offset);
        Assert.Equal(cc, def.Convention);
        Assert.Equal(args, def.ArgCount);
    }

    [Fact]
    public void DllOffsetEncodingMatchesTheMacro()
    {
        // DLLOFFSET(FOG, -10021) = 8 | (-10021 << 8)
        Assert.Equal(-2565368, D2Pointers.Get(D2Ptr.FOG_10021).Encoded);
        Assert.Equal(0x2481001, D2Pointers.Get(D2Ptr.D2COMMON_LoadAct).Encoded);
        foreach (var def in D2Pointers.All)
        {
            Assert.Equal((int)def.Dll, def.Encoded & 0xff);
            Assert.Equal(def.Offset, def.Encoded >> 8);
        }
    }

    [Fact]
    public void DllNamesMatchOffsetC()
    {
        Assert.Equal(["D2Client.DLL", "D2Common.DLL", "D2Gfx.DLL", "D2Lang.DLL", "D2Win.DLL", "D2Net.DLL", "D2Game.DLL", "D2Launch.DLL", "Fog.DLL", "BNClient.DLL", "Storm.DLL", "D2Cmp.DLL"], D2Pointers.DllNames);
    }

    [Fact]
    public void VariableNamesHavePPrefix()
    {
        Assert.Equal("p_STORM_Pod_MPQHashTable", D2Pointers.Get(D2Ptr.STORM_Pod_MPQHashTable).CName);
        Assert.Equal("FOG_10021", D2Pointers.Get(D2Ptr.FOG_10021).CName);
    }
}

public class DllOffsetsTests
{
    private static (DllOffsets Offsets, FakeGame Game, MemoryStream Out, List<int> Exits) Make(int level = LogLevel.Debug)
    {
        var game = new FakeGame();
        var ms = new MemoryStream();
        var log = new Logger(new CStream(ms), new FixedClock(1));
        log.SetLevel(level);
        var exits = new List<int>();
        var offsets = new DllOffsets(game, log, c => { exits.Add(c); throw new FakeExit(c, "exit"); });
        return (offsets, game, ms, exits);
    }

    [Fact]
    public void PositiveOffsetIsBasePlusOffset()
    {
        var (o, _, _, _) = Make();
        Assert.Equal(0x6FAB0000u + 0x737F0, o.GetDllOffset("D2Client.DLL", 0x737F0));
    }

    [Fact]
    public void NegativeOffsetIsOrdinal()
    {
        var (o, _, _, _) = Make();
        Assert.Equal(0x6FF50000u + 0x100000 + 10021, o.GetDllOffset("Fog.DLL", -10021));
    }

    [Fact]
    public void LoadsLibraryOnlyWhenNoModuleHandle()
    {
        var (o, g, _, _) = Make();
        o.GetDllOffset("Fog.DLL", 1);
        o.GetDllOffset("Fog.DLL", 2);
        Assert.Equal(["Fog.DLL"], g.LoadOrder);
    }

    [Fact]
    public void MissingDllReturnsZero()
    {
        var (o, g, _, _) = Make();
        g.MissingDlls.Add("Fog.DLL");
        Assert.Equal(0u, o.GetDllOffset("Fog.DLL", -10021));
    }

    [Fact]
    public void DllNumberAbove12ReturnsZeroWithoutExit()
    {
        var (o, _, _, exits) = Make();
        Assert.Equal(0u, o.GetDllOffset(13 | (5 << 8)));
        Assert.Empty(exits);
    }

    [Fact]
    public void FailedResolutionLogsAndExits1()
    {
        var (o, g, ms, exits) = Make();
        g.MissingOrdinals.Add(10021);
        var ex = Assert.Throws<FakeExit>(() => o.GetDllOffset(D2Pointers.Get(D2Ptr.FOG_10021).Encoded));
        Assert.Equal(1, ex.Code);
        Assert.Equal([1], exits);
        Assert.Equal("{\"level\":50,\"time\":1,\"source\":\"map/offset.c:43\",\"msg\":\"Dll:Init:Failed\",\"dll\":\"Fog.DLL\",\"offset\":0}\n", System.Text.Encoding.Latin1.GetString(ms.ToArray()));
    }

    [Fact]
    public void TraceLogsResolvedOffsetAsSignedInt()
    {
        var (o, _, ms, _) = Make(LogLevel.Trace);
        o.GetDllOffset(D2Pointers.Get(D2Ptr.D2CLIENT_Pd2_LoadAct_1).Encoded);
        Assert.Equal("{\"level\":10,\"time\":1,\"source\":\"map/offset.c:46\",\"msg\":\"Dll:Init\",\"dll\":\"D2Client.DLL\",\"offset\":1873881760}\n", System.Text.Encoding.Latin1.GetString(ms.ToArray()));
    }

    [Fact]
    public void DefineOffsetsResolvesAllInOrderAndLoadsDllsInFirstUseOrder()
    {
        var (o, g, _, _) = Make();
        var a = o.DefineOffsets();
        Assert.Equal(["Fog.DLL", "D2Common.DLL", "D2Client.DLL", "Storm.DLL", "D2Lang.DLL", "D2Win.DLL"], g.LoadOrder);
        Assert.Equal(0x6FBF0000u + 0x53120, a[D2Ptr.STORM_Pd2_MPQHashTable]);
        Assert.Equal(0x6FD50000u + 0x100000 + 10943, a[D2Ptr.D2COMMON_10943]);
        Assert.Equal(0x6FAB0000u + 0x62760, a[D2Ptr.D2CLIENT_Pd2_LoadAct_2]);
    }
}

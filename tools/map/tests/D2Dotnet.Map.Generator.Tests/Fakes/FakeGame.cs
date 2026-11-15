using System.Diagnostics.CodeAnalysis;
using D2Dotnet.Map.Generator.Game;
using D2Dotnet.Map.Generator.Native;
using D2Dotnet.Map.Generator.Output;

namespace D2Dotnet.Map.Generator.Tests.Fakes;

/// <summary>Thrown by the fake platform instead of terminating the test host.</summary>
public sealed class FakeExit(int code, string how) : Exception($"{how}({code})")
{
    public int Code { get; } = code;
    public string How { get; } = how;
}

public sealed class FixedClock(long now = 1000) : IClock
{
    public long Now { get; set; } = now;

    public long CurrentTimeMillis() => Now;
}

public sealed class FakeFiles(params string[] existing) : IFileProbe
{
    public List<string> Probed { get; } = [];

    public bool CanOpen(string path)
    {
        Probed.Add(path);
        return existing.Contains(path);
    }
}

/// <summary>
/// A fake Diablo 2: implements the D2 exports over a <see cref="FakeMemory"/> world, plus the
/// platform and module loader. Every call is recorded in <see cref="Calls"/>.
/// </summary>
[SuppressMessage("Naming", "CA1707")]
public sealed class FakeGame : ID2Functions, IGamePlatform, IModuleLoader
{
    public static readonly Dictionary<string, uint> DllBases = new(StringComparer.Ordinal)
    {
        ["D2Client.DLL"] = 0x6FAB0000,
        ["D2Common.DLL"] = 0x6FD50000,
        ["D2Lang.DLL"] = 0x6FC00000,
        ["D2Win.DLL"] = 0x6F8E0000,
        ["Fog.DLL"] = 0x6FF50000,
        ["Storm.DLL"] = 0x6FBF0000,
    };

    public FakeMemory Memory { get; } = new();
    public List<string> Calls { get; } = [];
    public Dictionary<int, uint> LevelTxts { get; } = [];
    public Dictionary<uint, uint> ObjectTxts { get; } = [];
    public Dictionary<int, uint> ActsBySeed { get; } = [];

    /// <summary>Returned by LoadAct (one act per act id).</summary>
    public Dictionary<int, uint> Acts { get; } = [];

    /// <summary>Room2 address → Room1 to attach when AddRoomData is called for it.</summary>
    public Dictionary<uint, uint> PendingRoom1 { get; } = [];

    /// <summary>Level address → Room2 chain head that InitLevel attaches.</summary>
    public Dictionary<uint, uint> PendingRooms { get; } = [];

    public HashSet<string> LoadedModules { get; } = [];
    public List<string> LoadOrder { get; } = [];
    public HashSet<string> MissingDlls { get; } = [];
    public HashSet<int> MissingOrdinals { get; } = [];
    public bool RegistryKeyExists { get; set; } = true;
    public Dictionary<string, string> Registry { get; } = [];
    public List<string> Directories { get; } = [];
    public uint Fog10218Result { get; set; } = 1;
    public uint D2WinInitResult { get; set; } = 1;

    private void Call(string s) => Calls.Add(s);

    // ---------------- module loader ----------------
    public uint GetModuleHandle(string dllName) => LoadedModules.Contains(dllName) ? DllBases[dllName] : 0;

    public uint LoadLibrary(string dllName)
    {
        if (MissingDlls.Contains(dllName) || !DllBases.TryGetValue(dllName, out var b)) return 0;
        LoadedModules.Add(dllName);
        LoadOrder.Add(dllName);
        return b;
    }

    /// <summary>Exports live at base + 0x100000 + ordinal so they are easy to recognise.</summary>
    public uint GetProcAddressByOrdinal(uint module, int ordinal) => MissingOrdinals.Contains(ordinal) ? 0 : module + 0x100000 + (uint)ordinal;

    // ---------------- platform ----------------
    public bool RegOpenCurrentUserKey(string subKey, out nint hKey)
    {
        Call($"RegOpenKeyEx(HKCU, {subKey})");
        hKey = RegistryKeyExists ? 0x1234 : 0;
        return RegistryKeyExists;
    }

    public int RegSetStringValue(nint hKey, string name, string data)
    {
        Call($"RegSetValueEx({name}={data})");
        Registry[name] = data;
        return 0;
    }

    public void RegCloseKey(nint hKey) => Call("RegCloseKey");

    public void SetCurrentDirectory(string path)
    {
        Call($"SetCurrentDirectory({path})");
        Directories.Add(path);
    }

    [DoesNotReturn]
    public void ExitProcess(uint exitCode) => throw new FakeExit((int)exitCode, "ExitProcess");

    [DoesNotReturn]
    public void Exit(int exitCode) => throw new FakeExit(exitCode, "exit");

    public uint AllocateStatic(int size) => Memory.Alloc(size);

    public Dictionary<uint, string> Strings { get; } = [];

    public uint AllocateAnsiString(string value)
    {
        var a = Memory.NewString(value);
        Strings[a] = value;
        return a;
    }

    public const uint D2ClientInterfaceAddress = 0x00401000;
    public const uint ExceptionHandlerAddress = 0x00402000;

    public uint D2ClientInterfaceCallback(uint d2ClientAddress) => D2ClientInterfaceAddress;

    public uint ExceptionHandlerCallback() => ExceptionHandlerAddress;

    public D2Addresses? BoundAddresses { get; private set; }

    public ID2Functions BindFunctions(D2Addresses addresses)
    {
        BoundAddresses = addresses;
        // The storm hash table globals must be writable memory.
        Memory.Write<uint>(addresses[D2Ptr.STORM_Pod_MPQHashTable], 0xDEADBEEF);
        Memory.Write<uint>(addresses[D2Ptr.STORM_Pd2_MPQHashTable], 0xDEADBEEF);
        return this;
    }

    private string S(uint a) => Strings.TryGetValue(a, out var s) ? $"\"{s}\"" : $"0x{a:x}";

    // ---------------- D2 exports ----------------
    public void FOG_10021(uint szProg) => Call($"FOG_10021({S(szProg)})");
    public uint FOG_10019(uint szAppName, uint pExceptionHandler, uint szTitle, uint _1) { Call($"FOG_10019({S(szAppName)}, 0x{pExceptionHandler:x}, {S(szTitle)}, {_1})"); return 1; }
    public uint FOG_10101(uint _1, uint _2) { Call($"FOG_10101({_1}, {_2})"); return 1; }
    public uint FOG_10089(uint _1) { Call($"FOG_10089({_1})"); return 1; }
    public uint FOG_10218() { Call("FOG_10218()"); return Fog10218Result; }

    public uint D2COMMON_Pod_InitDataTables(uint _1, uint _2, uint _3) { Call($"D2COMMON_Pod_InitDataTables({_1}, {_2}, {_3})"); return 1; }
    public uint D2LANG_10009(uint _1, uint _2, uint _3) { Call($"D2LANG_10009({_1}, {S(_2)}, {_3})"); return 1; }
    public uint D2WIN_10174() { Call("D2WIN_10174()"); return D2WinInitResult; }
    public uint D2WIN_10072(uint _1, uint _2, uint _3, uint pD2Client) { Call($"D2WIN_10072({_1}, {_2}, {_3}, client)"); return 1; }
    public void D2CLIENT_Pod_InitGameMisc() => Call("D2CLIENT_Pod_InitGameMisc()");

    public uint D2COMMON_Pd2_InitDataTables(uint _1, uint _2, uint _3) { Call($"D2COMMON_Pd2_InitDataTables({_1}, {_2}, {_3})"); return 1; }
    public uint D2LANG_10008(uint _1, uint _2, uint _3) { Call($"D2LANG_10008({_1}, {S(_2)}, {_3})"); return 1; }
    public uint D2WIN_10086() { Call("D2WIN_10086()"); return D2WinInitResult; }
    public uint D2WIN_10005(uint _1, uint _2, uint _3, uint pD2Client) { Call($"D2WIN_10005({_1}, {_2}, {_3}, client)"); return 1; }
    public void D2CLIENT_Pd2_InitGameMisc() => Call("D2CLIENT_Pd2_InitGameMisc()");

    private uint LoadAct(string v, uint actNumber, uint mapId, uint unk, uint unk2, uint unk3, uint unk4, uint townLevelId, uint func1, uint func2)
    {
        Call($"D2COMMON_{v}_LoadAct({actNumber}, {mapId}, {unk}, {unk2}, {unk3}, {unk4}, {townLevelId}, 0x{func1:x}, 0x{func2:x})");
        return Acts.TryGetValue((int)actNumber, out var a) ? a : 0;
    }

    public uint D2COMMON_Pod_LoadAct(uint actNumber, uint mapId, uint unk, uint unk2, uint unk3, uint unk4, uint townLevelId, uint func1, uint func2) => LoadAct("Pod", actNumber, mapId, unk, unk2, unk3, unk4, townLevelId, func1, func2);
    public uint D2COMMON_Pd2_LoadAct(uint actNumber, uint mapId, uint unk, uint unk2, uint unk3, uint unk4, uint townLevelId, uint func1, uint func2) => LoadAct("Pd2", actNumber, mapId, unk, unk2, unk3, unk4, townLevelId, func1, func2);

    public void D2COMMON_Pod_UnloadAct(uint pAct) => Call($"D2COMMON_Pod_UnloadAct(0x{pAct:x})");
    public void D2COMMON_Pd2_UnloadAct(uint pAct) => Call($"D2COMMON_Pd2_UnloadAct(0x{pAct:x})");

    public uint D2COMMON_Pod_GetLevelText(uint levelno) => LevelTxts.GetValueOrDefault((int)levelno);
    public uint D2COMMON_Pd2_GetLevelText(uint levelno) => LevelTxts.GetValueOrDefault((int)levelno);

    public uint D2COMMON_Pod_GetObjectTxt(uint objno) => ObjectTxts[objno];
    public uint D2COMMON_Pd2_GetObjectTxt(uint objno) => ObjectTxts[objno];

    private void InitLevel(string v, uint pLevel)
    {
        Call($"D2COMMON_{v}_InitLevel(level {Memory.Read<Level>(pLevel).dwLevelNo})");
        if (PendingRooms.TryGetValue(pLevel, out var head)) Memory.Update<Level>(pLevel, l => { l.pRoom2First = head; return l; });
    }

    public void D2COMMON_Pod_InitLevel(uint pLevel) => InitLevel("Pod", pLevel);
    public void D2COMMON_Pd2_InitLevel(uint pLevel) => InitLevel("Pd2", pLevel);

    private void AddRoom(string v, uint ptAct, int levelId, int xpos, int ypos, uint pRoom)
    {
        Call($"D2COMMON_{v}_AddRoomData(0x{ptAct:x}, {levelId}, {xpos}, {ypos}, {pRoom})");
        foreach (var (room2, room1) in PendingRoom1)
        {
            var r = Memory.Read<Room2>(room2);
            if (r.dwPosX == (uint)xpos && r.dwPosY == (uint)ypos && r.pRoom1 == 0)
            {
                Memory.Update<Room2>(room2, x => { x.pRoom1 = room1; return x; });
            }
        }
    }

    public void D2COMMON_Pod_AddRoomData(uint ptAct, int levelId, int xpos, int ypos, uint pRoom) => AddRoom("Pod", ptAct, levelId, xpos, ypos, pRoom);
    public void D2COMMON_Pd2_AddRoomData(uint ptAct, int levelId, int xpos, int ypos, uint pRoom) => AddRoom("Pd2", ptAct, levelId, xpos, ypos, pRoom);
    public void D2COMMON_Pod_RemoveRoomData(uint ptAct, int levelId, int xpos, int ypos, uint pRoom) => Call("D2COMMON_Pod_RemoveRoomData");
    public void D2COMMON_Pd2_RemoveRoomData(uint ptAct, int levelId, int xpos, int ypos, uint pRoom) => Call("D2COMMON_Pd2_RemoveRoomData");

    private uint GetLevel(string v, uint pMisc, uint dwLevelNo)
    {
        Call($"D2COMMON_{v}_GetLevel(misc, {dwLevelNo})");
        return 0;
    }

    public uint D2COMMON_Pod_GetLevel(uint pMisc, uint dwLevelNo) => GetLevel("Pod", pMisc, dwLevelNo);
    public uint D2COMMON_Pd2_GetLevel(uint pMisc, uint dwLevelNo) => GetLevel("Pd2", pMisc, dwLevelNo);

    // ---------------- world builders ----------------
    public unsafe uint AddLevelTxt(int levelNo, string name)
    {
        var txt = new LevelTxt { dwLevelNo = (uint)levelNo };
        var bytes = System.Text.Encoding.Latin1.GetBytes(name);
        for (var i = 0; i < bytes.Length && i < 39; i++) txt.szName[i] = bytes[i];
        var a = Memory.New(txt);
        LevelTxts[levelNo] = a;
        return a;
    }

    public unsafe uint AddObjectTxt(uint objNo, string name, byte selectable, byte operateFn)
    {
        var txt = new ObjectTxt { nSelectable0 = selectable, nOperateFn = operateFn };
        var bytes = System.Text.Encoding.Latin1.GetBytes(name);
        for (var i = 0; i < bytes.Length; i++) txt.szName[i] = bytes[i];
        var a = Memory.New(txt);
        ObjectTxts[objNo] = a;
        return a;
    }

    /// <summary>A collision map at game position (x, y) with rows of values (row-major, y outer).</summary>
    public uint AddCollMap(int gameX, int gameY, ushort[][] rows)
    {
        var w = rows[0].Length;
        var cells = Memory.Alloc(rows.Length * w * 2);
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < w; x++) Memory.Write(cells + (uint)((y * w + x) * 2), rows[y][x]);
        }
        return Memory.New(new CollMap
        {
            dwPosGameX = (uint)gameX,
            dwPosGameY = (uint)gameY,
            dwSizeGameX = (uint)w,
            dwSizeGameY = (uint)rows.Length,
            pMapStart = cells,
            pMapEnd = cells + (uint)(rows.Length * w * 2),
        });
    }

    public uint AddRoom1(uint coll) => Memory.New(new Room1 { Coll = coll });

    /// <summary>Builds a linked list of preset units, returns its head.</summary>
    public uint AddPresets(params (uint Type, uint TxtFileNo, uint X, uint Y)[] presets)
    {
        uint next = 0;
        for (var i = presets.Length - 1; i >= 0; i--)
        {
            var p = presets[i];
            next = Memory.New(new PresetUnit { dwType = p.Type, dwTxtFileNo = p.TxtFileNo, dwPosX = p.X, dwPosY = p.Y, pPresetNext = next });
        }
        return next;
    }
}

/// <summary>Builds a <see cref="GeneratorContext"/> over a <see cref="FakeGame"/> capturing stdout/stderr.</summary>
public sealed class Harness
{
    public FakeGame Game { get; } = new();
    public MemoryStream Stdout { get; } = new();
    public MemoryStream Stderr { get; } = new();
    public FixedClock Clock { get; } = new();
    public FakeFiles Files { get; }
    public GeneratorContext Context { get; }

    public Harness(params string[] existingFiles)
        : this(false, existingFiles)
    {
    }

    /// <param name="textMode">msvcrt text-mode stdio, as the Windows exe uses.</param>
    /// <param name="existingFiles">Files the fake file probe reports as present.</param>
    public Harness(bool textMode, params string[] existingFiles)
    {
        Files = new FakeFiles(existingFiles);
        Context = new GeneratorContext(Stdout, Stderr, Clock, Game, Game.Memory, Game, Files, "v-test", "abc123", textMode);
    }

    public string Out => System.Text.Encoding.Latin1.GetString(Stdout.ToArray());

    public string Err => System.Text.Encoding.Latin1.GetString(Stderr.ToArray());

    public Logger Log => Context.Log;
}

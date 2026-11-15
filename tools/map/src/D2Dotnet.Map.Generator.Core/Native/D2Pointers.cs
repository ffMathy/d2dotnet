// Port of upstream map/d2_ptrs.h (blacha/diablo2, MIT, (c) 2021 Blayne Chard).
#pragma warning disable CA1707 // names mirror the C globals
namespace D2Dotnet.Map.Generator.Native;

/// <summary>DLL numbers, the <c>DLLNO_*</c> enum. Only the first 12 have a name in <c>dlls[]</c>.</summary>
public enum D2Dll
{
    D2CLIENT = 0,
    D2COMMON = 1,
    D2GFX = 2,
    D2LANG = 3,
    D2WIN = 4,
    D2NET = 5,
    D2GAME = 6,
    D2LAUNCH = 7,
    FOG = 8,
    BNCLIENT = 9,
    STORM = 10,
    D2CMP = 11,
    D2MULTI = 12,
    D2SOUND = 13,
}

/// <summary>Which macro declared the pointer.</summary>
public enum D2PointerKind
{
    /// <summary><c>FUNCPTR</c>: a function pointer.</summary>
    Function,
    /// <summary><c>VARPTR</c>: a pointer to a variable inside the DLL (<c>p_</c> prefix in C).</summary>
    Variable,
    /// <summary><c>ASMPTR</c>: a raw address (DWORD) passed to the game as data.</summary>
    Asm,
}

/// <summary>Calling convention of a <c>FUNCPTR</c>.</summary>
public enum D2CallingConvention
{
    None,
    Stdcall,
    Fastcall,
}

/// <summary>
/// Every pointer declared in d2_ptrs.h, in declaration order. <c>DefineOffsets</c> walks the
/// globals from <c>_D2PTRS_START</c> (FOG_10021) to <c>_D2PTRS_END</c> (D2COMMON_10943)
/// inclusive in exactly this order, so the order (and therefore the DLL load order) matters.
/// </summary>
public enum D2Ptr
{
    // Shared between versions
    FOG_10021,
    D2COMMON_LoadAct,
    FOG_10019,
    FOG_10101,
    FOG_10089,
    FOG_10218,

    // Path of diablo specific
    D2CLIENT_Pod_InitGameMisc_I,
    STORM_Pod_MPQHashTable,
    D2CLIENT_Pod_LoadAct_1,
    D2CLIENT_Pod_LoadAct_2,
    D2COMMON_Pod_AddRoomData,
    D2COMMON_Pod_RemoveRoomData,
    D2COMMON_Pod_GetLevel,
    D2COMMON_Pod_InitDataTables,
    D2COMMON_Pod_InitLevel,
    D2COMMON_Pod_LoadAct,
    D2COMMON_Pod_UnloadAct,
    D2COMMON_Pod_GetLevelText,
    D2COMMON_Pod_GetObjectTxt,
    D2COMMON_10081,
    D2LANG_10009,
    D2WIN_10174,
    D2WIN_10072,

    // Project Diablo 2 Specific / v1.13c
    D2CLIENT_Pd2_InitGameMisc_I,
    STORM_Pd2_MPQHashTable,
    D2CLIENT_Pd2_LoadAct_1,
    D2CLIENT_Pd2_LoadAct_2,
    D2COMMON_Pd2_AddRoomData,
    D2COMMON_Pd2_RemoveRoomData,
    D2COMMON_Pd2_GetLevel,
    D2COMMON_Pd2_InitLevel,
    D2COMMON_Pd2_LoadAct,
    D2COMMON_Pd2_UnloadAct,
    D2COMMON_Pd2_GetLevelText,
    D2COMMON_Pd2_GetObjectTxt,
    D2COMMON_Pd2_InitDataTables,
    D2WIN_10086,
    D2WIN_10005,
    D2LANG_10008,
    D2COMMON_10943,
}

/// <summary>One d2_ptrs.h declaration.</summary>
/// <param name="Ptr">The global it declares.</param>
/// <param name="Dll">DLL the offset is relative to.</param>
/// <param name="Offset">Positive: offset from the module base. Negative: export ordinal (negated).</param>
/// <param name="Kind">FUNCPTR / VARPTR / ASMPTR.</param>
/// <param name="Convention">Calling convention for functions.</param>
/// <param name="ArgCount">Number of DWORD-sized arguments of a function.</param>
/// <param name="Signature">The C signature, for documentation.</param>
public sealed record D2PointerDef(D2Ptr Ptr, D2Dll Dll, int Offset, D2PointerKind Kind, D2CallingConvention Convention, int ArgCount, string Signature)
{
    /// <summary>The value the C global is initialised with: <c>DLLOFFSET(dll, offset) = DLLNO | (offset &lt;&lt; 8)</c>.</summary>
    public int Encoded => D2Pointers.DllOffset(Dll, Offset);

    /// <summary>The C global's name (<c>p_</c> prefix for VARPTR).</summary>
    public string CName => Kind == D2PointerKind.Variable ? "p_" + Ptr : Ptr.ToString();
}

/// <summary>The d2_ptrs.h table and the <c>dlls[]</c> name table from offset.c.</summary>
public static class D2Pointers
{
    /// <summary><c>dlls[]</c> from offset.c (indexed by <see cref="D2Dll"/>; 12 entries).</summary>
    public static IReadOnlyList<string> DllNames { get; } =
    [
        "D2Client.DLL",
        "D2Common.DLL",
        "D2Gfx.DLL",
        "D2Lang.DLL",
        "D2Win.DLL",
        "D2Net.DLL",
        "D2Game.DLL",
        "D2Launch.DLL",
        "Fog.DLL",
        "BNClient.DLL",
        "Storm.DLL",
        "D2Cmp.DLL",
    ];

    /// <summary><c>DLLOFFSET(a1, b1) ((DLLNO_##a1) | ((b1) &lt;&lt; 8))</c>.</summary>
    public static int DllOffset(D2Dll dll, int offset) => (int)dll | (offset << 8);

    private const D2PointerKind F = D2PointerKind.Function;
    private const D2CallingConvention Std = D2CallingConvention.Stdcall;
    private const D2CallingConvention Fast = D2CallingConvention.Fastcall;

    private static D2PointerDef Fn(D2Ptr p, D2Dll dll, int offset, D2CallingConvention cc, int args, string sig) => new(p, dll, offset, F, cc, args, sig);

    /// <summary>All declarations in d2_ptrs.h order.</summary>
    public static IReadOnlyList<D2PointerDef> All { get; } =
    [
        // Shared between versions
        Fn(D2Ptr.FOG_10021, D2Dll.FOG, -10021, Fast, 1, "VOID __fastcall (CHAR * szProg)"),
        Fn(D2Ptr.D2COMMON_LoadAct, D2Dll.D2COMMON, 0x24810, Std, 9, "Act *__stdcall (DWORD ActNumber, DWORD MapId, DWORD Unk, DWORD Unk_2, DWORD Unk_3, DWORD Unk_4, DWORD TownLevelId, DWORD Func_1, DWORD Func_2)"),
        Fn(D2Ptr.FOG_10019, D2Dll.FOG, -10019, Fast, 4, "DWORD __fastcall (CHAR * szAppName, DWORD pExceptionHandler, CHAR *szTitle, DWORD _1)"),
        Fn(D2Ptr.FOG_10101, D2Dll.FOG, -10101, Fast, 2, "DWORD __fastcall (DWORD _1, DWORD _2)"),
        Fn(D2Ptr.FOG_10089, D2Dll.FOG, -10089, Fast, 1, "DWORD __fastcall (DWORD _1)"),
        Fn(D2Ptr.FOG_10218, D2Dll.FOG, -10218, Fast, 0, "DWORD __fastcall (VOID)"),

        // Path of diablo specific
        Fn(D2Ptr.D2CLIENT_Pod_InitGameMisc_I, D2Dll.D2CLIENT, 0x4454B, Std, 3, "VOID __stdcall (DWORD Dummy1, DWORD Dummy2, DWORD Dummy3)"),
        new(D2Ptr.STORM_Pod_MPQHashTable, D2Dll.STORM, 0x52A60, D2PointerKind.Variable, D2CallingConvention.None, 0, "DWORD"),
        new(D2Ptr.D2CLIENT_Pod_LoadAct_1, D2Dll.D2CLIENT, 0x737F0, D2PointerKind.Asm, D2CallingConvention.None, 0, "DWORD"),
        new(D2Ptr.D2CLIENT_Pod_LoadAct_2, D2Dll.D2CLIENT, 0x2B420, D2PointerKind.Asm, D2CallingConvention.None, 0, "DWORD"),
        Fn(D2Ptr.D2COMMON_Pod_AddRoomData, D2Dll.D2COMMON, 0x24990, Std, 5, "void __stdcall (Act * ptAct, int LevelId, int Xpos, int Ypos, Room1 *pRoom)"),
        Fn(D2Ptr.D2COMMON_Pod_RemoveRoomData, D2Dll.D2COMMON, 0x24930, Std, 5, "void __stdcall (Act * ptAct, int LevelId, int Xpos, int Ypos, Room1 *pRoom)"),
        Fn(D2Ptr.D2COMMON_Pod_GetLevel, D2Dll.D2COMMON, 0x6D440, Fast, 2, "Level *__fastcall (ActMisc * pMisc, DWORD dwLevelNo)"),
        Fn(D2Ptr.D2COMMON_Pod_InitDataTables, D2Dll.D2COMMON, -10081, Std, 3, "DWORD __stdcall (DWORD _1, DWORD _2, DWORD _3)"),
        Fn(D2Ptr.D2COMMON_Pod_InitLevel, D2Dll.D2COMMON, 0x6DDF0, Std, 1, "void __stdcall (Level * pLevel)"),
        Fn(D2Ptr.D2COMMON_Pod_LoadAct, D2Dll.D2COMMON, 0x24810, Std, 9, "Act *__stdcall (DWORD ActNumber, DWORD MapId, DWORD Unk, DWORD Unk_2, DWORD Unk_3, DWORD Unk_4, DWORD TownLevelId, DWORD Func_1, DWORD Func_2)"),
        Fn(D2Ptr.D2COMMON_Pod_UnloadAct, D2Dll.D2COMMON, 0x24590, Std, 1, "VOID __stdcall (Act * pAct)"),
        Fn(D2Ptr.D2COMMON_Pod_GetLevelText, D2Dll.D2COMMON, 0x30CA0, Std, 1, "LevelTxt *__stdcall (DWORD levelno)"),
        Fn(D2Ptr.D2COMMON_Pod_GetObjectTxt, D2Dll.D2COMMON, 0x1ADC0, Std, 1, "ObjectTxt *__stdcall (DWORD objno)"),
        Fn(D2Ptr.D2COMMON_10081, D2Dll.D2COMMON, -10081, Std, 3, "DWORD __stdcall (DWORD _1, DWORD _2, DWORD _3)"),
        Fn(D2Ptr.D2LANG_10009, D2Dll.D2LANG, -10009, Fast, 3, "DWORD __fastcall (DWORD _1, CHAR *_2, DWORD _3)"),
        Fn(D2Ptr.D2WIN_10174, D2Dll.D2WIN, -10174, Fast, 0, "DWORD __fastcall (VOID)"),
        Fn(D2Ptr.D2WIN_10072, D2Dll.D2WIN, -10072, Fast, 4, "DWORD __fastcall (DWORD _1, DWORD _2, DWORD _3, d2client_struct *pD2Client)"),

        // Project Diablo 2 Specific / v1.13c
        Fn(D2Ptr.D2CLIENT_Pd2_InitGameMisc_I, D2Dll.D2CLIENT, 0x4454B, Std, 3, "VOID __stdcall (DWORD Dummy1, DWORD Dummy2, DWORD Dummy3)"),
        new(D2Ptr.STORM_Pd2_MPQHashTable, D2Dll.STORM, 0x53120, D2PointerKind.Variable, D2CallingConvention.None, 0, "DWORD"),
        new(D2Ptr.D2CLIENT_Pd2_LoadAct_1, D2Dll.D2CLIENT, 0x62AA0, D2PointerKind.Asm, D2CallingConvention.None, 0, "DWORD"),
        new(D2Ptr.D2CLIENT_Pd2_LoadAct_2, D2Dll.D2CLIENT, 0x62760, D2PointerKind.Asm, D2CallingConvention.None, 0, "DWORD"),
        Fn(D2Ptr.D2COMMON_Pd2_AddRoomData, D2Dll.D2COMMON, -10401, Std, 5, "void __stdcall (Act * ptAct, int LevelId, int Xpos, int Ypos, Room1 *pRoom)"),
        Fn(D2Ptr.D2COMMON_Pd2_RemoveRoomData, D2Dll.D2COMMON, -11099, Std, 5, "void __stdcall (Act * ptAct, int LevelId, int Xpos, int Ypos, Room1 *pRoom)"),
        Fn(D2Ptr.D2COMMON_Pd2_GetLevel, D2Dll.D2COMMON, -10207, Fast, 2, "Level *__fastcall (ActMisc * pMisc, DWORD dwLevelNo)"),
        Fn(D2Ptr.D2COMMON_Pd2_InitLevel, D2Dll.D2COMMON, -10322, Std, 1, "void __stdcall (Level * pLevel)"),
        Fn(D2Ptr.D2COMMON_Pd2_LoadAct, D2Dll.D2COMMON, 0x3CB30, Std, 9, "Act *__stdcall (DWORD ActNumber, DWORD MapId, DWORD Unk, DWORD Unk_2, DWORD Unk_3, DWORD Unk_4, DWORD TownLevelId, DWORD Func_1, DWORD Func_2)"),
        Fn(D2Ptr.D2COMMON_Pd2_UnloadAct, D2Dll.D2COMMON, -10868, Std, 1, "VOID __stdcall (Act * pAct)"),
        Fn(D2Ptr.D2COMMON_Pd2_GetLevelText, D2Dll.D2COMMON, -10014, Std, 1, "LevelTxt *__stdcall (DWORD levelno)"),
        Fn(D2Ptr.D2COMMON_Pd2_GetObjectTxt, D2Dll.D2COMMON, -10688, Std, 1, "ObjectTxt *__stdcall (DWORD objno)"),
        Fn(D2Ptr.D2COMMON_Pd2_InitDataTables, D2Dll.D2COMMON, -10943, Std, 3, "DWORD __stdcall (DWORD _1, DWORD _2, DWORD _3)"),
        Fn(D2Ptr.D2WIN_10086, D2Dll.D2WIN, -10086, Fast, 0, "DWORD __fastcall (VOID)"),
        Fn(D2Ptr.D2WIN_10005, D2Dll.D2WIN, -10005, Fast, 4, "DWORD __fastcall (DWORD _1, DWORD _2, DWORD _3, d2client_struct *pD2Client)"),
        Fn(D2Ptr.D2LANG_10008, D2Dll.D2LANG, -10008, Fast, 3, "DWORD __fastcall (DWORD _1, CHAR *_2, DWORD _3)"),
        Fn(D2Ptr.D2COMMON_10943, D2Dll.D2COMMON, -10943, Std, 3, "DWORD __stdcall (DWORD _1, DWORD _2, DWORD _3)"),
    ];

    /// <summary>Lookup by pointer.</summary>
    public static D2PointerDef Get(D2Ptr ptr) => All[(int)ptr];

    /// <summary>
    /// Hard-coded jump target of the Path of Diablo <c>D2CLIENT_Pod_InitGameMisc</c> stub
    /// (<c>D2CLIENT_Pod_InitGameMisc_I_P</c>; upstream README: "the address is hardcoded").
    /// </summary>
    public const uint D2CLIENT_Pod_InitGameMisc_I_P = 0x6faf559b;

    /// <summary>Hard-coded jump target of the PD2 / 1.13c <c>D2CLIENT_Pd2_InitGameMisc</c> stub.</summary>
    public const uint D2CLIENT_Pd2_InitGameMisc_I_P = 0x6faf454b;

    /// <summary>Preferred image base of D2Client.dll (1.13c), which the hard-coded jump targets assume.</summary>
    public const uint D2ClientPreferredBase = 0x6FAB0000;

    /// <summary>
    /// D2Client.dll's actual base, derived from a resolved base-relative D2Client pointer
    /// (<c>D2CLIENT_Pd2_LoadAct_1 = base + 0x62AA0</c>).
    /// </summary>
    public static uint D2ClientBase(D2Addresses addresses) =>
        unchecked(addresses[D2Ptr.D2CLIENT_Pd2_LoadAct_1] - (uint)Get(D2Ptr.D2CLIENT_Pd2_LoadAct_1).Offset);

    /// <summary>
    /// The magic-jump target for the loaded D2Client.dll: the hard-coded C address when the DLL is
    /// at its preferred base (the normal case, identical to upstream), otherwise the same offset
    /// rebased onto the actual base (where the C program would jump into unmapped memory).
    /// </summary>
    public static uint MagicJumpTarget(uint hardCoded, uint d2ClientBase) =>
        unchecked(hardCoded - D2ClientPreferredBase + d2ClientBase);
}

/// <summary>The resolved values of all d2_ptrs.h globals after <c>DefineOffsets</c>.</summary>
public sealed class D2Addresses
{
    private readonly uint[] _values;

    public D2Addresses(uint[] values)
    {
        if (values.Length != D2Pointers.All.Count) throw new ArgumentException("one value per pointer required", nameof(values));
        _values = values;
    }

    public uint this[D2Ptr ptr] => _values[(int)ptr];
}

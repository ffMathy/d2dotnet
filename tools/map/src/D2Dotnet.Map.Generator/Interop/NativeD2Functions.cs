namespace D2Dotnet.Map.Generator.Interop;

/// <summary>
/// Calls into the D2 DLLs.
/// <list type="bullet">
/// <item><c>__stdcall</c> exports are called directly through <c>delegate* unmanaged[Stdcall]</c>
/// at the resolved address.</item>
/// <item><c>__fastcall</c> exports (unsupported by the .NET runtime on x86) are called through a
/// generated stdcall-to-fastcall bridge (<see cref="X86Thunks.FastcallBridge"/>), itself
/// invoked as <c>delegate* unmanaged[Stdcall]</c>.</item>
/// <item>The naked magic-jump stubs are generated with <see cref="X86Thunks.MagicJump"/> and
/// invoked as a zero-argument <c>delegate* unmanaged[Cdecl]</c>.</item>
/// </list>
/// </summary>
internal sealed unsafe class NativeD2Functions : ID2Functions
{
    private readonly D2Addresses _a;
    private readonly Dictionary<D2Ptr, nint> _fastcallBridges = [];
    private readonly nint _podInitGameMisc;
    private readonly nint _pd2InitGameMisc;

    public NativeD2Functions(D2Addresses addresses, ExecutableMemory exec)
    {
        _a = addresses;
        foreach (var def in D2Pointers.All)
        {
            if (def.Kind == D2PointerKind.Function && def.Convention == D2CallingConvention.Fastcall)
            {
                _fastcallBridges[def.Ptr] = exec.Emit(X86Thunks.FastcallBridge(addresses[def.Ptr], def.ArgCount));
            }
        }

        // The magic jumps use the hard-coded targets of the C inline asm (0x6faf559b / 0x6faf454b);
        // they are only rebased if D2Client.dll did not load at its preferred base 0x6FAB0000.
        var d2ClientBase = D2Pointers.D2ClientBase(addresses);
        _podInitGameMisc = exec.Reserve(X86Thunks.MagicJumpSize);
        ExecutableMemory.Write(_podInitGameMisc, X86Thunks.MagicJump((uint)_podInitGameMisc, D2Pointers.MagicJumpTarget(D2Pointers.D2CLIENT_Pod_InitGameMisc_I_P, d2ClientBase)));
        _pd2InitGameMisc = exec.Reserve(X86Thunks.MagicJumpSize);
        ExecutableMemory.Write(_pd2InitGameMisc, X86Thunks.MagicJump((uint)_pd2InitGameMisc, D2Pointers.MagicJumpTarget(D2Pointers.D2CLIENT_Pd2_InitGameMisc_I_P, d2ClientBase)));
    }

    /// <summary>A resolved stdcall export.</summary>
    private nint Std(D2Ptr p) => (nint)(nuint)_a[p];

    /// <summary>The bridge for a fastcall export.</summary>
    private nint Fast(D2Ptr p) => _fastcallBridges[p];

    // ---- fastcall (through bridges) ----
    public void FOG_10021(uint szProg) => ((delegate* unmanaged[Stdcall]<uint, void>)Fast(D2Ptr.FOG_10021))(szProg);
    public uint FOG_10019(uint szAppName, uint pExceptionHandler, uint szTitle, uint _1) => ((delegate* unmanaged[Stdcall]<uint, uint, uint, uint, uint>)Fast(D2Ptr.FOG_10019))(szAppName, pExceptionHandler, szTitle, _1);
    public uint FOG_10101(uint _1, uint _2) => ((delegate* unmanaged[Stdcall]<uint, uint, uint>)Fast(D2Ptr.FOG_10101))(_1, _2);
    public uint FOG_10089(uint _1) => ((delegate* unmanaged[Stdcall]<uint, uint>)Fast(D2Ptr.FOG_10089))(_1);
    public uint FOG_10218() => ((delegate* unmanaged[Stdcall]<uint>)Fast(D2Ptr.FOG_10218))();
    public uint D2LANG_10009(uint _1, uint _2, uint _3) => ((delegate* unmanaged[Stdcall]<uint, uint, uint, uint>)Fast(D2Ptr.D2LANG_10009))(_1, _2, _3);
    public uint D2LANG_10008(uint _1, uint _2, uint _3) => ((delegate* unmanaged[Stdcall]<uint, uint, uint, uint>)Fast(D2Ptr.D2LANG_10008))(_1, _2, _3);
    public uint D2WIN_10174() => ((delegate* unmanaged[Stdcall]<uint>)Fast(D2Ptr.D2WIN_10174))();
    public uint D2WIN_10086() => ((delegate* unmanaged[Stdcall]<uint>)Fast(D2Ptr.D2WIN_10086))();
    public uint D2WIN_10072(uint _1, uint _2, uint _3, uint pD2Client) => ((delegate* unmanaged[Stdcall]<uint, uint, uint, uint, uint>)Fast(D2Ptr.D2WIN_10072))(_1, _2, _3, pD2Client);
    public uint D2WIN_10005(uint _1, uint _2, uint _3, uint pD2Client) => ((delegate* unmanaged[Stdcall]<uint, uint, uint, uint, uint>)Fast(D2Ptr.D2WIN_10005))(_1, _2, _3, pD2Client);
    public uint D2COMMON_Pod_GetLevel(uint pMisc, uint dwLevelNo) => ((delegate* unmanaged[Stdcall]<uint, uint, uint>)Fast(D2Ptr.D2COMMON_Pod_GetLevel))(pMisc, dwLevelNo);
    public uint D2COMMON_Pd2_GetLevel(uint pMisc, uint dwLevelNo) => ((delegate* unmanaged[Stdcall]<uint, uint, uint>)Fast(D2Ptr.D2COMMON_Pd2_GetLevel))(pMisc, dwLevelNo);

    // ---- naked magic jumps ----
    public void D2CLIENT_Pod_InitGameMisc() => ((delegate* unmanaged[Cdecl]<void>)_podInitGameMisc)();
    public void D2CLIENT_Pd2_InitGameMisc() => ((delegate* unmanaged[Cdecl]<void>)_pd2InitGameMisc)();

    // ---- stdcall (direct) ----
    public uint D2COMMON_Pod_InitDataTables(uint _1, uint _2, uint _3) => ((delegate* unmanaged[Stdcall]<uint, uint, uint, uint>)Std(D2Ptr.D2COMMON_Pod_InitDataTables))(_1, _2, _3);
    public uint D2COMMON_Pd2_InitDataTables(uint _1, uint _2, uint _3) => ((delegate* unmanaged[Stdcall]<uint, uint, uint, uint>)Std(D2Ptr.D2COMMON_Pd2_InitDataTables))(_1, _2, _3);

    public uint D2COMMON_Pod_LoadAct(uint actNumber, uint mapId, uint unk, uint unk2, uint unk3, uint unk4, uint townLevelId, uint func1, uint func2) =>
        ((delegate* unmanaged[Stdcall]<uint, uint, uint, uint, uint, uint, uint, uint, uint, uint>)Std(D2Ptr.D2COMMON_Pod_LoadAct))(actNumber, mapId, unk, unk2, unk3, unk4, townLevelId, func1, func2);
    public uint D2COMMON_Pd2_LoadAct(uint actNumber, uint mapId, uint unk, uint unk2, uint unk3, uint unk4, uint townLevelId, uint func1, uint func2) =>
        ((delegate* unmanaged[Stdcall]<uint, uint, uint, uint, uint, uint, uint, uint, uint, uint>)Std(D2Ptr.D2COMMON_Pd2_LoadAct))(actNumber, mapId, unk, unk2, unk3, unk4, townLevelId, func1, func2);

    public void D2COMMON_Pod_UnloadAct(uint pAct) => ((delegate* unmanaged[Stdcall]<uint, void>)Std(D2Ptr.D2COMMON_Pod_UnloadAct))(pAct);
    public void D2COMMON_Pd2_UnloadAct(uint pAct) => ((delegate* unmanaged[Stdcall]<uint, void>)Std(D2Ptr.D2COMMON_Pd2_UnloadAct))(pAct);

    public uint D2COMMON_Pod_GetLevelText(uint levelno) => ((delegate* unmanaged[Stdcall]<uint, uint>)Std(D2Ptr.D2COMMON_Pod_GetLevelText))(levelno);
    public uint D2COMMON_Pd2_GetLevelText(uint levelno) => ((delegate* unmanaged[Stdcall]<uint, uint>)Std(D2Ptr.D2COMMON_Pd2_GetLevelText))(levelno);

    public uint D2COMMON_Pod_GetObjectTxt(uint objno) => ((delegate* unmanaged[Stdcall]<uint, uint>)Std(D2Ptr.D2COMMON_Pod_GetObjectTxt))(objno);
    public uint D2COMMON_Pd2_GetObjectTxt(uint objno) => ((delegate* unmanaged[Stdcall]<uint, uint>)Std(D2Ptr.D2COMMON_Pd2_GetObjectTxt))(objno);

    public void D2COMMON_Pod_InitLevel(uint pLevel) => ((delegate* unmanaged[Stdcall]<uint, void>)Std(D2Ptr.D2COMMON_Pod_InitLevel))(pLevel);
    public void D2COMMON_Pd2_InitLevel(uint pLevel) => ((delegate* unmanaged[Stdcall]<uint, void>)Std(D2Ptr.D2COMMON_Pd2_InitLevel))(pLevel);

    public void D2COMMON_Pod_AddRoomData(uint ptAct, int levelId, int xpos, int ypos, uint pRoom) =>
        ((delegate* unmanaged[Stdcall]<uint, int, int, int, uint, void>)Std(D2Ptr.D2COMMON_Pod_AddRoomData))(ptAct, levelId, xpos, ypos, pRoom);
    public void D2COMMON_Pd2_AddRoomData(uint ptAct, int levelId, int xpos, int ypos, uint pRoom) =>
        ((delegate* unmanaged[Stdcall]<uint, int, int, int, uint, void>)Std(D2Ptr.D2COMMON_Pd2_AddRoomData))(ptAct, levelId, xpos, ypos, pRoom);
    public void D2COMMON_Pod_RemoveRoomData(uint ptAct, int levelId, int xpos, int ypos, uint pRoom) =>
        ((delegate* unmanaged[Stdcall]<uint, int, int, int, uint, void>)Std(D2Ptr.D2COMMON_Pod_RemoveRoomData))(ptAct, levelId, xpos, ypos, pRoom);
    public void D2COMMON_Pd2_RemoveRoomData(uint ptAct, int levelId, int xpos, int ypos, uint pRoom) =>
        ((delegate* unmanaged[Stdcall]<uint, int, int, int, uint, void>)Std(D2Ptr.D2COMMON_Pd2_RemoveRoomData))(ptAct, levelId, xpos, ypos, pRoom);
}

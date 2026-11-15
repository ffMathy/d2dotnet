using System.Diagnostics.CodeAnalysis;
using System.Text;
using D2Dotnet.Map.Generator.Native;

namespace D2Dotnet.Map.Generator.Game;

/// <summary>
/// Access to the 32-bit address space the D2 DLLs live in. On win-x86 this dereferences raw
/// pointers; tests use a fake heap. Every read is a fresh read (the C code is built with -O0
/// and never caches fields), so callers re-read structures after calling into the game.
/// </summary>
public interface IGameMemory
{
    T Read<T>(uint address) where T : unmanaged;

    void Write<T>(uint address, T value) where T : unmanaged;
}

/// <summary>Helpers over <see cref="IGameMemory"/>.</summary>
public static class GameMemoryExtensions
{
    /// <summary>Reads a NUL-terminated byte string (no length limit, like <c>printf("%s")</c>), decoded as Latin-1.</summary>
    public static string ReadCString(this IGameMemory memory, uint address)
    {
        var bytes = new List<byte>();
        for (var a = address; ; a++)
        {
            var b = memory.Read<byte>(a);
            if (b == 0) break;
            bytes.Add(b);
        }
        return Encoding.Latin1.GetString([.. bytes]);
    }

    /// <summary><c>memset(address, value, size)</c>.</summary>
    public static void Fill(this IGameMemory memory, uint address, byte value, int size)
    {
        for (var i = 0; i < size; i++) memory.Write(address + (uint)i, value);
    }
}

/// <summary>
/// The raw D2 exports used by the program (the <c>FUNCPTR</c> globals of d2_ptrs.h that are
/// actually called, plus the two naked magic-jump stubs). Pointers are 32-bit addresses.
/// Implemented with function pointers and x86 thunks on win-x86, faked in tests.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "Names mirror the C globals")]
public interface ID2Functions
{
    // Shared (fastcall)
    void FOG_10021(uint szProg);
    uint FOG_10019(uint szAppName, uint pExceptionHandler, uint szTitle, uint _1);
    uint FOG_10101(uint _1, uint _2);
    uint FOG_10089(uint _1);
    uint FOG_10218();

    // Path of Diablo
    uint D2COMMON_Pod_InitDataTables(uint _1, uint _2, uint _3);
    uint D2LANG_10009(uint _1, uint _2, uint _3);
    uint D2WIN_10174();
    uint D2WIN_10072(uint _1, uint _2, uint _3, uint pD2Client);
    void D2CLIENT_Pod_InitGameMisc();
    uint D2COMMON_Pod_LoadAct(uint actNumber, uint mapId, uint unk, uint unk2, uint unk3, uint unk4, uint townLevelId, uint func1, uint func2);
    void D2COMMON_Pod_UnloadAct(uint pAct);
    uint D2COMMON_Pod_GetLevelText(uint levelno);
    uint D2COMMON_Pod_GetObjectTxt(uint objno);
    void D2COMMON_Pod_InitLevel(uint pLevel);
    void D2COMMON_Pod_AddRoomData(uint ptAct, int levelId, int xpos, int ypos, uint pRoom);
    void D2COMMON_Pod_RemoveRoomData(uint ptAct, int levelId, int xpos, int ypos, uint pRoom);
    uint D2COMMON_Pod_GetLevel(uint pMisc, uint dwLevelNo);

    // Project Diablo 2 / 1.13c
    uint D2COMMON_Pd2_InitDataTables(uint _1, uint _2, uint _3);
    uint D2LANG_10008(uint _1, uint _2, uint _3);
    uint D2WIN_10086();
    uint D2WIN_10005(uint _1, uint _2, uint _3, uint pD2Client);
    void D2CLIENT_Pd2_InitGameMisc();
    uint D2COMMON_Pd2_LoadAct(uint actNumber, uint mapId, uint unk, uint unk2, uint unk3, uint unk4, uint townLevelId, uint func1, uint func2);
    void D2COMMON_Pd2_UnloadAct(uint pAct);
    uint D2COMMON_Pd2_GetLevelText(uint levelno);
    uint D2COMMON_Pd2_GetObjectTxt(uint objno);
    void D2COMMON_Pd2_InitLevel(uint pLevel);
    void D2COMMON_Pd2_AddRoomData(uint ptAct, int levelId, int xpos, int ypos, uint pRoom);
    void D2COMMON_Pd2_RemoveRoomData(uint ptAct, int levelId, int xpos, int ypos, uint pRoom);
    uint D2COMMON_Pd2_GetLevel(uint pMisc, uint dwLevelNo);
}

/// <summary>Operating-system services used by d2_client.c and main.c.</summary>
public interface IGamePlatform
{
    /// <summary><c>RegOpenKeyEx(HKEY_CURRENT_USER, subKey, 0, KEY_ALL_ACCESS, &amp;hKey) == ERROR_SUCCESS</c>.</summary>
    bool RegOpenCurrentUserKey(string subKey, out nint hKey);

    /// <summary><c>RegSetValueEx(hKey, name, 0, REG_SZ, data, strlen(data) + 1)</c>; the result is ignored upstream.</summary>
    int RegSetStringValue(nint hKey, string name, string data);

    /// <summary><c>RegCloseKey</c>.</summary>
    void RegCloseKey(nint hKey);

    /// <summary><c>SetCurrentDirectoryA</c>.</summary>
    void SetCurrentDirectory(string path);

    /// <summary><c>ExitProcess</c>: terminates immediately without flushing stdio buffers.</summary>
    [DoesNotReturn]
    void ExitProcess(uint exitCode);

    /// <summary>C <c>exit()</c>: the caller has flushed stdio; terminates the process.</summary>
    [DoesNotReturn]
    void Exit(int exitCode);

    /// <summary>Zero-initialised static storage that lives for the whole process (the C globals).</summary>
    uint AllocateStatic(int size);

    /// <summary>A NUL-terminated ANSI copy of a C string literal that lives for the whole process.</summary>
    uint AllocateAnsiString(string value);

    /// <summary>
    /// Address of <c>DWORD D2ClientInterface(VOID)</c> (cdecl) which returns the <c>dwInit</c> field
    /// of the d2client_struct at <paramref name="d2ClientAddress"/>.
    /// </summary>
    uint D2ClientInterfaceCallback(uint d2ClientAddress);

    /// <summary>Address of <c>VOID __stdcall ExceptionHandler(VOID)</c>.</summary>
    uint ExceptionHandlerCallback();

    /// <summary>Resolves addresses into callable D2 functions once <c>DefineOffsets</c> ran.</summary>
    ID2Functions BindFunctions(D2Addresses addresses);
}

/// <summary>File probing used by game version detection (<c>std::ifstream</c> open check).</summary>
public interface IFileProbe
{
    /// <summary>True when the file can be opened for reading.</summary>
    bool CanOpen(string path);
}

/// <summary>Default <see cref="IFileProbe"/>.</summary>
public sealed class FileProbe : IFileProbe
{
    public bool CanOpen(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}

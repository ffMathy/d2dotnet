using System.Runtime.InteropServices;

namespace D2Dotnet.Map.Generator.Interop;

/// <summary>
/// The Win32 calls the C program makes. The C code is compiled without UNICODE, so the ANSI
/// ("A") entry points are used and strings are passed as NUL-terminated ANSI bytes.
/// </summary>
internal static unsafe partial class Win32
{
    public const uint HKEY_CURRENT_USER = 0x80000001;
    public const uint KEY_ALL_ACCESS = 0xF003F;
    public const uint REG_SZ = 1;
    public const int ERROR_SUCCESS = 0;

    public const uint MEM_COMMIT = 0x1000;
    public const uint MEM_RESERVE = 0x2000;
    public const uint PAGE_EXECUTE_READWRITE = 0x40;

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleA")]
    public static partial nint GetModuleHandleA(byte* lpModuleName);

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryA")]
    public static partial nint LoadLibraryA(byte* lpLibFileName);

    /// <summary><c>GetProcAddress(hMod, (LPCSTR)ordinal)</c>: an ordinal is passed as a pointer value below 0x10000.</summary>
    [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress")]
    public static partial nint GetProcAddress(nint hModule, nint lpProcName);

    [LibraryImport("kernel32.dll", EntryPoint = "SetCurrentDirectoryA")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCurrentDirectoryA(byte* lpPathName);

    [LibraryImport("kernel32.dll", EntryPoint = "ExitProcess")]
    public static partial void ExitProcess(uint uExitCode);

    [LibraryImport("kernel32.dll", EntryPoint = "VirtualAlloc")]
    public static partial nint VirtualAlloc(nint lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    [LibraryImport("kernel32.dll", EntryPoint = "FlushInstructionCache")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FlushInstructionCache(nint hProcess, nint lpBaseAddress, nuint dwSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", EntryPoint = "RegOpenKeyExA")]
    public static partial int RegOpenKeyExA(nint hKey, byte* lpSubKey, uint ulOptions, uint samDesired, out nint phkResult);

    [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExA")]
    public static partial int RegSetValueExA(nint hKey, byte* lpValueName, uint reserved, uint dwType, byte* lpData, uint cbData);

    [LibraryImport("advapi32.dll", EntryPoint = "RegCloseKey")]
    public static partial int RegCloseKey(nint hKey);

    /// <summary>Converts a uint (HKEY constant) to a handle without sign extension issues.</summary>
    public static nint Handle(uint value) => (nint)(nuint)value;
}

/// <summary>A temporary NUL-terminated ANSI copy of a string (system code page), freed on dispose.</summary>
internal readonly unsafe struct AnsiString : IDisposable
{
    private readonly nint _ptr;

    public AnsiString(string value) => _ptr = Marshal.StringToHGlobalAnsi(value);

    public byte* Pointer => (byte*)_ptr;

    /// <summary><c>strlen</c>.</summary>
    public uint Length
    {
        get
        {
            uint n = 0;
            while (Pointer[n] != 0) n++;
            return n;
        }
    }

    public void Dispose() => Marshal.FreeHGlobal(_ptr);
}

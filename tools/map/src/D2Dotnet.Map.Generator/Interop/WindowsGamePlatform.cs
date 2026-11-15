using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace D2Dotnet.Map.Generator.Interop;

/// <summary>Win32 implementation of the platform services and module loader used by the port.</summary>
internal sealed unsafe class WindowsGamePlatform : IGamePlatform, IModuleLoader
{
    private readonly ExecutableMemory _exec = new();

    public bool RegOpenCurrentUserKey(string subKey, out nint hKey)
    {
        using var key = new AnsiString(subKey);
        return Win32.RegOpenKeyExA(Win32.Handle(Win32.HKEY_CURRENT_USER), key.Pointer, 0, Win32.KEY_ALL_ACCESS, out hKey) == Win32.ERROR_SUCCESS;
    }

    public int RegSetStringValue(nint hKey, string name, string data)
    {
        using var n = new AnsiString(name);
        using var d = new AnsiString(data);
        return Win32.RegSetValueExA(hKey, n.Pointer, 0, Win32.REG_SZ, d.Pointer, d.Length + 1);
    }

    public void RegCloseKey(nint hKey) => Win32.RegCloseKey(hKey);

    public void SetCurrentDirectory(string path)
    {
        using var p = new AnsiString(path);
        Win32.SetCurrentDirectoryA(p.Pointer);
    }

    [DoesNotReturn]
    public void ExitProcess(uint exitCode)
    {
        Win32.ExitProcess(exitCode);
        throw new InvalidOperationException("ExitProcess returned");
    }

    [DoesNotReturn]
    public void Exit(int exitCode)
    {
        Environment.Exit(exitCode);
        throw new InvalidOperationException("Environment.Exit returned");
    }

    public uint AllocateStatic(int size) => (uint)(nuint)NativeMemory.AllocZeroed((nuint)size);

    public uint AllocateAnsiString(string value) => (uint)(nuint)Marshal.StringToHGlobalAnsi(value);

    public uint D2ClientInterfaceCallback(uint d2ClientAddress)
    {
        Callbacks.D2ClientStruct = d2ClientAddress;
        return Callbacks.D2ClientInterfaceAddress;
    }

    public uint ExceptionHandlerCallback() => Callbacks.ExceptionHandlerAddress;

    public ID2Functions BindFunctions(D2Addresses addresses) => new NativeD2Functions(addresses, _exec);

    public uint GetModuleHandle(string dllName)
    {
        using var n = new AnsiString(dllName);
        return (uint)(nuint)Win32.GetModuleHandleA(n.Pointer);
    }

    public uint LoadLibrary(string dllName)
    {
        using var n = new AnsiString(dllName);
        return (uint)(nuint)Win32.LoadLibraryA(n.Pointer);
    }

    public uint GetProcAddressByOrdinal(uint module, int ordinal) =>
        (uint)(nuint)Win32.GetProcAddress((nint)(nuint)module, ordinal);
}

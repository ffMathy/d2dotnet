// Port of the Windows parts of d2go pkg/memory/process.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace D2Dotnet.D2Go.Memory;

/// <summary>A module loaded in a process (Go <c>memory.ModuleInfo</c>).</summary>
/// <param name="ProcessId">The process the module is loaded in.</param>
/// <param name="ModuleBaseAddress">Where the module is loaded.</param>
/// <param name="ModuleBaseSize">The size of the module's image.</param>
/// <param name="ModuleHandle">The module handle (<c>HMODULE</c>).</param>
/// <param name="ModuleName">The module's full path.</param>
public readonly record struct ModuleInfo(uint ProcessId, nuint ModuleBaseAddress, uint ModuleBaseSize, nint ModuleHandle, string ModuleName)
{
    /// <summary>The game module's name (Go <c>moduleName</c>).</summary>
    public const string GameModuleName = "d2r.exe";

    /// <summary>
    /// The first module whose path, lower-cased, contains <c>d2r.exe</c> (Go <c>getMainModule</c>'s match).
    /// When there is none, returns false and <paramref name="module"/> is the zero value.
    /// </summary>
    public static bool TryFindGameModule(IEnumerable<ModuleInfo> modules, out ModuleInfo module)
    {
        ArgumentNullException.ThrowIfNull(modules);
        foreach (var m in modules)
        {
            if ((m.ModuleName ?? "").ToLowerInvariant().Contains(GameModuleName, StringComparison.Ordinal))
            {
                module = m;
                return true;
            }
        }

        module = default;
        return false;
    }
}

/// <summary>
/// Reads the memory of a Windows process with <c>ReadProcessMemory</c>: the Win32 side of Go's <c>memory.Process</c>
/// (<c>NewProcess</c>, <c>NewProcessForPID</c>, <c>GetProcessModules</c>, <c>Close</c>).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsProcessMemory : IProcessMemory
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const int MaxPath = 260;
    private const int MaxProcesses = 2048;
    private const int MaxModules = 1024;

    private readonly SafeProcessHandle _handle;

    private WindowsProcessMemory(SafeProcessHandle handle, ModuleInfo module)
    {
        _handle = handle;
        ProcessId = module.ProcessId;
        ModuleBaseAddress = module.ModuleBaseAddress;
        ModuleBaseSize = module.ModuleBaseSize;
    }

    /// <inheritdoc/>
    public uint ProcessId { get; }

    /// <inheritdoc/>
    public nuint ModuleBaseAddress { get; }

    /// <inheritdoc/>
    public uint ModuleBaseSize { get; }

    /// <summary>
    /// Opens, for reading, the first running process that has loaded <c>d2r.exe</c> (Go <c>NewProcess</c>).
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">Not running on Windows.</exception>
    /// <exception cref="Win32Exception">The process list could not be read, or the process could not be opened.</exception>
    /// <exception cref="InvalidOperationException">No process has loaded <c>d2r.exe</c>.</exception>
    public static WindowsProcessMemory OpenGameProcess()
    {
        ThrowIfNotWindows();
        var processes = new uint[MaxProcesses];
        uint length;
        unsafe
        {
            fixed (uint* p = processes)
            {
                if (!EnumProcesses(p, (uint)(processes.Length * sizeof(uint)), out length))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        var count = Math.Min(processes.Length, (int)(length / sizeof(uint)));
        for (var i = 0; i < count; i++)
        {
            if (TryGetMainModule(processes[i], out var module))
                return Open(module);
        }

        throw new InvalidOperationException($"No running process has loaded {ModuleInfo.GameModuleName}.");
    }

    /// <summary>Opens, for reading, process <paramref name="pid"/>, which must have loaded <c>d2r.exe</c> (Go <c>NewProcessForPID</c>).</summary>
    /// <exception cref="PlatformNotSupportedException">Not running on Windows.</exception>
    /// <exception cref="InvalidOperationException">The process has not loaded <c>d2r.exe</c>, or its modules could not be read.</exception>
    /// <exception cref="Win32Exception">The process could not be opened.</exception>
    public static WindowsProcessMemory OpenProcess(uint pid)
    {
        ThrowIfNotWindows();
        if (!TryGetMainModule(pid, out var module))
            throw new InvalidOperationException("no module found for the specified PID");

        return Open(module);
    }

    /// <summary>The modules loaded in process <paramref name="processId"/> (Go <c>GetProcessModules</c>).</summary>
    /// <exception cref="PlatformNotSupportedException">Not running on Windows.</exception>
    /// <exception cref="Win32Exception">The process could not be opened or its modules read.</exception>
    /// <exception cref="IndexOutOfRangeException">The process has more than 1024 modules (Go panics).</exception>
    public static IReadOnlyList<ModuleInfo> GetProcessModules(uint processId)
    {
        ThrowIfNotWindows();
        using var hProcess = OpenProcessNative(ProcessQueryInformation | ProcessVmRead, false, processId);
        if (hProcess.IsInvalid)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        var modules = new nint[MaxModules];
        uint needed;
        unsafe
        {
            fixed (nint* m = modules)
            {
                if (!EnumProcessModules(hProcess, m, (uint)(sizeof(nint) * MaxModules), out needed))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        var count = needed / (uint)nint.Size;
        var moduleInfos = new List<ModuleInfo>();
        var moduleName = new char[MaxPath];
        for (uint i = 0; i < count; i++)
        {
            var handle = modules[i];
            if (!GetModuleInformation(hProcess, handle, out var mi, (uint)Marshal.SizeOf<NativeModuleInfo>()))
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            uint nameLength;
            unsafe
            {
                fixed (char* n = moduleName)
                    nameLength = GetModuleFileNameEx(hProcess, handle, n, MaxPath);
            }

            if (nameLength == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            moduleInfos.Add(new ModuleInfo(processId, (nuint)mi.BaseOfDll, mi.SizeOfImage, handle, Utf16ToString(moduleName)));
        }

        return moduleInfos;
    }

    /// <inheritdoc/>
    public bool ReadMemory(nuint address, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (buffer.IsEmpty)
            return true;

        unsafe
        {
            fixed (byte* b = buffer)
                return ReadProcessMemory(_handle, address, b, (nuint)buffer.Length, out var read) && read == (nuint)buffer.Length;
        }
    }

    /// <summary>Closes the process handle (Go <c>Close</c>).</summary>
    public void Dispose() => _handle.Dispose();

    // Go getMainModule: false when the modules can not be listed or none is d2r.exe.
    private static bool TryGetMainModule(uint pid, out ModuleInfo module)
    {
        IReadOnlyList<ModuleInfo> modules;
        try
        {
            modules = GetProcessModules(pid);
        }
        catch (Win32Exception)
        {
            module = default;
            return false;
        }

        return ModuleInfo.TryFindGameModule(modules, out module);
    }

    private static WindowsProcessMemory Open(ModuleInfo module)
    {
        var h = OpenProcessNative(ProcessVmRead, false, module.ProcessId);
        if (h.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            h.Dispose();
            throw new Win32Exception(error);
        }

        return new WindowsProcessMemory(h, module);
    }

    // Go syscall.UTF16ToString: up to the first NUL.
    private static string Utf16ToString(ReadOnlySpan<char> s)
    {
        var nul = s.IndexOf('\0');
        return new string(nul < 0 ? s : s[..nul]);
    }

    private static void ThrowIfNotWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Reading the game process requires Windows.");
    }

    [LibraryImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcessNative(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ReadProcessMemory(SafeProcessHandle process, nuint baseAddress, byte* buffer, nuint size, out nuint numberOfBytesRead);

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumProcesses(uint* processIds, uint cb, out uint bytesReturned);

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumProcessModules(SafeProcessHandle process, nint* modules, uint cb, out uint needed);

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleInformation(SafeProcessHandle process, nint module, out NativeModuleInfo moduleInfo, uint cb);

    [LibraryImport("psapi.dll", EntryPoint = "GetModuleFileNameExW", SetLastError = true)]
    private static unsafe partial uint GetModuleFileNameEx(SafeProcessHandle process, nint module, char* fileName, uint size);
}

/// <summary>Win32 <c>MODULEINFO</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeModuleInfo
{
    public nint BaseOfDll;
    public uint SizeOfImage;
    public nint EntryPoint;
}

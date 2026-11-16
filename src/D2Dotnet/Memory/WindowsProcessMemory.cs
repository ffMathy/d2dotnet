// The Windows side of d2go pkg/memory/process.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace D2Dotnet.Memory;

/// <summary>A module loaded in a process.</summary>
/// <param name="BaseAddress">Where the module is loaded.</param>
/// <param name="Size">The size of the module's image, in bytes.</param>
/// <param name="Path">The module's full path.</param>
public readonly record struct ProcessModuleInfo(nuint BaseAddress, uint Size, string Path)
{
    /// <summary>The game module's file name.</summary>
    public const string GameModuleName = "d2r.exe";

    /// <summary>True when the module's file name is <see cref="GameModuleName"/> (ignoring case).</summary>
    public bool IsGameModule =>
        string.Equals(System.IO.Path.GetFileName((Path ?? "").Replace('\\', '/')), GameModuleName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Reads the memory of a running Windows process with <c>ReadProcessMemory</c>.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsProcessMemory : IProcessMemory
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const int MaxPath = 32767;

    private readonly SafeProcessHandle _handle;

    private WindowsProcessMemory(SafeProcessHandle handle, uint processId, ProcessModuleInfo module)
    {
        _handle = handle;
        ProcessId = processId;
        ModuleBaseAddress = module.BaseAddress;
        ModuleSize = module.Size;
    }

    /// <inheritdoc/>
    public uint ProcessId { get; }

    /// <inheritdoc/>
    public nuint ModuleBaseAddress { get; }

    /// <inheritdoc/>
    public uint ModuleSize { get; }

    /// <summary>Opens, for reading, the first running process that has loaded <c>d2r.exe</c>.</summary>
    /// <exception cref="PlatformNotSupportedException">Not running on Windows.</exception>
    /// <exception cref="Win32Exception">The process list could not be read, or the process could not be opened.</exception>
    /// <exception cref="InvalidOperationException">No process has loaded <c>d2r.exe</c>.</exception>
    public static WindowsProcessMemory OpenGame()
    {
        ThrowIfNotWindows();
        foreach (var pid in GetProcessIds())
        {
            if (TryGetGameModule(pid, out var module))
                return Open(pid, module);
        }

        throw new InvalidOperationException($"No running process has loaded {ProcessModuleInfo.GameModuleName}.");
    }

    /// <summary>Opens, for reading, process <paramref name="processId"/>, which must have loaded <c>d2r.exe</c>.</summary>
    /// <exception cref="PlatformNotSupportedException">Not running on Windows.</exception>
    /// <exception cref="InvalidOperationException">The process has not loaded <c>d2r.exe</c>, or its modules could not be read.</exception>
    /// <exception cref="Win32Exception">The process could not be opened.</exception>
    public static WindowsProcessMemory Open(uint processId)
    {
        ThrowIfNotWindows();
        if (!TryGetGameModule(processId, out var module))
            throw new InvalidOperationException($"Process {processId} has not loaded {ProcessModuleInfo.GameModuleName}.");
        return Open(processId, module);
    }

    /// <summary>The modules loaded in process <paramref name="processId"/>.</summary>
    /// <exception cref="PlatformNotSupportedException">Not running on Windows.</exception>
    /// <exception cref="Win32Exception">The process could not be opened or its modules read.</exception>
    public static IReadOnlyList<ProcessModuleInfo> GetModules(uint processId)
    {
        ThrowIfNotWindows();
        using var process = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, processId);
        if (process.IsInvalid)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        var handles = new nint[256];
        while (true)
        {
            uint needed;
            unsafe
            {
                fixed (nint* m = handles)
                {
                    if (!EnumProcessModules(process, m, (uint)(sizeof(nint) * handles.Length), out needed))
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
            }

            var count = (int)(needed / (uint)nint.Size);
            if (count <= handles.Length)
            {
                Array.Resize(ref handles, count);
                break;
            }

            handles = new nint[count];
        }

        var modules = new List<ProcessModuleInfo>(handles.Length);
        var name = new char[MaxPath];
        foreach (var handle in handles)
        {
            if (!GetModuleInformation(process, handle, out var info, (uint)Marshal.SizeOf<NativeModuleInfo>()))
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            uint length;
            unsafe
            {
                fixed (char* n = name)
                    length = GetModuleFileNameEx(process, handle, n, (uint)name.Length);
            }

            if (length == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            modules.Add(new ProcessModuleInfo((nuint)info.BaseOfDll, info.SizeOfImage, new string(name, 0, (int)length)));
        }

        return modules;
    }

    /// <inheritdoc/>
    public bool TryRead(nuint address, Span<byte> buffer)
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

    /// <summary>Closes the process handle.</summary>
    public void Dispose() => _handle.Dispose();

    /// <summary>The first module of <paramref name="modules"/> that is the game module.</summary>
    internal static ProcessModuleInfo? FindGameModule(IEnumerable<ProcessModuleInfo> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        foreach (var m in modules)
        {
            if (m.IsGameModule)
                return m;
        }

        return null;
    }

    private static uint[] GetProcessIds()
    {
        var ids = new uint[1024];
        while (true)
        {
            uint bytes;
            unsafe
            {
                fixed (uint* p = ids)
                {
                    if (!EnumProcesses(p, (uint)(ids.Length * sizeof(uint)), out bytes))
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
            }

            var count = (int)(bytes / sizeof(uint));
            if (count < ids.Length)
                return ids[..count];
            ids = new uint[ids.Length * 2]; // the buffer was full: there may be more
        }
    }

    // False when the modules cannot be listed (a process we may not open) or none is the game module.
    private static bool TryGetGameModule(uint processId, out ProcessModuleInfo module)
    {
        try
        {
            if (FindGameModule(GetModules(processId)) is { } found)
            {
                module = found;
                return true;
            }
        }
        catch (Win32Exception)
        {
        }

        module = default;
        return false;
    }

    private static WindowsProcessMemory Open(uint processId, ProcessModuleInfo module)
    {
        var handle = OpenProcess(ProcessVmRead, false, processId);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        return new WindowsProcessMemory(handle, processId, module);
    }

    private static void ThrowIfNotWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Reading a game process requires Windows.");
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

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

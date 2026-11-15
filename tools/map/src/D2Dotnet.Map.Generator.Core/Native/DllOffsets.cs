using D2Dotnet.Map.Generator.Output;

namespace D2Dotnet.Map.Generator.Native;

/// <summary>Module loading primitives used by offset.c (kernel32 on Windows, a fake in tests).</summary>
public interface IModuleLoader
{
    /// <summary><c>GetModuleHandleA</c>.</summary>
    uint GetModuleHandle(string dllName);

    /// <summary><c>LoadLibraryA</c>.</summary>
    uint LoadLibrary(string dllName);

    /// <summary><c>GetProcAddress(hMod, (LPCSTR)ordinal)</c>.</summary>
    uint GetProcAddressByOrdinal(uint module, int ordinal);
}

/// <summary>Port of upstream map/offset.c.</summary>
public sealed class DllOffsets
{
    private static readonly LogSource SrcInitFailed = new("map/offset.c", 31);
    private static readonly LogSource SrcInitFailed2 = new("map/offset.c", 43);
    private static readonly LogSource SrcInit = new("map/offset.c", 46);

    private readonly IModuleLoader _loader;
    private readonly Logger _log;
    private readonly Action<int> _exit;

    /// <param name="loader">Win32 module API.</param>
    /// <param name="log">Logger.</param>
    /// <param name="exit">C <c>exit()</c> (flushes stdio, does not return).</param>
    public DllOffsets(IModuleLoader loader, Logger log, Action<int> exit)
    {
        _loader = loader;
        _log = log;
        _exit = exit;
    }

    /// <summary>
    /// <c>GetDllOffset(const char*, int)</c>: module handle (loading the DLL if needed), then
    /// either the export with ordinal <c>-offset</c> or <c>base + offset</c>. The C try/catch only
    /// catches C++ exceptions, which none of these Win32 calls throw, so it is not reproduced.
    /// </summary>
    public uint GetDllOffset(string dllName, int offset)
    {
        var hMod = _loader.GetModuleHandle(dllName);
        if (hMod == 0) hMod = _loader.LoadLibrary(dllName);
        if (hMod == 0) return 0;
        if (offset < 0) return _loader.GetProcAddressByOrdinal(hMod, -offset);
        return unchecked(hMod + (uint)offset);
    }

    /// <summary><c>GetDllOffset(int num)</c>: decode <c>DLLOFFSET</c>, resolve, exit(1) on failure.</summary>
    public uint GetDllOffset(int num)
    {
        if ((num & 0xff) > 12) return 0;

        // dlls[12] would read past the 12-entry array in C; no d2_ptrs.h entry uses D2MULTI.
        var dllName = D2Pointers.DllNames[num & 0xff];
        var offset = GetDllOffset(dllName, num >> 8);
        if (offset == 0)
        {
            _log.Error(SrcInitFailed2, "Dll:Init:Failed", LogField.S("dll", dllName), LogField.I("offset", unchecked((int)offset)));
            _exit(1);
        }
        _log.Trace(SrcInit, "Dll:Init", LogField.S("dll", dllName), LogField.I("offset", unchecked((int)offset)));
        return offset;
    }

    /// <summary><c>DefineOffsets</c>: resolves every d2_ptrs.h global in declaration order.</summary>
    public D2Addresses DefineOffsets()
    {
        var values = new uint[D2Pointers.All.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = GetDllOffset(D2Pointers.All[i].Encoded);
        }
        return new D2Addresses(values);
    }

    /// <summary>Unused upstream log site kept for completeness (the catch(...) branch).</summary>
    internal static LogSource CatchSource => SrcInitFailed;
}

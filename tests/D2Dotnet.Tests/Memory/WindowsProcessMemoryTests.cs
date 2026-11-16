using System.Runtime.InteropServices;
using D2Dotnet.Memory;

namespace D2Dotnet.Tests.Memory;

/// <summary>
/// The Win32 reader needs Windows and a running game; what is platform independent is tested: the game module match,
/// the native struct layout, and that every entry point refuses other platforms.
/// </summary>
public sealed class WindowsProcessMemoryTests
{
    private static ProcessModuleInfo Module(string path) => new(0x1000, 0x2000, path);

    [Fact]
    public void FindGameModule_MatchesTheFileNameIgnoringCase()
    {
        ProcessModuleInfo[] modules =
        [
            Module(@"C:\Windows\System32\ntdll.dll"),
            Module(@"C:\Program Files (x86)\Diablo II Resurrected\D2R.EXE"),
            Module(@"C:\other\d2r.exe"),
        ];
#pragma warning disable CA1416 // platform independent helper of a Windows-only type
        Assert.Equal(modules[1], WindowsProcessMemory.FindGameModule(modules));

        // d2go matched any path containing "d2r.exe", such as d2r.exe.bak or not_d2r.exe.
        Assert.Null(WindowsProcessMemory.FindGameModule([Module(@"C:\x\d2r.exe.bak"), Module(@"C:\x\not_d2r.exe")]));
        Assert.Null(WindowsProcessMemory.FindGameModule([Module(@"C:\x\d2r.ex"), Module("")]));
        Assert.Throws<ArgumentNullException>(() => WindowsProcessMemory.FindGameModule(null!));
#pragma warning restore CA1416
        Assert.True(Module("d2r.exe").IsGameModule);
        Assert.True(Module("/games/D2R.exe").IsGameModule);
        Assert.Equal("d2r.exe", ProcessModuleInfo.GameModuleName);
    }

    [Fact]
    public void NativeModuleInfo_HasTheWin64Layout()
    {
        // MODULEINFO: LPVOID lpBaseOfDll; DWORD SizeOfImage; LPVOID EntryPoint.
        Assert.Equal(24, Marshal.SizeOf<NativeModuleInfo>());
        Assert.Equal(8, (int)Marshal.OffsetOf<NativeModuleInfo>(nameof(NativeModuleInfo.SizeOfImage)));
        Assert.Equal(16, (int)Marshal.OffsetOf<NativeModuleInfo>(nameof(NativeModuleInfo.EntryPoint)));
    }

    [Fact]
    public void EntryPoints_RefuseOtherPlatforms()
    {
        if (OperatingSystem.IsWindows())
            return; // the Windows behaviour needs a running game

#pragma warning disable CA1416 // deliberately called off Windows to check the platform guard
        Assert.Throws<PlatformNotSupportedException>(() => WindowsProcessMemory.OpenGame());
        Assert.Throws<PlatformNotSupportedException>(() => WindowsProcessMemory.Open(1234));
        Assert.Throws<PlatformNotSupportedException>(() => WindowsProcessMemory.GetModules(1234));
        Assert.Throws<PlatformNotSupportedException>(() => GameProcess.Attach());
        Assert.Throws<PlatformNotSupportedException>(() => GameProcess.Attach(1234));
#pragma warning restore CA1416
    }
}

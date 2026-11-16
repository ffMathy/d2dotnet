using System.Runtime.InteropServices;

namespace D2Dotnet.D2Go.Memory.Tests;

/// <summary>
/// The Win32 reader can not run here; what is platform independent is tested: the module match of
/// <c>getMainModule</c>, the native struct layout, and that every entry point refuses other platforms.
/// </summary>
public sealed class WindowsProcessMemoryTests
{
    private static ModuleInfo Module(string name) => new(1, 0x1000, 0x2000, 0x1000, name);

    [Fact]
    public void TryFindGameModule_MatchesTheLowerCasedPath()
    {
        ModuleInfo[] modules =
        [
            Module(@"C:\Windows\System32\ntdll.dll"),
            Module(@"C:\Program Files (x86)\Diablo II Resurrected\D2R.EXE"),
            Module(@"C:\other\d2r.exe"),
        ];
        Assert.True(ModuleInfo.TryFindGameModule(modules, out var m));
        Assert.Equal(modules[1], m);

        // A substring match, as Go's strings.Contains.
        Assert.True(ModuleInfo.TryFindGameModule([Module(@"C:\x\d2r.exe.bak")], out _));
        Assert.False(ModuleInfo.TryFindGameModule([Module(@"C:\x\d2r.ex"), Module("")], out m));
        Assert.Equal(default, m);
        Assert.Throws<ArgumentNullException>(() => ModuleInfo.TryFindGameModule(null!, out _));
        Assert.Equal("d2r.exe", ModuleInfo.GameModuleName);
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
            return; // the Windows behaviour needs a running game; see the README

#pragma warning disable CA1416 // deliberately called off Windows to check the platform guard
        Assert.Throws<PlatformNotSupportedException>(() => WindowsProcessMemory.OpenGameProcess());
        Assert.Throws<PlatformNotSupportedException>(() => WindowsProcessMemory.OpenProcess(1234));
        Assert.Throws<PlatformNotSupportedException>(() => WindowsProcessMemory.GetProcessModules(1234));
        Assert.Throws<PlatformNotSupportedException>(() => GameProcess.Open());
        Assert.Throws<PlatformNotSupportedException>(() => GameProcess.OpenForPid(1234));
#pragma warning restore CA1416
    }
}

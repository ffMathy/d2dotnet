// Abstraction over the Win32 calls of d2go pkg/memory/process.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Memory;

/// <summary>
/// Read access to the memory of a game process: the only platform-specific part of the port. d2go calls
/// <c>ReadProcessMemory</c> directly; here <see cref="GameProcess"/> and <see cref="GameReader"/> read through this
/// interface. <see cref="WindowsProcessMemory"/> reads a live <c>d2r.exe</c>; tests supply an in-memory image.
/// </summary>
/// <remarks>Addresses are 64-bit virtual addresses of the target process (Go <c>uintptr</c>).</remarks>
public interface IProcessMemory : IDisposable
{
    /// <summary>The id of the process (Go <c>Process.pid</c>).</summary>
    uint ProcessId { get; }

    /// <summary>Where the game module (<c>d2r.exe</c>) is loaded (Go <c>moduleBaseAddressPtr</c>).</summary>
    nuint ModuleBaseAddress { get; }

    /// <summary>The size of the game module's image in bytes (Go <c>moduleBaseSize</c>).</summary>
    uint ModuleBaseSize { get; }

    /// <summary>
    /// Copies <c>buffer.Length</c> bytes from <paramref name="address"/> into <paramref name="buffer"/>. Must not throw
    /// for memory that cannot be read: it returns false instead, and the bytes it could not read keep the value they
    /// had (callers pass a zeroed buffer, which is what d2go's ignored read errors leave behind).
    /// </summary>
    /// <returns>True when every byte was read.</returns>
    bool ReadMemory(nuint address, Span<byte> buffer);
}

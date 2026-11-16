namespace D2Dotnet.Memory;

/// <summary>
/// Read access to the memory of a game process, and where its game module (<c>d2r.exe</c>) is loaded. The only
/// platform-specific part of reading a game: <see cref="WindowsProcessMemory"/> reads a live process; any other source
/// (a memory dump, a test image) can implement it.
/// </summary>
/// <remarks>Addresses are 64-bit virtual addresses of the target process. Implementations must be safe to call from several threads.</remarks>
public interface IProcessMemory : IDisposable
{
    /// <summary>The id of the process.</summary>
    uint ProcessId { get; }

    /// <summary>Where the game module is loaded.</summary>
    nuint ModuleBaseAddress { get; }

    /// <summary>The size of the game module's image, in bytes.</summary>
    uint ModuleSize { get; }

    /// <summary>
    /// Copies <c>buffer.Length</c> bytes from <paramref name="address"/> into <paramref name="buffer"/>. Does not throw
    /// for memory that cannot be read: returns false instead, and leaves the bytes it could not read as they were.
    /// </summary>
    /// <returns>True when every byte was read.</returns>
    bool TryRead(nuint address, Span<byte> buffer);
}

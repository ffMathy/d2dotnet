namespace D2Dotnet.Map.Generator.Interop;

/// <summary>
/// A bump allocator over one <c>VirtualAlloc(PAGE_EXECUTE_READWRITE)</c> block, used for the
/// generated x86 thunks (see <see cref="X86Thunks"/>). Thunks live for the whole process.
/// </summary>
internal sealed unsafe class ExecutableMemory
{
    private const int BlockSize = 4096;

    private nint _block;
    private int _used;

    /// <summary>Reserves <paramref name="size"/> bytes (16-byte aligned) and returns their address.</summary>
    public nint Reserve(int size)
    {
        if (_block == 0 || _used + size > BlockSize)
        {
            _block = Win32.VirtualAlloc(0, BlockSize, Win32.MEM_COMMIT | Win32.MEM_RESERVE, Win32.PAGE_EXECUTE_READWRITE);
            if (_block == 0) throw new OutOfMemoryException("VirtualAlloc(PAGE_EXECUTE_READWRITE) failed");
            _used = 0;
        }
        var address = _block + _used;
        _used += (size + 15) & ~15;
        return address;
    }

    /// <summary>Copies machine code to <paramref name="address"/> and flushes the instruction cache.</summary>
    public static void Write(nint address, byte[] code)
    {
        new ReadOnlySpan<byte>(code).CopyTo(new Span<byte>((void*)address, code.Length));
        Win32.FlushInstructionCache(Win32.GetCurrentProcess(), address, (nuint)code.Length);
    }

    /// <summary>Reserves space for and writes position-independent code.</summary>
    public nint Emit(byte[] code)
    {
        var address = Reserve(code.Length);
        Write(address, code);
        return address;
    }
}

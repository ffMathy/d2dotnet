using System.Buffers.Binary;
using System.Text;
using D2Dotnet.Memory;

namespace D2Dotnet.Tests.Memory;

/// <summary>
/// An in-memory process: a sparse 64-bit address space of 4 KiB pages. Reads of unmapped bytes fail and leave the buffer
/// untouched there, like <c>ReadProcessMemory</c> on unmapped memory.
/// </summary>
internal sealed class FakeProcessMemory(nuint moduleBaseAddress, uint moduleSize, uint processId = 4242) : IProcessMemory
{
    private const ulong PageSize = 0x1000;
    private readonly Dictionary<ulong, byte[]> _pages = [];
    private ulong _heap = 0x0000_0200_0000_0000;

    public uint ProcessId { get; } = processId;

    public nuint ModuleBaseAddress { get; } = moduleBaseAddress;

    public uint ModuleSize { get; set; } = moduleSize;

    public bool IsDisposed { get; private set; }

    public bool TryRead(nuint address, Span<byte> buffer)
    {
        var ok = true;
        for (var i = 0; i < buffer.Length; i++)
        {
            var a = unchecked((ulong)address + (ulong)i);
            if (_pages.TryGetValue(a / PageSize, out var page))
                buffer[i] = page[a % PageSize];
            else
                ok = false;
        }

        return ok;
    }

    public void Dispose() => IsDisposed = true;

    /// <summary>Makes <paramref name="size"/> bytes at <paramref name="address"/> readable (zero unless written).</summary>
    public void Map(ulong address, ulong size)
    {
        for (var p = address / PageSize; p <= (address + size - 1) / PageSize; p++)
            _pages.TryAdd(p, new byte[PageSize]);
    }

    public void Write(ulong address, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;
        Map(address, (ulong)data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            var a = address + (ulong)i;
            _pages[a / PageSize][a % PageSize] = data[i];
        }
    }

    public void WriteU8(ulong address, byte value) => Write(address, [value]);

    public void WriteU16(ulong address, ushort value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, value);
        Write(address, b);
    }

    public void WriteU32(ulong address, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        Write(address, b);
    }

    public void WriteU64(ulong address, ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, value);
        Write(address, b);
    }

    public ulong ReadU64(ulong address)
    {
        Span<byte> b = stackalloc byte[8];
        TryRead((nuint)address, b);
        return BinaryPrimitives.ReadUInt64LittleEndian(b);
    }

    /// <summary>Writes the UTF-8 bytes of <paramref name="s"/> and a NUL.</summary>
    public void WriteCString(ulong address, string s) => Write(address, [.. Encoding.UTF8.GetBytes(s), 0]);

    /// <summary>A fresh, mapped, zeroed block at an address aligned to <paramref name="align"/>.</summary>
    public ulong Alloc(ulong size, ulong align = 0x10)
    {
        var address = (_heap + align - 1) / align * align;
        _heap = address + Math.Max(size, 1) + 0x100; // a gap between blocks
        Map(address, Math.Max(size, 1));
        return address;
    }
}

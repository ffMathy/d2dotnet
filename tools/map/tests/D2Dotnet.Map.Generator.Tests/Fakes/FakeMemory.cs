using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using D2Dotnet.Map.Generator.Game;

namespace D2Dotnet.Map.Generator.Tests.Fakes;

/// <summary>Raised for a read of unmapped memory (an access violation in the real process).</summary>
public sealed class FakeAccessViolation(uint address) : Exception($"access violation reading 0x{address:x8}")
{
    public uint Address { get; } = address;
}

/// <summary>A sparse 32-bit address space: 4 KiB pages created on write or allocation.</summary>
public sealed class FakeMemory : IGameMemory
{
    private const int PageSize = 4096;
    private readonly Dictionary<uint, byte[]> _pages = [];
    private uint _next = 0x10000000;

    public List<(uint Address, int Size)> Writes { get; } = [];

    private byte[] Page(uint address, bool create)
    {
        var index = address / PageSize;
        if (_pages.TryGetValue(index, out var page)) return page;
        if (!create) throw new FakeAccessViolation(address);
        page = new byte[PageSize];
        _pages[index] = page;
        return page;
    }

    private byte ReadByte(uint a) => Page(a, false)[a % PageSize];

    private void WriteByte(uint a, byte b) => Page(a, true)[a % PageSize] = b;

    public T Read<T>(uint address) where T : unmanaged
    {
        if (address < 0x10000) throw new FakeAccessViolation(address);
        Span<byte> buf = stackalloc byte[Unsafe.SizeOf<T>()];
        for (var i = 0; i < buf.Length; i++) buf[i] = ReadByte(address + (uint)i);
        return MemoryMarshal.Read<T>(buf);
    }

    public void Write<T>(uint address, T value) where T : unmanaged
    {
        Span<byte> buf = stackalloc byte[Unsafe.SizeOf<T>()];
        MemoryMarshal.Write(buf, in value);
        for (var i = 0; i < buf.Length; i++) WriteByte(address + (uint)i, buf[i]);
        Writes.Add((address, buf.Length));
    }

    /// <summary>Allocates zeroed memory (16-byte aligned).</summary>
    public uint Alloc(int size)
    {
        var a = _next;
        _next += (uint)((size + 15) & ~15);
        for (var i = 0; i < size; i++) WriteByte(a + (uint)i, 0);
        return a;
    }

    /// <summary>Allocates and stores a struct.</summary>
    public uint New<T>(T value) where T : unmanaged
    {
        var a = Alloc(Unsafe.SizeOf<T>());
        Write(a, value);
        return a;
    }

    /// <summary>Allocates a NUL-terminated Latin-1 string.</summary>
    public uint NewString(string s)
    {
        var a = Alloc(s.Length + 1);
        WriteBytes(a, System.Text.Encoding.Latin1.GetBytes(s + "\0"));
        return a;
    }

    public void WriteBytes(uint address, byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++) WriteByte(address + (uint)i, bytes[i]);
    }

    /// <summary>Applies a mutation to a struct stored at an address.</summary>
    public void Update<T>(uint address, Func<T, T> change) where T : unmanaged => Write(address, change(Read<T>(address)));
}

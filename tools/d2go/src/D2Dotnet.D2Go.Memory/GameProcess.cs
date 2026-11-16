// Port of d2go pkg/memory/process.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;

namespace D2Dotnet.D2Go.Memory;

/// <summary>The width of an unsigned integer read from memory (Go <c>memory.IntType</c> and its constants).</summary>
public enum IntType : uint
{
    Uint8 = 1,
    Uint16 = 2,
    Uint32 = 4,
    Uint64 = 8,
}

/// <summary>
/// The game process and the primitive reads d2go builds on (Go <c>memory.Process</c>). All reads go through an
/// <see cref="IProcessMemory"/>; as in Go, a failed read is not an error and yields zero bytes.
/// </summary>
/// <remarks>
/// Integers read from memory are <see cref="ulong"/> (Go <c>uint</c>, 64-bit) and addresses <see cref="nuint"/> (Go
/// <c>uintptr</c>); the port is meant for 64-bit processes. Disposing closes the process (Go <c>Close</c>).
/// </remarks>
public class GameProcess : IDisposable
{
    private const int PointerSize = 8;

    /// <summary>Wraps an opened process.</summary>
    public GameProcess(IProcessMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        Memory = memory;
    }

    /// <summary>
    /// Shares <paramref name="process"/>'s memory reader, the way Go's <c>GameReader</c> embeds a copy of the
    /// <c>Process</c> value: disposing either one closes the process for both.
    /// </summary>
    protected GameProcess(GameProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        Memory = process.Memory;
    }

    /// <summary>The memory reader of the process.</summary>
    public IProcessMemory Memory { get; }

    /// <summary>Where <c>d2r.exe</c> is loaded (Go <c>moduleBaseAddressPtr</c>).</summary>
    public nuint ModuleBaseAddress => Memory.ModuleBaseAddress;

    /// <summary>The size of the <c>d2r.exe</c> image (Go <c>moduleBaseSize</c>).</summary>
    public uint ModuleBaseSize => Memory.ModuleBaseSize;

    /// <summary>
    /// Opens the first running process that has loaded <c>d2r.exe</c> (Go <c>NewProcess</c>), with
    /// <see cref="WindowsProcessMemory.OpenGameProcess"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static GameProcess Open() => new(WindowsProcessMemory.OpenGameProcess());

    /// <summary>
    /// Opens process <paramref name="pid"/>, which must have loaded <c>d2r.exe</c> (Go <c>NewProcessForPID</c>), with
    /// <see cref="WindowsProcessMemory.OpenProcess"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static GameProcess OpenForPid(uint pid) => new(WindowsProcessMemory.OpenProcess(pid));

    /// <summary>The process id (Go <c>GetPID</c>).</summary>
    public uint GetPid() => Memory.ProcessId;

    /// <summary>
    /// The whole <c>d2r.exe</c> image (Go <c>getProcessMemory</c>), or null when it could not be read (Go returns an
    /// error).
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The module size is 0 (Go panics taking the address of the empty buffer).</exception>
    internal byte[]? GetProcessMemory()
    {
        var data = NewBuffer(ModuleBaseSize);
        return Memory.ReadMemory(ModuleBaseAddress, data) ? data : null;
    }

    /// <summary>
    /// Reads <paramref name="size"/> bytes at <paramref name="address"/> (Go <c>ReadBytesFromMemory</c>). Errors are
    /// ignored: bytes that cannot be read are 0.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException"><paramref name="size"/> is 0 (Go panics).</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is larger than an array can be (Go panics or runs out of memory).</exception>
    public byte[] ReadBytesFromMemory(nuint address, ulong size)
    {
        var data = NewBuffer(size);
        Memory.ReadMemory(address, data);

        return data;
    }

    /// <summary>A little-endian unsigned integer of <paramref name="size"/> bytes at <paramref name="address"/> (Go <c>ReadUInt</c>).</summary>
    public ulong ReadUInt(nuint address, IntType size)
    {
        var bytes = ReadBytesFromMemory(address, (ulong)size);

        return BytesToUInt(bytes, size);
    }

    /// <summary>
    /// A little-endian unsigned integer of <paramref name="size"/> bytes at <paramref name="offset"/> of
    /// <paramref name="bytes"/> (Go <c>ReadUIntFromBuffer</c>); 0 for a size that is not an <see cref="IntType"/> member.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The range is outside <paramref name="bytes"/> (Go panics).</exception>
    public static ulong ReadUIntFromBuffer(ReadOnlySpan<byte> bytes, ulong offset, IntType size)
    {
        var end = offset + (ulong)size;
        if (end < offset || end > (ulong)bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "The read is outside the buffer.");

        return BytesToUInt(bytes[(int)offset..(int)end], size);
    }

    private static ulong BytesToUInt(ReadOnlySpan<byte> bytes, IntType size) => size switch
    {
        IntType.Uint8 => bytes[0],
        IntType.Uint16 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
        IntType.Uint32 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
        IntType.Uint64 => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
        _ => 0,
    };

    /// <summary>
    /// A string at <paramref name="address"/> (Go <c>ReadStringFromMemory</c>). With a <paramref name="size"/> of 0
    /// it is the bytes before the first NUL; otherwise it is <paramref name="size"/> bytes with leading and trailing
    /// NULs removed (NULs inside are kept, as Go's <c>bytes.Trim</c> does). Bytes are decoded as UTF-8.
    /// </summary>
    public string ReadStringFromMemory(nuint address, ulong size)
    {
        if (size == 0)
        {
            // Go re-reads 1, 2, 3... bytes until the last one is NUL; reading one more byte each step gives the same
            // bytes.
            var bytes = new List<byte>();
            Span<byte> one = stackalloc byte[1];
            for (nuint i = 0; ; i++)
            {
                one[0] = 0;
                Memory.ReadMemory(unchecked(address + i), one);
                if (one[0] == 0)
                    return Encoding.UTF8.GetString([.. bytes]);
                bytes.Add(one[0]);
            }
        }

        var data = ReadBytesFromMemory(address, size).AsSpan();
        var start = 0;
        var end = data.Length;
        while (start < end && data[start] == 0)
            start++;
        while (end > start && data[end - 1] == 0)
            end--;

        return Encoding.UTF8.GetString(data[start..end]);
    }

    /// <summary>
    /// The offset in <paramref name="memory"/> (the module image) of the first match of <paramref name="pattern"/>,
    /// where <c>?</c> in <paramref name="mask"/> matches any byte (Go <c>findPattern</c>). As in Go, 0 means "not
    /// found" (so a match at offset 0 is not found), the scan is bounded by the module size, not the buffer, and the
    /// last possible position (module size minus pattern length) is not tried.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException"><paramref name="memory"/> is shorter than the module or <paramref name="mask"/> shorter than the pattern (Go panics).</exception>
    internal int FindPatternOffset(ReadOnlySpan<byte> memory, ReadOnlySpan<byte> pattern, string mask)
    {
        var patternLength = pattern.Length;
        for (long i = 0; i < (long)ModuleBaseSize - patternLength; i++)
        {
            var found = true;
            for (var j = 0; j < patternLength; j++)
            {
                if (mask[j] != '?' && pattern[j] != ByteAt(memory, i + j))
                {
                    found = false;
                    break;
                }
            }

            if (found)
                return (int)i;
        }

        return 0;
    }

    private static byte ByteAt(ReadOnlySpan<byte> memory, long index) =>
        index < memory.Length ? memory[(int)index] : throw new IndexOutOfRangeException("The pattern scan ran past the module image.");

    /// <summary>
    /// The address of the first match of <paramref name="pattern"/> in the module image <paramref name="memory"/>,
    /// or 0 when there is none (Go <c>FindPattern</c>); see <see cref="FindPatternOffset"/> for the matching rules.
    /// </summary>
    public nuint FindPattern(ReadOnlySpan<byte> memory, ReadOnlySpan<byte> pattern, string mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var offset = FindPatternOffset(memory, pattern, mask);
        if (offset != 0)
            return unchecked(ModuleBaseAddress + (nuint)offset);

        return 0;
    }

    /// <summary>
    /// For a match of a <c>rip</c>-relative instruction of 7 bytes whose 32-bit operand is at bytes 3 to 6, the
    /// address it refers to: match address + operand + 7, or 0 when there is no match (Go
    /// <c>FindPatternByOperand</c>). As in Go, the operand is read from <paramref name="memory"/> as an unsigned
    /// value, so a negative displacement is not sign-extended.
    /// </summary>
    public nuint FindPatternByOperand(ReadOnlySpan<byte> memory, ReadOnlySpan<byte> pattern, string mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var offset = FindPatternOffset(memory, pattern, mask);
        if (offset != 0)
        {
            // Adjust the address based on the operand value
            var operandAddress = unchecked(ModuleBaseAddress + (nuint)offset);
            var operandValue = BinaryPrimitives.ReadUInt32LittleEndian(memory[(offset + 3)..(offset + 7)]);
            var finalAddress = unchecked(operandAddress + operandValue + 7); // 7 is the length of the instruction
            return finalAddress;
        }

        return 0;
    }

    /// <summary>
    /// The 64-bit pointer at <paramref name="address"/> (Go <c>ReadPointer</c>). Go's error result can not happen:
    /// a <paramref name="size"/> of 0 panics before it, and any other size reads a non-empty buffer.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is less than 8 (Go reads past its buffer).</exception>
    public nuint ReadPointer(nuint address, int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, PointerSize);
        var buffer = ReadBytesFromMemory(address, (ulong)size);

        return (nuint)BinaryPrimitives.ReadUInt64LittleEndian(buffer);
    }

    /// <summary>
    /// Reads the <c>WidgetContainer</c> at <paramref name="address"/> (Go <c>ReadWidgetContainer</c>): keys
    /// <c>WidgetNameString</c> (<see cref="string"/>), <c>WidgetNameLength</c> (<see cref="ulong"/>),
    /// <c>WidgetVisible</c> and <c>WidgetActive</c> (<see cref="bool"/>); with <paramref name="full"/> also
    /// <c>ChildWidgetsListPointer</c>, <c>WidgetListPointer</c>, <c>WidgetList2Pointer</c> (<see cref="nuint"/>) and
    /// <c>ChildWidgetSize</c>, <c>WidgetListSize</c>, <c>WidgetList2Size</c> (<see cref="ulong"/>).
    /// </summary>
    /// <returns>False when the widget name is empty (Go: "failed to read widget name"); <paramref name="widget"/> is then empty.</returns>
    public bool TryReadWidgetContainer(nuint address, bool full, out IReadOnlyDictionary<string, object?> widget)
    {
        var widgetPtr = ReadPointer(unchecked(address + 0x8), 8);

        var widgetNameLength = ReadUInt(unchecked(address + 0x10), IntType.Uint32);

        var widgetName = ReadStringFromMemory(widgetPtr, widgetNameLength);
        if (widgetName.Length == 0)
        {
            widget = new Dictionary<string, object?>();
            return false;
        }

        var widgetVisible = ReadUInt(unchecked(address + 0x51), IntType.Uint8) == 1;
        var widgetActive = ReadUInt(unchecked(address + 0x50), IntType.Uint8) == 1;

        var result = new Dictionary<string, object?>
        {
            ["WidgetNameString"] = widgetName,
            ["WidgetNameLength"] = widgetNameLength,
            ["WidgetVisible"] = widgetVisible,
            ["WidgetActive"] = widgetActive,
        };

        if (full)
        {
            var childWidgetsListPtr = ReadPointer(unchecked(widgetPtr + 0x38), 8);

            var childWidgetSize = ReadUInt(unchecked(widgetPtr + 0x40), IntType.Uint32);

            var widgetListPtr = ReadPointer(unchecked(widgetPtr + 0x68), 8);

            var widgetListSize = ReadUInt(unchecked(widgetPtr + 0x78), IntType.Uint32);

            var widgetList2Ptr = ReadPointer(unchecked(widgetPtr + 0x80), 8);

            var widgetList2Size = ReadUInt(unchecked(widgetPtr + 0x90), IntType.Uint32);

            result["ChildWidgetsListPointer"] = childWidgetsListPtr;
            result["ChildWidgetSize"] = childWidgetSize;
            result["WidgetListPointer"] = widgetListPtr;
            result["WidgetListSize"] = widgetListSize;
            result["WidgetList2Pointer"] = widgetList2Ptr;
            result["WidgetList2Size"] = widgetList2Size;
        }

        widget = result;
        return true;
    }

    /// <summary>
    /// Reads <paramref name="listSize"/> widget pointers at <paramref name="listPointer"/> and their containers, by
    /// name (Go <c>ReadWidgetList</c>); a later widget with the same name replaces an earlier one.
    /// </summary>
    /// <returns>False as soon as one widget has no name (Go returns that error); <paramref name="widgets"/> is then empty.</returns>
    public bool TryReadWidgetList(nuint listPointer, int listSize, out IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> widgets)
    {
        var widgetMap = new Dictionary<string, IReadOnlyDictionary<string, object?>>();

        for (var i = 0; i < listSize; i++)
        {
            var widgetAddr = ReadPointer(unchecked(listPointer + (nuint)(i * PointerSize)), 8);

            if (!TryReadWidgetContainer(widgetAddr, false, out var widgetContainer))
            {
                widgets = new Dictionary<string, IReadOnlyDictionary<string, object?>>();
                return false;
            }

            var widgetName = (string)widgetContainer["WidgetNameString"]!;

            widgetMap[widgetName] = widgetContainer;
        }

        widgets = widgetMap;
        return true;
    }

    /// <summary>Closes the process (Go <c>Close</c>).</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes <see cref="Memory"/> when <paramref name="disposing"/>.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            Memory.Dispose();
    }

    private static byte[] NewBuffer(ulong size)
    {
        // Go: make([]byte, size), then &data[0], which panics for an empty buffer.
        if (size == 0)
            throw new IndexOutOfRangeException("Cannot read 0 bytes (d2go takes the address of the first byte).");
        if (size > (ulong)Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(size), size, "The read is larger than an array can be.");

        return new byte[size];
    }
}

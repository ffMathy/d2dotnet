// Memory reads after d2go pkg/memory/process.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;

namespace D2Dotnet.Memory;

/// <summary>
/// A game process, and typed reads of its memory. Memory that cannot be read reads as zero bytes: a game's structures
/// change under the reader, so a failed read is not an error.
/// </summary>
/// <remarks>Thread-safe when the <see cref="IProcessMemory"/> is. Disposing closes the memory when the process owns it.</remarks>
public sealed class GameProcess : IDisposable
{
    /// <summary>The longest string <see cref="ReadString"/> reads when it looks for the terminating NUL.</summary>
    public const int MaxStringLength = 4096;

    private const int StringChunk = 64;
    private const int PageSize = 0x1000;
    private readonly bool _ownsMemory;

    /// <summary>A process over <paramref name="memory"/>.</summary>
    /// <param name="memory">The process memory.</param>
    /// <param name="ownsMemory">True to dispose <paramref name="memory"/> with the process.</param>
    public GameProcess(IProcessMemory memory, bool ownsMemory = true)
    {
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _ownsMemory = ownsMemory;
    }

    /// <summary>The process memory.</summary>
    public IProcessMemory Memory { get; }

    /// <summary>The id of the process.</summary>
    public uint ProcessId => Memory.ProcessId;

    /// <summary>Where <c>d2r.exe</c> is loaded.</summary>
    public nuint ModuleBaseAddress => Memory.ModuleBaseAddress;

    /// <summary>The size of the <c>d2r.exe</c> image, in bytes.</summary>
    public uint ModuleSize => Memory.ModuleSize;

    /// <summary>Opens the first running process that has loaded <c>d2r.exe</c>, for reading.</summary>
    /// <exception cref="InvalidOperationException">No process has loaded <c>d2r.exe</c>.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The process list could not be read or the process could not be opened.</exception>
    [SupportedOSPlatform("windows")]
    public static GameProcess Attach() => new(WindowsProcessMemory.OpenGame());

    /// <summary>Opens process <paramref name="processId"/>, which must have loaded <c>d2r.exe</c>, for reading.</summary>
    /// <exception cref="InvalidOperationException">The process has not loaded <c>d2r.exe</c>.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The process could not be opened.</exception>
    [SupportedOSPlatform("windows")]
    public static GameProcess Attach(uint processId) => new(WindowsProcessMemory.Open(processId));

    /// <summary>Reads <c>buffer.Length</c> bytes at <paramref name="address"/>; bytes that cannot be read are 0.</summary>
    /// <returns>True when every byte was read.</returns>
    public bool TryRead(nuint address, Span<byte> buffer)
    {
        buffer.Clear();
        return buffer.IsEmpty || Memory.TryRead(address, buffer);
    }

    /// <summary><paramref name="length"/> bytes at <paramref name="address"/>; bytes that cannot be read are 0.</summary>
    public byte[] ReadBytes(nuint address, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var data = new byte[length];
        TryRead(address, data);
        return data;
    }

    /// <summary>The byte at <paramref name="address"/>.</summary>
    public byte ReadByte(nuint address)
    {
        Span<byte> b = stackalloc byte[1];
        TryRead(address, b);
        return b[0];
    }

    /// <summary>The little-endian 16-bit value at <paramref name="address"/>.</summary>
    public ushort ReadUInt16(nuint address)
    {
        Span<byte> b = stackalloc byte[2];
        TryRead(address, b);
        return BinaryPrimitives.ReadUInt16LittleEndian(b);
    }

    /// <summary>The little-endian 32-bit value at <paramref name="address"/>.</summary>
    public uint ReadUInt32(nuint address)
    {
        Span<byte> b = stackalloc byte[4];
        TryRead(address, b);
        return BinaryPrimitives.ReadUInt32LittleEndian(b);
    }

    /// <summary>The little-endian 64-bit value at <paramref name="address"/>.</summary>
    public ulong ReadUInt64(nuint address)
    {
        Span<byte> b = stackalloc byte[8];
        TryRead(address, b);
        return BinaryPrimitives.ReadUInt64LittleEndian(b);
    }

    /// <summary>The 64-bit pointer at <paramref name="address"/>.</summary>
    public nuint ReadPointer(nuint address) => (nuint)ReadUInt64(address);

    /// <summary>
    /// A UTF-8 string at <paramref name="address"/>: with a <paramref name="length"/> of 0, the bytes up to the first NUL
    /// (at most <see cref="MaxStringLength"/>); otherwise a field of <paramref name="length"/> bytes, up to its first NUL.
    /// Bytes that cannot be read end the string. Invalid UTF-8 becomes U+FFFD.
    /// </summary>
    public string ReadString(nuint address, int length = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > 0)
        {
            var field = ReadBytes(address, length).AsSpan();
            var nul = field.IndexOf((byte)0);
            return Encoding.UTF8.GetString(nul < 0 ? field : field[..nul]);
        }

        var bytes = new List<byte>();
        Span<byte> chunk = stackalloc byte[StringChunk];
        while (bytes.Count < MaxStringLength)
        {
            var at = unchecked(address + (nuint)bytes.Count);
            if (!TryRead(at, chunk))
            {
                // A string can end just before unreadable memory: read what can be read, one byte at a time.
                for (var i = 0; i < chunk.Length; i++)
                    chunk[i] = ReadByte(unchecked(at + (nuint)i));
            }

            var nul = chunk.IndexOf((byte)0);
            var take = Math.Min(nul < 0 ? chunk.Length : nul, MaxStringLength - bytes.Count);
            bytes.AddRange(chunk[..take]);
            if (nul >= 0)
                break;
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>
    /// The link timestamp in the <c>d2r.exe</c> PE header, which tells game builds apart: a key for offsets kept between
    /// runs (see <see cref="GameReader(GameProcess, Data.GameDatabase, GameOffsets)"/>). 0 when the header cannot be read.
    /// </summary>
    public uint ReadBuildTimestamp()
    {
        var peHeader = ReadUInt32(unchecked(ModuleBaseAddress + 0x3C));
        if (peHeader is 0 or > 0x1000 || ReadUInt32(unchecked(ModuleBaseAddress + peHeader)) != 0x0000_4550) // "PE\0\0"
            return 0;
        return ReadUInt32(unchecked(ModuleBaseAddress + peHeader + 8));
    }

    /// <summary>
    /// The whole <c>d2r.exe</c> image. Pages that cannot be read (the running game protects some of its image) read
    /// as zero bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">The module size is 0, or no page of the image could be read.</exception>
    public byte[] ReadModule()
    {
        if (ModuleSize == 0)
            throw new InvalidOperationException("The game module has a size of 0.");
        var image = new byte[ModuleSize];
        if (TryRead(ModuleBaseAddress, image))
            return image;

        // One unreadable page fails a whole read: read the image a page at a time instead.
        var anyRead = false;
        for (var offset = 0; offset < image.Length; offset += PageSize)
        {
            var page = image.AsSpan(offset, Math.Min(PageSize, image.Length - offset));
            anyRead |= TryRead(unchecked(ModuleBaseAddress + (nuint)offset), page);
        }

        if (!anyRead)
            throw new InvalidOperationException($"The game module image ({ModuleSize} bytes at 0x{ModuleBaseAddress:X}) could not be read.");
        return image;
    }

    /// <summary>
    /// The offset of the first match of <paramref name="pattern"/> in <paramref name="image"/>, where a <c>?</c> in
    /// <paramref name="mask"/> matches any byte; -1 when there is none. Every position is tried, the first and last
    /// included.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="mask"/> is not as long as <paramref name="pattern"/>.</exception>
    public static int FindPattern(ReadOnlySpan<byte> image, ReadOnlySpan<byte> pattern, string mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (mask.Length != pattern.Length)
            throw new ArgumentException("The mask must be as long as the pattern.", nameof(mask));
        if (pattern.IsEmpty)
            return 0;

        for (var i = 0; i <= image.Length - pattern.Length; i++)
        {
            var found = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (mask[j] != '?' && image[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }

            if (found)
                return i;
        }

        return -1;
    }

    /// <summary>Disposes <see cref="Memory"/> when the process owns it.</summary>
    public void Dispose()
    {
        if (_ownsMemory)
            Memory.Dispose();
    }
}

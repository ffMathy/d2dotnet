using System.Buffers.Binary;
using D2Dotnet.Map.Binary;
using D2Dotnet.Map.Mpq.Compression;

namespace D2Dotnet.Map.Mpq;

/// <summary>
/// Minimal MPQ (v1) archive reader (upstream <c>@diablo2/mpq</c> <c>mpq.ts</c>).
/// </summary>
public abstract class Mpq : IAsyncDisposable
{
    private static int _nextId;

    private readonly Lazy<Task<MpqHeader>> _header;
    private Dictionary<(uint HashB, uint HashA), MpqHashEntry> _hashTable = [];
    private List<MpqBlockEntry> _blockTable = [];

    protected Mpq(string? name = null)
    {
        Id = Interlocked.Increment(ref _nextId);
        Name = name ?? $"Mpq:{Id}";
        _header = new Lazy<Task<MpqHeader>>(ReadHeaderAsync);
    }

    /// <summary>Unique id of the MPQ since load.</summary>
    public int Id { get; }

    /// <summary>Human friendly name of the MPQ.</summary>
    public string Name { get; }

    /// <summary>Hash table, keyed by (hashB, hashA). Populated once <see cref="Header"/> has been read.</summary>
    public IReadOnlyDictionary<(uint HashB, uint HashA), MpqHashEntry> HashTable => _hashTable;

    /// <summary>Block table. Populated once <see cref="Header"/> has been read.</summary>
    public IReadOnlyList<MpqBlockEntry> BlockTable => _blockTable;

    /// <summary>Create a reader for a file on disk; nothing is read until it is needed.</summary>
    public static Mpq Load(string fileName) => new MpqFile(fileName);

    /// <summary>Create a reader for an archive already in memory.</summary>
    public static Mpq Load(byte[] buffer, string? name = null) => new MpqBuffer(buffer, name);

    /// <summary>Read <paramref name="byteCount"/> bytes starting at <paramref name="offset"/>.</summary>
    public abstract Task<byte[]> ReadAsync(long offset, int byteCount);

    /// <summary>Close the file if it has been opened.</summary>
    public abstract ValueTask DisposeAsync();

    /// <summary>The archive header; reading it also loads the hash and block tables.</summary>
    public Task<MpqHeader> Header => _header.Value;

    private async Task<MpqHeader> ReadHeaderAsync()
    {
        var headerBuf = await ReadAsync(0, MpqHeader.Size).ConfigureAwait(false);
        var header = MpqHeader.Read(new BinaryCursor(headerBuf));

        if (header.Magic != "MPQ\x1a") throw new InvalidDataException("Only MPQ.magic 0x1a is supported");
        if (header.FormatVersion != (ushort)MpqFormatVersion.Version1) throw new InvalidDataException("Only MPQ.format 0x00 is supported");

        var hashTableBuf = await ReadAsync(header.HashTableOffset, checked((int)header.HashTableEntries * 16)).ConfigureAwait(false);
        Decrypt(hashTableBuf, Hash("(hash table)", MpqHashType.Table));

        var blockTableBuf = await ReadAsync(header.BlockTableOffset, checked((int)header.BlockTableEntries * 16)).ConfigureAwait(false);
        Decrypt(blockTableBuf, Hash("(block table)", MpqHashType.Table));

        var blockCursor = new BinaryCursor(blockTableBuf);
        var blockTable = new List<MpqBlockEntry>((int)header.BlockTableEntries);
        for (var i = 0; i < header.BlockTableEntries; i++) blockTable.Add(MpqBlockEntry.Read(blockCursor));
        _blockTable = blockTable;

        var hashCursor = new BinaryCursor(hashTableBuf);
        var hashTable = new Dictionary<(uint, uint), MpqHashEntry>();
        for (var i = 0; i < header.HashTableEntries; i++)
        {
            var entry = MpqHashEntry.Read(hashCursor);
            hashTable[(entry.HashB, entry.HashA)] = entry;
        }
        _hashTable = hashTable;

        return header;
    }

    private async Task<MpqHashEntry?> GetFileEntryAsync(string fileName)
    {
        await Header.ConfigureAwait(false);
        var hashA = Hash(fileName, MpqHashType.HashA);
        var hashB = Hash(fileName, MpqHashType.HashB);
        return _hashTable.GetValueOrDefault((hashB, hashA));
    }

    /// <summary>Does a file exist inside the MPQ.</summary>
    public async Task<bool> HasAsync(string fileName) => await GetFileEntryAsync(fileName).ConfigureAwait(false) != null;

    /// <summary>Alias for <see cref="HasAsync"/>.</summary>
    public Task<bool> ExistsAsync(string fileName) => HasAsync(fileName);

    /// <summary>Extract a file from the MPQ.</summary>
    /// <returns>File bytes if it exists, null otherwise.</returns>
    public async Task<byte[]?> ExtractAsync(string fileName)
    {
        var header = await Header.ConfigureAwait(false);
        var hashEntry = await GetFileEntryAsync(fileName).ConfigureAwait(false);
        if (hashEntry == null) return null;

        if (hashEntry.BlockTableIndex >= _blockTable.Count) return null;
        var blockEntry = _blockTable[(int)hashEntry.BlockTableIndex];

        // Upstream also has `if ((flags & MpqFlags.Exists) === MpqFlags.Exists) return null;` but in JavaScript the
        // left hand side is a signed int32 and never equals 0x80000000, so the check never triggers. It is omitted here.
        if (blockEntry.ArchivedSize == 0) return [];

        var isEncrypted = (blockEntry.Flags & MpqFlags.Encrypted) == MpqFlags.Encrypted;
        var isEncryptionFix = (blockEntry.Flags & MpqFlags.EncryptionFix) == MpqFlags.EncryptionFix;

        // TODO Should really handle these flags
        if ((blockEntry.Flags & MpqFlags.SingleUnit) != 0) throw new NotSupportedException("MPQ flag:SingleUnit not supported");
        if ((blockEntry.Flags & MpqFlags.Crc) != 0) throw new NotSupportedException("MPQ flag:Crc not supported");

        uint decryptionKey = uint.MaxValue;
        if (isEncrypted)
        {
            decryptionKey = DecryptionKey(fileName);
            if (isEncryptionFix) decryptionKey = (decryptionKey + blockEntry.Offset) ^ blockEntry.Size;
        }
        var isImplode = (blockEntry.Flags & MpqFlags.Implode) == MpqFlags.Implode;

        var sectorSize = 512 << header.SectorSizeShift;
        var sectors = (int)((blockEntry.Size + (long)sectorSize - 1) / sectorSize);

        var fileData = await ReadAsync(blockEntry.Offset, checked((int)blockEntry.ArchivedSize)).ConfigureAwait(false);
        if (isEncrypted) Decrypt(fileData, decryptionKey - 1, 0, (sectors + 1) * 4);

        var outputBuffer = new byte[blockEntry.Size];
        for (var i = 0; i < sectors; i++)
        {
            var currentOffset = BinaryPrimitives.ReadUInt32LittleEndian(fileData.AsSpan(i * 4));
            var nextOffset = BinaryPrimitives.ReadUInt32LittleEndian(fileData.AsSpan(i * 4 + 4));
            if (nextOffset < currentOffset) throw new InvalidDataException("Failed to read MPQ invalid sectors detected");

            var currentSectorSize = (int)(nextOffset - currentOffset);
            if (currentSectorSize > sectorSize) throw new InvalidDataException("Failed to read MPQ invalid sectors detected");
            if (currentOffset > blockEntry.ArchivedSize) throw new InvalidDataException("Failed to read MPQ invalid sector overflow");

            // Decrypt the sector if needed
            if (isEncrypted) Decrypt(fileData, decryptionKey + (uint)i, (int)currentOffset, currentSectorSize);

            byte[] sectorBytes;
            int sectorStart;
            int sectorLength;
            if (currentSectorSize == sectorSize)
            {
                // If the sector is not compressed just copy it
                sectorBytes = fileData;
                sectorStart = (int)currentOffset;
                sectorLength = Math.Min(sectorSize, fileData.Length - sectorStart);
            }
            else
            {
                sectorBytes = isImplode
                    ? MpqCompression.DecompressPkWare(fileData, (int)currentOffset, currentSectorSize)
                    : MpqCompression.DecompressSector(fileData, (int)currentOffset, currentSectorSize);
                sectorStart = 0;
                sectorLength = sectorBytes.Length;
            }

            // Buffer.copy semantics: copy as much as fits into the output
            var target = i * sectorSize;
            var count = Math.Min(sectorLength, outputBuffer.Length - target);
            if (count > 0) Array.Copy(sectorBytes, sectorStart, outputBuffer, target, count);
        }

        // Upstream clears MpqFlags.Encrypted here because MpqBuffer hands out views and decrypts the archive in place.
        // Every read here returns a fresh copy, so the flag is kept and repeated extractions keep working.
        return outputBuffer;
    }

    /// <summary>File decryption key: hash of the file name without its directory.</summary>
    public uint DecryptionKey(string str)
    {
        var lastIndex = str.Length - 1;
        for (; lastIndex >= 0; lastIndex--)
        {
            var ch = str[lastIndex];
            if (ch == '\\') break;
            if (ch == '/') break;
        }
        return Hash(str[(lastIndex + 1)..], MpqHashType.Table);
    }

    /// <summary>Create a MPQ hash of a string using the given hash type.</summary>
    public uint Hash(string str, MpqHashType type)
    {
        // Allow the use of '/' as a path separator
        if (str.Contains('/') && !str.Contains('\\')) str = str.Replace('/', '\\');
        uint seed1 = 0x7fed7fed;
        uint seed2 = 0xeeeeeeee;
        var table = MpqEncryption.TableValues;
        foreach (var c in str)
        {
            uint ch = c;
            // upper case lowercase characters (upstream excludes 'z' itself: `ch < charZ`)
            if (ch >= 'a' && ch < 'z') ch -= 'a' - 'A';
            var index = ((uint)type << 8) + ch;
            // Out of range lookups are `undefined` upstream, which bitwise operators treat as 0
            var value = index < table.Length ? table[index] : 0;
            seed1 = value ^ (seed1 + seed2);
            seed2 = ch + seed1 + seed2 + (seed2 << 5) + 3;
        }
        return seed1;
    }

    /// <summary>Decrypt MPQ data in place.</summary>
    /// <remarks>
    /// Decrypts <c>size / 4</c> whole little-endian dwords like StormLib; trailing bytes are left untouched.
    /// Upstream reads one more (partial) dword for sizes that are not a multiple of 4, which reads past the block.
    /// </remarks>
    public void Decrypt(Span<byte> data, uint key, int offset = 0, int? size = null)
    {
        var length = size ?? data.Length;
        var hash = key;
        uint seed = 0xeeeeeeee;
        var table = MpqEncryption.TableValues;

        for (var i = 0; i + 4 <= length; i += 4)
        {
            seed += table[0x400 + (hash & 0xff)];
            var slot = data.Slice(offset, 4);
            var value = BinaryPrimitives.ReadUInt32LittleEndian(slot) ^ (hash + seed);
            BinaryPrimitives.WriteUInt32LittleEndian(slot, value);
            offset += 4;

            hash = ((~hash << 0x15) + 0x11111111) | (hash >> 0x0b);
            seed = value + seed + (seed << 5) + 3;
        }
    }
}

/// <summary>
/// MPQ loaded from a file, reading only the bytes needed. Good for large MPQ files.
/// </summary>
public sealed class MpqFile(string fileName) : Mpq(Path.GetFileName(fileName))
{
    private readonly Lock _lock = new();
    private Microsoft.Win32.SafeHandles.SafeFileHandle? _handle;

    public string FileName { get; } = fileName;

    private Microsoft.Win32.SafeHandles.SafeFileHandle Handle
    {
        get
        {
            lock (_lock)
            {
                return _handle ??= File.OpenHandle(FileName, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous | FileOptions.RandomAccess);
            }
        }
    }

    public override async Task<byte[]> ReadAsync(long offset, int byteCount)
    {
        var output = new byte[byteCount];
        var read = 0;
        while (read < byteCount)
        {
            var n = await RandomAccess.ReadAsync(Handle, output.AsMemory(read), offset + read).ConfigureAwait(false);
            if (n == 0) break;
            read += n;
        }
        return output;
    }

    public override ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            _handle?.Dispose();
            _handle = null;
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// MPQ reader from memory. Fast for small MPQ files and for files that are already in memory.
/// </summary>
public sealed class MpqBuffer(byte[] buffer, string? name = null) : Mpq(name)
{
    public byte[] Buffer { get; } = buffer;

    /// <remarks>Returns a copy (upstream returns a view); extraction decrypts in place.</remarks>
    public override Task<byte[]> ReadAsync(long offset, int byteCount)
    {
        var start = (int)Math.Min(offset, Buffer.Length);
        var end = (int)Math.Min(offset + byteCount, Buffer.Length);
        return Task.FromResult(Buffer.AsSpan(start, end - start).ToArray());
    }

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

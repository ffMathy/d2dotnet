using D2Dotnet.Map.Binary;

namespace D2Dotnet.Map.Bintools.Readers;

/// <summary>A .tbl hash table node (upstream <c>HashNode</c> / <c>LangNode</c>).</summary>
public sealed record LangNode(
    // Is the record active, generally "1"
    byte Active,
    ushort Index,
    uint Hash,
    // Lang key (read at an absolute offset)
    string Key,
    // Lang value (read at an absolute offset)
    string Value,
    // Length of the value, includes null byte
    ushort ValueLength);

/// <summary>A parsed .tbl string table (upstream <c>LangReader</c>).</summary>
public sealed record LangTable(
    ushort Crc,
    // Number of records
    ushort Count,
    // Number of records in hash table
    uint CountHash,
    // Table version generally 0
    byte Version,
    // Offset to the first data record
    uint DataOffset,
    // Max number of collisions for string key search based on its hash value
    uint MaxTries,
    // File size in bytes
    uint FileSize,
    // Hash table index
    IReadOnlyList<ushort> Indexes,
    // Hash table containing all the key value pairs
    IReadOnlyList<LangNode> HashTable);

/// <summary>Reader for Diablo 2 string tables (string.tbl, expansionstring.tbl, patchstring.tbl).</summary>
public static class LangReader
{
    public static LangTable Parse(byte[] bytes) => Parse(new BinaryCursor(bytes));

    public static LangTable Parse(BinaryCursor bp)
    {
        var crc = bp.Lu16();
        var count = bp.Lu16();
        var countHash = bp.Lu32();
        var version = bp.U8();
        var dataOffset = bp.Lu32();
        var maxTries = bp.Lu32();
        var fileSize = bp.Lu32();

        var indexes = new ushort[count];
        for (var i = 0; i < count; i++) indexes[i] = bp.Lu16();

        var hashTable = new List<LangNode>((int)Math.Min(countHash, 1 << 20));
        for (var i = 0; i < countHash; i++)
        {
            hashTable.Add(new LangNode(
                Active: bp.U8(),
                Index: bp.Lu16(),
                Hash: bp.Lu32(),
                Key: ReadOffsetString(bp),
                Value: ReadOffsetString(bp),
                ValueLength: bp.Lu16()));
        }

        return new LangTable(crc, count, countHash, version, dataOffset, maxTries, fileSize, indexes, hashTable);
    }

    /// <summary>binparse <c>bp.offset(bp.lu32, bp.string())</c>: an absolute offset to a null terminated string.</summary>
    private static string ReadOffsetString(BinaryCursor bp)
    {
        var readAt = bp.Lu32();
        if (readAt > bp.Length)
            throw new BinaryOverflowException($"[Offset:lu32:String]: BufferOverflow attempted read at {Hex.ToHex(readAt)}");
        return new BinaryCursor(bp.Buffer, (int)readAt).NullString();
    }
}

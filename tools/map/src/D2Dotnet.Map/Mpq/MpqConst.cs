namespace D2Dotnet.Map.Mpq;

/// <summary>Hash types used by <see cref="Mpq.Hash"/> (upstream <c>MpqHashType</c>).</summary>
public enum MpqHashType
{
    TableOffset = 0,
    HashA = 1,
    HashB = 2,
    Table = 3,
}

/// <summary>Sector compression types (upstream <c>MpqCompressionType</c>).</summary>
public enum MpqCompressionType
{
    Huffman = 0x01,
    Zlib = 0x02,
    PkWare = 0x08,
    BZip2 = 0x10,
}

/// <summary>Block table flags (upstream <c>MpqFlags</c>).</summary>
[Flags]
public enum MpqFlags : uint
{
    Implode = 0x00000100,
    Compressed = 0x00000200,
    Encrypted = 0x00010000,
    EncryptionFix = 0x00020000,
    SingleUnit = 0x01000000,
    Crc = 0x04000000,
    Exists = 0x80000000,
}

/// <summary>MPQ archive format versions (upstream <c>MpqFormatVersion</c>).</summary>
public enum MpqFormatVersion
{
    Version1 = 0,
    Version2 = 1,
    Version3 = 2,
    Version4 = 3,
}

using D2Dotnet.Map.Binary;

namespace D2Dotnet.Map.Mpq;

/// <summary>MPQ archive header (upstream <c>MpqHeaderReader</c>).</summary>
public sealed record MpqHeader(
    string Magic,
    uint HeaderSize,
    uint ArchivedSize,
    ushort FormatVersion,
    ushort SectorSizeShift,
    uint HashTableOffset,
    uint BlockTableOffset,
    uint HashTableEntries,
    uint BlockTableEntries)
{
    /// <summary>Size in bytes of the fields read by <see cref="Read"/>.</summary>
    public const int Size = 32;

    public static MpqHeader Read(BinaryCursor bp) => new(
        Magic: bp.FixedString(4),
        HeaderSize: bp.Lu32(),
        ArchivedSize: bp.Lu32(),
        FormatVersion: bp.Lu16(),
        SectorSizeShift: bp.Lu16(),
        HashTableOffset: bp.Lu32(),
        BlockTableOffset: bp.Lu32(),
        HashTableEntries: bp.Lu32(),
        BlockTableEntries: bp.Lu32());
}

/// <summary>Hash table entry (upstream <c>MpqHashTableEntry</c>).</summary>
public sealed record MpqHashEntry(uint HashA, uint HashB, ushort Locale, ushort Platform, uint BlockTableIndex)
{
    public static MpqHashEntry Read(BinaryCursor bp) => new(
        HashA: bp.Lu32(),
        HashB: bp.Lu32(),
        Locale: bp.Lu16(),
        Platform: bp.Lu16(),
        BlockTableIndex: bp.Lu32());
}

/// <summary>Block table entry (upstream <c>MpqBlockTableEntry</c>).</summary>
public sealed class MpqBlockEntry
{
    public uint Offset { get; init; }
    public uint ArchivedSize { get; init; }
    public uint Size { get; init; }
    public MpqFlags Flags { get; init; }

    public static MpqBlockEntry Read(BinaryCursor bp) => new()
    {
        Offset = bp.Lu32(),
        ArchivedSize = bp.Lu32(),
        Size = bp.Lu32(),
        Flags = (MpqFlags)bp.Lu32(),
    };
}

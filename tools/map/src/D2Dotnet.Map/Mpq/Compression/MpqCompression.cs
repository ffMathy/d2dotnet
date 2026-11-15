namespace D2Dotnet.Map.Mpq.Compression;

/// <summary>Sector decompression (upstream <c>compression/index.ts</c> and <c>compression/compress.pkware.ts</c>).</summary>
public static class MpqCompression
{
    /// <summary>Decompress a PKWARE imploded block (upstream <c>decompressPkWare</c>).</summary>
    public static byte[] DecompressPkWare(ReadOnlySpan<byte> sector, int offset, int size) =>
        PkWareExplode.Decompress(sector.Slice(offset, size));

    /// <summary>
    /// Decompress a sector whose first byte is the compression type (upstream <c>decompressSector</c>).
    /// Only PKWARE is supported, as upstream; stacked compressions are not supported.
    /// </summary>
    /// <exception cref="NotSupportedException">The compression type is not PKWARE.</exception>
    public static byte[] DecompressSector(ReadOnlySpan<byte> sector, int offset, int size)
    {
        var compressionType = sector[offset];

        // TODO Compressions can be stacked
        return compressionType switch
        {
            (byte)MpqCompressionType.PkWare => DecompressPkWare(sector, offset + 1, size - 1),
            _ => throw new NotSupportedException(
                $"Mpq.Compression: {(Enum.IsDefined((MpqCompressionType)compressionType) ? ((MpqCompressionType)compressionType).ToString() : "undefined")}:{compressionType} not supported"),
        };
    }
}

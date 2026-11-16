using System.Buffers.Binary;
using System.Security.Cryptography;

namespace D2Dotnet.Files.Casc;

/// <summary>One CKey page entry of the ENCODING file.</summary>
internal sealed record EncodingEntry(byte[] CKey, ulong ContentSize, IReadOnlyList<byte[]> EKeys);

/// <summary>
/// The ENCODING manifest: content key (MD5 of the decoded file) to encoding keys.
/// </summary>
/// <remarks>
/// Layout (big-endian), per CascLib <c>CaptureEncodingHeader</c>/<c>LoadEncodingCKeyPage</c> and wowdev.wiki TACT:
/// <code>
/// char[2] "EN"  u8 version (1)  u8 ckeyLength (16)  u8 ekeyLength (16)
/// u16 ckeyPageSizeKB  u16 ekeyPageSizeKB  u32 ckeyPageCount  u32 ekeyPageCount  u8 0  u32 especBlockSize
/// char especs[especBlockSize]                       NUL-separated ESpec strings
/// { u8 firstCKey[16]; u8 pageMd5[16]; } [ckeyPageCount]
/// page[ckeyPageCount] of ckeyPageSizeKB*1024 bytes:
///     { u8 ekeyCount; u40 contentSize; u8 ckey[16]; u8 ekeys[ekeyCount][16]; } ... zero padding
/// (then the EKey -> ESpec pages, not needed for reading)
/// </code>
/// TVFS manifests reference files by EKey directly, so for D2R this table is only needed to turn
/// the build config's CKeys into EKeys when the config does not carry the EKey itself.
/// </remarks>
internal sealed class EncodingTable
{
    private readonly Dictionary<string, EncodingEntry> _byCKey;

    private EncodingTable(Dictionary<string, EncodingEntry> byCKey, IReadOnlyList<string> especs)
    {
        _byCKey = byCKey;
        ESpecs = especs;
    }

    public int Count => _byCKey.Count;

    /// <summary>The ESpec strings (how files were encoded, e.g. <c>z</c> or <c>b:{16K*=z}</c>).</summary>
    public IReadOnlyList<string> ESpecs { get; }

    public bool TryGet(ReadOnlySpan<byte> ckey, out EncodingEntry entry) =>
        _byCKey.TryGetValue(Hex.Of(ckey), out entry!);

    public static EncodingTable Parse(ReadOnlySpan<byte> data)
    {
        const int headerSize = 22;
        if (data.Length < headerSize || data[0] != (byte)'E' || data[1] != (byte)'N')
            throw new CascFormatException("ENCODING file has no 'EN' signature.");
        if (data[2] != 1)
            throw new CascFormatException($"Unsupported ENCODING version {data[2]}.");
        int ckeyLength = data[3], ekeyLength = data[4];
        if (ckeyLength != 16 || ekeyLength != 16)
            throw new CascFormatException($"Unsupported ENCODING key sizes {ckeyLength}/{ekeyLength}.");
        var ckeyPageSize = BinaryPrimitives.ReadUInt16BigEndian(data[5..]) * 1024;
        var ckeyPageCount = BinaryPrimitives.ReadUInt32BigEndian(data[9..]);
        var especSize = BinaryPrimitives.ReadUInt32BigEndian(data[18..]);

        long pos = headerSize;
        if (pos + especSize > data.Length)
            throw new CascFormatException("ENCODING ESpec block is truncated.");
        var especs = System.Text.Encoding.ASCII.GetString(data.Slice((int)pos, (int)especSize))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        pos += especSize;

        var tocStart = pos;
        var pagesStart = tocStart + 32L * ckeyPageCount;
        if (ckeyPageSize == 0 || pagesStart + (long)ckeyPageSize * ckeyPageCount > data.Length)
            throw new CascFormatException("ENCODING CKey pages are truncated.");

        var byCKey = new Dictionary<string, EncodingEntry>();
        Span<byte> md5 = stackalloc byte[16];
        for (var p = 0; p < ckeyPageCount; p++)
        {
            var toc = data.Slice((int)(tocStart + 32L * p), 32);
            var page = data.Slice((int)(pagesStart + (long)ckeyPageSize * p), ckeyPageSize);
            MD5.HashData(page, md5);
            if (!md5.SequenceEqual(toc[16..]))
                throw new CascFormatException($"ENCODING CKey page {p} fails its MD5 check.");

            var e = 0;
            while (e + 6 + ckeyLength <= page.Length)
            {
                var count = page[e];
                if (count == 0)
                    break; // zero padding to the end of the page
                var entryLength = 6 + ckeyLength + count * ekeyLength;
                if (e + entryLength > page.Length)
                    throw new CascFormatException($"ENCODING CKey page {p} has an entry that runs past the page.");
                var contentSize = BigEndian.ReadUInt40(page[(e + 1)..]);
                var ckey = page.Slice(e + 6, ckeyLength).ToArray();
                var ekeys = new byte[count][];
                for (var k = 0; k < count; k++)
                    ekeys[k] = page.Slice(e + 6 + ckeyLength + k * ekeyLength, ekeyLength).ToArray();
                byCKey.TryAdd(Hex.Of(ckey), new EncodingEntry(ckey, contentSize, ekeys));
                e += entryLength;
            }
        }
        return new EncodingTable(byCKey, especs);
    }
}

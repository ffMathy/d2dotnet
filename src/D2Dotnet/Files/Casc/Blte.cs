using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace D2Dotnet.Files.Casc;

/// <summary>
/// Decoder for BLTE, the block-encoding container every CASC file is stored in.
/// </summary>
/// <remarks>
/// Layout (all integers big-endian), as parsed by CascLib's <c>ParseBlteHeader</c>/<c>DecodeFileFrame</c>:
/// <code>
/// char[4]  "BLTE"
/// u32      headerSize   0 = one frame that runs to the end of the data, which starts at offset 8;
///                       otherwise the frame table size including these 8 bytes, = 12 + 24 * frameCount
/// -- only when headerSize != 0 --
/// u8       0x0F         table format
/// u24      frameCount
/// frame[frameCount] { u32 encodedSize; u32 contentSize; u8 md5[16] /* of the encoded frame */ }
/// -- frame data follows at offset headerSize (or 8) --
/// </code>
/// Each frame starts with a mode byte: <c>'N'</c> stored, <c>'Z'</c> zlib (with zlib header),
/// <c>'E'</c> encrypted (then another mode), <c>'F'</c> nested BLTE. This decoder handles N and Z,
/// reports E with <see cref="CascEncryptedException"/>, and rejects anything else.
/// </remarks>
internal static class Blte
{
    public const uint Magic = 0x424C5445; // "BLTE" read big-endian

    /// <summary>Decode a whole BLTE stream.</summary>
    /// <param name="data">The BLTE bytes, starting at the <c>BLTE</c> signature.</param>
    /// <param name="verifyFrameHashes">Check each frame's MD5 from the frame table.</param>
    public static byte[] Decode(ReadOnlySpan<byte> data, bool verifyFrameHashes = true)
    {
        if (data.Length < 8 || BinaryPrimitives.ReadUInt32BigEndian(data) != Magic)
            throw new CascFormatException("Missing BLTE signature.");

        var headerSize = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        if (headerSize == 0)
            return DecodeFrame(data[8..], contentSize: null, frameIndex: 0);

        if (data.Length < 12)
            throw new CascFormatException("BLTE frame table is truncated.");
        if (data[8] != 0x0F)
            throw new CascFormatException($"Unsupported BLTE frame table format 0x{data[8]:X2} (expected 0x0F).");
        var frameCount = (int)BigEndian.ReadUInt24(data[9..]);
        if (frameCount == 0 || headerSize != 12 + 24L * frameCount)
            throw new CascFormatException($"BLTE header size {headerSize} does not match {frameCount} frames.");
        if (data.Length < headerSize)
            throw new CascFormatException("BLTE frame table is truncated.");

        long totalContent = 0, totalEncoded = 0;
        for (var i = 0; i < frameCount; i++)
        {
            var entry = data.Slice(12 + 24 * i, 24);
            totalEncoded += BinaryPrimitives.ReadUInt32BigEndian(entry);
            totalContent += BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
        }
        if (headerSize + totalEncoded > data.Length)
            throw new CascFormatException($"BLTE data is truncated: frames need {headerSize + totalEncoded} bytes, have {data.Length}.");
        if (totalContent > Array.MaxLength)
            throw new CascFormatException($"BLTE content of {totalContent} bytes is too large.");

        var output = new byte[totalContent];
        var outPos = 0;
        var inPos = (int)headerSize;
        Span<byte> md5 = stackalloc byte[16];
        for (var i = 0; i < frameCount; i++)
        {
            var entry = data.Slice(12 + 24 * i, 24);
            var encodedSize = (int)BinaryPrimitives.ReadUInt32BigEndian(entry);
            var contentSize = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            var frame = data.Slice(inPos, encodedSize);
            if (verifyFrameHashes)
            {
                MD5.HashData(frame, md5);
                if (!md5.SequenceEqual(entry.Slice(8, 16)))
                    throw new CascFormatException($"BLTE frame {i} fails its MD5 check.");
            }
            var decoded = DecodeFrame(frame, contentSize, i);
            decoded.CopyTo(output, outPos);
            outPos += contentSize;
            inPos += encodedSize;
        }
        return output;
    }

    private static byte[] DecodeFrame(ReadOnlySpan<byte> frame, int? contentSize, int frameIndex)
    {
        if (frame.Length == 0)
            throw new CascFormatException($"BLTE frame {frameIndex} is empty.");
        var payload = frame[1..];
        switch ((char)frame[0])
        {
            case 'N':
                if (contentSize is { } n && payload.Length != n)
                    throw new CascFormatException($"BLTE frame {frameIndex} stores {payload.Length} bytes, expected {n}.");
                return payload.ToArray();

            case 'Z':
                return Inflate(payload, contentSize, frameIndex);

            case 'E':
                throw Encrypted(payload, frameIndex);

            case 'F':
                throw new CascFormatException($"BLTE frame {frameIndex} uses nested BLTE ('F'), which is not supported.");

            default:
                throw new CascFormatException($"BLTE frame {frameIndex} has unknown encoding mode 0x{frame[0]:X2}.");
        }
    }

    private static byte[] Inflate(ReadOnlySpan<byte> payload, int? contentSize, int frameIndex)
    {
        try
        {
            using var input = new MemoryStream(payload.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            if (contentSize is not { } size)
            {
                using var all = new MemoryStream();
                zlib.CopyTo(all);
                return all.ToArray();
            }

            // Like CascLib: a stream that inflates to less than the frame's content size is zero-padded.
            var output = new byte[size];
            var read = 0;
            while (read < size)
            {
                var n = zlib.Read(output, read, size - read);
                if (n == 0)
                    break;
                read += n;
            }
            if (read == size && zlib.ReadByte() >= 0)
                throw new CascFormatException($"BLTE frame {frameIndex} inflates to more than {size} bytes.");
            return output;
        }
        catch (InvalidDataException e)
        {
            throw new CascFormatException($"BLTE frame {frameIndex} holds corrupt zlib data.", e);
        }
    }

    // 'E' frame payload: u8 keyNameSize (8), key name, u8 ivSize (4 or 8), iv, u8 cipher ('S' Salsa20, 'A' ARC4), data.
    private static CascFormatException Encrypted(ReadOnlySpan<byte> payload, int frameIndex)
    {
        if (payload.Length < 1 + 8 + 1)
            return new CascFormatException($"BLTE frame {frameIndex} is encrypted but its header is truncated.");
        var keyNameSize = payload[0];
        if (keyNameSize != 8)
            return new CascFormatException($"BLTE frame {frameIndex} is encrypted with an unsupported key name size {keyNameSize}.");
        var keyName = BinaryPrimitives.ReadUInt64LittleEndian(payload[1..]);
        var ivSize = payload[9];
        var cipherPos = 10 + ivSize;
        var cipher = cipherPos < payload.Length ? (char)payload[cipherPos] : '?';
        return new CascEncryptedException(keyName, cipher);
    }
}

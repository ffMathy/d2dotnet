using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using D2Dotnet.Files;

namespace D2Dotnet.Tests.Files.Writer;

/// <summary>One BLTE frame to write: its encoding mode and how many content bytes it holds.</summary>
public readonly record struct BlteFrame(char Mode, int Size);

/// <summary>How a file is BLTE-encoded.</summary>
/// <param name="Frames">Frames with a frame table; null writes one frame with no table (header size 0).</param>
/// <param name="HeaderlessMode">Mode of the single frame when <paramref name="Frames"/> is null.</param>
public sealed record BlteLayout(IReadOnlyList<BlteFrame>? Frames, char HeaderlessMode = 'Z')
{
    public static readonly BlteLayout HeaderlessZ = new(null, 'Z');
    public static readonly BlteLayout HeaderlessN = new(null, 'N');

    /// <summary>One frame of the whole content, with a frame table.</summary>
    public static BlteLayout Single(char mode) => new([new BlteFrame(mode, -1)]);

    /// <summary>Frames of <paramref name="chunkSize"/> bytes (the last one shorter), alternating the given modes.</summary>
    public static BlteLayout Chunked(int contentLength, int chunkSize, params char[] modes)
    {
        var frames = new List<BlteFrame>();
        for (int pos = 0, i = 0; pos < contentLength; pos += chunkSize, i++)
            frames.Add(new BlteFrame(modes[i % modes.Length], Math.Min(chunkSize, contentLength - pos)));
        return new BlteLayout(frames);
    }

    /// <summary>The ESpec string describing this layout, as stored in ENCODING.</summary>
    public string ESpec => Frames is null
        ? char.ToLowerInvariant(HeaderlessMode).ToString()
        : "b:{" + string.Join(",", Frames.Select(f => $"{f.Size}={char.ToLowerInvariant(f.Mode)}")) + "}";
}

/// <summary>Test-side BLTE encoder.</summary>
public static class BlteWriter
{
    /// <summary>Encode <paramref name="content"/>. Returns the BLTE bytes and the EKey.</summary>
    /// <remarks>
    /// The EKey is the MD5 of the BLTE header (signature through frame table) when there is a frame
    /// table, and of the whole BLTE stream when there is none (wowdev.wiki BLTE).
    /// </remarks>
    public static (byte[] Blte, byte[] EKey) Encode(byte[] content, BlteLayout layout)
    {
        if (layout.Frames is null)
        {
            var blte = new byte[8];
            "BLTE"u8.CopyTo(blte);
            var frame = EncodeFrame(content, layout.HeaderlessMode, 0);
            var all = blte.Concat(frame).ToArray();
            return (all, MD5.HashData(all));
        }

        var frames = layout.Frames.Select(f => f.Size < 0 ? f with { Size = content.Length } : f).ToList();
        if (frames.Sum(f => f.Size) != content.Length)
            throw new ArgumentException("Frame sizes do not add up to the content length.");

        var headerSize = 12 + 24 * frames.Count;
        var header = new byte[headerSize];
        "BLTE"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)headerSize);
        header[8] = 0x0F;
        header[9] = (byte)(frames.Count >> 16);
        header[10] = (byte)(frames.Count >> 8);
        header[11] = (byte)frames.Count;

        using var body = new MemoryStream();
        var pos = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            var encoded = EncodeFrame(content.AsSpan(pos, frames[i].Size).ToArray(), frames[i].Mode, i);
            var entry = header.AsSpan(12 + 24 * i, 24);
            BinaryPrimitives.WriteUInt32BigEndian(entry, (uint)encoded.Length);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)frames[i].Size);
            MD5.HashData(encoded).CopyTo(entry[8..]);
            body.Write(encoded);
            pos += frames[i].Size;
        }
        return (header.Concat(body.ToArray()).ToArray(), MD5.HashData(header));
    }

    public static byte[] EncodeFrame(byte[] content, char mode, int frameIndex) => mode switch
    {
        'N' => [(byte)'N', .. content],
        'Z' => [(byte)'Z', .. Zlib(content)],
        // An 'E' frame with a made-up key name: the reader must refuse it with a clear error.
        'E' => [(byte)'E', 8, .. BitConverter.GetBytes(0xFEEDFACE00C0FFEEUL), 4, 1, 2, 3, (byte)frameIndex, (byte)'S', .. content],
        _ => throw new ArgumentException($"Unsupported test BLTE mode '{mode}'."),
    };

    public static byte[] Zlib(byte[] content)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(content);
        return output.ToArray();
    }
}

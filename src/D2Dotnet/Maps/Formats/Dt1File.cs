// Port of libd2 packages/formats/src/dt1.zig (MIT, (c) 2026 jaenster).
//
// Parser for Diablo II `.dt1` tile-library files — the graphics + collision data for the
// tiles a DS1 places. For the collision grid we only need each tile's identity
// (orientation / mainIndex / subIndex / rarity) and its 5x5 block of per-subtile collision
// flags; the pixel blocks are skipped.
//
// Header layout (version 7.6, the 1.14d format):
//   0x000 i32 versionMajor (7)
//   0x004 i32 versionMinor (6)
//   0x008 .. 0x10C  unknown / zero padding (260 bytes)
//   0x10C i32 numTiles
//   0x110 i32 tileHeadersOffset
// Each tile header is 96 bytes:
//   +0x14 i32 orientation   +0x18 i32 mainIndex   +0x1C i32 subIndex
//   +0x20 i32 rarityOrFrame +0x28 u8[25] subTileFlags

using System.Buffers.Binary;

namespace D2Dotnet.Maps.Formats;

/// <summary>One tile-library entry. <see cref="Flags"/> is the 5x5 subtile collision block in
/// file order: index = row*5 + col, row 0 = top.</summary>
internal sealed class Dt1Tile
{
    public int Orientation { get; init; }
    public int Main { get; init; }
    public int Sub { get; init; }
    public int Rarity { get; init; }
    public byte[] Flags { get; init; } = new byte[25];
}

/// <summary>A parsed DT1 (identity + collision flags only).</summary>
internal sealed class Dt1File
{
    private const int HeaderNumTilesOffset = 0x10C;
    private const int TileHeaderSize = 96;

    public int VersionMajor { get; init; }
    public int VersionMinor { get; init; }
    public Dt1Tile[] Tiles { get; init; } = Array.Empty<Dt1Tile>();

    private Dictionary<(int, int, int), int[]>? _index;

    /// <summary>Tile indices sharing this identity, in the engine's candidate order: REVERSE
    /// record order, because D2CMP FINDTILE's chains (AddTileToChain 0x60cea0) are push-front
    /// over records 0..N-1 and the candidate order decides which rarity variant a roll lands on.</summary>
    public int[] Lookup(int orientation, int main, int sub)
    {
        var idx = _index;
        if (idx == null)
        {
            var build = new Dictionary<(int, int, int), List<int>>();
            for (int n = Tiles.Length - 1; n >= 0; n--)
            {
                var t = Tiles[n];
                var key = (t.Orientation, t.Main, t.Sub);
                if (!build.TryGetValue(key, out var list)) build[key] = list = new List<int>();
                list.Add(n);
            }
            idx = new Dictionary<(int, int, int), int[]>(build.Count);
            foreach (var kv in build) idx[kv.Key] = kv.Value.ToArray();
            _index = idx;
        }
        return idx.TryGetValue((orientation, main, sub), out var r) ? r : Array.Empty<int>();
    }

    private static int ReadI32(ReadOnlySpan<byte> b, int off)
    {
        if (off < 0 || off + 4 > b.Length) throw new InvalidDataException("dt1: truncated");
        return BinaryPrimitives.ReadInt32LittleEndian(b.Slice(off, 4));
    }

    /// <summary>Parse a raw .dt1 file.</summary>
    public static Dt1File Parse(ReadOnlySpan<byte> bytes)
    {
        int major = ReadI32(bytes, 0x00);
        int minor = ReadI32(bytes, 0x04);
        int numTiles = ReadI32(bytes, HeaderNumTilesOffset);
        int headersOff = ReadI32(bytes, HeaderNumTilesOffset + 4);
        if (numTiles < 0 || headersOff < 0) throw new InvalidDataException("dt1: corrupt header");
        if ((long)headersOff + (long)numTiles * TileHeaderSize > bytes.Length) throw new InvalidDataException("dt1: truncated");

        var tiles = new Dt1Tile[numTiles];
        for (int i = 0; i < numTiles; i++)
        {
            int o = headersOff + i * TileHeaderSize;
            tiles[i] = new Dt1Tile
            {
                Orientation = ReadI32(bytes, o + 0x14),
                Main = ReadI32(bytes, o + 0x18),
                Sub = ReadI32(bytes, o + 0x1C),
                Rarity = ReadI32(bytes, o + 0x20),
                Flags = bytes.Slice(o + 0x28, 25).ToArray(),
            };
        }
        return new Dt1File { VersionMajor = major, VersionMinor = minor, Tiles = tiles };
    }
}

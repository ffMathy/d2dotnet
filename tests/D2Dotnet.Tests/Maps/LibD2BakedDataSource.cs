using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using D2Dotnet.Data;
using D2Dotnet.Maps.Engine;
using D2Dotnet.Maps.Formats;

namespace D2Dotnet.Tests.Maps;

/// <summary>Test-only <see cref="IMapDataSource"/> over libd2's own baked data: the excel .txt
/// files of packages/data/src/excel, the DS1 blob (packages/drlg/src/blobs/ds1_blob.bin), the DT1
/// subtile-flag blob (packages/formats/src/blobs/dt1_blob.bin) and PresetObjectTable.bin — exactly
/// what the libd2 native generator embeds, so the port can be compared to it bit for bit.</summary>
internal sealed class LibD2BakedDataSource : IMapDataSource
{
    private readonly string _excelDir;
    private readonly Dictionary<string, byte[]> _ds1Records;
    private readonly Dictionary<string, byte[]> _dt1Records;
    private readonly int[] _presetObjects;
    private readonly ConcurrentDictionary<string, ExcelTable?> _tables = new(StringComparer.OrdinalIgnoreCase);

    public LibD2BakedDataSource(string libd2Root)
    {
        _excelDir = Path.Combine(libd2Root, "packages", "data", "src", "excel");
        _ds1Records = BakedBlob.ReadIndex(File.ReadAllBytes(Path.Combine(libd2Root, "packages", "drlg", "src", "blobs", "ds1_blob.bin")));
        _dt1Records = BakedBlob.ReadIndex(File.ReadAllBytes(Path.Combine(libd2Root, "packages", "formats", "src", "blobs", "dt1_blob.bin")));
        var pot = File.ReadAllBytes(Path.Combine(libd2Root, "packages", "drlg", "src", "excel", "PresetObjectTable.bin"));
        _presetObjects = new int[pot.Length / 4];
        for (int i = 0; i < _presetObjects.Length; i++) _presetObjects[i] = BinaryPrimitives.ReadInt32LittleEndian(pot.AsSpan(i * 4));
    }

    public IReadOnlyDictionary<string, byte[]> Ds1Records => _ds1Records;
    public IReadOnlyDictionary<string, byte[]> Dt1Records => _dt1Records;

    public ExcelTable? GetTable(string name) => _tables.GetOrAdd(name, n =>
    {
        foreach (var f in Directory.GetFiles(_excelDir, "*.txt"))
            if (string.Equals(Path.GetFileNameWithoutExtension(f), n, StringComparison.OrdinalIgnoreCase))
                return ExcelTable.Parse(File.ReadAllBytes(f), Path.GetFileName(f));
        return null;
    });

    public Ds1File? GetDs1(string tilesRelativePath) =>
        _ds1Records.TryGetValue(tilesRelativePath.ToLowerInvariant(), out var rec) ? BakedBlob.UnpackDs1(rec) : null;

    public Dt1File? GetDt1(string tilesRelativePath) =>
        _dt1Records.TryGetValue(tilesRelativePath.ToLowerInvariant(), out var rec) ? BakedBlob.UnpackDt1(rec) : null;

    public int[] GetPresetObjectTable() => _presetObjects;
}

/// <summary>libd2's baked blob container (ds1_blob.zig / dt1_blob.zig): a raw-deflated
/// <c>"D2DS1B01"</c> | u32 count | count × (u16 keyLen, key, u32 recOff, u32 recLen) | records.
/// Includes the bake tools' <c>packRecord</c> so a raw parse can be re-packed and compared to the
/// baked record byte for byte.</summary>
internal static class BakedBlob
{
    public const string Magic = "D2DS1B01";

    public static byte[] Inflate(byte[] comp)
    {
        using var ms = new MemoryStream(comp);
        using var d = new DeflateStream(ms, CompressionMode.Decompress);
        using var o = new MemoryStream();
        d.CopyTo(o);
        return o.ToArray();
    }

    public static Dictionary<string, byte[]> ReadIndex(byte[] compressed)
    {
        var blob = Inflate(compressed);
        if (blob.Length < 12 || System.Text.Encoding.ASCII.GetString(blob, 0, 8) != Magic) throw new InvalidDataException("bad blob magic");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(8));
        int pos = 12;
        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (uint i = 0; i < count; i++)
        {
            int keyLen = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(pos));
            pos += 2;
            string key = System.Text.Encoding.ASCII.GetString(blob, pos, keyLen);
            pos += keyLen;
            int off = (int)BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(pos));
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(pos + 4));
            pos += 8;
            map[key] = blob.AsSpan(off, len).ToArray();
        }
        return map;
    }

    private ref struct Cursor
    {
        private readonly ReadOnlySpan<byte> _b;
        public int Pos;

        public Cursor(ReadOnlySpan<byte> b)
        {
            _b = b;
            Pos = 0;
        }

        public uint U32()
        {
            if (Pos + 4 > _b.Length) throw new InvalidDataException("bad blob record");
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(_b.Slice(Pos));
            Pos += 4;
            return v;
        }

        public int I32() => (int)U32();

        public byte U8() => Pos < _b.Length ? _b[Pos++] : throw new InvalidDataException("bad blob record");

        public ulong Varint()
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                byte b = U8();
                result |= (ulong)(b & 0x7f) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return result;
        }

        public uint[] Cells(int total)
        {
            var o = new uint[total];
            int i = 0;
            while (i < total)
            {
                uint v = U32();
                int run = (int)Varint();
                if (i + run > total) throw new InvalidDataException("bad blob run");
                o.AsSpan(i, run).Fill(v);
                i += run;
            }
            return o;
        }
    }

    /// <summary>ds1_blob.unpack: a Ds1File with the fields fillDrlgFileFromDs1 reads (npc paths empty).</summary>
    public static Ds1File UnpackDs1(byte[] rec)
    {
        var c = new Cursor(rec);
        uint width = c.U32(), height = c.U32();
        int act = c.I32(), actId = c.I32();
        int total = checked((int)(width * height));
        var walls = new Ds1WallLayer[c.U32()];
        for (int i = 0; i < walls.Length; i++)
        {
            var w = c.Cells(total);
            var o = c.Cells(total);
            walls[i] = new Ds1WallLayer(w, o);
        }
        var floors = new uint[c.U32()][];
        for (int i = 0; i < floors.Length; i++) floors[i] = c.Cells(total);
        var shadow = c.Cells(total);
        uint[]? tags = c.U8() == 1 ? c.Cells(total) : null;
        var groups = new Ds1SubstGroup[c.U32()];
        for (int i = 0; i < groups.Length; i++) groups[i] = new Ds1SubstGroup(c.I32(), c.I32(), c.I32(), c.I32(), c.I32());
        var objs = new Ds1Object[c.U32()];
        for (int i = 0; i < objs.Length; i++) objs[i] = new Ds1Object(c.I32(), c.I32(), c.I32(), c.I32(), c.I32());
        return new Ds1File
        {
            Version = 0,
            Width = width,
            Height = height,
            Act = act,
            ActId = actId,
            WallLayers = walls,
            FloorLayers = floors,
            Shadow = shadow,
            SubstTags = tags,
            SubstGroups = groups,
            Objects = objs,
            BytesConsumed = c.Pos,
        };
    }

    /// <summary>dt1_blob.unpack.</summary>
    public static Dt1File UnpackDt1(byte[] rec)
    {
        var c = new Cursor(rec);
        var tiles = new Dt1Tile[c.U32()];
        for (int i = 0; i < tiles.Length; i++)
        {
            int o = c.I32(), m = c.I32(), s = c.I32(), r = c.I32();
            var flags = new byte[25];
            for (int k = 0; k < 25; k++) flags[k] = c.U8();
            tiles[i] = new Dt1Tile { Orientation = o, Main = m, Sub = s, Rarity = r, Flags = flags };
        }
        return new Dt1File { Tiles = tiles };
    }

    private static void PutU32(List<byte> l, uint v)
    {
        l.Add((byte)v);
        l.Add((byte)(v >> 8));
        l.Add((byte)(v >> 16));
        l.Add((byte)(v >> 24));
    }

    private static void PutVarint(List<byte> l, ulong v)
    {
        while (true)
        {
            byte b = (byte)(v & 0x7f);
            v >>= 7;
            if (v != 0) b |= 0x80;
            l.Add(b);
            if (v == 0) break;
        }
    }

    private static void PackCells(List<byte> l, uint[] cells)
    {
        int i = 0;
        while (i < cells.Length)
        {
            uint v = cells[i];
            int run = 1;
            while (i + run < cells.Length && cells[i + run] == v) run++;
            PutU32(l, v);
            PutVarint(l, (ulong)run);
            i += run;
        }
    }

    /// <summary>ds1_blob.packRecord (the bake tool's serializer) over a parsed Ds1File.</summary>
    public static byte[] PackDs1(Ds1File d)
    {
        var l = new List<byte>();
        PutU32(l, d.Width);
        PutU32(l, d.Height);
        PutU32(l, (uint)d.Act);
        PutU32(l, (uint)d.ActId);
        PutU32(l, (uint)d.WallLayers.Length);
        foreach (var wl in d.WallLayers)
        {
            PackCells(l, wl.Wall);
            PackCells(l, wl.Orient);
        }
        PutU32(l, (uint)d.FloorLayers.Length);
        foreach (var fl in d.FloorLayers) PackCells(l, fl);
        PackCells(l, d.Shadow);
        if (d.SubstTags != null)
        {
            l.Add(1);
            PackCells(l, d.SubstTags);
        }
        else
        {
            l.Add(0);
        }
        PutU32(l, (uint)d.SubstGroups.Length);
        foreach (var g in d.SubstGroups)
        {
            PutU32(l, (uint)g.X);
            PutU32(l, (uint)g.Y);
            PutU32(l, (uint)g.W);
            PutU32(l, (uint)g.H);
            PutU32(l, (uint)g.Unknown);
        }
        PutU32(l, (uint)d.Objects.Length);
        foreach (var o in d.Objects)
        {
            PutU32(l, (uint)o.Kind);
            PutU32(l, (uint)o.Id);
            PutU32(l, (uint)o.X);
            PutU32(l, (uint)o.Y);
            PutU32(l, (uint)o.Flags);
        }
        return l.ToArray();
    }

    /// <summary>dt1_blob.packRecord over a parsed Dt1File.</summary>
    public static byte[] PackDt1(Dt1File d)
    {
        var l = new List<byte>();
        PutU32(l, (uint)d.Tiles.Length);
        foreach (var t in d.Tiles)
        {
            PutU32(l, (uint)t.Orientation);
            PutU32(l, (uint)t.Main);
            PutU32(l, (uint)t.Sub);
            PutU32(l, (uint)t.Rarity);
            l.AddRange(t.Flags);
        }
        return l.ToArray();
    }
}

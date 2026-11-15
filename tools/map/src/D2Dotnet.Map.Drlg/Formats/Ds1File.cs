// Port of libd2 packages/formats/src/ds1.zig (MIT, (c) 2026 jaenster).
//
// Parser for Diablo II `.ds1` preset map files — the tile grid that defines a level's
// hand-authored layout (floors, walls, shadows, objects).
//
// Ported faithfully from the reconstructed 1.14d `ParsePresetsOfDrlgFile`
// (D2Common/Drlg/Preset.cpp, ~lines 82-449). The reconstruction reads the DS1 straight out
// of a loaded file buffer that the decompiler aliases through `D2ArchiveStrc` fields; here we
// drop that aliasing and walk the byte stream with a plain cursor. The version gates are the
// authoritative part and are reproduced exactly.
//
// Cell counts: the DS1 header stores width/height as (tiles - 1); the real tile grid is
// `(width+1) * (height+1)` cells. We store the *actual* tile counts.

using System.Buffers.Binary;

namespace D2Dotnet.Map.Drlg.Formats;

/// <summary>One wall layer: the wall cells plus their paired orientation/type cells (raw
/// packed u32 per cell: prop1 = bits 0..7, prop2 = bits 8..15, orientation = bits 16..23).</summary>
public sealed record Ds1WallLayer(uint[] Wall, uint[] Orient);

/// <summary>A preset object record: { type, id, x, y, flags }. `Flags` is only present for
/// version >= 6; older files read back 0.</summary>
public readonly record struct Ds1Object(int Kind, int Id, int X, int Y, int Flags);

/// <summary>A substitution-group record (recon D2DrlgSubstGroupStrc): a tile box plus one
/// unknown field (version >= 13, else 0).</summary>
public readonly record struct Ds1SubstGroup(int X, int Y, int W, int H, int Unknown);

/// <summary>One node of an NPC walk path.</summary>
public readonly record struct Ds1PathPoint(int X, int Y, int Action);

/// <summary>An NPC-path stream entry, matched to the preset object sharing its coords.</summary>
public sealed record Ds1NpcPath(int X, int Y, int? ObjectIndex, Ds1PathPoint[] Points);

/// <summary>A parsed DS1 (the fields the generator reads, plus the NPC paths).</summary>
public sealed class Ds1File
{
    public int Version { get; init; }
    /// <summary>Actual tile counts (header value + 1).</summary>
    public uint Width { get; init; }
    public uint Height { get; init; }
    /// <summary>The version>=10 act field that gates the subst-tag layer and subst groups (nAct).</summary>
    public int Act { get; init; }
    /// <summary>The version>=8 act field clamped to &lt;=4 (nActId, used for preset lookups).</summary>
    public int ActId { get; init; }
    public Ds1WallLayer[] WallLayers { get; init; } = Array.Empty<Ds1WallLayer>();
    public uint[][] FloorLayers { get; init; } = Array.Empty<uint[]>();
    public uint[] Shadow { get; init; } = Array.Empty<uint>();
    /// <summary>Substitution-group tag layer, present only when Act is 1 or 2.</summary>
    public uint[]? SubstTags { get; init; }
    public Ds1Object[] Objects { get; init; } = Array.Empty<Ds1Object>();
    /// <summary>Substitution groups (version >= 12, act 1/2). Empty for other files.</summary>
    public Ds1SubstGroup[] SubstGroups { get; init; } = Array.Empty<Ds1SubstGroup>();
    public Ds1NpcPath[] NpcPaths { get; init; } = Array.Empty<Ds1NpcPath>();
    /// <summary>Total bytes consumed by the parse.</summary>
    public int BytesConsumed { get; init; }
    /// <summary>True when a version outside the recon's handled range (> 18) appeared.</summary>
    public bool NpcPathsDeferred { get; init; }

    public static byte Prop1(uint raw) => (byte)raw;
    public static byte Prop2(uint raw) => (byte)(raw >> 8);
    public static byte Orientation(uint raw) => (byte)(raw >> 16);

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _b;
        public int Pos;

        public Reader(ReadOnlySpan<byte> b)
        {
            _b = b;
            Pos = 0;
        }

        public int ReadI32() => (int)ReadU32();

        // The engine loads a DS1 into a zero-padded MPQ buffer (D2FILE_AllocAndLoadD2File)
        // and reads from it without bounds checks, so any read past the file's true end
        // yields 0. Some shipped DS1s rely on this: Act1\Outdoors\Trees.ds1 (version 12)
        // declares 14 substitution groups but its last group's y/w/h fall in the padding.
        // Mirror the padded buffer: over-reads return 0 rather than failing.
        public uint ReadU32()
        {
            if (Pos + 4 > _b.Length)
            {
                Span<byte> buf = stackalloc byte[4];
                buf.Clear();
                int avail = Pos < _b.Length ? _b.Length - Pos : 0;
                if (avail > 0) _b.Slice(Pos, avail).CopyTo(buf);
                Pos += 4;
                return BinaryPrimitives.ReadUInt32LittleEndian(buf);
            }
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(_b.Slice(Pos, 4));
            Pos += 4;
            return v;
        }

        public void Skip(int n) => Pos += n; // padded-buffer semantics

        /// <summary>Skip one NUL-terminated string. Past the true end the padded buffer is all
        /// zeros — i.e. an immediate terminator.</summary>
        public void SkipCStr()
        {
            while (true)
            {
                if (Pos >= _b.Length)
                {
                    Pos += 1;
                    return;
                }
                byte c = _b[Pos];
                Pos += 1;
                if (c == 0) return;
            }
        }

        public uint[] ReadBlock(int n)
        {
            var cells = new uint[n];
            for (int i = 0; i < n; i++) cells[i] = ReadU32();
            return cells;
        }
    }

    /// <summary>Parse a raw .ds1 file.</summary>
    public static Ds1File Parse(ReadOnlySpan<byte> bytes)
    {
        var r = new Reader(bytes);

        int version = r.ReadI32();
        int rawWidth = r.ReadI32();
        int rawHeight = r.ReadI32();

        int actId = 0;
        if (version > 7)
        {
            actId = r.ReadI32();
            if (actId > 4) actId = 4;
        }

        int act = 0;
        if (version > 9) act = r.ReadI32();

        // version >= 3: numFiles, then that many NUL-terminated path strings.
        if (version > 2)
        {
            int numFiles = r.ReadI32();
            for (int i = 0; i < numFiles; i++) r.SkipCStr();
        }

        if (rawWidth < 0 || rawHeight < 0) throw new InvalidDataException("ds1: negative size");
        int w = rawWidth + 1;
        int h = rawHeight + 1;
        int cells = checked(w * h);

        // Murky recon gate (pure decompiler aliasing). Public DS1 spec: skip 8 bytes for
        // version in [9..13].
        if (version >= 9 && version <= 13) r.Skip(8);

        var walls = new List<Ds1WallLayer>();
        var floors = new List<uint[]>();

        if (version < 4)
        {
            // Legacy packed layout: one wall, one orientation/type, one floor block (plus a
            // fourth block the recon reserves for subst tags). Not exercised by 1.14d data.
            var wall = r.ReadBlock(cells);
            var orient = r.ReadBlock(cells);
            walls.Add(new Ds1WallLayer(wall, orient));
            floors.Add(r.ReadBlock(cells));
            r.Skip(cells * 4);
        }
        else
        {
            int numWalls = r.ReadI32();
            int numFloors = 1;
            if (version >= 16) numFloors = r.ReadI32();
            for (int i = 0; i < numWalls; i++)
            {
                var wall = r.ReadBlock(cells);
                var orient = r.ReadBlock(cells);
                walls.Add(new Ds1WallLayer(wall, orient));
            }
            for (int i = 0; i < numFloors; i++) floors.Add(r.ReadBlock(cells));
        }

        // version < 7 remaps wall orientation indices via DRLGANIM_GetOrientationFromIndex.
        // 1.14d files are >= 7, so the raw orientation byte is already in final form.

        var shadow = r.ReadBlock(cells);

        // Subst-group tag layer present when act is 1 or 2 (recon: nAct-1U < 2).
        uint[]? substTags = null;
        if (act >= 1 && act <= 2) substTags = r.ReadBlock(cells);

        // version >= 2: object list.
        var objects = new List<Ds1Object>();
        if (version > 1)
        {
            int numObjects = r.ReadI32();
            for (int i = 0; i < numObjects; i++)
            {
                int kind = r.ReadI32();
                int id = r.ReadI32();
                int x = r.ReadI32();
                int y = r.ReadI32();
                int flags = 0;
                if (version > 5) flags = r.ReadI32();
                objects.Add(new Ds1Object(kind, id, x, y, flags));
            }
        }

        // Substitution groups: version >= 12 AND act in {1,2}. version >= 18 prepends a
        // 4-byte field before the count. Each group is a 4-int tile box; version >= 13 adds a
        // 5th "unknown" int.
        var groups = new List<Ds1SubstGroup>();
        if (version > 11 && act >= 1 && act <= 2)
        {
            if (version > 17) r.Skip(4);
            int numGroups = r.ReadI32();
            for (int i = 0; i < numGroups; i++)
            {
                int x = r.ReadI32();
                int y = r.ReadI32();
                int gw = r.ReadI32();
                int gh = r.ReadI32();
                int unknown = 0;
                if (version > 12) unknown = r.ReadI32();
                groups.Add(new Ds1SubstGroup(x, y, gw, gh, unknown));
            }
        }

        // NPC paths: version >= 14. int32 numNpcs, then per NPC a { count, x, y } header;
        // when count != 0, that many path nodes follow. Each node is { x, y } and, for
        // version >= 15, an int32 action (else action = 1).
        var paths = new List<Ds1NpcPath>();
        bool deferred = false;
        if (version > 13)
        {
            if (version > 18) deferred = true;
            int numNpcs = r.ReadI32();
            for (int n = 0; n < numNpcs; n++)
            {
                int count = r.ReadI32();
                int x = r.ReadI32();
                int y = r.ReadI32();
                if (count <= 0) continue;
                var points = new Ds1PathPoint[count];
                for (int k = 0; k < count; k++)
                {
                    int px = r.ReadI32();
                    int py = r.ReadI32();
                    int action = 1;
                    if (version > 14) action = r.ReadI32();
                    points[k] = new Ds1PathPoint(px, py, action);
                }
                // Match to a preset object by coords (recon scans preset units, which are
                // prepended — i.e. reverse object order — so we scan in reverse).
                int? objectIndex = null;
                for (int oi = objects.Count - 1; oi >= 0; oi--)
                {
                    if (objects[oi].X == x && objects[oi].Y == y)
                    {
                        objectIndex = oi;
                        break;
                    }
                }
                paths.Add(new Ds1NpcPath(x, y, objectIndex, points));
            }
        }

        return new Ds1File
        {
            Version = version,
            Width = (uint)w,
            Height = (uint)h,
            Act = act,
            ActId = actId,
            WallLayers = walls.ToArray(),
            FloorLayers = floors.ToArray(),
            Shadow = shadow,
            SubstTags = substTags,
            Objects = objects.ToArray(),
            SubstGroups = groups.ToArray(),
            NpcPaths = paths.ToArray(),
            BytesConsumed = r.Pos,
            NpcPathsDeferred = deferred,
        };
    }
}

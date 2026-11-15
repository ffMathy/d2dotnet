using System.Buffers.Binary;
using System.Text;

namespace D2Dotnet.Map.GameData.Tests.Writer;

/// <summary>One span of a TVFS file to write.</summary>
public sealed record TvfsSpanSpec(byte[] EKey, uint FileOffset, uint ContentSize, uint EncodedSize);

/// <summary>A TVFS file to write. A <see cref="SpanCountOverride"/> of 255 writes a deleted entry.</summary>
public sealed record TvfsFileSpec(string Path, IReadOnlyList<TvfsSpanSpec> Spans, byte? SpanCountOverride = null);

/// <summary>
/// Test-side TVFS manifest writer. It builds a real prefix tree: names are split wherever paths
/// diverge (also in the middle of a name), and separators are encoded as the before/after flags,
/// like the manifests CascLib parses.
/// </summary>
public static class TvfsWriter
{
    private const int Separator = -1;

    public static byte[] Write(IEnumerable<TvfsFileSpec> files, uint flags = 0)
    {
        var list = files.ToList();

        // Container file table: one entry per distinct EKey: ekey[9], u32 encodedSize.
        var cft = new MemoryStream();
        var cftOffsets = new Dictionary<string, uint>();
        foreach (var span in list.SelectMany(f => f.Spans))
        {
            var key = Convert.ToHexString(span.EKey, 0, 9);
            if (cftOffsets.ContainsKey(key))
                continue;
            cftOffsets[key] = (uint)cft.Length;
            cft.Write(span.EKey.AsSpan(0, 9));
            cft.Write(U32(span.EncodedSize));
        }
        var cftBytes = cft.ToArray();
        var cftOffsetSize = cftBytes.Length switch { > 0xFFFFFF => 4, > 0xFFFF => 3, > 0xFF => 2, _ => 1 };

        // VFS table: u8 spanCount, then { u32 offset, u32 size, cftOffset } per span.
        var vfs = new MemoryStream();
        var root = new Node();
        foreach (var file in list)
        {
            var offset = (uint)vfs.Length;
            vfs.WriteByte(file.SpanCountOverride ?? (byte)file.Spans.Count);
            foreach (var span in file.Spans)
            {
                vfs.Write(U32(span.FileOffset));
                vfs.Write(U32(span.ContentSize));
                var cftOffset = cftOffsets[Convert.ToHexString(span.EKey, 0, 9)];
                for (var b = cftOffsetSize - 1; b >= 0; b--)
                    vfs.WriteByte((byte)(cftOffset >> (8 * b)));
            }
            root.Insert(Tokens(file.Path), offset);
        }
        var vfsBytes = vfs.ToArray();

        var children = WriteChildren(root, out var depth);
        var pathTable = new List<byte> { 0xFF };
        pathTable.AddRange(U32(0x80000000u | (uint)(4 + children.Count)));
        pathTable.AddRange(children);

        const int headerSize = 0x26;
        var header = new byte[headerSize];
        "TVFS"u8.CopyTo(header);
        header[4] = 1;          // format version
        header[5] = headerSize;
        header[6] = 9;          // EKey size
        header[7] = 9;          // patch key size
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), flags);
        var pathOffset = (uint)headerSize;
        var vfsOffset = pathOffset + (uint)pathTable.Count;
        var cftOffsetPos = vfsOffset + (uint)vfsBytes.Length;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), pathOffset);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), (uint)pathTable.Count);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), vfsOffset);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(24), (uint)vfsBytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(28), cftOffsetPos);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(32), (uint)cftBytes.Length);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(36), (ushort)depth);

        return [.. header, .. pathTable, .. vfsBytes, .. cftBytes];
    }

    private static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        return b;
    }

    private static List<int> Tokens(string path) =>
        Encoding.UTF8.GetBytes(path).Select(b => b == (byte)'/' ? Separator : b).ToList();

    private sealed class Node
    {
        public SortedDictionary<int, Node> Children { get; } = new();
        public uint? Value { get; set; }

        public void Insert(List<int> tokens, uint value)
        {
            var node = this;
            foreach (var t in tokens)
            {
                if (!node.Children.TryGetValue(t, out var next))
                    node.Children[t] = next = new Node();
                node = next;
            }
            if (node.Value is not null)
                throw new ArgumentException("Duplicate TVFS path.");
            node.Value = value;
        }
    }

    // An edge is [separator] name [separator]: no separator inside, name 1..254 bytes
    // (a length byte of 0xFF would read as a node value marker), or a lone separator.
    private static bool IsValidEdge(List<int> edge)
    {
        if (edge.Count == 1 && edge[0] == Separator)
            return true;
        var start = edge[0] == Separator ? 1 : 0;
        var end = edge.Count > start && edge[^1] == Separator ? edge.Count - 1 : edge.Count;
        var length = end - start;
        if (length < 1 || length > 254)
            return false;
        for (var i = start; i < end; i++)
            if (edge[i] == Separator)
                return false;
        return true;
    }

    private static List<byte> WriteChildren(Node node, out int depth)
    {
        var output = new List<byte>();
        depth = 0;
        foreach (var (token, child) in node.Children)
        {
            var edge = new List<int> { token };
            var current = child;
            while (current.Value is null && current.Children.Count == 1)
            {
                var (nextToken, next) = current.Children.First();
                if (!IsValidEdge([.. edge, nextToken]))
                    break;
                edge.Add(nextToken);
                current = next;
            }

            var pre = edge[0] == Separator;
            var post = edge.Count > 1 && edge[^1] == Separator;
            var name = edge.Skip(pre ? 1 : 0).Take(edge.Count - (pre ? 1 : 0) - (post ? 1 : 0)).Select(t => (byte)t).ToArray();
            if (pre)
                output.Add(0);
            if (name.Length > 0)
            {
                output.Add((byte)name.Length);
                output.AddRange(name);
            }
            if (post)
                output.Add(0);

            output.Add(0xFF);
            if (current.Children.Count == 0)
            {
                output.AddRange(U32(current.Value!.Value));
                depth = Math.Max(depth, 1);
            }
            else
            {
                if (current.Value is not null)
                    throw new ArgumentException("A TVFS path cannot be both a file and a folder.");
                var grandChildren = WriteChildren(current, out var childDepth);
                output.AddRange(U32(0x80000000u | (uint)(4 + grandChildren.Count)));
                output.AddRange(grandChildren);
                depth = Math.Max(depth, childDepth + 1);
            }
        }
        return output;
    }
}

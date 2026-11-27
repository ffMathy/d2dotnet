using System.Text;

namespace D2Dotnet.Files.Casc;

/// <summary>
/// Reads whole files while sharing writes and deletes, so files that a running game or Battle.net agent holds open
/// (the CASC indexes, .build.info, the configs) can still be read.
/// </summary>
internal static class SharedFile
{
    public static byte[] ReadAllBytes(string path)
    {
        using var stream = Open(path);
        using var buffer = new MemoryStream(stream.CanSeek ? (int)stream.Length : 0);
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static string ReadAllText(string path)
    {
        using var reader = new StreamReader(Open(path), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}

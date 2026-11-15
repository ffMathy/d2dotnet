using System.Text;

namespace D2Dotnet.Map.Generator.Output;

/// <summary>
/// A minimal stand-in for a C <c>FILE*</c>: text is written as single bytes (Latin-1, so every
/// byte read from game memory round-trips unchanged) into a buffer that is only pushed to the
/// underlying stream on <see cref="Flush"/>, mirroring stdio buffering of a piped stdout. An
/// unbuffered stream (C stderr) flushes after every write.
/// </summary>
public sealed class CStream
{
    private readonly Stream _stream;
    private readonly bool _unbuffered;
    private readonly MemoryStream _buffer = new();

    /// <summary>Encoding used for every string: one byte per char, chars above 0xFF become '?'.</summary>
    public static Encoding TextEncoding { get; } = Encoding.Latin1;

    public CStream(Stream stream, bool unbuffered = false)
    {
        _stream = stream;
        _unbuffered = unbuffered;
    }

    /// <summary><c>fputs</c>/<c>printf("%s")</c>.</summary>
    public void Write(string text)
    {
        var bytes = TextEncoding.GetBytes(text);
        _buffer.Write(bytes, 0, bytes.Length);
        if (_unbuffered) Flush();
    }

    /// <summary><c>fflush</c>.</summary>
    public void Flush()
    {
        if (_buffer.Length > 0)
        {
            _buffer.Position = 0;
            _buffer.CopyTo(_stream);
            _buffer.SetLength(0);
        }
        _stream.Flush();
    }
}

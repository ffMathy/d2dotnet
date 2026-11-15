using System.Text;
using D2Dotnet.Map.Generator.Output;
using D2Dotnet.Map.Generator.Tests.Fakes;

namespace D2Dotnet.Map.Generator.Tests;

public class JsonWriterTests
{
    private static (JsonWriter Json, MemoryStream Out, CStream Stream) Make()
    {
        var ms = new MemoryStream();
        var s = new CStream(ms);
        return (new JsonWriter(s), ms, s);
    }

    private static string Text(MemoryStream ms) => Encoding.Latin1.GetString(ms.ToArray());

    [Fact]
    public void WritesFlatObjectWithCommaSpaceSeparators()
    {
        var (json, ms, _) = Make();
        json.Start();
        json.KeyValue("type", "info");
        json.KeyValue("seed", 4278255360u);
        json.KeyValue("difficulty", -1);
        json.KeyValue("big", 1L << 40);
        json.KeyValue("ok", true);
        json.KeyValue("no", false);
        json.End();
        Assert.Equal("\n{\"type\":\"info\", \"seed\":4278255360, \"difficulty\":-1, \"big\":1099511627776, \"ok\":true, \"no\":false}\n", Text(ms));
    }

    [Fact]
    public void NestedObjectsAndArraysMatchCOutput()
    {
        var (json, ms, _) = Make();
        json.Start();
        json.KeyValue("type", "map");
        json.ObjectStart("offset");
        json.KeyValue("x", 1);
        json.KeyValue("y", 2);
        json.ObjectEnd();
        json.ArrayStart("objects");
        json.ObjectStart();
        json.KeyValue("id", 3);
        json.ObjectEnd();
        json.ObjectStart();
        json.KeyValue("id", 4);
        json.ObjectEnd();
        json.ArrayEnd();
        json.ArrayStart("map");
        json.ArrayStart();
        json.Value(0);
        json.Value(5);
        json.ArrayEnd();
        json.ArrayStart();
        json.ArrayEnd();
        json.ArrayEnd();
        json.End();
        Assert.Equal("\n{\"type\":\"map\", \"offset\":{\"x\":1, \"y\":2}, \"objects\":[{\"id\":3}, {\"id\":4}], \"map\":[[0, 5], []]}\n", Text(ms));
    }

    [Fact]
    public void StringsAreNotEscapedLikeC()
    {
        var (json, ms, _) = Make();
        json.Start();
        json.KeyValue("name", "a\"b\\c");
        json.End(false);
        Assert.Equal("\n{\"name\":\"a\"b\\c\"}", Text(ms));
    }

    [Fact]
    public void EndFlushesButStartDoesNot()
    {
        var (json, ms, _) = Make();
        json.Start();
        json.KeyValue("a", 1);
        Assert.Equal(0, ms.Length);
        json.End();
        Assert.NotEqual(0, ms.Length);
    }

    [Fact]
    public void StartWithStreamRedirectsOutput()
    {
        var (json, ms, _) = Make();
        var other = new MemoryStream();
        json.Start(new CStream(other));
        json.KeyValue("a", 1);
        json.End();
        Assert.Equal(0, ms.Length);
        Assert.Equal("\n{\"a\":1}\n", Text(other));
    }
}

public class LoggerTests
{
    private static (Logger Log, MemoryStream Out) Make(long now = 1636000000123)
    {
        var ms = new MemoryStream();
        return (new Logger(new CStream(ms), new FixedClock(now)), ms);
    }

    private static string Text(MemoryStream ms) => Encoding.Latin1.GetString(ms.ToArray());

    [Fact]
    public void FormatsAllFieldTypes()
    {
        var (log, ms) = Make();
        log.Info(new LogSource("map/main.c", 81), "Map:Generation:Done", LogField.UI("seed", 1122334), LogField.I("difficulty", 2), LogField.S("path", "C:\\x\\y"), LogField.B("verbose", true));
        Assert.Equal("{\"level\":30,\"time\":1636000000123,\"source\":\"map/main.c:81\",\"msg\":\"Map:Generation:Done\",\"seed\": \"0x11201e\",\"difficulty\":2,\"path\":\"C:/x/y\",\"verbose\":\"true\"}\n", Text(ms));
    }

    [Fact]
    public void DefaultLevelIsDebugAndTraceIsDropped()
    {
        var (log, ms) = Make();
        log.Trace(new LogSource("f", 1), "hidden");
        Assert.Equal(0, ms.Length);
        log.Debug(new LogSource("f", 2), "shown");
        Assert.Equal("{\"level\":20,\"time\":1636000000123,\"source\":\"f:2\",\"msg\":\"shown\"}\n", Text(ms));
        log.SetLevel(LogLevel.Trace);
        log.Trace(new LogSource("f", 3), "now");
        Assert.EndsWith("\"msg\":\"now\"}\n", Text(ms));
    }

    [Theory]
    [InlineData(0u, "00000000")]
    [InlineData(5u, "0x000005")]
    [InlineData(1122334u, "0x11201e")]
    [InlineData(0x1234567u, "0x1234567")]
    [InlineData(0xff00ff00u, "0xff00ff00")]
    public void AltHexMatchesPrintf(uint value, string expected) => Assert.Equal(expected, Logger.FormatAltHex08(value));

    [Fact]
    public void LevelsMatchCEnum()
    {
        Assert.Equal([10, 20, 30, 40, 50, 60], new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal });
    }

    [Fact]
    public void FatalWarnErrorUseTheirLevels()
    {
        var (log, ms) = Make(5);
        log.Warn(new LogSource("a", 1), "w");
        log.Error(new LogSource("a", 2), "e");
        log.Fatal(new LogSource("a", 3), "f");
        Assert.Equal("{\"level\":40,\"time\":5,\"source\":\"a:1\",\"msg\":\"w\"}\n{\"level\":50,\"time\":5,\"source\":\"a:2\",\"msg\":\"e\"}\n{\"level\":60,\"time\":5,\"source\":\"a:3\",\"msg\":\"f\"}\n", Text(ms));
    }
}

public class CStreamTests
{
    [Fact]
    public void BufferedUntilFlushAndLatin1()
    {
        var ms = new MemoryStream();
        var s = new CStream(ms);
        s.Write("caf\u00e9");
        Assert.Equal(0, ms.Length);
        s.Flush();
        Assert.Equal(new byte[] { 0x63, 0x61, 0x66, 0xE9 }, ms.ToArray());
    }

    [Fact]
    public void TextModeWritesCrLfLikeMsvcrt()
    {
        // mingw printf("\n{"); fprintf(stdout, "}\n"); on a text-mode stdout: \r \n { } \r \n
        var ms = new MemoryStream();
        var s = new CStream(ms, textMode: true);
        s.Write("\n{");
        s.Write("}\n");
        s.Flush();
        Assert.Equal("\r\n{}\r\n"u8.ToArray(), ms.ToArray());
    }

    [Fact]
    public void BinaryModeKeepsLf()
    {
        var ms = new MemoryStream();
        var s = new CStream(ms);
        s.Write("\n{}\n");
        s.Flush();
        Assert.Equal("\n{}\n"u8.ToArray(), ms.ToArray());
    }

    [Fact]
    public void UnbufferedWritesImmediately()
    {
        var ms = new MemoryStream();
        new CStream(ms, unbuffered: true).Write("-1, ");
        Assert.Equal("-1, ", Encoding.Latin1.GetString(ms.ToArray()));
    }
}

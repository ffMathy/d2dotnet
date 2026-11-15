using System.Globalization;

namespace D2Dotnet.Map.Generator.Output;

/// <summary>Log levels from upstream map/log.h.</summary>
public static class LogLevel
{
    public const int Trace = 10;
    public const int Debug = 20;
    public const int Info = 30;
    public const int Warn = 40;
    public const int Error = 50;
    public const int Fatal = 60;
}

/// <summary>Field kinds from the C <c>log_field_types</c> enum.</summary>
public enum LogFieldType : byte
{
    Int = 0,
    UInt = 1,
    String = 2,
    Boolean = 3,
}

/// <summary>A structured log field, port of <c>struct log_obj</c>.</summary>
public readonly record struct LogField(LogFieldType Type, string Key, int IntValue, uint UIntValue, string? StringValue)
{
    /// <summary><c>lk_i</c>.</summary>
    public static LogField I(string key, int value) => new(LogFieldType.Int, key, value, 0, null);

    /// <summary><c>lk_ui</c>.</summary>
    public static LogField UI(string key, uint value) => new(LogFieldType.UInt, key, 0, value, null);

    /// <summary><c>lk_s</c>: copies the value replacing every backslash with a forward slash.</summary>
    public static LogField S(string key, string value) => new(LogFieldType.String, key, 0, 0, value.Replace('\\', '/'));

    /// <summary><c>lk_b</c>: booleans are logged as the strings "true"/"false".</summary>
    public static LogField B(string key, bool value) => S(key, value ? "true" : "false");
}

/// <summary>Source location of a log call, the C <c>__FILE__:__LINE__</c> of the upstream call site.</summary>
public readonly record struct LogSource(string File, int Line);

/// <summary>Clock used for log timestamps and durations (<c>currentTimeMillis</c>).</summary>
public interface IClock
{
    /// <summary>Milliseconds since the Unix epoch.</summary>
    long CurrentTimeMillis();
}

/// <summary>System clock, <c>gettimeofday</c> based in C.</summary>
public sealed class SystemClock : IClock
{
    public long CurrentTimeMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// Port of upstream map/log.c. Every line is printed to stdout as
/// <c>{"level":L,"time":T,"source":"file:line","msg":"M"[,fields]}\n</c> followed by fflush.
/// </summary>
public sealed class Logger
{
    private readonly CStream _stdout;
    private readonly IClock _clock;

    /// <summary>Current threshold, <c>LogLevel</c> in C (default <c>LOG_DEBUG</c>).</summary>
    public int Level { get; private set; } = LogLevel.Debug;

    public Logger(CStream stdout, IClock clock)
    {
        _stdout = stdout;
        _clock = clock;
    }

    /// <summary><c>log_level</c>.</summary>
    public void SetLevel(int logLevel) => Level = logLevel;

    /// <summary><c>currentTimeMillis</c>.</summary>
    public long CurrentTimeMillis() => _clock.CurrentTimeMillis();

    public void Trace(LogSource src, string msg, params LogField[] fields) => Process(LogLevel.Trace, src, msg, fields);
    public void Debug(LogSource src, string msg, params LogField[] fields) => Process(LogLevel.Debug, src, msg, fields);
    public void Info(LogSource src, string msg, params LogField[] fields) => Process(LogLevel.Info, src, msg, fields);
    public void Warn(LogSource src, string msg, params LogField[] fields) => Process(LogLevel.Warn, src, msg, fields);
    public void Error(LogSource src, string msg, params LogField[] fields) => Process(LogLevel.Error, src, msg, fields);
    public void Fatal(LogSource src, string msg, params LogField[] fields) => Process(LogLevel.Fatal, src, msg, fields);

    /// <summary><c>log_process</c>.</summary>
    public void Process(int level, LogSource src, string msg, params LogField[] fields)
    {
        if (level < Level) return;

        var time = _clock.CurrentTimeMillis();
        var ci = CultureInfo.InvariantCulture;
        _stdout.Write("{\"level\":" + level.ToString(ci) + ",\"time\":" + time.ToString(ci) +
                      ",\"source\":\"" + src.File + ":" + src.Line.ToString(ci) + "\",\"msg\":\"" + msg + "\"");
        foreach (var arg in fields)
        {
            if (arg.Type == LogFieldType.Int) _stdout.Write(",\"" + arg.Key + "\":" + arg.IntValue.ToString(ci));
            else if (arg.Type == LogFieldType.UInt) _stdout.Write(",\"" + arg.Key + "\": \"" + FormatAltHex08(arg.UIntValue) + "\"");
            else if (arg.Type == LogFieldType.String) _stdout.Write(",\"" + arg.Key + "\":\"" + arg.StringValue + "\"");
        }
        _stdout.Write("}\n");
        _stdout.Flush();
    }

    /// <summary>
    /// printf <c>"%#08x"</c>: zero padded to a total width of 8 including the "0x" prefix; the
    /// prefix is omitted for zero (C99 7.19.6.1: '#' only prefixes a nonzero result).
    /// </summary>
    public static string FormatAltHex08(uint value)
    {
        if (value == 0) return "00000000";
        var hex = value.ToString("x", CultureInfo.InvariantCulture);
        return "0x" + hex.PadLeft(6, '0');
    }
}

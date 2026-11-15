using System.Text.Json;
using System.Text.Json.Serialization;
using D2Dotnet.Map.Data;

namespace D2Dotnet.Map.Process;

/// <summary>
/// The JSON-lines protocol spoken by the native generator (<c>d2-map.exe</c>) on stdout (upstream <c>map.ts</c>).
/// </summary>
/// <remarks>
/// stdin commands: <c>$seed N</c>, <c>$difficulty N</c>, <c>$act N</c> (each answered by an <c>info</c> message),
/// <c>$map</c> (answered by zero or more <c>map</c> messages then <c>done</c>) and <c>$exit</c>.
/// After start up the generator prints <c>{"type":"init"}</c>. Lines containing a <c>time</c> field are log lines.
/// </remarks>
public static class MapGenMessage
{
    public const string Init = "init";
    public const string Info = "info";
    public const string Map = "map";
    public const string Done = "done";
    /// <summary>Pseudo type for generator log lines.</summary>
    public const string Log = "log";
    /// <summary>Pseudo type emitted when the generator process exits.</summary>
    public const string Close = "close";

    /// <summary>Serializer settings for generator output and HTTP responses (camelCase, like upstream).</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Upstream <c>isLogMessage</c>: any JSON object that has a <c>time</c> field.</summary>
    public static bool IsLogMessage(JsonElement json) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty("time", out _);

    /// <summary>
    /// Classify one stdout line: returns the event name to dispatch (<see cref="Log"/>, or the message's
    /// <c>type</c>) or null when the line should be ignored (not JSON, JSON null, or no truthy <c>type</c>).
    /// </summary>
    public static string? Classify(string line, out JsonElement json)
    {
        json = default;
        try
        {
            using var doc = JsonDocument.Parse(line);
            json = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }

        if (json.ValueKind != JsonValueKind.Object) return null;
        if (IsLogMessage(json)) return Log;
        if (!json.TryGetProperty("type", out var type)) return null;
        return type.ValueKind switch
        {
            JsonValueKind.String when type.GetString() is { Length: > 0 } s => s,
            JsonValueKind.Number when type.GetDouble() != 0 => type.GetRawText(),
            JsonValueKind.True => "true",
            _ => null,
        };
    }

    /// <summary>Parse a <c>map</c> message into a level.</summary>
    public static Diablo2Level ParseLevel(JsonElement json) =>
        json.Deserialize<Diablo2Level>(JsonOptions) ?? throw new JsonException("Invalid map message");
}

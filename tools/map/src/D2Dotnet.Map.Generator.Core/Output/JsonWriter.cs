using System.Globalization;

namespace D2Dotnet.Map.Generator.Output;

/// <summary>
/// Port of upstream map/json.c: a "really stupid JSON encoder" that streams straight to a
/// <see cref="CStream"/>. Behaviour (including the lack of string escaping and the shared
/// comma flag) is kept byte-for-byte; method names map 1:1 to the C functions.
/// </summary>
public sealed class JsonWriter
{
    private readonly CStream _stdout;
    private CStream _fp;
    private bool _commaEnabled;

    public JsonWriter(CStream stdout)
    {
        _stdout = stdout;
        _fp = stdout;
    }

    private void Print(string s) => _fp.Write(s);

    private static string D(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary><c>json_start()</c>: selects stdout and prints <c>"\n{"</c>. The comma flag is not reset.</summary>
    public void Start()
    {
        _fp = _stdout;
        Print("\n{");
    }

    /// <summary><c>json_start(FILE*)</c>.</summary>
    public void Start(CStream fp)
    {
        _fp = fp;
        Print("\n{");
    }

    /// <summary><c>json_comma()</c>.</summary>
    public void Comma()
    {
        if (_commaEnabled) Print(", ");
        _commaEnabled = true;
    }

    /// <summary><c>json_value(int)</c>.</summary>
    public void Value(int value)
    {
        Comma();
        Print(D(value));
    }

    /// <summary><c>json_key_raw(char*)</c>.</summary>
    public void KeyRaw(string key) => Print("\"" + key + "\":");

    /// <summary><c>json_key_value(char*, int)</c> (<c>%d</c>).</summary>
    public void KeyValue(string key, int value)
    {
        Comma();
        KeyRaw(key);
        Print(D(value));
    }

    /// <summary><c>json_key_value(char*, unsigned int)</c> (<c>%u</c>).</summary>
    public void KeyValue(string key, uint value)
    {
        Comma();
        KeyRaw(key);
        Print(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary><c>json_key_value(char*, int64_t)</c> (<c>%lld</c>).</summary>
    public void KeyValue(string key, long value)
    {
        Comma();
        KeyRaw(key);
        Print(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary><c>json_key_value(char*, char*)</c>: <c>"%s"</c>, no escaping.</summary>
    public void KeyValue(string key, string value)
    {
        Comma();
        KeyRaw(key);
        Print("\"" + value + "\"");
    }

    /// <summary><c>json_key_value(char*, bool)</c>.</summary>
    public void KeyValue(string key, bool value)
    {
        Comma();
        KeyRaw(key);
        Print(value ? "true" : "false");
    }

    /// <summary><c>json_array_start()</c>.</summary>
    public void ArrayStart()
    {
        Comma();
        _commaEnabled = false;
        Print("[");
    }

    /// <summary><c>json_array_start(char*)</c>.</summary>
    public void ArrayStart(string key)
    {
        Comma();
        KeyRaw(key);
        _commaEnabled = false;
        Print("[");
    }

    /// <summary><c>json_array_end()</c>.</summary>
    public void ArrayEnd()
    {
        Print("]");
        _commaEnabled = true;
    }

    /// <summary><c>json_object_start()</c>.</summary>
    public void ObjectStart()
    {
        Comma();
        Print("{");
        _commaEnabled = false;
    }

    /// <summary><c>json_object_start(char*)</c>.</summary>
    public void ObjectStart(string key)
    {
        Comma();
        KeyRaw(key);
        Print("{");
        _commaEnabled = false;
    }

    /// <summary><c>json_object_end()</c>.</summary>
    public void ObjectEnd()
    {
        Print("}");
        _commaEnabled = true;
    }

    /// <summary><c>json_end(bool force)</c>: closes the object, optional newline, then <c>fflush</c>.</summary>
    public void End(bool force)
    {
        Print("}");
        _commaEnabled = false;
        if (force) Print("\n");
        _fp.Flush();
    }

    /// <summary><c>json_end()</c>.</summary>
    public void End() => End(true);
}

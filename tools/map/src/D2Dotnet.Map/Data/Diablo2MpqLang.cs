namespace D2Dotnet.Map.Data;

/// <summary>A single .tbl string table (upstream <c>Diablo2MpqLangTbl</c>).</summary>
public sealed class Diablo2MpqLangTbl
{
    /// <summary>Translations by key.</summary>
    public Dictionary<string, string> ByKey { get; } = [];

    /// <summary>Translations by index (sparse).</summary>
    public Dictionary<int, string> ByIndex { get; } = [];

    public void Add(string key, int index, string value)
    {
        ByIndex[index] = value;

        if (key is "x" or "X") return;
        if (JsNumber.TrimJs(value).Length == 0) return;
        if (ByKey.TryGetValue(key, out var existingKey) && existingKey.Length > 0)
        {
            if (JsNumber.TrimJs(existingKey) == JsNumber.TrimJs(value)) return;
        }
        ByKey[key] = value;
    }

    public string? Get(int index) => ByIndex.GetValueOrDefault(index);
}

/// <summary>All string tables (upstream <c>Diablo2MpqLang</c>).</summary>
public sealed class Diablo2MpqLang(Diablo2MpqData mpq)
{
    public const int ExpansionOffset = 20000;
    public const int PatchOffset = 10000;
    public const int ClassicOffset = 0;

    public Diablo2MpqLangTbl Expansion { get; } = new();
    public Diablo2MpqLangTbl Patch { get; } = new();
    public Diablo2MpqLangTbl Classic { get; } = new();

    public Diablo2MpqData Mpq { get; } = mpq;

    public int Size => Expansion.ByKey.Count + Patch.ByKey.Count + Classic.ByKey.Count;

    public string? GetByIndex(int index)
    {
        // This is a commonly used bad key?
        if (index == 5382) return null;
        if (index >= ExpansionOffset) return Expansion.Get(index - ExpansionOffset);
        if (index >= PatchOffset) return Patch.Get(index - PatchOffset);
        return Classic.Get(index);
    }

    public string? GetByKey(string key) =>
        Expansion.ByKey.GetValueOrDefault(key) ?? Patch.ByKey.GetValueOrDefault(key) ?? Classic.ByKey.GetValueOrDefault(key);
}

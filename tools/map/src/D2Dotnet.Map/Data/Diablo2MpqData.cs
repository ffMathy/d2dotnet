namespace D2Dotnet.Map.Data;

/// <summary>Level record loaded from levels.bin (upstream <c>Diablo2MpqLevel</c>).</summary>
public sealed record Diablo2MpqLevel(int Id, Act Act, string Name, bool IsRaining, bool IsTeleportEnabled);

/// <summary>Object record loaded from objects.bin (upstream <c>Diablo2MpqObject</c> in <c>mpq.ts</c>).</summary>
public sealed record Diablo2MpqObject(string Name);

/// <summary>Game data loaded from the Diablo 2 MPQ files (upstream <c>Diablo2MpqData</c>).</summary>
public sealed class Diablo2MpqData
{
    public Diablo2MpqData() => Reset();

    /// <summary>Last path to load MPQ data from.</summary>
    public string? BasePath { get; set; }

    public Diablo2MpqLang Lang { get; private set; } = null!;
    public Diablo2MpqMonsters Monsters { get; private set; } = null!;
    public Diablo2MpqItem Items { get; private set; } = null!;
    public Dictionary<int, Diablo2MpqObject> Objects { get; private set; } = null!;
    public Dictionary<int, Diablo2MpqLevel> Levels { get; private set; } = null!;

    public void Reset()
    {
        Lang = new Diablo2MpqLang(this);
        Monsters = new Diablo2MpqMonsters(this);
        Items = new Diablo2MpqItem(this);
        Objects = [];
        Levels = [];
    }

    /// <summary>Translate a lang index.</summary>
    public string? T(int? key) => key == null ? null : Lang.GetByIndex(key.Value);

    /// <summary>Translate a lang key.</summary>
    public string? T(string? key) => key == null ? null : Lang.GetByKey(key);
}

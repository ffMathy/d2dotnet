namespace D2Dotnet.Map.Data;

/// <summary>Monster record (upstream <c>Diablo2MpqMonster</c>).</summary>
public sealed record Diablo2MpqMonster(int Id, int BaseId, int NameLangId);

/// <summary>Monster tables (upstream <c>Diablo2MpqMonsters</c>).</summary>
public sealed class Diablo2MpqMonsters(Diablo2MpqData mpq)
{
    /// <summary>SuperUniqueId -&gt; Name</summary>
    public IReadOnlyList<string> SuperUniques { get; } =
    [
        "Bishibosh",
        "Bonebreaker",
        "Coldcrow",
        "Rakanishu",
        "Treehead WoodFist",
        "Griswold",
        "The Countess",
        "Pitspawn Fouldog",
        "Flamespike the Crawler",
        "Bone Ash",
        "Radament",
        "Bloodwitch the Wild",
        "Fangskin",
        "Beetleburst",
        "Creeping Feature",
        "Coldworm the Burrower",
        "Fire Eye",
        "Dark Elder",
        "The Summoner",
        "Ancient Kaa the Soulless",
        "The Smith",
        "Sszark the Burning",
        "Witch Doctor Endugu",
        "Stormtree",
        "Battlemaid Sarina",
        "Icehawk Riftwing",
        "Ismail Vilehand",
        "Geleb Flamefinger",
        "Bremm Sparkfist",
        "Toorc Icefist",
        "Wyand Voidbringer",
        "Maffer Dragonhand",
        "Darkwing",
        "The Tormentor",
        "Taintbreeder",
        "Riftwraith the Cannibal",
        "Infector of Souls",
        "Lord De Seis",
        "Grand Vizier of Chaos",
        "The Cow King",
        "Corpsefire",
        "Hephasto The Armorer",
        "Shenk the Overseer",
        "Talic",
        "Madawc",
        "Korlic",
        "Axe Dweller",
        "Bonesaw Breaker",
        "Dac Farren",
        "Eldritch the Rectifier",
        "Eyeback the Unleashed",
        "Thresh Socket",
        "Pindleskin",
        "Snapchip Shatter",
        "Hell's Belle",
        "Vinvear Molech",
        "Sharptooth Slayer",
        "Magma Torquer",
        "Blaze Ripper",
        "Frozenstein",
        "Nihlathak Boss",
        "Colenzo the Annihilator",
        "Achmel the Cursed",
        "Bartuc the Bloody",
        "Ventar the Unholy",
        "Lister the Tormentor",
    ];

    /// <summary>MonsterId -&gt; Monster translation string.</summary>
    public Dictionary<int, Diablo2MpqMonster> Monsters { get; } = [];

    /// <summary>baseMonsterId -&gt; Monster state.</summary>
    public Dictionary<int, byte[]> State { get; } = [];

    public Diablo2MpqData Mpq { get; } = mpq;

    /// <summary>Number of normal monsters. For v1.13c diablo2 this is 734.</summary>
    public int Size { get; private set; }

    public string? SuperUniqueName(int superUniqueId) =>
        superUniqueId >= 0 && superUniqueId < SuperUniques.Count ? SuperUniques[superUniqueId] : null;

    public string? Name(int monsterId)
    {
        if (monsterId >= Size) return SuperUniqueName(monsterId - Size);
        return Mpq.T(Monsters.TryGetValue(monsterId, out var mon) ? mon.NameLangId : null);
    }

    public void Add(int monsterId, Diablo2MpqMonster mon)
    {
        Size++;
        Monsters[monsterId] = mon;
    }

    public byte[] GetState(int baseMonsterId) => State.GetValueOrDefault(baseMonsterId) ?? [];

    public void AddState(int baseMonsterId, byte[] state)
    {
        if (!Monsters.ContainsKey(baseMonsterId))
            throw new InvalidOperationException($"Unable to add monster state missing monster {baseMonsterId}");
        State[baseMonsterId] = state;
    }
}

using System.Collections.Frozen;

namespace D2Dotnet.Game;

/// <summary>
/// The state of a running game at one moment, as <c>D2Dotnet.Memory.GameReader.ReadSnapshot</c> reads it. Immutable.
/// </summary>
/// <remarks>
/// Positions are world subtiles. The snapshot holds what the game process holds about units and the user interface; the
/// map (collision, rooms, exits) is generated with <c>D2Dotnet.Maps</c> from the map seed and the area.
/// </remarks>
public sealed record GameSnapshot
{
    /// <summary>The player this game client controls; null when not in a game.</summary>
    public MainPlayer? Player { get; init; }

    /// <summary>Every player unit: players, the player's corpse and the shared stash units.</summary>
    public IReadOnlyList<PlayerUnit> Players { get; init; } = [];

    /// <summary>The main player's corpse, or null when there is none.</summary>
    public PlayerCorpse? Corpse { get; init; }

    /// <summary>
    /// The living monsters, NPCs, mercenaries and summons, nearest to the player first. Critters, town folk, traps and
    /// similar units are left out unless they give experience.
    /// </summary>
    public IReadOnlyList<Monster> Monsters { get; init; } = [];

    /// <summary>The dead monsters, nearest to the player first (with the same exclusions as <see cref="Monsters"/>).</summary>
    public IReadOnlyList<Monster> Corpses { get; init; } = [];

    /// <summary>The objects, nearest to the player first.</summary>
    public IReadOnlyList<GameObject> Objects { get; init; } = [];

    /// <summary>The main player's items, the items on the ground and at vendors, and the gold.</summary>
    public Inventory Inventory { get; init; } = new();

    /// <summary>The party: the main player first, then the other members.</summary>
    public IReadOnlyList<PartyMember> Party { get; init; } = [];

    /// <summary>The unit under the mouse cursor, or null.</summary>
    public HoveredUnit? Hover { get; init; }

    /// <summary>The open panels.</summary>
    public OpenMenus Menus { get; init; }

    /// <summary>The user interface widgets, by name.</summary>
    public IReadOnlyDictionary<string, Widget> Widgets { get; init; } = FrozenDictionary<string, Widget>.Empty;

    /// <summary>The terrorized areas.</summary>
    public IReadOnlyList<AreaId> TerrorZones { get; init; } = [];

    /// <summary>The status of every quest.</summary>
    public IReadOnlyDictionary<QuestId, QuestStatus> Quests { get; init; } = FrozenDictionary<QuestId, QuestStatus>.Empty;

    /// <summary>The key bindings.</summary>
    public KeyBindings KeyBindings { get; init; } = new();

    /// <summary>The current (or last) online game.</summary>
    public OnlineGameInfo Game { get; init; } = new("", "", 0);

    /// <summary>True when legacy graphics are on.</summary>
    public bool LegacyGraphics { get; init; }

    /// <summary>True when the Online tab of the character selection screen is selected.</summary>
    public bool IsOnline { get; init; }

    /// <summary>True in a game.</summary>
    public bool IsInGame { get; init; }

    /// <summary>True in the lobby.</summary>
    public bool IsInLobby { get; init; }

    /// <summary>True in the character selection screen.</summary>
    public bool IsInCharacterSelection { get; init; }

    /// <summary>True in the character creation screen.</summary>
    public bool IsInCharacterCreation { get; init; }

    /// <summary>The living hostile monsters (<see cref="Monster.IsEnemy"/>), nearest first.</summary>
    public IEnumerable<Monster> Enemies => Monsters.Where(m => m.IsEnemy);

    /// <summary>The first mercenary of <see cref="Monsters"/>, or null.</summary>
    public Monster? Mercenary => Monsters.FirstOrDefault(m => m.IsMercenary);

    /// <summary>
    /// The mercenary's life in percent, or null without a mercenary or when its max life is unknown or 0 (a mercenary
    /// without a life stat is at 0%). A life stat of up to 32768 is read as a fraction of 32768 of the max life, as the
    /// game keeps it for some mercenaries.
    /// </summary>
    public int? MercenaryLifePercent
    {
        get
        {
            if (Mercenary is not { } merc)
                return null;
            var maxLife = (merc.GetStat(StatId.MaxLife) ?? 0) >> 8;
            if (maxLife <= 0)
                return null;
            var lifeStat = merc.GetStat(StatId.Life) ?? 0;
            var life = lifeStat <= 32768 ? lifeStat / 32768.0 * maxLife : lifeStat >> 8;
            return (int)(life / maxLife * 100);
        }
    }

    /// <summary>The status of <paramref name="quest"/>; <see cref="QuestStatus.None"/> when unknown.</summary>
    public QuestStatus GetQuestStatus(QuestId quest) => Quests.GetValueOrDefault(quest);

    /// <summary>The monster (alive or dead) with this unit id, or null.</summary>
    public Monster? FindMonster(UnitId id) => Monsters.FirstOrDefault(m => m.Id == id) ?? Corpses.FirstOrDefault(m => m.Id == id);

    /// <summary>The object with this unit id, or null.</summary>
    public GameObject? FindObject(UnitId id) => Objects.FirstOrDefault(o => o.Id == id);

    /// <summary>The first party member with this name (ignoring case), or null.</summary>
    public PartyMember? FindPartyMember(string name) =>
        Party.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}

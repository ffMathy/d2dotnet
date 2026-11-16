// Port of d2go pkg/memory/game_reader.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using D2Dotnet.D2Go.Data;
using D2Dotnet.D2Go.Data.Stat;

namespace D2Dotnet.D2Go.Memory;

/// <summary>
/// Reads the game state from a running game (Go <c>memory.GameReader</c>, which embeds <c>Process</c>: the
/// <see cref="GameProcess"/> members are inherited). The constructor scans the game code for the
/// <see cref="Offset"/>s, as Go's <c>NewGameReader</c> does.
/// </summary>
/// <remarks>
/// <see cref="GameTables"/> is needed because <see cref="Inventory"/> looks up item types (Go reads its compiled-in
/// tables). Fixed addresses and structure offsets are those of the D2R build d2go targets, see the README.
/// </remarks>
public sealed partial class GameReader : GameProcess
{
    // game_reader.go, relative to the module base.
    internal const nuint QuestsAddress = 0x22E2978;
    internal const nuint SelectedCharacterNameAddress = 0x2A01A50;
    internal const nuint LegacyGraphicsAddress = 0x21E2308;
    internal const nuint IsOnlineAddress = 0x2140ED0;
    internal const nuint IsIngameAddress = 0x22D5D78;
    internal const nuint LastGameAddress = 0x29DBD10;
    internal const nuint FpsAddress = 0x2140DF4;

    private Offset _offset;

    /// <summary>A reader over <paramref name="process"/> (Go <c>NewGameReader</c>); both share the process.</summary>
    /// <param name="process">The game process.</param>
    /// <param name="tables">The game tables, for item types.</param>
    public GameReader(GameProcess process, GameTables tables)
        : base(process)
    {
        ArgumentNullException.ThrowIfNull(tables);
        Tables = tables;
        _offset = Offsets.Calculate(this);
    }

    /// <summary>The game tables the reader looks item types up in.</summary>
    public GameTables Tables { get; }

    /// <summary>The offsets found by the last pattern scan.</summary>
    public Offset Offset => _offset;

    /// <summary>
    /// A snapshot of the whole game state (Go <c>GetData</c>). Scans for the offsets again first when the unit table
    /// offset is 0. Fields of <see cref="GameData"/> that d2go does not read from memory (NPC presets, rooms, levels,
    /// collision grid) keep their defaults.
    /// </summary>
    public GameData GetData()
    {
        if (_offset.UnitTable == 0)
            _offset = Offsets.Calculate(this);

        var rawPlayerUnits = GetRawPlayerUnits();
        var roster = GetRoster(rawPlayerUnits);
        var mainPlayerUnit = rawPlayerUnits.GetMainPlayer();

        var pu = GetPlayerUnit(mainPlayerUnit);
        var hover = HoveredData();

        // Quests
        var q1 = (nuint)ReadUInt(unchecked(ModuleBaseAddress + QuestsAddress), IntType.Uint64);
        var q2 = (nuint)ReadUInt(q1, IntType.Uint64);
        var gameQuestsBytes = ReadBytesFromMemory(q2, 85).AsSpan(3);

        var corpseUnit = rawPlayerUnits.GetCorpse();

        // Same order as Go's composite literal, which evaluates its calls left to right.
        var corpse = new Corpse(Found: corpseUnit.Address != 0, IsHovered: corpseUnit.IsHovered, Position: corpseUnit.Position);
        var game = new OnlineGame { LastGameName = LastGameName(), LastGamePassword = LastGamePass(), Fps = Fps() };
        var monsters = Monsters(pu.Position, hover);
        var corpses = Corpses(pu.Position, hover);
        var inventory = Inventory(rawPlayerUnits, hover);
        var objects = Objects(pu.Position, hover);
        var openMenus = GetOpenMenus();
        var widgets = UpdateWidgets();
        var terrorZones = TerrorZones();
        var quests = GetQuests(gameQuestsBytes);
        var keyBindings = GetKeyBindings();
        var legacyGraphics = LegacyGraphics();
        var isOnline = IsOnline();
        var isIngame = IsIngame();
        var isInCharCreationScreen = IsInCharacterCreationScreen();
        var isInLobby = IsInLobby();
        var isInCharSelectionScreen = IsInCharacterSelectionScreen();

        return new GameData
        {
            Corpse = corpse,
            Game = game,
            Monsters = monsters,
            Corpses = corpses,
            PlayerUnit = pu,
            Inventory = inventory,
            Objects = objects,
            OpenMenus = openMenus,
            Widgets = widgets,
            Roster = roster,
            HoverData = hover,
            TerrorZones = terrorZones,
            Quests = quests,
            KeyBindings = keyBindings,
            LegacyGraphics = legacyGraphics,
            IsOnline = isOnline,
            IsIngame = isIngame,
            IsInCharCreationScreen = isInCharCreationScreen,
            IsInLobby = isInLobby,
            IsInCharSelectionScreen = isInCharSelectionScreen,
        };
    }

    /// <summary>True when the main player has a unit id, a position and an area (Go <c>InGame</c>).</summary>
    public bool InGame()
    {
        var player = GetRawPlayerUnits().GetMainPlayer();

        return player.UnitId.Value > 0 && player.Position.X > 0 && player.Position.Y > 0 && player.Area > 0;
    }

    // Go openMenus.
    internal OpenMenus GetOpenMenus()
    {
        var uiBase = unchecked(ModuleBaseAddress + _offset.UI - 0xA);

        var buffer = ReadBytesFromMemory(uiBase, 0x16D);

        var isMapShown = ReadUInt(unchecked(ModuleBaseAddress + _offset.UI), IntType.Uint8);

        return new OpenMenus
        {
            Inventory = buffer[0x01] != 0,
            LoadingScreen = buffer[0x16C] != 0,
            NpcInteract = buffer[0x08] != 0,
            NpcShop = buffer[0x0B] != 0,
            Stash = buffer[0x18] != 0,
            Waypoint = buffer[0x13] != 0,
            MapShown = isMapShown != 0,
            SkillTree = buffer[0x04] != 0,
            Character = buffer[0x02] != 0,
            QuitMenu = buffer[0x09] != 0,
            Cube = buffer[0x19] != 0,
            SkillSelect = buffer[0x03] != 0,
            Anvil = buffer[0x0D] != 0,
        };
    }

    // Go hoveredData.
    internal HoverData HoveredData()
    {
        var hoverAddressPtr = unchecked(ModuleBaseAddress + _offset.Hover);
        var hoverBuffer = ReadBytesFromMemory(hoverAddressPtr, 12);
        var isUnitHovered = ReadUIntFromBuffer(hoverBuffer, 0, IntType.Uint16);
        if (isUnitHovered > 0)
        {
            var hoveredType = ReadUIntFromBuffer(hoverBuffer, 0x04, IntType.Uint32);
            var hoveredUnitId = ReadUIntFromBuffer(hoverBuffer, 0x08, IntType.Uint32);

            return new HoverData(IsHovered: true, UnitId: new UnitId(GoInt.Narrow(hoveredUnitId)), UnitType: GoInt.Narrow(hoveredType));
        }

        return default;
    }

    /// <summary>
    /// The stat list at <paramref name="statListPtr"/>: a pointer to 8-byte entries (layer u16, id u16, value u32)
    /// and a count (Go <c>getStatsList</c>). Some stats are rescaled as in Go.
    /// </summary>
    /// <exception cref="DivideByZeroException">A replenish stat has the value 0 (Go panics).</exception>
    internal Stats GetStatsList(nuint statListPtr)
    {
        var statsListBuffer = ReadBytesFromMemory(statListPtr, 0x10);
        var statList = ReadUIntFromBuffer(statsListBuffer, 0, IntType.Uint64);
        var statCount = ReadUIntFromBuffer(statsListBuffer, 0x08, IntType.Uint64);
        if (statCount == 0)
            return Stats.Empty;

        var stats = new List<StatData>();
        var statBuffer = ReadBytesFromMemory((nuint)statList, unchecked(statCount * 10));
        for (long i = 0; i < unchecked((long)statCount); i++)
        {
            var offset = (ulong)(i * 8);
            var statLayer = ReadUIntFromBuffer(statBuffer, offset, IntType.Uint16);
            var statEnum = ReadUIntFromBuffer(statBuffer, offset + 0x2, IntType.Uint16);
            var statValue = ReadUIntFromBuffer(statBuffer, offset + 0x4, IntType.Uint32);

            var id = (StatId)unchecked((short)statEnum);
            stats.Add(new StatData(id, GoInt.Narrow(StatValue(id, statValue)), GoInt.Narrow(statLayer)));
        }

        return new Stats(stats);
    }

    // The rescaling switch of Go getStatsList, computed with Go's 64-bit int.
    internal static long StatValue(StatId id, ulong statValue)
    {
        var value = (long)statValue;
        switch (id)
        {
            case StatId.Life:
            case StatId.MaxLife:
            case StatId.Mana:
            case StatId.MaxMana:
            case StatId.Stamina:
            case StatId.MaxStamina:
                value = (long)(statValue >> 8);
                break;
            case StatId.ColdLength:
            case StatId.PoisonLength:
                value = (long)(statValue / 25);
                break;
            case StatId.DeadlyStrikePerLevel:
                value = (long)(statValue / .8);
                break;
            case StatId.HitCausesMonsterToFlee:
                value = (long)(statValue / 1.28);
                break;
            case StatId.AttackRatingUndeadPerLevel:
                value = (long)(statValue / 2);
                break;
            case StatId.MagicFindPerLevel:
            case StatId.ExtraGoldPerLevel:
            case StatId.DamageDemonPerLevel:
            case StatId.DamageUndeadPerLevel:
            case StatId.DefensePerLevel:
            case StatId.MaxDamagePerLevel:
            case StatId.MaxDamagePercentPerLevel:
            case StatId.StrengthPerLevel:
            case StatId.DexterityPerLevel:
            case StatId.VitalityPerLevel:
            case StatId.ThornsPerLevel:
                value = (long)Math.Max(statValue / 8, 1.0);
                break;
            case StatId.LifePerLevel:
            case StatId.ManaPerLevel:
                value = (long)Math.Max(statValue / 2048, 1.0);
                break;
            case StatId.ReplenishDurability:
            case StatId.ReplenishQuantity:
                value = (long)Math.Max(2 / statValue, 1.0);
                break;
            case StatId.RegenStaminaPerLevel:
                value = (long)statValue * 10;
                break;
            case StatId.LevelRequirePercent:
                value = (long)statValue * -1;
                break;
            case StatId.AttackRatingPerLevel:
                value = (long)Math.Max(statValue, 15.0);
                break;
        }

        return value;
    }

    // TODO (upstream): Take a look to better ways to get this data, now it's very flakky, is just a random memory position + not in game
    /// <summary>True when the character selection panel is active and visible (Go <c>InCharacterSelectionScreen</c>).</summary>
    public bool InCharacterSelectionScreen() => IsWidgetVisible("CharacterSelectPanel");

    /// <summary>The name of the character selected in the character selection screen (Go <c>GetSelectedCharacterName</c>).</summary>
    public string GetSelectedCharacterName() => ReadStringFromMemory(unchecked(ModuleBaseAddress + SelectedCharacterNameAddress), 0);

    /// <summary>True when legacy graphics are on (Go <c>LegacyGraphics</c>).</summary>
    public bool LegacyGraphics() => ReadUInt(unchecked(ModuleBaseAddress + LegacyGraphicsAddress), IntType.Uint64) == 1;

    /// <summary>True when the Online tab of the character selection screen is selected (Go <c>IsOnline</c>).</summary>
    public bool IsOnline() => ReadUInt(unchecked(ModuleBaseAddress + IsOnlineAddress), IntType.Uint8) == 1;

    /// <summary>True in a game (Go <c>IsIngame</c>).</summary>
    public bool IsIngame() => ReadUInt(unchecked(ModuleBaseAddress + IsIngameAddress), IntType.Uint8) == 1;

    /// <summary>True when the lobby panel is active and visible (Go <c>IsInLobby</c>).</summary>
    public bool IsInLobby() => IsActiveAndVisible(UpdateWidgets(), "LobbyBackgroundPanel");

    /// <summary>True when the character selection panel is active and visible (Go <c>IsInCharacterSelectionScreen</c>).</summary>
    public bool IsInCharacterSelectionScreen() => IsActiveAndVisible(UpdateWidgets(), "CharacterSelectPanel");

    /// <summary>True when the character creation panel is active and visible (Go <c>IsInCharacterCreationScreen</c>).</summary>
    public bool IsInCharacterCreationScreen() => IsActiveAndVisible(UpdateWidgets(), "CharacterCreatePanel");

    /// <summary>The last game's name (Go <c>LastGameName</c>; marked outdated upstream).</summary>
    public string LastGameName() => ReadStringFromMemory(unchecked(ModuleBaseAddress + LastGameAddress + 0x8), 0);

    /// <summary>The last game's password (Go <c>LastGamePass</c>; marked outdated upstream).</summary>
    public string LastGamePass() => ReadStringFromMemory(unchecked(ModuleBaseAddress + LastGameAddress + 0x60), 0);

    /// <summary>The frame rate (Go <c>FPS</c>).</summary>
    public int Fps() => GoInt.Narrow(ReadUInt(unchecked(ModuleBaseAddress + FpsAddress), IntType.Uint32));

    /// <summary>
    /// The child widgets of the panel manager, by name (Go <c>UpdateWidgets</c>); empty when any read fails. Scans
    /// for the offsets again first when the panel manager offset is 0.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> UpdateWidgets()
    {
        var widgets = new Dictionary<string, IReadOnlyDictionary<string, object?>>();

        if (_offset.PanelManagerContainerOffset == 0)
            _offset = Offsets.Calculate(this);

        // Assuming PanelManagerContainer address is known and stored in gd
        var panelManagerContainerPtrAddr = unchecked(ModuleBaseAddress + _offset.PanelManagerContainerOffset);

        // Read the PanelManagerContainer pointer value
        var panelManagerContainerAddr = ReadPointer(panelManagerContainerPtrAddr, 8);

        // Read the Panel Managers WidgetContainer
        if (!TryReadWidgetContainer(panelManagerContainerAddr, true, out var widgetContainer))
            return widgets;

        // Read the list of child widgets
        var childList = (nuint)widgetContainer["ChildWidgetsListPointer"]!;
        var childCount = GoInt.Narrow((ulong)widgetContainer["ChildWidgetSize"]!);
        if (!TryReadWidgetList(childList, childCount, out var childWidgets))
            return widgets;

        return childWidgets;
    }

    /// <summary>
    /// True when a child widget of the panel manager has this name and is active and visible (Go
    /// <c>IsWidgetVisible</c>, whose error result is always nil).
    /// </summary>
    public bool IsWidgetVisible(string widgetName) => IsActiveAndVisible(UpdateWidgets(), widgetName);

    private static bool IsActiveAndVisible(IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> widgets, string name) =>
        widgets.TryGetValue(name, out var widget) && (bool)widget["WidgetActive"]! && (bool)widget["WidgetVisible"]!;
}

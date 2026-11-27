// Memory layout from d2go pkg/memory/game_reader.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Collections.Frozen;
using D2Dotnet.Data;
using D2Dotnet.Game;

namespace D2Dotnet.Memory;

/// <summary>
/// Reads the state of a running game: a whole <see cref="GameSnapshot"/>, or one part of it. The constructor scans the
/// game code for the <see cref="GameOffsets"/>; the fixed addresses and structure offsets are those of the D2R build
/// d2go targets (see <c>docs/memory.md</c>).
/// </summary>
/// <remarks>
/// The reader keeps no state besides its offsets, so it is safe to use from several threads. Each read sees the game as
/// it is at that moment; a snapshot's parts are read one after the other while the game runs.
/// </remarks>
public sealed partial class GameReader
{
    // game_reader.go, relative to the module base.
    internal const nuint QuestsAddress = 0x22E2978;
    internal const nuint SelectedCharacterNameAddress = 0x2A01A50;
    internal const nuint LegacyGraphicsAddress = 0x21E2308;
    internal const nuint IsOnlineAddress = 0x2140ED0;
    internal const nuint IsIngameAddress = 0x22D5D78;
    internal const nuint LastGameAddress = 0x29DBD10;
    internal const nuint FpsAddress = 0x2140DF4;

    // The game keeps one hash table of 128 unit lists per unit type, 1024 bytes apart.
    internal const int UnitTableBuckets = 128;
    internal const int UnitTableSize = UnitTableBuckets * 8;

    // Guards against reading garbage forever: the longest unit, skill or party list, and the most stats, followed.
    internal const int MaxListLength = 0x4000;
    internal const int MaxStats = 0x1000;

    /// <summary>A reader of <paramref name="process"/>; scans the game code for the offsets.</summary>
    /// <param name="process">The game process; the reader does not dispose it.</param>
    /// <param name="database">The game data, for item types and object classes.</param>
    /// <exception cref="InvalidOperationException">The game module could not be read.</exception>
    /// <exception cref="InvalidDataException">A code pattern is not in the game module (an unsupported game build).</exception>
    public GameReader(GameProcess process, GameDatabase database)
    {
        Process = process ?? throw new ArgumentNullException(nameof(process));
        Database = database ?? throw new ArgumentNullException(nameof(database));
        Offsets = GameOffsets.Find(process.ReadModule());
    }

    /// <summary>The game process.</summary>
    public GameProcess Process { get; }

    /// <summary>The game data the reader looks item types and object classes up in.</summary>
    public GameDatabase Database { get; }

    /// <summary>The offsets found in the game code.</summary>
    public GameOffsets Offsets { get; }

    private nuint Module(nuint offset) => unchecked(Process.ModuleBaseAddress + offset);

    /// <summary>Reads the whole game state.</summary>
    public GameSnapshot ReadSnapshot()
    {
        var hover = ReadHover();
        var players = ReadPlayers(hover);
        var mainUnit = players.FirstOrDefault(p => p.IsMainPlayer);
        var player = mainUnit is null ? null : ReadMainPlayer(mainUnit);
        var map = mainUnit is null ? null : ReadMapContext(mainUnit);
        var origin = player?.Position ?? default;
        var (monsters, corpses) = ReadMonsterTable(origin, hover);
        var widgets = ReadWidgets();
        var isOnline = IsOnline();
        var corpse = players.FirstOrDefault(p => p.IsCorpse);

        return new GameSnapshot
        {
            Player = player,
            Map = map,
            Players = players,
            Corpse = corpse is null ? null : new PlayerCorpse(corpse.Position, corpse.IsHovered),
            Monsters = monsters,
            Corpses = corpses,
            Objects = ReadObjects(origin, hover),
            Inventory = ReadInventory(players, hover),
            Party = ReadParty(players),
            Hover = hover,
            Menus = ReadMenus(),
            Widgets = widgets,
            TerrorZones = ReadTerrorZones(isOnline),
            Quests = ReadQuests(),
            KeyBindings = ReadKeyBindings(),
            Game = ReadOnlineGame(),
            LegacyGraphics = IsLegacyGraphics(),
            IsOnline = isOnline,
            IsInGame = IsInGame(),
            IsInLobby = IsShown(widgets, LobbyPanel),
            IsInCharacterSelection = IsShown(widgets, CharacterSelectPanel),
            IsInCharacterCreation = IsShown(widgets, CharacterCreatePanel),
        };
    }

    /// <summary>
    /// True when the main player has a unit id, a position and an area: a quicker check than <see cref="IsInGame"/> that
    /// the player is in the world.
    /// </summary>
    public bool IsPlayerInWorld() =>
        ReadPlayers(null).FirstOrDefault(p => p.IsMainPlayer) is { } p
        && p.Id.Value > 0 && p.Position.X > 0 && p.Position.Y > 0 && p.Area > 0;

    /// <summary>The unit under the mouse cursor, or null.</summary>
    public HoveredUnit? ReadHover()
    {
        Span<byte> hover = stackalloc byte[12];
        Process.TryRead(Module(Offsets.Hover), hover);
        if (BitConverter.ToUInt16(hover) == 0)
            return null;
        return new HoveredUnit((UnitType)BitConverter.ToUInt32(hover[0x04..]), new UnitId(BitConverter.ToUInt32(hover[0x08..])));
    }

    /// <summary>The open panels.</summary>
    public OpenMenus ReadMenus()
    {
        var buffer = Process.ReadBytes(unchecked(Module(Offsets.UI) - 0xA), 0x16D);
        return new OpenMenus
        {
            Inventory = buffer[0x01] != 0,
            LoadingScreen = buffer[0x16C] != 0,
            NpcInteract = buffer[0x08] != 0,
            NpcShop = buffer[0x0B] != 0,
            Stash = buffer[0x18] != 0,
            Waypoint = buffer[0x13] != 0,
            MapShown = Process.ReadByte(Module(Offsets.UI)) != 0,
            SkillTree = buffer[0x04] != 0,
            Character = buffer[0x02] != 0,
            QuitMenu = buffer[0x09] != 0,
            Cube = buffer[0x19] != 0,
            SkillSelect = buffer[0x03] != 0,
            Anvil = buffer[0x0D] != 0,
        };
    }

    /// <summary>The name of the character selected in the character selection screen.</summary>
    public string ReadSelectedCharacterName() => Process.ReadString(Module(SelectedCharacterNameAddress));

    /// <summary>True when legacy graphics are on.</summary>
    public bool IsLegacyGraphics() => Process.ReadUInt64(Module(LegacyGraphicsAddress)) == 1;

    /// <summary>True when the Online tab of the character selection screen is selected.</summary>
    public bool IsOnline() => Process.ReadByte(Module(IsOnlineAddress)) == 1;

    /// <summary>True in a game.</summary>
    public bool IsInGame() => Process.ReadByte(Module(IsIngameAddress)) == 1;

    /// <summary>The last game's name and password (marked outdated upstream), and the frame rate.</summary>
    public OnlineGameInfo ReadOnlineGame() => new(
        Process.ReadString(Module(LastGameAddress + 0x8)),
        Process.ReadString(Module(LastGameAddress + 0x60)),
        Process.ReadUInt32(Module(FpsAddress)));

    /// <summary>
    /// The stat list at <paramref name="header"/>: a pointer to 8-byte entries (layer u16, id u16, value u32) and a 64-bit
    /// count. Some stats are rescaled (see <see cref="ScaleStat"/>).
    /// </summary>
    internal StatList ReadStatList(nuint header)
    {
        var list = Process.ReadPointer(header);
        var count = Process.ReadUInt64(unchecked(header + 0x08));
        if (count == 0 || count > MaxStats)
            return StatList.Empty;

        var buffer = Process.ReadBytes(list, (int)count * 8).AsSpan();
        var stats = new Stat[count];
        for (var i = 0; i < stats.Length; i++)
        {
            var entry = buffer[(i * 8)..];
            var id = (StatId)BitConverter.ToInt16(entry[0x2..]);
            stats[i] = new Stat(id, ScaleStat(id, BitConverter.ToUInt32(entry[0x4..])), BitConverter.ToUInt16(entry));
        }

        return new StatList(stats);
    }

    /// <summary>
    /// A stat's value from the game's 32-bit field: the field as a signed number, or for the stats d2go rescales, its
    /// formula on the unsigned field (saturated to the <see cref="int"/> range).
    /// </summary>
    internal static int ScaleStat(StatId id, uint raw)
    {
        long value;
        switch (id)
        {
            case StatId.Life:
            case StatId.MaxLife:
            case StatId.Mana:
            case StatId.MaxMana:
            case StatId.Stamina:
            case StatId.MaxStamina:
                value = raw >> 8;
                break;
            case StatId.ColdLength:
            case StatId.PoisonLength:
                value = raw / 25;
                break;
            case StatId.DeadlyStrikePerLevel:
                value = (long)(raw / .8);
                break;
            case StatId.HitCausesMonsterToFlee:
                value = (long)(raw / 1.28);
                break;
            case StatId.AttackRatingUndeadPerLevel:
                value = raw / 2;
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
                value = Math.Max(raw / 8, 1);
                break;
            case StatId.LifePerLevel:
            case StatId.ManaPerLevel:
                value = Math.Max(raw / 2048, 1);
                break;
            case StatId.ReplenishDurability:
            case StatId.ReplenishQuantity:
                value = raw == 0 ? 0 : Math.Max(2 / raw, 1);
                break;
            case StatId.RegenStaminaPerLevel:
                value = (long)raw * 10;
                break;
            case StatId.LevelRequirePercent:
                value = -(long)raw;
                break;
            case StatId.AttackRatingPerLevel:
                value = Math.Max(raw, 15);
                break;
            default:
                return unchecked((int)raw);
        }

        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    // Units are listed nearest to the player first; equal distances keep the game's table order.
    private static IReadOnlyList<T> NearestFirst<T>(List<T> items, Func<T, Point> position, Point origin) =>
        items.OrderBy(i => position(i).DistanceSquaredTo(origin)).ToArray();

    private static IReadOnlyDictionary<TKey, TValue> Freeze<TKey, TValue>(Dictionary<TKey, TValue> values)
        where TKey : notnull => values.ToFrozenDictionary(values.Comparer);
}

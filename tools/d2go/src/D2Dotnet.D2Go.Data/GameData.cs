// Port of d2go pkg/data/data.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using D2Dotnet.D2Go.Data.Area;
using D2Dotnet.D2Go.Data.Quest;
using D2Dotnet.D2Go.Data.Stat;

namespace D2Dotnet.D2Go.Data;

/// <summary>A snapshot of the game state read from memory (Go <c>data.Data</c>).</summary>
public sealed record GameData
{
    public Position AreaOrigin { get; init; }

    public Corpse Corpse { get; init; }

    public Monsters Monsters { get; init; } = Monsters.Empty;

    public Monsters Corpses { get; init; } = Monsters.Empty;

    public OnlineGame Game { get; init; } = new();

    /// <summary>Walkable cells: the first index is X and the second Y.</summary>
    public bool[][] CollisionGrid { get; init; } = [];

    public PlayerUnit PlayerUnit { get; init; } = new();

    public GameNpcs Npcs { get; init; } = GameNpcs.Empty;

    public Inventory Inventory { get; init; } = new();

    public GameObjects Objects { get; init; } = GameObjects.Empty;

    public IReadOnlyList<Level> AdjacentLevels { get; init; } = [];

    public IReadOnlyList<Room> Rooms { get; init; } = [];

    public OpenMenus OpenMenus { get; init; }

    /// <summary>UI widgets by name, each a property bag (Go <c>map[string]map[string]interface{}</c>).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Widgets { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, object?>>();

    public Roster Roster { get; init; } = Roster.Empty;

    public HoverData HoverData { get; init; }

    public IReadOnlyList<AreaId> TerrorZones { get; init; } = [];

    public Quests Quests { get; init; } = Quests.Empty;

    public KeyBindings KeyBindings { get; init; } = new();

    public bool LegacyGraphics { get; init; }

    public bool IsOnline { get; init; }

    public bool IsIngame { get; init; }

    public bool IsInCharCreationScreen { get; init; }

    public bool IsInCharSelectionScreen { get; init; }

    public bool IsInLobby { get; init; }

    /// <summary>
    /// The mercenary's life in percent, or 0 without a mercenary (Go <c>MercHPPercent</c>). Uses the first monster
    /// that <see cref="Monster.IsMerc"/>; a life stat up to 32768 is read as a fraction of 32768 of the max life.
    /// </summary>
    public int MercHPPercent()
    {
        foreach (var m in Monsters)
        {
            if (m.IsMerc())
            {
                // Hacky thing to read merc life properly
                var lifeStat = m.Stats.GetValueOrDefault(StatId.Life);
                var maxLife = m.Stats.GetValueOrDefault(StatId.MaxLife) >> 8;
                var life = (double)(lifeStat >> 8);
                if (lifeStat <= 32768)
                    life = lifeStat / 32768.0 * maxLife;

                return GoConvert.ToInt(life / maxLife * 100);
            }
        }

        return 0;
    }
}

/// <summary>A room of the current level (Go <c>data.Room</c>, which embeds <see cref="Data.Position"/>).</summary>
public readonly record struct Room(Position Position, int Width, int Height)
{
    public int X => Position.X;

    public int Y => Position.Y;

    /// <summary>The center, rounding the half size toward zero.</summary>
    public Position GetCenter() => new(Position.X + Width / 2, Position.Y + Height / 2);

    /// <summary>True when <paramref name="p"/> is inside the room, borders included on all four sides.</summary>
    public bool IsInside(Position p)
    {
        if (p.X >= X && p.X <= X + Width)
            return p.Y >= Y && p.Y <= Y + Height;

        return false;
    }

    /// <summary>
    /// The walkable cells of <paramref name="room"/> in <paramref name="d"/>'s collision grid, X-major. As in Go, the
    /// receiver is not used: the room passed as an argument is the one scanned.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The room is outside the grid (Go panics).</exception>
    public IReadOnlyList<Position> GetWalkableTiles(GameData d, Room room)
    {
        ArgumentNullException.ThrowIfNull(d);
        var walkableTiles = new List<Position>();
        for (var x = room.X; x < room.X + room.Width; x++)
        {
            for (var y = room.Y; y < room.Y + room.Height; y++)
            {
                if (d.CollisionGrid[x][y])
                    walkableTiles.Add(new Position(x, y));
            }
        }

        return walkableTiles;
    }
}

/// <summary>The unit under the mouse cursor (Go <c>data.HoverData</c>, which embeds <c>UnitID</c>).</summary>
public readonly record struct HoverData(bool IsHovered, UnitId UnitId, int UnitType);

/// <summary>The current online game (Go <c>data.OnlineGame</c>).</summary>
public sealed record OnlineGame
{
    public string LastGameName { get; init; } = "";

    public string LastGamePassword { get; init; } = "";

    public int Fps { get; init; }
}

/// <summary>A party member (Go <c>data.RosterMember</c>).</summary>
public sealed record RosterMember
{
    public string Name { get; init; } = "";

    public AreaId Area { get; init; }

    public Position Position { get; init; }
}

/// <summary>The party roster (Go <c>data.Roster</c>).</summary>
[CollectionBuilder(typeof(Roster), nameof(Create))]
public sealed class Roster : ReadOnlyCollection<RosterMember>
{
    public Roster()
        : base([])
    {
    }

    public Roster(IEnumerable<RosterMember> members)
        : base([.. members])
    {
    }

    /// <summary>No members (Go's nil slice).</summary>
    public static new Roster Empty { get; } = new();

    /// <summary>Collection expression builder.</summary>
    public static Roster Create(ReadOnlySpan<RosterMember> members) => new(members.ToArray());

    /// <summary>
    /// The first member whose name equals <paramref name="name"/> ignoring case (Go <c>FindByName</c>, which uses
    /// <c>strings.EqualFold</c>). When there is none, returns false and <paramref name="member"/> is the zero value.
    /// </summary>
    public bool TryFindByName(string name, out RosterMember member)
    {
        foreach (var rm in this)
        {
            if (GoStrings.EqualFold(rm.Name, name))
            {
                member = rm;
                return true;
            }
        }

        member = new RosterMember();
        return false;
    }
}

/// <summary>A level next to the current one (Go <c>data.Level</c>).</summary>
/// <param name="Area">The level.</param>
/// <param name="Position">Where its entrance or exit is.</param>
/// <param name="IsEntrance">The level can not be reached by walking through it: its entrance has to be clicked.</param>
public readonly record struct Level(AreaId Area, Position Position, bool IsEntrance);

/// <summary>Character classes (Go <c>data.Class</c>).</summary>
public enum Class : uint
{
    Amazon = 0,
    Sorceress = 1,
    Necromancer = 2,
    Paladin = 3,
    Barbarian = 4,
    Druid = 5,
    Assassin = 6,
}

/// <summary>The player's corpse (Go <c>data.Corpse</c>).</summary>
public readonly record struct Corpse(bool Found, bool IsHovered, Position Position);

/// <summary>A named point (Go <c>data.PointOfInterest</c>).</summary>
public sealed record PointOfInterest
{
    public string Name { get; init; } = "";

    public Position Position { get; init; }
}

/// <summary>Which UI panels are open (Go <c>data.OpenMenus</c>).</summary>
public readonly record struct OpenMenus
{
    public bool Inventory { get; init; }

    public bool LoadingScreen { get; init; }

    public bool NpcInteract { get; init; }

    public bool NpcShop { get; init; }

    public bool Stash { get; init; }

    public bool Waypoint { get; init; }

    public bool MapShown { get; init; }

    public bool SkillTree { get; init; }

    public bool Character { get; init; }

    public bool QuitMenu { get; init; }

    public bool Cube { get; init; }

    public bool SkillSelect { get; init; }

    public bool Anvil { get; init; }

    /// <summary>True when any menu is open; <see cref="LoadingScreen"/> and <see cref="MapShown"/> do not count.</summary>
    public bool IsMenuOpen() =>
        Inventory || NpcInteract || NpcShop || Stash || Waypoint || SkillTree || Character || QuitMenu || Cube || SkillSelect || Anvil;
}

using D2Dotnet.D2Go.Data.Area;
using D2Dotnet.D2Go.Data.Npc;
using D2Dotnet.D2Go.Data.Stat;
using D2Dotnet.D2Go.Data.State;

namespace D2Dotnet.D2Go.Data.Tests;

// Inside the namespace, so these win over the library's types of the same name in the enclosing D2Dotnet namespace.
using AreaId = D2Dotnet.D2Go.Data.Area.AreaId;
using StatId = D2Dotnet.D2Go.Data.Stat.StatId;
using StateId = D2Dotnet.D2Go.Data.State.StateId;

// Expected numbers come from running d2go's Go code (go run, amd64). Where Go's int(NaN/Inf) gives
// math.MinInt64, the port gives int.MinValue, the same "integer indefinite" value at 32 bits.
public class PlayerUnitTests
{
    private static PlayerUnit WithStats(params StatData[] stats) => new() { Stats = [.. stats] };

    [Fact]
    public void TryFindStat_PrefersStats_ThenBaseStats()
    {
        var pu = new PlayerUnit
        {
            Stats = [new StatData(StatId.Level, 5, 0)],
            BaseStats = [new StatData(StatId.Level, 9, 0), new StatData(StatId.Strength, 30, 0)],
        };

        Assert.True(pu.TryFindStat(StatId.Level, 0, out var level));
        Assert.Equal(5, level.Value);
        Assert.True(pu.TryFindStat(StatId.Strength, 0, out var strength));
        Assert.Equal(30, strength.Value);
        Assert.False(pu.TryFindStat(StatId.Level, 1, out var none));
        Assert.Equal(default, none);
    }

    [Fact]
    public void MaxGold_Is10000PerLevel()
    {
        Assert.Equal(50000, WithStats(new StatData(StatId.Level, 5, 0)).MaxGold());
        Assert.Equal(0, new PlayerUnit().MaxGold());
        // Go's int is 64-bit: no 32-bit overflow.
        Assert.Equal(3_000_000_000L, WithStats(new StatData(StatId.Level, 300_000, 0)).MaxGold());
    }

    [Fact]
    public void TotalPlayerGold_AddsInventoryAndStash()
    {
        Assert.Equal(300, WithStats(new StatData(StatId.Gold, 100, 0), new StatData(StatId.StashGold, 200, 0)).TotalPlayerGold());
        Assert.Equal(100, WithStats(new StatData(StatId.Gold, 100, 0)).TotalPlayerGold());
        Assert.Equal(0, new PlayerUnit().TotalPlayerGold());
        Assert.Equal(8_589_934_590L, WithStats(new StatData(StatId.Gold, uint.MaxValue, 0), new StatData(StatId.StashGold, uint.MaxValue, 0)).TotalPlayerGold());
    }

    [Theory]
    [InlineData(50, 200, 25)]
    [InlineData(199, 200, 99)]
    [InlineData(200, 200, 100)]
    [InlineData(300, 200, 150)]
    [InlineData(-10, 200, -5)]
    [InlineData(5, 0, int.MinValue)]
    [InlineData(0, 0, int.MinValue)]
    [InlineData(-5, 0, int.MinValue)]
    public void HPPercent_AndMPPercent_MatchGo(int value, int max, int expected)
    {
        var pu = WithStats(
            new StatData(StatId.Life, value, 0), new StatData(StatId.MaxLife, max, 0),
            new StatData(StatId.Mana, value, 0), new StatData(StatId.MaxMana, max, 0));
        Assert.Equal(expected, pu.HPPercent());
        Assert.Equal(expected, pu.MPPercent());
    }

    [Fact]
    public void HPPercent_UsesBaseStatsWhenStatsLackThem()
    {
        var pu = new PlayerUnit { BaseStats = [new StatData(StatId.Life, 30, 0), new StatData(StatId.MaxLife, 60, 0)] };
        Assert.Equal(50, pu.HPPercent());
    }

    [Theory]
    [InlineData(0u, -500, 11)]
    [InlineData(0u, -120, -1)]
    [InlineData(0u, -50, 146)]
    [InlineData(0u, 0, 19)]
    [InlineData(0u, 9, 18)]
    [InlineData(0u, 20, 17)]
    [InlineData(0u, 37, 15)]
    [InlineData(0u, 63, 14)]
    [InlineData(0u, 105, 12)]
    [InlineData(0u, 200, 11)]
    [InlineData(0u, 1000, 11)]
    [InlineData(1u, -500, 7)]
    [InlineData(1u, -120, -1)]
    [InlineData(1u, -50, 102)]
    [InlineData(1u, 0, 13)]
    [InlineData(1u, 9, 12)]
    [InlineData(1u, 20, 11)]
    [InlineData(1u, 37, 10)]
    [InlineData(1u, 63, 9)]
    [InlineData(1u, 105, 8)]
    [InlineData(1u, 200, 7)]
    [InlineData(1u, 1000, 7)]
    [InlineData(2u, -500, 9)]
    [InlineData(2u, -120, -1)]
    [InlineData(2u, -50, 117)]
    [InlineData(2u, 0, 15)]
    [InlineData(2u, 9, 14)]
    [InlineData(2u, 20, 13)]
    [InlineData(2u, 37, 12)]
    [InlineData(2u, 63, 11)]
    [InlineData(2u, 105, 10)]
    [InlineData(2u, 200, 9)]
    [InlineData(2u, 1000, 9)]
    [InlineData(3u, -500, 9)]
    [InlineData(3u, -120, -1)]
    [InlineData(3u, -50, 117)]
    [InlineData(3u, 0, 15)]
    [InlineData(3u, 9, 14)]
    [InlineData(3u, 20, 13)]
    [InlineData(3u, 37, 12)]
    [InlineData(3u, 63, 11)]
    [InlineData(3u, 105, 10)]
    [InlineData(3u, 200, 9)]
    [InlineData(3u, 1000, 9)]
    [InlineData(4u, -500, 7)]
    [InlineData(4u, -120, -1)]
    [InlineData(4u, -50, 102)]
    [InlineData(4u, 0, 13)]
    [InlineData(4u, 9, 12)]
    [InlineData(4u, 20, 11)]
    [InlineData(4u, 37, 10)]
    [InlineData(4u, 63, 9)]
    [InlineData(4u, 105, 8)]
    [InlineData(4u, 200, 7)]
    [InlineData(4u, 1000, 7)]
    [InlineData(5u, -500, 8)]
    [InlineData(5u, -120, -1)]
    [InlineData(5u, -50, 109)]
    [InlineData(5u, 0, 14)]
    [InlineData(5u, 9, 13)]
    [InlineData(5u, 20, 12)]
    [InlineData(5u, 37, 11)]
    [InlineData(5u, 63, 10)]
    [InlineData(5u, 105, 9)]
    [InlineData(5u, 200, 8)]
    [InlineData(5u, 1000, 8)]
    [InlineData(6u, -500, 9)]
    [InlineData(6u, -120, -1)]
    [InlineData(6u, -50, 124)]
    [InlineData(6u, 0, 16)]
    [InlineData(6u, 9, 15)]
    [InlineData(6u, 20, 14)]
    [InlineData(6u, 37, 13)]
    [InlineData(6u, 63, 12)]
    [InlineData(6u, 105, 10)]
    [InlineData(6u, 200, 9)]
    [InlineData(6u, 1000, 9)]
    [InlineData(99u, -500, 9)]
    [InlineData(99u, -120, -1)]
    [InlineData(99u, -50, 117)]
    [InlineData(99u, 0, 15)]
    [InlineData(99u, 9, 14)]
    [InlineData(99u, 20, 13)]
    [InlineData(99u, 37, 12)]
    [InlineData(99u, 63, 11)]
    [InlineData(99u, 105, 10)]
    [InlineData(99u, 200, 9)]
    [InlineData(99u, 1000, 9)]
    public void CastingFrames_MatchesGo(uint playerClass, int fasterCastRate, int expected)
    {
        var pu = new PlayerUnit { Class = (Class)playerClass, Stats = [new StatData(StatId.FasterCastRate, fasterCastRate, 0)] };
        Assert.Equal(expected, pu.CastingFrames());
    }

    [Fact]
    public void HasDebuff_IsTrueForAnyCurseState()
    {
        Assert.False(new PlayerUnit().HasDebuff());
        Assert.False(new PlayerUnit { States = [StateId.Freeze, StateId.Resistfire] }.HasDebuff());
        Assert.True(new PlayerUnit { States = [StateId.Freeze, StateId.Amplifydamage] }.HasDebuff());
        Assert.True(new PlayerUnit { States = [StateId.DefenseCurse] }.HasDebuff());
    }

    [Fact]
    public void Defaults_AreGoZeroValues()
    {
        var pu = new PlayerUnit();
        Assert.Equal("", pu.Name);
        Assert.Empty(pu.Stats);
        Assert.Empty(pu.BaseStats);
        Assert.Empty(pu.Skills);
        Assert.Empty(pu.States);
        Assert.Empty(pu.AvailableWaypoints);
        Assert.Equal(Class.Amazon, pu.Class);
        Assert.Equal(0u, (uint)pu.Address);
    }
}

public class GameDataTests
{
    private static Monster Merc(int life, int maxLife, NpcId name = NpcId.Guard) => new()
    {
        Name = name,
        Stats = new Dictionary<StatId, long> { [StatId.Life] = life, [StatId.MaxLife] = maxLife },
    };

    [Theory]
    [InlineData(300 << 8, 400 << 8, 75)]
    [InlineData(16384, 400 << 8, 50)]
    [InlineData(32768, 400 << 8, 100)]
    [InlineData(32769, 400 << 8, 32)]
    [InlineData(0, 400 << 8, 0)]
    [InlineData(0, 0, int.MinValue)]
    [InlineData(100, 0, int.MinValue)]
    [InlineData(40000, 0, int.MinValue)]
    public void MercHPPercent_MatchesGo(int life, int maxLife, int expected)
    {
        var data = new GameData { Monsters = [Merc(life, maxLife)] };
        Assert.Equal(expected, data.MercHPPercent());
    }

    [Fact]
    public void MercHPPercent_IsZeroWithoutMerc_AndUsesTheFirstMerc()
    {
        Assert.Equal(0, new GameData().MercHPPercent());
        Assert.Equal(0, new GameData { Monsters = [new Monster { Name = NpcId.Zombie, Stats = new Dictionary<StatId, long> { [StatId.Life] = 9000 } }] }.MercHPPercent());

        var two = new GameData
        {
            Monsters = [new Monster { Name = NpcId.Zombie }, Merc(16384, 400 << 8, NpcId.Rogue2), Merc(300 << 8, 400 << 8, NpcId.IronWolf)],
        };
        Assert.Equal(50, two.MercHPPercent());
    }

    [Fact]
    public void Defaults_AreGoZeroValues()
    {
        var data = new GameData();
        Assert.Empty(data.Monsters);
        Assert.Empty(data.Corpses);
        Assert.Empty(data.CollisionGrid);
        Assert.Empty(data.Npcs);
        Assert.Empty(data.Objects);
        Assert.Empty(data.AdjacentLevels);
        Assert.Empty(data.Rooms);
        Assert.Empty(data.Widgets);
        Assert.Empty(data.Roster);
        Assert.Empty(data.TerrorZones);
        Assert.Empty(data.Quests);
        Assert.Equal("", data.Game.LastGameName);
        Assert.Equal(4, data.Inventory.StashedGold.Count);
        Assert.Equal(16, data.KeyBindings.Skills.Count);
        Assert.Equal(4, data.KeyBindings.UseBelt.Count);
        Assert.False(data.OpenMenus.IsMenuOpen());
    }
}

public class RoomTests
{
    [Fact]
    public void GetCenter_AddsHalfTheSize_TruncatingTowardZero()
    {
        Assert.Equal(new Position(12, 23), new Room(new Position(10, 20), 5, 7).GetCenter());
        Assert.Equal(new Position(-1, -1), new Room(new Position(0, 0), -3, -3).GetCenter());
    }

    [Theory]
    [InlineData(10, 20, true)]
    [InlineData(15, 27, true)]
    [InlineData(12, 23, true)]
    [InlineData(16, 20, false)]
    [InlineData(9, 20, false)]
    [InlineData(10, 28, false)]
    [InlineData(10, 19, false)]
    public void IsInside_IncludesAllBorders(int x, int y, bool expected)
    {
        Assert.Equal(expected, new Room(new Position(10, 20), 5, 7).IsInside(new Position(x, y)));
    }

    [Fact]
    public void PromotedPosition()
    {
        var room = new Room(new Position(3, 4), 1, 1);
        Assert.Equal((3, 4), (room.X, room.Y));
    }

    [Fact]
    public void GetWalkableTiles_ScansTheArgumentRoom_XMajor()
    {
        var grid = new bool[4][];
        for (var x = 0; x < 4; x++)
            grid[x] = new bool[4];
        grid[1][1] = true;
        grid[1][2] = true;
        grid[2][1] = true;
        grid[3][3] = true; // outside the scanned room
        var data = new GameData { CollisionGrid = grid };

        var receiver = new Room(new Position(0, 0), 4, 4); // not used, as in Go
        var tiles = receiver.GetWalkableTiles(data, new Room(new Position(1, 1), 2, 2));

        Assert.Equal([new Position(1, 1), new Position(1, 2), new Position(2, 1)], tiles);
        Assert.Empty(receiver.GetWalkableTiles(data, new Room(new Position(0, 0), 0, 0)));
        Assert.Throws<IndexOutOfRangeException>(() => receiver.GetWalkableTiles(data, new Room(new Position(3, 3), 2, 2)));
    }
}

public class OpenMenusTests
{
    public static TheoryData<string, OpenMenus, bool> Menus() => new()
    {
        { "none", new OpenMenus(), false },
        { "Inventory", new OpenMenus { Inventory = true }, true },
        { "NpcInteract", new OpenMenus { NpcInteract = true }, true },
        { "NpcShop", new OpenMenus { NpcShop = true }, true },
        { "Stash", new OpenMenus { Stash = true }, true },
        { "Waypoint", new OpenMenus { Waypoint = true }, true },
        { "SkillTree", new OpenMenus { SkillTree = true }, true },
        { "Character", new OpenMenus { Character = true }, true },
        { "QuitMenu", new OpenMenus { QuitMenu = true }, true },
        { "Cube", new OpenMenus { Cube = true }, true },
        { "SkillSelect", new OpenMenus { SkillSelect = true }, true },
        { "Anvil", new OpenMenus { Anvil = true }, true },
        { "LoadingScreen", new OpenMenus { LoadingScreen = true }, false },
        { "MapShown", new OpenMenus { MapShown = true }, false },
        { "LoadingScreen+MapShown", new OpenMenus { LoadingScreen = true, MapShown = true }, false },
    };

    [Theory]
    [MemberData(nameof(Menus))]
    public void IsMenuOpen_IgnoresLoadingScreenAndMap(string name, OpenMenus menus, bool expected)
    {
        Assert.True(expected == menus.IsMenuOpen(), name);
    }
}

public class RosterTests
{
    [Fact]
    public void TryFindByName_IgnoresCase_FirstMatchWins()
    {
        Roster roster =
        [
            new RosterMember { Name = "Alice", Area = AreaId.Harrogath },
            new RosterMember { Name = "BOB", Area = AreaId.LutGholein },
            new RosterMember { Name = "bob", Area = AreaId.KurastDocks },
        ];

        Assert.True(roster.TryFindByName("alice", out var alice));
        Assert.Equal(AreaId.Harrogath, alice.Area);
        Assert.True(roster.TryFindByName("Bob", out var bob));
        Assert.Equal(AreaId.LutGholein, bob.Area);
        Assert.False(roster.TryFindByName("carol", out var none));
        Assert.Equal(new RosterMember(), none);
        Assert.False(Roster.Empty.TryFindByName("", out _));
    }
}

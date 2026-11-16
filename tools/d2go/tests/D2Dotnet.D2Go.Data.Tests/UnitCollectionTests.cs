using D2Dotnet.D2Go.Data.Item;
using D2Dotnet.D2Go.Data.Npc;
using D2Dotnet.D2Go.Data.Object;
using D2Dotnet.D2Go.Data.Skill;
using D2Dotnet.D2Go.Data.Stat;

namespace D2Dotnet.D2Go.Data.Tests;

public class InventoryTests
{
    private static GameItem Item(string name, LocationType location, int unitId = 0) =>
        new() { Name = name, Location = new ItemLocation(location, 0), UnitId = new UnitId(unitId) };

    private static readonly Inventory Inventory = new()
    {
        AllItems =
        [
            Item("HealingPotion", LocationType.Stash, 1),
            Item("ManaPotion", LocationType.Inventory, 2),
            Item("healingpotion", LocationType.Inventory, 3),
            Item("HealingPotion", LocationType.Belt, 4),
        ],
    };

    [Fact]
    public void TryFind_WithoutLocations_ReturnsFirstNameMatchIgnoringCase()
    {
        Assert.True(Inventory.TryFind("HEALINGPOTION", out var item));
        Assert.Equal(1, item.UnitId.Value);
    }

    [Fact]
    public void TryFind_WithLocations_ReturnsFirstItemInAnyOfThem()
    {
        Assert.True(Inventory.TryFind("HealingPotion", out var item, LocationType.Inventory));
        Assert.Equal(3, item.UnitId.Value);
        Assert.True(Inventory.TryFind("HealingPotion", out item, LocationType.Belt, LocationType.Inventory));
        Assert.Equal(3, item.UnitId.Value);
        Assert.True(Inventory.TryFind("HealingPotion", out item, LocationType.Belt));
        Assert.Equal(4, item.UnitId.Value);
    }

    [Fact]
    public void TryFind_NotFound_GivesZeroValue()
    {
        Assert.False(Inventory.TryFind("ManaPotion", out var item, LocationType.Stash));
        Assert.Equal(new GameItem().Id, item.Id);
        Assert.Equal("", item.Name.Value);
        Assert.False(Inventory.TryFind("Key", out _));
        Assert.False(new Inventory().TryFind("", out _));
    }

    [Fact]
    public void ByLocation_KeepsInventoryOrder_AndRepeatsForRepeatedLocations()
    {
        Assert.Equal([2, 3, 4], Inventory.ByLocation(LocationType.Belt, LocationType.Inventory).Select(i => i.UnitId.Value));
        Assert.Equal([2, 2, 3, 3], Inventory.ByLocation(LocationType.Inventory, LocationType.Inventory).Select(i => i.UnitId.Value));
        Assert.Empty(Inventory.ByLocation());
        Assert.Empty(Inventory.ByLocation(LocationType.Cube));
    }
}

public class GameItemTests
{
    private static GameTables Tables => SyntheticTables.Tables;

    private static GameItem Item(int id) => new() { Id = id };

    [Fact]
    public void Desc_And_Type_LookUpTheTables()
    {
        Assert.Equal("Minor Healing Potion", Item(SyntheticTables.MinorHealingPotion).Desc(Tables).Name);
        Assert.Equal("Healing Potion", Item(SyntheticTables.MinorHealingPotion).Type(Tables).Name);
        Assert.Equal(new ItemDescription(), Item(999).Desc(Tables));
        Assert.Equal(new ItemType(), Item(SyntheticTables.Weird).Type(Tables));
        Assert.Throws<ArgumentNullException>(() => Item(0).Desc(null!));
    }

    [Theory]
    [InlineData(SyntheticTables.MinorHealingPotion, true, true, false, false, false)]
    [InlineData(SyntheticTables.MinorManaPotion, true, false, true, false, false)]
    [InlineData(SyntheticTables.RejuvenationPotion, true, false, false, true, false)]
    [InlineData(SyntheticTables.HoradricCube, false, false, false, false, true)]
    [InlineData(SyntheticTables.HierophantAxe, false, false, false, false, false)]
    [InlineData(SyntheticTables.Weird, false, false, false, false, false)]
    [InlineData(999, false, false, false, false, false)]
    public void TypePredicates(int id, bool potion, bool healing, bool mana, bool rejuv, bool quest)
    {
        var item = Item(id);
        Assert.Equal(potion, item.IsPotion(Tables));
        Assert.Equal(healing, item.IsHealingPotion(Tables));
        Assert.Equal(mana, item.IsManaPotion(Tables));
        Assert.Equal(rejuv, item.IsRejuvPotion(Tables));
        Assert.Equal(quest, item.IsFromQuest(Tables));
    }

    [Fact]
    public void TryFindStat_PrefersStats_ThenBaseStats()
    {
        var item = new GameItem
        {
            Stats = [new StatData(StatId.Defense, 50, 0)],
            BaseStats = [new StatData(StatId.Defense, 10, 0), new StatData(StatId.Durability, 20, 0)],
        };

        Assert.True(item.TryFindStat(StatId.Defense, 0, out var defense));
        Assert.Equal(50, defense.Value);
        Assert.True(item.TryFindStat(StatId.Durability, 0, out var durability));
        Assert.Equal(20, durability.Value);
        Assert.False(item.TryFindStat(StatId.Durability, 1, out var none));
        Assert.Equal(default, none);
    }
}

public class BeltTests
{
    private static GameItem Potion(string name, int x, int y) => new() { Name = name, Position = new Position(x, y) };

    [Fact]
    public void TryGetFirstPotion_TakesTheFirstMatchOfTheBottomRow()
    {
        var belt = new Belt
        {
            Items =
            [
                Potion("SuperHealingPotion", 0, 1), // not row 0
                Potion("ManaPotion", 2, 0),
                Potion("HealingPotion", 4, 0), // not columns 0-3
                Potion("healingpotion", 1, 0), // case-sensitive match
                Potion("GreaterHealingPotion", 3, 0),
                Potion("HealingPotion", 0, 0),
            ],
        };

        Assert.True(belt.TryGetFirstPotion(PotionType.HealingPotion, out var position));
        Assert.Equal(new Position(3, 0), position);
        Assert.True(belt.TryGetFirstPotion(PotionType.ManaPotion, out position));
        Assert.Equal(new Position(2, 0), position);
        Assert.False(belt.TryGetFirstPotion(PotionType.RejuvenationPotion, out position));
        Assert.Equal(default, position);
        Assert.True(new Belt { Items = [Potion("FullRejuvenationPotion", 0, 0)] }.TryGetFirstPotion(PotionType.RejuvenationPotion, out _));
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("Sash", 2)]
    [InlineData("LightBelt", 2)]
    [InlineData("Belt", 3)]
    [InlineData("HeavyBelt", 3)]
    [InlineData("PlatedBelt", 4)]
    [InlineData("sash", 4)]
    [InlineData("Arachnid Mesh", 4)]
    public void Rows_ByBeltName(string name, int rows)
    {
        Assert.Equal(rows, new Belt { Name = name }.Rows());
    }

    [Fact]
    public void Rows_DefaultBelt_IsOneRow()
    {
        Assert.Equal(1, new Belt().Rows());
    }
}

public class MonsterTests
{
    private static Monster Mon(NpcId name, MonsterType type = default, int life = 100, int unitId = 0) => new()
    {
        Name = name,
        Type = type,
        UnitId = new UnitId(unitId),
        Stats = new Dictionary<StatId, int> { [StatId.Life] = life },
    };

    [Fact]
    public void TryFindOne_MatchesClass_AndTypeUnlessNone()
    {
        Monsters monsters =
        [
            Mon(NpcId.Zombie, MonsterType.Champion, unitId: 1),
            Mon(NpcId.Zombie, MonsterType.Minion, unitId: 2),
            Mon(NpcId.Fallen, unitId: 3),
        ];

        Assert.True(monsters.TryFindOne(NpcId.Zombie, MonsterType.None, out var m));
        Assert.Equal(1, m.UnitId.Value);
        Assert.True(monsters.TryFindOne(NpcId.Zombie, MonsterType.Minion, out m));
        Assert.Equal(2, m.UnitId.Value);
        Assert.False(monsters.TryFindOne(NpcId.Zombie, MonsterType.Unique, out m));
        Assert.Equal(new Monster().Name, m.Name);
        // Go: the zero MonsterType ("") is not MonsterTypeNone ("None"): it only matches monsters whose type is "".
        Assert.True(monsters.TryFindOne(NpcId.Fallen, default, out m));
        Assert.False(monsters.TryFindOne(NpcId.Zombie, default, out _));
    }

    [Fact]
    public void TryFindById()
    {
        Monsters monsters = [Mon(NpcId.Zombie, unitId: 7), Mon(NpcId.Fallen, unitId: 8)];
        Assert.True(monsters.TryFindById(new UnitId(8), out var m));
        Assert.Equal(NpcId.Fallen, m.Name);
        Assert.False(monsters.TryFindById(new UnitId(9), out _));
    }

    [Fact]
    public void Enemies_DropsMercsSkipsGoodNpcsPetsAndTheDead()
    {
        Monsters monsters =
        [
            Mon(NpcId.Zombie, MonsterType.None, unitId: 1),
            Mon(NpcId.Guard, unitId: 2), // merc
            Mon(NpcId.BaalTaunt, unitId: 3), // skip
            Mon((NpcId)146, unitId: 4), // good NPC
            Mon(NpcId.ClayGolem, unitId: 5), // pet
            Mon(NpcId.Zombie, life: 0, unitId: 6), // dead
            new Monster { Name = NpcId.Zombie, UnitId = new UnitId(7) }, // no life stat
            Mon(NpcId.Fallen, MonsterType.Unique, unitId: 8),
            Mon(NpcId.Fallen, MonsterType.Champion, life: -5, unitId: 9),
        ];

        Assert.Equal([1, 8], monsters.Enemies().Select(m => m.UnitId.Value));
        Assert.Equal([8], monsters.Enemies(MonsterFilters.MonsterEliteFilter()).Select(m => m.UnitId.Value));
        Assert.Equal([1, 8], monsters.Enemies(MonsterFilters.MonsterAnyFilter()).Select(m => m.UnitId.Value));
        Assert.Empty(monsters.Enemies(MonsterFilters.MonsterEliteFilter(), m => new Monsters(m.Where(x => x.Name == NpcId.Zombie))));
        Assert.Equal([1], monsters.Enemies(m => new Monsters(m.Where(x => x.UnitId.Value == 1)), MonsterFilters.MonsterAnyFilter()).Select(m => m.UnitId.Value));
        Assert.Empty(Monsters.Empty.Enemies());
    }

    [Fact]
    public void MonsterAnyFilter_ReturnsItsArgument()
    {
        Monsters monsters = [Mon(NpcId.Zombie)];
        Assert.Same(monsters, MonsterFilters.MonsterAnyFilter()(monsters));
        Assert.Empty(MonsterFilters.MonsterEliteFilter()(monsters));
    }

    public static TheoryData<StatId, int, string, bool> Immunities() => new()
    {
        { StatId.ColdResist, 100, "cold", true },
        { StatId.ColdResist, 99, "cold", false },
        { StatId.ColdResist, 150, "fire", false },
        { StatId.FireResist, 100, "fire", true },
        { StatId.LightningResist, 120, "light", true },
        { StatId.LightningResist, 120, "lightning", false },
        { StatId.PoisonResist, 100, "poison", true },
        { StatId.MagicResist, 100, "magic", true },
        { StatId.Life, 1000, "cold", false },
        { StatId.MaxColdResist, 100, "cold", false },
    };

    [Theory]
    [MemberData(nameof(Immunities))]
    public void IsImmune_NeedsTheMatchingResistAtLeast100(StatId stat, int value, string resist, bool expected)
    {
        var monster = new Monster { Stats = new Dictionary<StatId, int> { [stat] = value } };
        Assert.Equal(expected, monster.IsImmune(resist));
    }

    [Fact]
    public void IsImmune_ChecksEveryStat()
    {
        var monster = new Monster
        {
            Stats = new Dictionary<StatId, int> { [StatId.FireResist] = 50, [StatId.ColdResist] = 100, [StatId.Life] = 5 },
        };
        Assert.True(monster.IsImmune(Resist.ColdImmune));
        Assert.False(monster.IsImmune(Resist.FireImmune));
        Assert.False(new Monster().IsImmune(Resist.MagicImmune));
    }

    [Theory]
    [InlineData("Champion", true)]
    [InlineData("Minion", true)]
    [InlineData("Unique", true)]
    [InlineData("SuperUnique", true)]
    [InlineData("None", false)]
    [InlineData("", false)]
    [InlineData("unique", false)]
    public void IsElite(string type, bool expected)
    {
        Assert.Equal(expected, new Monster { Type = type }.IsElite());
    }

    [Fact]
    public void ClassPredicates_Samples()
    {
        // Full lists are checked against the Go source in ConstantsOracleTests.
        Assert.True(Mon(NpcId.Act5Hireling2Hand).IsMerc());
        Assert.False(Mon(NpcId.Zombie).IsMerc());
        Assert.True(Mon(NpcId.ShadowMaster).IsPet());
        Assert.False(Mon(NpcId.Zombie).IsPet());
        Assert.True(Mon((NpcId)406).IsGoodNpc());
        Assert.False(Mon(NpcId.Zombie).IsGoodNpc());
        Assert.True(Mon(NpcId.FallenShaman).IsMonsterRaiser());
        Assert.False(Mon(NpcId.Fallen).IsMonsterRaiser());
        Assert.True(Mon(NpcId.WaterWatcherHead).IsSkip());
        Assert.False(Mon(NpcId.Zombie).IsSkip());
    }
}

public class NpcAndObjectCollectionTests
{
    [Fact]
    public void GameNpcs_TryFindOne()
    {
        GameNpcs npcs =
        [
            new GameNpc { Id = NpcId.Akara, Name = "first", Positions = [new Position(1, 2)] },
            new GameNpc { Id = NpcId.Akara, Name = "second" },
        ];

        Assert.True(npcs.TryFindOne(NpcId.Akara, out var npc));
        Assert.Equal("first", npc.Name);
        Assert.False(npcs.TryFindOne(NpcId.Kashya, out npc));
        Assert.Equal("", npc.Name);
        Assert.Empty(npc.Positions);
    }

    [Fact]
    public void GameObjects_TryFindOne_AndTryFindById()
    {
        GameObjects objects =
        [
            new GameObject { Id = new UnitId(1), Name = ObjectName.Shrine },
            new GameObject { Id = new UnitId(2), Name = ObjectName.TownPortal, Owner = "me" },
            new GameObject { Id = new UnitId(3), Name = ObjectName.TownPortal },
        ];

        Assert.True(objects.TryFindOne(ObjectName.TownPortal, out var obj));
        Assert.Equal(2, obj.Id.Value);
        Assert.False(objects.TryFindOne(ObjectName.Barrel, out obj));
        Assert.Equal("", obj.Owner);
        Assert.True(objects.TryFindById(new UnitId(3), out obj));
        Assert.Equal(ObjectName.TownPortal, obj.Name);
        Assert.False(objects.TryFindById(new UnitId(4), out _));
    }

    [Fact]
    public void GameObject_Predicates_Samples()
    {
        Assert.True(new GameObject { Name = ObjectName.TownPortal }.IsPortal());
        Assert.False(new GameObject { Name = ObjectName.PermanentTownPortal }.IsPortal());
        Assert.True(new GameObject { Name = ObjectName.PermanentTownPortal }.IsRedPortal());
        Assert.True(new GameObject { Name = ObjectName.WaypointPortal }.IsWaypoint());
        Assert.True(new GameObject { Name = ObjectName.GoodChest }.IsChest());
        Assert.True(new GameObject { Name = ObjectName.GoodChest }.IsSuperChest());
        Assert.True(new GameObject { Name = ObjectName.NotSoGoodChestName }.IsChest());
        Assert.False(new GameObject { Name = ObjectName.NotSoGoodChestName }.IsSuperChest());
        Assert.True(new GameObject { Name = ObjectName.AndarielDoor }.IsDoor());
        Assert.True(new GameObject { Shrine = new ShrineData { ShrineType = ShrineType.PoisonShrine } }.IsShrine());
        Assert.False(new GameObject { Name = ObjectName.Shrine }.IsShrine()); // only the shrine type counts
    }

    [Fact]
    public void GameObject_Desc_IsPromotedFromName()
    {
        Assert.Equal("Shrine", new GameObject { Name = ObjectName.Shrine }.Desc(SyntheticTables.Tables).Name);
        Assert.Equal(new ObjectDescription(), new GameObject { Name = ObjectName.NotApplicable }.Desc(SyntheticTables.Tables));
    }
}

public class KeyBindingsTests
{
    private static KeyBinding Kb(byte a, byte b) => new(new KeyBytes(a, b), new KeyBytes(0, 0));

    [Fact]
    public void TryGetKeyBindingForSkill_FirstMatchingEntry()
    {
        var skills = new SkillBinding[16];
        for (var i = 0; i < 16; i++)
            skills[i] = new SkillBinding(SkillId.Unset, default);
        skills[3] = new SkillBinding(SkillId.Teleport, Kb(0x70, 0));
        skills[5] = new SkillBinding(SkillId.Teleport, Kb(0x71, 0));
        var kb = new KeyBindings { Skills = skills };

        Assert.True(kb.TryGetKeyBindingForSkill(SkillId.Teleport, out var binding));
        Assert.Equal(0x70, binding.Key1[0]);
        Assert.Equal(binding, kb.MustKBForSkill(SkillId.Teleport));
        Assert.False(kb.TryGetKeyBindingForSkill(SkillId.FrozenOrb, out binding));
        Assert.Equal(default, binding);
        Assert.Equal(default, kb.MustKBForSkill(SkillId.FrozenOrb));
    }

    [Fact]
    public void UnsetEntries_AreAttackSkill_AsInGo()
    {
        var kb = new KeyBindings();
        Assert.True(kb.TryGetKeyBindingForSkill(SkillId.AttackSkill, out var binding));
        Assert.Equal(default, binding);
        Assert.False(kb.TryGetKeyBindingForSkill(SkillId.Teleport, out _));
    }

    [Fact]
    public void KeyBytes_IndexesLikeAGoArray()
    {
        var key = new KeyBytes(0x41, 0x02);
        Assert.Equal(2, key.Length);
        Assert.Equal(0x41, key[0]);
        Assert.Equal(0x02, key[1]);
        Assert.Throws<IndexOutOfRangeException>(() => key[2]);
        Assert.Throws<IndexOutOfRangeException>(() => key[-1]);
    }

    [Fact]
    public void SkillBinding_PromotesKeys()
    {
        var sb = new SkillBinding(SkillId.Nova, new KeyBinding(new KeyBytes(1, 2), new KeyBytes(3, 4)));
        Assert.Equal(new KeyBytes(1, 2), sb.Key1);
        Assert.Equal(new KeyBytes(3, 4), sb.Key2);
    }
}

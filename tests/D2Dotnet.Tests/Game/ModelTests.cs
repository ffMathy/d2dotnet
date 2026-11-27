using D2Dotnet.Data;
using D2Dotnet.Game;

namespace D2Dotnet.Tests.Game;

public class MonsterTests
{
    private static Monster With(MonsterId id, params (StatId Id, int Value)[] stats) =>
        new() { Class = id, Stats = new StatList(stats.Select(s => new Stat(s.Id, s.Value))) };

    [Fact]
    public void Monster_LifePercent_IsAShareOfFullLife_OrOfMaxLife()
    {
        static Monster M(params (StatId Id, int Value)[] stats) => new() { Stats = new StatList(stats.Select(s => new Stat(s.Id, s.Value))) };

        Assert.Equal(100, M((StatId.Life, Monster.FullLife)).LifePercent);
        Assert.Equal(50, M((StatId.Life, Monster.FullLife / 2)).LifePercent);
        Assert.Equal(0, M((StatId.Life, 0)).LifePercent);
        Assert.Equal(25, M((StatId.Life, 50 * 256), (StatId.MaxLife, 200 * 256)).LifePercent);
        Assert.Equal(100, M((StatId.Life, 300), (StatId.MaxLife, 200)).LifePercent); // clamped
        Assert.Equal(100, M((StatId.Life, Monster.FullLife), (StatId.MaxLife, 0)).LifePercent); // no usable max life
        Assert.Null(M().LifePercent);
    }

    [Fact]
    public void IsImmune_NeedsAResistanceOf100()
    {
        var m = With(MonsterId.Zombie, (StatId.FireResist, 100), (StatId.ColdResist, 99), (StatId.LightningResist, 150),
            (StatId.PoisonResist, -10), (StatId.MagicResist, 100));
        Assert.True(m.IsImmune(Element.Fire));
        Assert.False(m.IsImmune(Element.Cold));
        Assert.True(m.IsImmune(Element.Lightning));
        Assert.False(m.IsImmune(Element.Poison)); // d2go: -10 read as 4294967286, "immune"
        Assert.True(m.IsImmune(Element.Magic));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.IsImmune((Element)99));
        Assert.False(With(MonsterId.Zombie).IsImmune(Element.Fire)); // no resistance stat
    }

    [Fact]
    public void GetStat_IsNullWhenMissing_AndReadsTheLayer()
    {
        // A missing stat is null, not 0: a monster without a fire resistance stat differs from one with 0%.
        var m = new Monster
        {
            Stats = new StatList([new Stat(StatId.FireResist, 0), new Stat(StatId.SingleSkill, 3, Layer: 54)]),
        };
        Assert.Equal(0, m.GetStat(StatId.FireResist));
        Assert.Null(m.GetStat(StatId.ColdResist));
        Assert.Equal(3, m.GetStat(StatId.SingleSkill, 54));
        Assert.Null(m.GetStat(StatId.SingleSkill));
        Assert.Null(new Monster().GetStat(StatId.Life));
    }

    [Fact]
    public void IsEnemy_LeavesOutFriendsPetsAndTheDead()
    {
        Assert.True(With(MonsterId.Zombie, (StatId.Life, 1)).IsEnemy);
        Assert.False(With(MonsterId.Zombie).IsEnemy);
        Assert.False(With(MonsterId.Guard, (StatId.Life, 1)).IsEnemy);
        Assert.False(With(MonsterId.ClayGolem, (StatId.Life, 1)).IsEnemy);
        Assert.False(With(MonsterId.BaalTaunt, (StatId.Life, 1)).IsEnemy);
        Assert.False(With((MonsterId)146, (StatId.Life, 1)).IsEnemy); // a town NPC
        Assert.False(With(MonsterId.Zombie, (StatId.Life, 0)).IsEnemy);
        Assert.Null(With(MonsterId.Zombie).GetStat(StatId.Life));
    }

    [Theory]
    [InlineData(MonsterType.Normal, false)]
    [InlineData(MonsterType.Champion, true)]
    [InlineData(MonsterType.Minion, true)]
    [InlineData(MonsterType.Unique, true)]
    [InlineData(MonsterType.SuperUnique, true)]
    public void IsElite_IsEveryTypeButNormal(MonsterType type, bool expected) => Assert.Equal(expected, new Monster { Type = type }.IsElite);
}

public class ObjectTests
{
    [Fact]
    public void ShrineNames_AndKinds()
    {
        Assert.Equal("Experience Shrine", ShrineType.ExperienceShrine.GetName());
        Assert.Equal("HP XChange Shrine", ShrineType.HPXChangeShrine.GetName());
        Assert.Null(((ShrineType)0).GetName());
        Assert.True(new GameObject { Shrine = ShrineType.PoisonShrine }.IsShrine);
        Assert.False(new GameObject { Shrine = (ShrineType)0x17 }.IsShrine);
        Assert.False(new GameObject().IsShrine);
    }

    [Fact]
    public void Classes()
    {
        Assert.True(new GameObject { Class = ObjectId.TownPortal }.IsPortal);
        Assert.True(new GameObject { Class = ObjectId.PermanentTownPortal }.IsRedPortal);
        Assert.True(new GameObject { Class = ObjectId.WaypointPortal }.IsWaypoint);
        Assert.True(new GameObject { Class = (ObjectId)104 }.IsChest);
        Assert.True(new GameObject { Class = (ObjectId)104 }.IsSuperChest);
        Assert.False(new GameObject { Class = (ObjectId)1 }.IsSuperChest);
        Assert.True(new GameObject { Class = ObjectId.AndarielDoor }.IsDoor);
    }
}

public class ItemTests
{
    private static ItemInfo Info(string code, string normal, string uber, string ultra, string type = "axe") =>
        new(ItemId.HandAxe, ItemKind.Weapon, "x", "x", code, normal, uber, ultra, type, 1, 1, 0, 0);

    [Fact]
    public void Tier_ComesFromTheTierCodes()
    {
        Assert.Equal(ItemTier.Normal, new Item { Info = Info("hax", "hax", "9ha", "7ha") }.Tier);
        Assert.Equal(ItemTier.Exceptional, new Item { Info = Info("9ha", "hax", "9ha", "7ha") }.Tier);
        Assert.Equal(ItemTier.Elite, new Item { Info = Info("7ha", "hax", "9ha", "7ha") }.Tier);
        // d2go called an item without tier codes (and its zero value) elite.
        Assert.Null(new Item { Info = Info("hp1", "", "", "") }.Tier);
        Assert.Null(new Item().Tier);
    }

    [Fact]
    public void Types_AreExactTypeCodes()
    {
        Assert.True(new Item { Info = Info("hp1", "", "", "", ItemTypeCode.HealingPotion) }.IsHealingPotion);
        Assert.True(new Item { Info = Info("mp1", "", "", "", ItemTypeCode.ManaPotion) }.IsPotion);
        Assert.True(new Item { Info = Info("rvs", "", "", "", ItemTypeCode.RejuvPotion) }.IsPotionOf(PotionType.Rejuvenation));
        Assert.False(new Item { Info = Info("rvs", "", "", "", ItemTypeCode.RejuvPotion) }.IsPotionOf(PotionType.Healing));
        Assert.True(new Item { Info = Info("box", "", "", "", ItemTypeCode.Quest) }.IsQuestItem);
        Assert.False(new Item { Info = Info("x", "", "", "", ItemTypeCode.Potion) }.IsPotion); // parents are not considered
        Assert.False(new Item().IsPotion);
        Assert.Null(new Item().TypeCode);
    }

    [Fact]
    public void GetStat_PrefersStats()
    {
        var item = new Item { Stats = new StatList([new Stat(StatId.Defense, 75)]), BaseStats = new StatList([new Stat(StatId.Defense, 50), new Stat(StatId.Durability, 3)]) };
        Assert.Equal(75, item.GetStat(StatId.Defense));
        Assert.Equal(3, item.GetStat(StatId.Durability));
        Assert.Null(item.GetStat(StatId.Strength));
    }
}

public class InventoryTests
{
    private static readonly Inventory Inv = new()
    {
        Items =
        [
            new Item { Id = new UnitId(1), Class = ItemId.Key, Location = ItemLocation.Stash },
            new Item { Id = new UnitId(2), Class = ItemId.Key, Location = ItemLocation.Inventory },
            new Item { Id = new UnitId(3), Class = ItemId.Buckler, Location = ItemLocation.Equipped },
        ],
    };

    [Fact]
    public void Find_ByBaseItemAndLocations()
    {
        Assert.Equal(1u, Inv.Find(ItemId.Key)?.Id.Value);
        Assert.Equal(2u, Inv.Find(ItemId.Key, ItemLocation.Inventory, ItemLocation.Cursor)?.Id.Value);
        Assert.Null(Inv.Find(ItemId.Key, ItemLocation.Equipped));
        Assert.Null(Inv.Find(ItemId.Axe));
    }

    [Fact]
    public void InLocation_ListsEachItemOnce()
    {
        // d2go listed an item once per matching location argument.
        Assert.Equal([1u, 2u], Inv.InLocation(ItemLocation.Inventory, ItemLocation.Stash, ItemLocation.Stash).Select(i => i.Id.Value));
        Assert.Empty(Inv.InLocation());
    }

    [Fact]
    public void Defaults()
    {
        var inv = new Inventory();
        Assert.Equal([0, 0, 0, 0], inv.StashGold);
        Assert.Equal(1, inv.Belt.Rows);
    }
}

public class BeltTests
{
    private static Item Potion(string type, int x, int y) => new()
    {
        Info = new ItemInfo(ItemId.HealingPotion, ItemKind.Misc, "p", "p", "p", "", "", "", type, 1, 1, 0, 0),
        Position = new Point(x, y),
    };

    [Theory]
    [InlineData(null, 1)]
    [InlineData(ItemId.Sash, 2)]
    [InlineData(ItemId.LightBelt, 2)]
    [InlineData(ItemId.Belt, 3)]
    [InlineData(ItemId.HeavyBelt, 3)]
    [InlineData(ItemId.PlatedBelt, 4)]
    [InlineData(ItemId.DemonhideSash, 4)]
    public void Rows_DependOnTheBelt(ItemId? belt, int rows) => Assert.Equal(rows, new Belt { Equipped = belt }.Rows);

    [Fact]
    public void FindPotion_IsTheFirstOfItsTypeInTheBottomRow()
    {
        // d2go matched a case-sensitive substring of the item name.
        var belt = new Belt
        {
            Items =
            [
                Potion(ItemTypeCode.HealingPotion, 0, 1),
                Potion(ItemTypeCode.ManaPotion, 1, 0),
                Potion(ItemTypeCode.HealingPotion, 4, 0),
                Potion(ItemTypeCode.HealingPotion, 3, 0),
                Potion(ItemTypeCode.RejuvPotion, 2, 0),
            ],
        };
        Assert.Equal(new Point(3, 0), belt.FindPotion(PotionType.Healing)?.Position);
        Assert.Equal(new Point(1, 0), belt.FindPotion(PotionType.Mana)?.Position);
        Assert.Equal(new Point(2, 0), belt.FindPotion(PotionType.Rejuvenation)?.Position);
        Assert.Null(new Belt().FindPotion(PotionType.Healing));
    }
}

public class KeyBindingsTests
{
    private static readonly KeyBinding F1 = new(new Key(0x70, 0), default);

    private static KeyBindings With(params SkillBinding[] first)
    {
        var skills = new SkillBinding[KeyBindings.SkillSlots];
        first.CopyTo(skills, 0);
        return new KeyBindings { Skills = skills };
    }

    [Fact]
    public void FindSkill_IgnoresSlotsWithoutAKey()
    {
        // d2go found AttackSkill (0) in the first unset slot.
        var kb = With(new SkillBinding(SkillId.Teleport, default), new SkillBinding(SkillId.Teleport, F1));
        Assert.Equal(F1, kb.FindSkill(SkillId.Teleport));
        Assert.Null(kb.FindSkill(SkillId.AttackSkill));
        Assert.Null(new KeyBindings().FindSkill(SkillId.AttackSkill));
    }

    [Fact]
    public void Lists_AreCopiedAndCompareByValue()
    {
        var skills = new SkillBinding[KeyBindings.SkillSlots];
        var kb = new KeyBindings { Skills = skills, Run = F1 };
        skills[0] = new SkillBinding(SkillId.Teleport, F1);
        Assert.Equal(default, kb.Skills[0]);

        Assert.Equal(new KeyBindings { Run = F1 }, kb);
        Assert.Equal(new KeyBindings { Run = F1 }.GetHashCode(), kb.GetHashCode());
        Assert.NotEqual(new KeyBindings(), kb);
        Assert.NotEqual(With(new SkillBinding(SkillId.Teleport, F1)), new KeyBindings());
        Assert.NotEqual(new KeyBindings { UseBelt = [F1, default, default, default] }, new KeyBindings());
        Assert.Throws<ArgumentException>(() => new KeyBindings { UseBelt = [F1] });
    }

    [Fact]
    public void Keys_AreNoneWhenZero()
    {
        Assert.True(default(Key).IsNone);
        Assert.False(new Key(0, 1).IsNone);
        Assert.True(default(KeyBinding).IsEmpty);
        Assert.False(F1.IsEmpty);
    }
}

public class QuestTests
{
    [Fact]
    public void IsCompleted_IsTheQuestLogFlag()
    {
        Assert.True((QuestStatus.UpdateQuestLogCompleted | QuestStatus.RewardGranted).IsCompleted());
        Assert.False(QuestStatus.PrimaryGoalCompleted.IsCompleted());
        Assert.False(QuestStatus.None.IsCompleted());
    }
}

public class AreasTests
{
    [Fact]
    public void Towns()
    {
        Assert.True(AreaId.RogueEncampment.IsTown());
        Assert.True(AreaId.PandemoniumFortress.IsTown());
        Assert.False(AreaId.BloodMoor.IsTown());
    }

    [Theory]
    [InlineData(-1, ActId.Act1)]
    [InlineData(39, ActId.Act1)]
    [InlineData(40, ActId.Act2)]
    [InlineData(74, ActId.Act2)]
    [InlineData(75, ActId.Act3)]
    [InlineData(102, ActId.Act3)]
    [InlineData(103, ActId.Act4)]
    [InlineData(108, ActId.Act4)]
    [InlineData(109, ActId.Act5)]
    [InlineData(200, ActId.Act5)]
    public void GetAct_ByIdRange(int area, ActId act) => Assert.Equal(act, ((AreaId)area).GetAct());

    [Fact]
    public void TerrorZonesAndWaypoints()
    {
        Assert.True(AreaId.ChaosSanctuary.CanBeTerrorized());
        Assert.False(AreaId.RogueEncampment.CanBeTerrorized());
        Assert.Equal(new WaypointLocation(4, 2, Areas.Waypoints[AreaId.CityOfTheDamned].LinkedFrom), Areas.Waypoints[AreaId.CityOfTheDamned]);
        Assert.Empty(Areas.Waypoints[AreaId.Harrogath].LinkedFrom);
    }
}

public class OpenMenusTests
{
    [Fact]
    public void IsAnyPanelOpen_IgnoresTheLoadingScreenAndTheAutomap()
    {
        Assert.False(new OpenMenus().IsAnyPanelOpen);
        Assert.False(new OpenMenus { LoadingScreen = true, MapShown = true }.IsAnyPanelOpen);
        Assert.True(new OpenMenus { Anvil = true }.IsAnyPanelOpen);
        Assert.True(new OpenMenus { Inventory = true, MapShown = true }.IsAnyPanelOpen);
    }

    [Fact]
    public void Widget_IsShownWhenActiveAndVisible()
    {
        Assert.True(new Widget("a", true, true).IsShown);
        Assert.False(new Widget("a", true, false).IsShown);
        Assert.False(new Widget("a", false, true).IsShown);
    }
}

public class GameSnapshotTests
{
    private static Monster Merc(int life, int maxLife, MonsterId id = MonsterId.Guard) => new()
    {
        Class = id,
        Stats = new StatList([new Stat(StatId.Life, life), new Stat(StatId.MaxLife, maxLife)]),
    };

    [Theory]
    [InlineData(300 << 8, 400 << 8, 75)]
    [InlineData(16384, 400 << 8, 50)]
    [InlineData(32768, 400 << 8, 100)]
    [InlineData(32769, 400 << 8, 32)]
    [InlineData(0, 400 << 8, 0)]
    public void MercenaryLifePercent_MatchesD2go(int life, int maxLife, int expected) =>
        Assert.Equal(expected, new GameSnapshot { Monsters = [Merc(life, maxLife)] }.MercenaryLifePercent);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 255)]
    [InlineData(40000, -256)]
    public void MercenaryLifePercent_WithoutAMaxLife_IsNull(int life, int maxLife)
    {
        // d2go divided by zero and returned the minimum integer.
        Assert.Null(new GameSnapshot { Monsters = [Merc(life, maxLife)] }.MercenaryLifePercent);
    }

    [Fact]
    public void MercenaryLifePercent_MissingStats()
    {
        // No max life: unknown. No life stat: 0%.
        Assert.Null(new GameSnapshot { Monsters = [new Monster { Class = MonsterId.Guard, Stats = new StatList([new Stat(StatId.Life, 300 << 8)]) }] }.MercenaryLifePercent);
        Assert.Equal(0, new GameSnapshot { Monsters = [new Monster { Class = MonsterId.Guard, Stats = new StatList([new Stat(StatId.MaxLife, 400 << 8)]) }] }.MercenaryLifePercent);
    }

    [Fact]
    public void Mercenary_IsTheFirstMercenary()
    {
        Assert.Null(new GameSnapshot().MercenaryLifePercent);
        Assert.Null(new GameSnapshot { Monsters = [new Monster { Class = MonsterId.Zombie }] }.Mercenary);
        var two = new GameSnapshot
        {
            Monsters = [new Monster { Class = MonsterId.Zombie }, Merc(16384, 400 << 8, MonsterId.Rogue2), Merc(300 << 8, 400 << 8, MonsterId.IronWolf)],
        };
        Assert.Equal(50, two.MercenaryLifePercent);
    }

    [Fact]
    public void Lookups()
    {
        var snapshot = new GameSnapshot
        {
            Party = [new PartyMember("Alice", AreaId.Harrogath, default), new PartyMember("BOB", AreaId.LutGholein, default), new PartyMember("bob", AreaId.KurastDocks, default)],
            Monsters = [new Monster { Id = new UnitId(5), Stats = new StatList([new Stat(StatId.Life, 1)]) }, Merc(1, 1)],
            Objects = [new GameObject { Id = new UnitId(9) }],
            Quests = new Dictionary<QuestId, QuestStatus> { [QuestId.Act1DenOfEvil] = QuestStatus.RewardGranted },
        };

        Assert.Equal(AreaId.LutGholein, snapshot.FindPartyMember("Bob")?.Area);
        Assert.Equal(AreaId.Harrogath, snapshot.FindPartyMember("ALICE")?.Area);
        Assert.Null(snapshot.FindPartyMember("carol"));
        Assert.Equal(5u, Assert.Single(snapshot.Enemies).Id.Value);
        Assert.NotNull(snapshot.FindObject(new UnitId(9)));
        Assert.Null(snapshot.FindMonster(new UnitId(77)));
        Assert.Equal(QuestStatus.RewardGranted, snapshot.GetQuestStatus(QuestId.Act1DenOfEvil));
        Assert.Equal(QuestStatus.None, snapshot.GetQuestStatus(QuestId.Act2TaintedSun));
    }

    [Fact]
    public void Defaults_AreEmpty()
    {
        var d = new GameSnapshot();
        Assert.Null(d.Player);
        Assert.Empty(d.Monsters);
        Assert.Empty(d.Corpses);
        Assert.Empty(d.Objects);
        Assert.Empty(d.Widgets);
        Assert.Empty(d.Party);
        Assert.Empty(d.TerrorZones);
        Assert.Empty(d.Quests);
        Assert.Equal("", d.Game.LastGameName);
        Assert.Equal(16, d.KeyBindings.Skills.Count);
        Assert.Equal(4, d.KeyBindings.UseBelt.Count);
    }
}

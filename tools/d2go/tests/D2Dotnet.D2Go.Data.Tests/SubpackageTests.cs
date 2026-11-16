using D2Dotnet.D2Go.Data.Area;
using D2Dotnet.D2Go.Data.Difficulty;
using D2Dotnet.D2Go.Data.Item;
using D2Dotnet.D2Go.Data.Object;
using D2Dotnet.D2Go.Data.Quest;
using D2Dotnet.D2Go.Data.Skill;
using D2Dotnet.D2Go.Data.Stat;
using D2Dotnet.D2Go.Data.State;

namespace D2Dotnet.D2Go.Data.Tests;

// Inside the namespace, so these win over the library's types of the same name in the enclosing D2Dotnet namespace.
using AreaId = D2Dotnet.D2Go.Data.Area.AreaId;
using QuestId = D2Dotnet.D2Go.Data.Quest.QuestId;
using SkillId = D2Dotnet.D2Go.Data.Skill.SkillId;
using StatId = D2Dotnet.D2Go.Data.Stat.StatId;
using StateId = D2Dotnet.D2Go.Data.State.StateId;

public class AreaTests
{
    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(39, 1)]
    [InlineData(40, 2)]
    [InlineData(74, 2)]
    [InlineData(75, 3)]
    [InlineData(102, 3)]
    [InlineData(103, 4)]
    [InlineData(108, 4)]
    [InlineData(109, 5)]
    [InlineData(141, 5)]
    [InlineData(1000, 5)]
    public void Act_ByIdRange_MatchesGo(int id, int act)
    {
        Assert.Equal(act, ((AreaId)id).Act());
    }

    [Fact]
    public void IsTown_AndCanBeTerrorized()
    {
        Assert.True(AreaId.Harrogath.IsTown());
        Assert.False(AreaId.BloodMoor.IsTown());
        Assert.True(AreaId.BloodMoor.CanBeTerrorized());
        Assert.False(AreaId.RogueEncampment.CanBeTerrorized());
        Assert.False(((AreaId)(-1)).CanBeTerrorized());
    }

    [Fact]
    public void Area_LooksUpTheTables_ZeroValueWhenMissing()
    {
        Assert.Equal("Rogue Encampment", AreaId.RogueEncampment.Area(SyntheticTables.Tables).Name);
        var missing = AreaId.Harrogath.Area(SyntheticTables.Tables);
        Assert.Equal(AreaId.None, missing.Id);
        Assert.Equal("", missing.Name);
        Assert.Throws<ArgumentNullException>(() => AreaId.None.Area(null!));
    }

    [Fact]
    public void AreaInfo_PromotesTheIdMethods()
    {
        var info = new AreaInfo { Id = AreaId.Harrogath, Name = "Harrogath" };
        Assert.True(info.IsTown());
        Assert.False(info.CanBeTerrorized());
        Assert.Equal(5, info.Act());
        Assert.Equal("Blood Moor", new AreaInfo { Id = AreaId.BloodMoor }.Area(SyntheticTables.Tables).Name);
    }

    [Fact]
    public void Waypoints_TownsHaveNoLinks()
    {
        Assert.Empty(Waypoints.WPAddresses[AreaId.RogueEncampment].LinkedFrom);
        Assert.Equal(new[] { AreaId.Harrogath, AreaId.BloodyFoothills }, Waypoints.WPAddresses[AreaId.FrigidHighlands].LinkedFrom);
        Assert.Equal((5, 2), (Waypoints.WPAddresses[AreaId.FrigidHighlands].Tab, Waypoints.WPAddresses[AreaId.FrigidHighlands].Row));
    }
}

public class ItemPackageTests
{
    [Theory]
    [InlineData("7ha", "hax", "9ha", "7ha", ItemTier.Elite)]
    [InlineData("9ha", "hax", "9ha", "7ha", ItemTier.Exceptional)]
    [InlineData("hax", "hax", "9ha", "7ha", ItemTier.Normal)]
    [InlineData("hp1", "", "", "", ItemTier.Normal)]
    [InlineData("", "", "", "", ItemTier.Elite)]
    [InlineData("abc", "abc", "abc", "abc", ItemTier.Elite)]
    [InlineData("abc", "x", "abc", "y", ItemTier.Exceptional)]
    public void Tier_ComparesTheCodes(string code, string normal, string uber, string ultra, ItemTier tier)
    {
        var d = new ItemDescription { Code = code, NormalCode = normal, UberCode = uber, UltraCode = ultra };
        Assert.Equal(tier, d.Tier());
    }

    [Fact]
    public void ZeroDescription_IsElite_AsInGo()
    {
        Assert.Equal(ItemTier.Elite, new ItemDescription().Tier());
    }

    [Fact]
    public void GetItemType_LooksUpTheType()
    {
        var tables = SyntheticTables.Tables;
        Assert.Equal("Healing Potion", tables.ItemDesc(SyntheticTables.MinorHealingPotion).GetItemType(tables).Name);
        Assert.Equal(new ItemType(), tables.ItemDesc(SyntheticTables.Weird).GetItemType(tables));
        Assert.Throws<ArgumentNullException>(() => new ItemDescription().GetItemType(null!));
    }

    [Fact]
    public void ItemType_IsType_ComparesTheCodeExactly()
    {
        var type = new ItemType { Code = "hpot" };
        Assert.True(type.IsType(ItemTypeCodes.TypeHealingPotion));
        Assert.False(type.IsType("HPOT"));
        Assert.False(type.IsType(ItemTypeCodes.TypePotion)); // no parent types
        Assert.True(new ItemType().IsType(""));
    }

    [Theory]
    [InlineData(ItemQuality.LowQuality, "LowQuality")]
    [InlineData(ItemQuality.Normal, "Normal")]
    [InlineData(ItemQuality.Superior, "Superior")]
    [InlineData(ItemQuality.Magic, "Magic")]
    [InlineData(ItemQuality.Set, "Set")]
    [InlineData(ItemQuality.Rare, "Rare")]
    [InlineData(ItemQuality.Unique, "Unique")]
    [InlineData(ItemQuality.Crafted, "UnknownItemQuality")] // missing case in Go
    [InlineData((ItemQuality)0, "UnknownItemQuality")]
    [InlineData((ItemQuality)9, "UnknownItemQuality")]
    public void ToQualityString_MatchesGo(ItemQuality quality, string expected)
    {
        Assert.Equal(expected, quality.ToQualityString());
    }

    [Fact]
    public void ItemNames_ById()
    {
        Assert.Equal("HandAxe", ItemNames.GetNameByEnum(0).Value);
        Assert.Equal(new ItemName("HealingPotion"), ItemNames.GetNameByEnum(589));
        Assert.Equal("StandardOfHeroes", ItemNames.GetNameByEnum((uint)ItemNames.Names.Count - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemNames.GetNameByEnum((uint)ItemNames.Names.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemNames.GetNameByEnum(uint.MaxValue));
    }

    [Fact]
    public void ItemNames_GetIdByName_IgnoresCase()
    {
        Assert.Equal(0, ItemNames.GetIdByName("handaxe"));
        Assert.Equal(589, ItemNames.GetIdByName("HEALINGPOTION"));
        Assert.Equal(-1, ItemNames.GetIdByName("NoSuchItem"));
        Assert.Equal(-1, ItemNames.GetIdByName(""));
    }

    [Fact]
    public void ItemLocation_HoldsTypeAndPage()
    {
        var location = new ItemLocation(LocationType.SharedStash, 2);
        Assert.Equal("shared_stash", location.LocationType.Value);
        Assert.Equal(2, location.Page);
        Assert.Equal("", default(ItemLocation).LocationType.Value);
    }
}

public class ObjectPackageTests
{
    [Fact]
    public void Desc_LooksUpByNumericId()
    {
        Assert.Equal(new ObjectDescription { Id = 2, Name = "Shrine", SizeX = 3, SizeY = 3 }, ObjectName.Shrine.Desc(SyntheticTables.Tables));
        Assert.Equal(new ObjectDescription(), ObjectName.NotApplicable.Desc(SyntheticTables.Tables));
        Assert.Throws<ArgumentNullException>(() => ObjectName.Shrine.Desc(null!));
    }

    [Fact]
    public void ShrineTypeNames()
    {
        Assert.Equal("HP XChange Shrine", ShrineTypes.ShrineTypeNames[ShrineType.HPXChangeShrine]);
        Assert.Equal(22, ShrineTypes.ShrineTypeNames.Count);
        Assert.Equal("", new ShrineData().ShrineName);
    }
}

public class QuestPackageTests
{
    [Fact]
    public void QuestStates_Completed_AndHasStatus()
    {
        QuestStates states = [QuestStatus.QuestInitiated, QuestStatus.UpdateQuestLogCompleted];
        Assert.True(states.Completed());
        Assert.True(states.HasStatus(QuestStatus.QuestInitiated));
        Assert.False(states.HasStatus(QuestStatus.RewardGranted));
        QuestStates pending = [QuestStatus.RewardPending];
        Assert.False(pending.Completed());
        Assert.False(QuestStates.Empty.Completed());
    }

    [Fact]
    public void Quests_StatesOf_IsEmptyForMissingQuests()
    {
        var quests = new Quests([new KeyValuePair<QuestId, QuestStates>(QuestId.Act1DenOfEvil, [QuestStatus.UpdateQuestLogCompleted])]);
        Assert.True(quests.StatesOf(QuestId.Act1DenOfEvil).Completed());
        Assert.Same(QuestStates.Empty, quests.StatesOf(QuestId.Act5EveOfDestruction));
        Assert.Empty(Quests.Empty);
    }
}

public class SkillPackageTests
{
    [Fact]
    public void Desc_IndexesSkillDescByRowIndex_AsInGo()
    {
        var tables = SyntheticTables.Tables;
        Assert.Equal(-1, SkillId.Kick.Desc(tables).ListRow); // row 1
        // Teleport is skill 54, but skilldesc.txt has only 3 rows: Go's Desc[54] is the zero value.
        Assert.Equal(new SkillDescription(), SkillId.Teleport.Desc(tables));
        Assert.Throws<ArgumentNullException>(() => SkillId.Kick.Desc(null!));
    }

    [Fact]
    public void SkillInfo_Desc_UsesItsRowIndexId()
    {
        var tables = SyntheticTables.Tables;
        // skills[54] has ID 2 (its row index), whose skilldesc row is the teleport one.
        Assert.Equal(18, tables.Skill(SkillId.Teleport).Desc(tables).IconCel);
    }

    [Fact]
    public void SkillNames_KeepGoIdentifiers()
    {
        Assert.Equal("monInfernoSentry", SkillNames.Names[SkillId.MonInfernoSentry]);
        Assert.Equal("Unset", SkillNames.Names[SkillId.Unset]);
    }

    [Fact]
    public void SkillPoints_IsAValue()
    {
        Assert.Equal(new SkillPoints(1, 2, 3), new SkillPoints(1, 2, 3));
        Assert.Equal(0u, default(SkillPoints).Charges);
    }
}

public class StatAndStatePackageTests
{
    [Fact]
    public void Stats_TryFindStat_MatchesIdAndLayer_FirstWins()
    {
        Stats stats = [new StatData(StatId.Life, 1, 0), new StatData(StatId.Life, 2, 1), new StatData(StatId.Life, 3, 1)];
        Assert.True(stats.TryFindStat(StatId.Life, 1, out var stat));
        Assert.Equal(2, stat.Value);
        Assert.False(stats.TryFindStat(StatId.Life, 2, out stat));
        Assert.Equal(default, stat);
        Assert.False(stats.TryFindStat(StatId.Mana, 0, out _));
    }

    [Fact]
    public void ToStatString_IsTheLowerCaseName()
    {
        Assert.Equal("strength", StatId.Strength.ToStatString());
        Assert.Equal("maxlife", StatId.MaxLife.ToStatString());
        Assert.Equal("skillmissiledamagescale", StatId.SkillMissileDamageScale.ToStatString());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((StatId)361).ToStatString());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((StatId)(-1)).ToStatString());
    }

    [Fact]
    public void States_HasState()
    {
        States states = [StateId.Freeze, StateId.Poison];
        Assert.True(states.HasState(StateId.Poison));
        Assert.False(states.HasState(StateId.None));
        Assert.False(States.Empty.HasState(StateId.None));
    }

    [Fact]
    public void Collections_CopyTheirInput()
    {
        var list = new List<StatData> { new(StatId.Life, 1, 0) };
        var stats = new Stats(list);
        list.Clear();
        Assert.Single(stats);
    }
}

public class GoStringTypeTests
{
    [Fact]
    public void Default_IsTheEmptyString()
    {
        Assert.Equal("", default(MonsterType).Value);
        Assert.Equal("", default(ItemName).ToString());
        Assert.Equal(default, new LocationType(""));
        Assert.Equal(default(Resist).GetHashCode(), new Resist("").GetHashCode());
        Assert.True(default(PotionType) == "");
        Assert.Equal("", default(GameDifficulty).Value);
    }

    [Fact]
    public void Equality_IsOrdinal()
    {
        Assert.True(MonsterType.Unique == "Unique");
        Assert.True(MonsterType.Unique != "unique");
        Assert.False(MonsterType.Unique.Equals((object)"Unique"));
        Assert.True(MonsterType.Unique.Equals((object)new MonsterType("Unique")));
        Assert.Equal(new ItemName("Key"), (ItemName)ItemNames.Key);
    }

    [Fact]
    public void Constants_HaveGoValues()
    {
        Assert.Equal("normal", GameDifficulty.Normal);
        GameDifficulty hell = GameDifficulty.Hell;
        Assert.Equal("hell", hell.ToString());
        Assert.Equal("cold", Resist.ColdImmune.Value);
        Assert.Equal("SuperUnique", MonsterType.SuperUnique.Value);
        Assert.Equal("RejuvenationPotion", PotionType.RejuvenationPotion.Value);
        Assert.Equal("cursor", LocationType.Cursor.Value);
    }

    [Fact]
    public void Constructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ItemName(null!));
    }

    [Fact]
    public void UnitId_IsAnInt()
    {
        var id = (UnitId)42;
        Assert.Equal(42, (int)id);
        Assert.Equal("42", id.ToString());
        Assert.Equal(new UnitId(42), id);
        Assert.Equal(0, default(UnitId).Value);
    }
}

using D2Dotnet.Data;
using D2Dotnet.Files;

namespace D2Dotnet.Tests.Data;

public class GameDatabaseTests
{
    private static readonly GameDatabase Db = SyntheticGame.Database();

    [Fact]
    public void Constructor_ChecksItsArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new GameDatabase(null!));
        Assert.Throws<ArgumentException>(() => new GameDatabase(new MemoryFiles(), " "));
        Assert.Equal("deDE", new GameDatabase(new MemoryFiles(), "DEU").Language);
        Assert.Equal("enUS", new GameDatabase(new MemoryFiles()).Language);
    }

    // ---- raw tables ----

    [Fact]
    public void GetTable_ReadsTheExcelFolder_IgnoringCaseAndExtension()
    {
        var levels = Db.GetTable("Levels");
        Assert.NotNull(levels);
        Assert.Equal("levels.txt", levels.Name);
        Assert.Same(levels, Db.GetTable("LEVELS.txt"));
        Assert.Same(levels, Db.GetRequiredTable("levels"));
        Assert.Null(Db.GetTable("LvlPrest"));
        var error = Assert.Throws<FileNotFoundException>(() => Db.GetRequiredTable("LvlPrest"));
        Assert.Equal("data/global/excel/lvlprest.txt", error.FileName);
        Assert.Throws<ArgumentException>(() => Db.GetTable(""));
    }

    [Fact]
    public void GetTable_IsThreadSafe()
    {
        var db = SyntheticGame.Database();
        var tables = new ExcelTable?[64];
        Parallel.For(0, tables.Length, i => tables[i] = db.GetTable(i % 2 == 0 ? "monstats" : "MonStats.txt"));
        Assert.All(tables, t => Assert.Same(tables[0], t));
    }

    // ---- typed tables ----

    [Theory]
    [InlineData("levels.txt")]
    [InlineData("monstats.txt")]
    [InlineData("superuniques.txt")]
    [InlineData("objects.txt")]
    [InlineData("weapons.txt")]
    [InlineData("armor.txt")]
    [InlineData("misc.txt")]
    [InlineData("itemtypes.txt")]
    [InlineData("skills.txt")]
    [InlineData("skilldesc.txt")]
    public void MissingFileIsAnError_AndStaysOne(string file)
    {
        var db = new GameDatabase(SyntheticGame.Files().Remove("data/global/excel/" + file));
        Func<object> table = file switch
        {
            "levels.txt" => () => db.Levels,
            "monstats.txt" => () => db.Monsters,
            "superuniques.txt" => () => db.SuperUniques,
            "objects.txt" => () => db.Objects,
            "itemtypes.txt" => () => db.ItemTypes,
            "skills.txt" => () => db.Skills,
            "skilldesc.txt" => () => db.SkillDescriptions,
            _ => () => db.Items,
        };
        var error = Assert.Throws<FileNotFoundException>(table);
        Assert.Equal("data/global/excel/" + file, error.FileName);
        Assert.Same(error, Assert.Throws<FileNotFoundException>(table));
    }

    [Fact]
    public void MissingKeyColumnIsAnError()
    {
        var db = new GameDatabase(SyntheticGame.Files().Add("data/global/excel/levels.txt", "Name\tId\nAct 1\t1\n"));
        var error = Assert.Throws<InvalidDataException>(() => db.Levels);
        Assert.Contains("LevelName", error.Message);
        Assert.Contains("levels.txt", error.Message);

        db = new GameDatabase(SyntheticGame.Files().Add("data/global/excel/skills.txt", "skill\tleftskill\trightskill\nAttack\t1\t1\n"));
        Assert.Contains("*Id", Assert.Throws<InvalidDataException>(() => db.Skills).Message);
    }

    [Fact]
    public void MalformedNumberIsAnError()
    {
        var db = new GameDatabase(SyntheticGame.Files().Add("data/global/excel/levels.txt", "Id\tLevelName\n1x\tTown\n"));
        var error = Assert.Throws<InvalidDataException>(() => db.Levels);
        Assert.Contains("'1x'", error.Message);
        Assert.Contains("'Id'", error.Message);
    }

    [Theory]
    [InlineData("40000")]
    [InlineData("-32769")]
    public void SkillIdOutside16Bits_IsAnInvalidDataError(string id)
    {
        // The game keeps skill ids in 16 bits; this was an OverflowException naming nothing.
        var db = new GameDatabase(SyntheticGame.Files().Add("data/global/excel/skills.txt", $"skill\t*Id\nAttack\t0\nBig\t{id}\n"));
        var error = Assert.Throws<InvalidDataException>(() => db.Skills);
        Assert.Contains("skills.txt", error.Message);
        Assert.Contains("'*Id'", error.Message);
        Assert.Contains($"'{id}'", error.Message);
        Assert.Contains("row 1", error.Message);

        db = new GameDatabase(SyntheticGame.Files().Add("data/global/excel/skills.txt", "skill\t*Id\nLast\t32767\nFirst\t-32768\n"));
        Assert.Equal([(SkillId)short.MaxValue, (SkillId)short.MinValue], db.Skills.Select(s => s.Id));
    }

    [Fact]
    public void Levels_AreKeyedByTheirId_WithoutTheDivider()
    {
        Assert.Equal([AreaId.None, AreaId.RogueEncampment, AreaId.BloodMoor, AreaId.DenOfEvil, AreaId.LutGholein], Db.Levels.Select(l => l.Id));
        Assert.Equal(new LevelInfo(AreaId.LutGholein, "Act 2 - Town", "Lut Gholein", ActId.Act2, 2, 5, 9), Db.Levels.Find(AreaId.LutGholein));
        Assert.Equal(new LevelInfo(AreaId.None, "Null", "", ActId.Act1, 0, 0, 255), Db.Levels[0]);
        Assert.Null(Db.Levels.Find(AreaId.ColdPlains));
        Assert.True(Db.Levels.TryGet(AreaId.BloodMoor, out var bloodMoor));
        Assert.Equal("Blood Moor", bloodMoor.NameKey);
        Assert.True(Db.Levels.Contains(AreaId.DenOfEvil));
        Assert.Equal(5, Db.Levels.Count);
    }

    [Fact]
    public void Levels_OptionalColumnsHaveDefaults()
    {
        var db = new GameDatabase(new MemoryFiles().Add("data/global/excel/levels.txt", "Id\tLevelName\n1\tTown\n"));
        Assert.Equal(new LevelInfo(AreaId.RogueEncampment, "", "Town", ActId.Act1, 0, 0, 255), db.Levels.Single());
    }

    [Fact]
    public void Monsters_AreGameRows_TheDividerNotCounted()
    {
        Assert.Equal(
            [new MonsterInfo((MonsterId)0, "zombie", "Zombie"), new MonsterInfo((MonsterId)1, "fallen", "Fallen1"), new MonsterInfo((MonsterId)2, "unnamed", "")],
            Db.Monsters);
        Assert.Equal("unnamed", Db.Monsters.Find((MonsterId)2)?.Code);
    }

    [Fact]
    public void SuperUniques_AreGameRows_TheDividerNotCounted()
    {
        Assert.Equal(["Bishibosh", "Bonebreaker", "Coldcrow"], Db.SuperUniques.Select(s => s.Code));
        Assert.Equal(new SuperUniqueInfo(2, "Coldcrow", "Coldcrow", "cr_archer1"), Db.SuperUniques.Find(2));
    }

    [Fact]
    public void Objects_AreKeyedByTheirIdColumn()
    {
        Assert.Equal(new ObjectInfo((ObjectId)119, " Waypoint ", 5, 5, false, true, 23), Db.Objects.Find((ObjectId)119));
        Assert.Equal(new ObjectInfo(ObjectId.LargeChestRight, "LargeChestR", 2, 2, true, false, 4), Db.Objects.Find(ObjectId.LargeChestRight));
        Assert.Equal("", Db.Objects.Find(ObjectId.TestData1)?.NameKey);
        Assert.Equal([0, 2, 5, 7, 119], Db.Objects.Select(o => (int)o.Id));
    }

    [Fact]
    public void Objects_WithoutAnIdColumnAreKeyedByGameRow_AndClassicNamesWork()
    {
        var noId = new GameDatabase(new MemoryFiles().Add("data/global/excel/objects.txt",
            "Name\tOperateFn\tSelectable0\nfirst\t1\t1\nExpansion\t\t\nsecond\t2\t0\n"));
        Assert.Equal(new ObjectInfo((ObjectId)0, "first", 0, 0, false, true, 1), noId.Objects.Find((ObjectId)0));
        Assert.Equal(new ObjectInfo((ObjectId)1, "second", 0, 0, false, false, 2), noId.Objects.Find((ObjectId)1));

        var classic = new GameDatabase(new MemoryFiles().Add("data/global/excel/objects.txt", "Name\tToken\tId\nWaypoint\twp\t119\n"));
        Assert.Equal("Waypoint", classic.Objects.Find((ObjectId)119)?.NameKey);
    }

    [Fact]
    public void Items_UseOneRunningIdAcrossWeaponsArmorAndMisc()
    {
        Assert.Equal(Enumerable.Range(0, 9), Db.Items.Select(i => (int)i.Id));
        Assert.Equal(
            new ItemInfo(ItemId.HandAxe, ItemKind.Weapon, "Heirophant Axe", "hax", "hax", "hax", "9ha", "7ha", "axe", 1, 3, 0, 0),
            Db.Items[0]);
        Assert.Equal(
            new ItemInfo((ItemId)2, ItemKind.Armor, "Colossal Shield", "", "uit", "buc", "uit", "xuit", "shie", 2, 2, 5, 10),
            Db.Items.Find((ItemId)2));
        Assert.Equal([ItemKind.Weapon, ItemKind.Weapon, ItemKind.Armor], Db.Items.Take(3).Select(i => i.Kind));
        Assert.Equal("Essense of Ornate Armor", Db.Items[7].Name); // the game's own spelling
    }

    [Fact]
    public void Items_WithoutNormcode_HaveNoTierCodes_EvenWithAnUbercodeColumn()
    {
        var potion = Db.Items.Find((ItemId)3)!;
        Assert.Equal(("hp1", "", "", ""), (potion.Code, potion.NormalCode, potion.UberCode, potion.UltraCode));
        Assert.Equal((0, 0), (potion.MinDefense, potion.MaxDefense));
        Assert.Equal(ItemKind.Misc, potion.Kind);
    }

    [Fact]
    public void Items_NumbersAreTrimmed_AndItemsAreFoundByCode()
    {
        Assert.Equal(2, Db.Items[8].InventoryWidth);
        Assert.Equal((ItemId)3, Db.FindItem("hp1")?.Id);
        Assert.Equal(ItemId.HandAxe, Db.FindItem("hax")?.Id);
        Assert.Null(Db.FindItem("nope"));
    }

    [Fact]
    public void Items_WithoutNameColumn_HaveEmptyNames()
    {
        var db = new GameDatabase(SyntheticGame.Files().Add("data/global/excel/misc.txt", "code\tx\tinvwidth\tinvheight\ttype\nhp1\t1\t1\t1\thpot\n"));
        Assert.Equal("", db.Items.Find((ItemId)3)!.Name);
    }

    [Fact]
    public void ItemTypes_AreGameRows_KeyedByCode()
    {
        Assert.Equal(["None", "Healing Potion", "Not Used", "Mana Potion", "Javelin"], Db.ItemTypes.Select(t => t.Name));
        Assert.Equal([0, 1, 2, 3, 4], Db.ItemTypes.Select(t => t.Index));
        Assert.Equal(new ItemTypeInfo(1, "Healing Potion", "hpot", "poti", "", false, true), Db.ItemTypes.Find(ItemTypeCode.HealingPotion));
        Assert.Equal(new ItemTypeInfo(4, "Javelin", "jave", "miss", "thro", true, false), Db.ItemTypes.Find(ItemTypeCode.Javelin));
        Assert.Equal(["hpot", "jave", "mpot", "none"], Db.ItemTypes.ByKey.Keys.Order(StringComparer.Ordinal)); // no key for the code-less row
    }

    [Fact]
    public void IsItemType_FollowsTheEquivColumnsUp()
    {
        var db = new GameDatabase(new MemoryFiles().Add("data/global/excel/itemtypes.txt",
            "ItemType\tCode\tEquiv1\tEquiv2\tThrowable\tBeltable\n" +
            "Weapon\tweap\t\t\t0\t0\n" +
            "Staff\tstaf\tweap\t\t0\t0\n" +
            "Armor\tarmo\t\t\t0\t0\n" +
            "Helm\thelm\tarmo\t\t0\t0\n" +
            "Circlet\tcirc\thelm\t\t0\t0\n" +
            "Javelin\tjave\tmiss\tweap\t1\t0\n" +
            "Loop\tloop\tloop\t\t0\t0\n"));
        Assert.True(db.IsItemType("staf", "weap"));
        Assert.True(db.IsItemType("circ", "armo")); // two steps up
        Assert.True(db.IsItemType("jave", "weap")); // through Equiv2
        Assert.True(db.IsItemType("helm", "helm"));
        Assert.False(db.IsItemType("helm", "weap"));
        Assert.False(db.IsItemType("loop", "weap")); // a cycle ends
        Assert.True(db.IsItemType("nope", "nope"));
        Assert.False(db.IsItemType("nope", "weap"));
    }

    [Fact]
    public void Skills_ReadTheManaColumns_AndCostByLevel()
    {
        var db = new GameDatabase(new MemoryFiles().Add("data/global/excel/skills.txt",
            "skill\t*Id\tmana\tlvlmana\tmanashift\tminmana\n" +
            "Ring of Fire\t394\t16\t1\t7\t1\n" +
            "Cleave\t391\t3\t0\t8\t1\n" +
            "Free\t0\t\t\t\t\n"));
        var ring = db.Skills.Find(SkillId.RingOfFire)!;
        Assert.Equal((16, 1, 7, 1), (ring.Mana, ring.ManaPerLevel, ring.ManaShift, ring.MinimumMana));
        Assert.Equal(8, ring.ManaCost(1));
        Assert.Equal(8.5, ring.ManaCost(2));
        Assert.Equal(8, ring.ManaCost(0)); // below 1 counts as 1
        Assert.Equal(3, db.Skills.Find(SkillId.Cleave)!.ManaCost(20));
        Assert.Equal(0, db.Skills.Find(SkillId.AttackSkill)!.ManaCost(1)); // empty cells

        // Tables without the columns: no cost; the minimum still applies.
        Assert.Equal(0, Db.Skills.Find(SkillId.Teleport)!.ManaCost(1));
        Assert.Equal(1, new SkillInfo(SkillId.Teleport, "", "", "", false, true) { MinimumMana = 1 }.ManaCost(1));
    }

    [Fact]
    public void Skills_ReadTheRequiredLevelAndSkills_ByName()
    {
        var db = new GameDatabase(new MemoryFiles().Add("data/global/excel/skills.txt",
            "skill\t*Id\treqlevel\treqskill1\treqskill2\treqskill3\n" +
            "Fire Bolt\t36\t1\t\t\t\n" +
            "Fire Ball\t47\t12\tFire Bolt\t\t\n" +
            "Meteor\t56\t24\tfire ball\tNo Such Skill\t\n"));
        Assert.Equal(1, db.Skills.Find(SkillId.FireBolt)!.RequiredLevel);
        Assert.Empty(db.Skills.Find(SkillId.FireBolt)!.RequiredSkills);
        Assert.Equal([SkillId.FireBolt], db.Skills.Find(SkillId.FireBall)!.RequiredSkills);
        Assert.Equal([SkillId.FireBall], db.Skills.Find(SkillId.Meteor)!.RequiredSkills); // names match ignoring case; unknown ones are left out
        Assert.Equal(24, db.Skills.Find(SkillId.Meteor)!.RequiredLevel);
        Assert.Empty(Db.Skills.Find(SkillId.Teleport)!.RequiredSkills); // no such columns
    }

    [Fact]
    public void Skills_AreKeyedByTheIdColumn()
    {
        Assert.Equal([SkillId.AttackSkill, SkillId.Kick, SkillId.Teleport], Db.Skills.Select(s => s.Id));
        Assert.Equal(new SkillInfo(SkillId.Teleport, "Teleport", "sor", "teleport", false, true), Db.Skills.Find(SkillId.Teleport));
        Assert.Equal(new SkillInfo(SkillId.AttackSkill, "Attack", "", "attack", true, true), Db.Skills.Find(SkillId.AttackSkill));

        var classic = new GameDatabase(new MemoryFiles().Add("data/global/excel/skills.txt", "skill\tId\tleftskill\trightskill\nTeleport\t54\t0\t1\n"));
        Assert.Equal("Teleport", classic.Skills.Find(SkillId.Teleport)?.Name);
    }

    [Fact]
    public void SkillDescriptions_AreKeyedByName()
    {
        Assert.Equal([0, 1, 2], Db.SkillDescriptions.Select(d => d.Index));
        Assert.Equal(new SkillDescriptionInfo(1, "kick", 0, 0, 0, -1, 0), Db.SkillDescriptions.Find("kick"));
        var teleport = Db.Skills.Find(SkillId.Teleport)!;
        Assert.Equal(new SkillDescriptionInfo(2, "teleport", 3, 4, 2, 0, 18), Db.SkillDescriptions.Find(teleport.Description));
    }

    [Fact]
    public void Tables_AreImmutable()
    {
        Assert.Throws<NotSupportedException>(() => ((IDictionary<ItemId, ItemInfo>)Db.Items.ByKey).Add((ItemId)100, Db.Items[0]));
    }

    [Fact]
    public void Tables_LoadOnceUnderConcurrency()
    {
        var db = SyntheticGame.Database();
        var seen = new object[32];
        Parallel.For(0, seen.Length, i => seen[i] = i % 3 == 0 ? db.Levels : i % 3 == 1 ? db.Items : db.Localization);
        Assert.All(seen.Where((_, i) => i % 3 == 0), l => Assert.Same(seen[0], l));
        Assert.All(seen.Where((_, i) => i % 3 == 1), l => Assert.Same(seen[1], l));
        Assert.All(seen.Where((_, i) => i % 3 == 2), l => Assert.Same(seen[2], l));
    }

    [Fact]
    public void Localization_UsesTheDatabaseLanguage()
    {
        Assert.Equal("enUS", Db.Localization.Language);
        Assert.Equal(["data/local/lng/strings/item-names.json", "data/local/lng/strings/levels.json", "data/local/lng/strings/monsters.json"], Db.Localization.Files);
        Assert.Equal("deDE", SyntheticGame.Database("deu").Localization.Language);
    }

    // ---- real tables ----

    [Fact]
    public void ClassicTables_LoadEveryTypedTable()
    {
        var db = new GameDatabase(Fixtures.LibD2Files);
        AssertEveryAreaUpToUberTristram(db);
        Assert.Equal("zombie1", db.Monsters.Find(MonsterId.Zombie)?.Code);
        Assert.Equal("andariel", db.Monsters.Find(MonsterId.Andariel)?.Code);
        Assert.Equal("Bishibosh", db.SuperUniques[0].Code);
        Assert.Equal("hax", db.Items.Find(ItemId.HandAxe)?.Code);
        Assert.Equal("Waypoint", db.Objects.Find(ObjectId.WaypointPortal)?.NameKey);
        Assert.Equal("teleport", db.Skills.Find(SkillId.Teleport)?.Name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Healing Potion", db.ItemTypes.Find(ItemTypeCode.HealingPotion)?.Name);
        Assert.NotNull(db.SkillDescriptions.Find(db.Skills.Find(SkillId.Teleport)!.Description));
        // The tables the map generator reads raw.
        foreach (var name in new[] { "LvlPrest", "LvlTypes", "LvlMaze", "LvlSub", "LvlWarp", "MonPlace", "MonPreset" })
            Assert.True(db.GetTable(name)?.RowCount > 0, name);
    }

    [Fact]
    public void ResurrectedTables_LoadEveryD2GoTable()
    {
        var db = new GameDatabase(Fixtures.D2GoFiles);
        AssertEveryAreaUpToUberTristram(db);
        Assert.Equal("hax", db.Items.Find(ItemId.HandAxe)?.Code);
        Assert.Equal("Teleport", db.Skills.Find(SkillId.Teleport)?.Name);
        Assert.Equal("Healing Potion", db.ItemTypes.Find(ItemTypeCode.HealingPotion)?.Name);
        Assert.True(db.Objects.Count > 500);
    }

    private static void AssertEveryAreaUpToUberTristram(GameDatabase db)
    {
        var expected = Enum.GetValues<AreaId>().Where(a => a <= AreaId.UberTristram).ToArray();
        Assert.Equal(expected, db.Levels.Select(l => l.Id));
        Assert.Equal("Rogue Encampment", db.Levels.Find(AreaId.RogueEncampment)?.NameKey);
        Assert.Equal(ActId.Act5, db.Levels.Find(AreaId.Harrogath)?.Act);
        Assert.Equal(ActId.Act2, db.Levels.Find(AreaId.LutGholein)?.Act);
    }
}

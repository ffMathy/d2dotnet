using System.Text;
using D2Dotnet.D2Go.Data.Area;
using D2Dotnet.D2Go.Data.Item;
using D2Dotnet.D2Go.Data.Skill;

namespace D2Dotnet.D2Go.Data.Tests;

// Inside the namespace, so these win over the library's types of the same name in the enclosing D2Dotnet namespace.
using AreaId = D2Dotnet.D2Go.Data.Area.AreaId;
using SkillId = D2Dotnet.D2Go.Data.Skill.SkillId;

public class GoCompatTests
{
    [Theory]
    [InlineData("Healing", "healing", true)]
    [InlineData("HEALING", "healing", true)]
    [InlineData("", "", true)]
    [InlineData("a", "", false)]
    [InlineData("", "a", false)]
    [InlineData("abc", "abcd", false)]
    [InlineData("abd", "abc", false)]
    [InlineData("K", "k", true)] // Kelvin sign folds to k in Go
    [InlineData("K", "K", true)]
    [InlineData("été", "ÉTÉ", true)]
    [InlineData("\u03F4", "\u03D1", true)] // theta orbit 0398, 03B8, 03D1, 03F4: neither upper nor lower case agrees
    [InlineData("\u03F4", "\u03B8", true)]
    [InlineData("\u0398", "\u03D1", true)]
    [InlineData("\u00B5", "\u039C", true)] // micro sign, capital mu
    [InlineData("\u00B5", "\u03BC", true)]
    [InlineData("\u017F", "S", true)] // long s
    [InlineData("\u2126", "\u03C9", true)] // ohm sign, small omega
    [InlineData("\u1E9E", "\u00DF", true)] // capital sharp s
    [InlineData("\u01C4", "\u01C5", true)] // DZ with caron: three cases
    [InlineData("\u01C4", "\u01C6", true)]
    [InlineData("\u0130", "i", false)] // Go keeps dotted capital I and dotless i out of the i orbit
    [InlineData("\u0130", "I", false)]
    [InlineData("\u0131", "I", false)]
    [InlineData("\u0131", "i", false)]
    [InlineData("K", "\u212A", true)] // Kelvin sign orbit: K, k, U+212A
    [InlineData("k", "\u212A", true)]
    [InlineData("A", "a\u0301", false)]
    [InlineData("\uD83D\uDE00x", "\uD83D\uDE00X", true)] // supplementary runes
    [InlineData("\U00010400", "\U00010428", true)] // Deseret long I
    public void EqualFold_FollowsGoSimpleFolding(string s, string t, bool expected)
    {
        Assert.Equal(expected, GoStrings.EqualFold(s, t));
        Assert.Equal(expected, GoStrings.EqualFold(t, s));
    }

    [Theory]
    [InlineData(0x4B, 0x6B)]
    [InlineData(0x6B, 0x212A)]
    [InlineData(0x212A, 0x4B)]
    [InlineData(0x3D1, 0x3F4)]
    [InlineData(0x3F4, 0x398)]
    [InlineData(0x398, 0x3B8)]
    [InlineData(0x3B8, 0x3D1)]
    [InlineData(0x130, 0x130)]
    [InlineData(0x131, 0x131)]
    [InlineData(0x41, 0x61)]
    [InlineData(0x31, 0x31)]
    [InlineData(0xD800, 0xD800)]
    [InlineData(-1, -1)]
    [InlineData(0x110000, 0x110000)]
    public void SimpleFold_IsGosNextRuneOfTheOrbit(int r, int expected) => Assert.Equal(expected, GoStrings.SimpleFold(r));

    [Fact]
    public void SimpleFold_EveryOrbitIsACycle_AndHasGosSize()
    {
        // Go 1.24's unicode.SimpleFold (Unicode 15.0) moves 2878 runes; every orbit returns to its start within four
        // steps, its other members are larger until it wraps to the smallest, and EqualFold matches every pair in it.
        var moved = 0;
        for (var r = 0; r <= 0x10FFFF; r++)
        {
            var next = GoStrings.SimpleFold(r);
            if (next == r)
                continue;
            moved++;

            var orbit = new List<int> { r };
            for (var x = next; x != r; x = GoStrings.SimpleFold(x))
            {
                orbit.Add(x);
                Assert.True(orbit.Count <= 4, $"Orbit of U+{r:X4} does not cycle.");
            }

            var wraps = 0;
            for (var i = 0; i < orbit.Count; i++)
            {
                if (orbit[(i + 1) % orbit.Count] < orbit[i])
                    wraps++;
            }

            Assert.Equal(1, wraps);
            var s = char.ConvertFromUtf32(r);
            foreach (var other in orbit)
                Assert.True(GoStrings.EqualFold(s, char.ConvertFromUtf32(other)), $"U+{r:X4} U+{other:X4}");
        }

        Assert.Equal(2878, moved);
    }

    [Theory]
    [InlineData(99.9, 99)]
    [InlineData(-99.9, -99)]
    [InlineData(0.0, 0)]
    [InlineData(2147483647.9, int.MaxValue)]
    [InlineData(-2147483648.9, int.MinValue)]
    [InlineData(2147483648.0, int.MinValue)]
    [InlineData(-2147483649.0, int.MinValue)]
    [InlineData(double.NaN, int.MinValue)]
    [InlineData(double.PositiveInfinity, int.MinValue)]
    [InlineData(double.NegativeInfinity, int.MinValue)]
    public void ToInt_TruncatesLikeGoOnAmd64(double value, int expected)
    {
        Assert.Equal(expected, GoConvert.ToInt(value));
    }
}

public class ExcelTableTests
{
    private static ExcelTable Parse(string text) => ExcelTable.Parse("test.txt", Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Parse_SplitsLinesLikeBufioScanLines()
    {
        var table = Parse("A\tB\r\n1\t2\r\n\n3\t4\r");
        Assert.Equal(["A", "B"], table.Headers);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(["1", "2"], table.Rows[0]);
        Assert.Equal([""], table.Rows[1]);
        Assert.Equal(["3", "4"], table.Rows[2]); // last line without newline, CR dropped
    }

    [Fact]
    public void Parse_TrailingNewlineDoesNotAddARow()
    {
        Assert.Single(Parse("A\tB\n1\t2\n").Rows);
    }

    [Fact]
    public void Parse_StripsUtf8Bom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("ItemType\tCode\nx\ty\n")).ToArray();
        var table = ExcelTable.Parse("bom.txt", bytes);
        Assert.Equal("ItemType", table.Headers[0]);
        Assert.Equal("x", table.Get(table.Rows[0], "ItemType"));
    }

    [Fact]
    public void Parse_EmptyFileIsAnError()
    {
        Assert.Throws<InvalidDataException>(() => Parse(""));
    }

    [Fact]
    public void Parse_KeepsCellsVerbatim()
    {
        var table = Parse("name\tcode\nBroad Axe \t bax\n");
        Assert.Equal("Broad Axe ", table.Get(table.Rows[0], "name"));
        Assert.Equal(" bax", table.Get(table.Rows[0], "code"));
    }

    [Fact]
    public void ColumnIndex_PrefersExactThenCaseInsensitive_LastWins()
    {
        var table = Parse("Name\tname\tCode\tCode\tX\n0\t1\t2\t3\t4\n");
        var row = table.Rows[0];
        Assert.Equal("0", table.Get(row, "Name"));
        Assert.Equal("1", table.Get(row, "name"));
        Assert.Equal("1", table.Get(row, "NAME")); // no exact match: last case-insensitive one
        Assert.Equal("3", table.Get(row, "Code"));
        Assert.Equal("4", table.Get(row, "x"));
        Assert.True(table.HasColumn("cOdE"));
        Assert.False(table.HasColumn("missing"));
        Assert.Null(table.ColumnIndex("missing"));
    }

    [Fact]
    public void Get_MissingColumnIsAnError_GetOptionalIsNull()
    {
        var table = Parse("A\tB\n1\t2\n");
        var error = Assert.Throws<InvalidDataException>(() => table.Get(table.Rows[0], "C"));
        Assert.Contains("'C'", error.Message);
        Assert.Contains("test.txt", error.Message);
        Assert.Null(table.GetOptional(table.Rows[0], "C"));
        Assert.Equal("2", table.GetOptional(table.Rows[0], "b"));
    }

    [Fact]
    public void Get_ShortRowsReadMissingCellsAsEmpty()
    {
        var table = Parse("A\tB\tC\n1\n");
        Assert.Equal("", table.Get(table.Rows[0], "C"));
    }

    [Fact]
    public void GeneratorRows_SkipsRowsWithEmptySecondColumn()
    {
        var table = Parse("Name\tAnything\tC\na\t1\tx\nb\t\ty\nc\t2\nd\n\ne\t3\n");
        Assert.Equal(["a", "c", "e"], table.GeneratorRows().Select(r => r[0]));
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("-1", -1)]
    [InlineData("+7", 7)]
    [InlineData(" 5 ", 5)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void GetInt_ParsesWholeNumbers(string cell, int expected)
    {
        var table = Parse($"A\tN\nx\t{cell}\n");
        Assert.Equal(expected, table.GetInt(table.Rows[0], "N"));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData("0x10")]
    [InlineData("99999999999")]
    public void GetInt_RejectsOtherText(string cell)
    {
        var table = Parse($"A\tN\nx\t{cell}\n");
        var error = Assert.Throws<InvalidDataException>(() => table.GetInt(table.Rows[0], "N"));
        Assert.Contains("'N'", error.Message);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(" 1", false)]
    [InlineData("01", false)]
    [InlineData("true", false)]
    public void GetFlag_IsTrueOnlyForExactlyOne(string cell, bool expected)
    {
        var table = Parse($"A\tF\nx\t{cell}\n");
        Assert.Equal(expected, table.GetFlag(table.Rows[0], "F"));
    }
}

public class GameTablesLoaderTests
{
    private static GameTables Tables => SyntheticTables.Tables;

    [Fact]
    public void Load_RequiresAProvider()
    {
        Assert.Throws<ArgumentNullException>(() => GameTables.Load(null!));
    }

    [Theory]
    [InlineData("itemtypes.txt")]
    [InlineData("weapons.txt")]
    [InlineData("armor.txt")]
    [InlineData("misc.txt")]
    [InlineData("levels.txt")]
    [InlineData("skills.txt")]
    [InlineData("skilldesc.txt")]
    [InlineData("objects.txt")]
    public void Load_MissingFileIsAnError(string file)
    {
        var full = SyntheticTables.Provider();
        var partial = new MemoryProvider();
        foreach (var path in full.EnumerateFiles("").Where(p => !p.EndsWith("/" + file, StringComparison.Ordinal)))
            partial.AddBytes(path, full.ReadFile(path)!);

        var error = Assert.Throws<FileNotFoundException>(() => GameTables.Load(partial));
        Assert.Equal("data/global/excel/" + file, error.FileName);
    }

    [Fact]
    public void Load_MissingRequiredColumnIsAnError()
    {
        var provider = SyntheticTables.Provider().Add("data/global/excel/levels.txt", "Name\tId\nAct 1\t1\n");
        var error = Assert.Throws<InvalidDataException>(() => GameTables.Load(provider));
        Assert.Contains("LevelName", error.Message);
    }

    [Fact]
    public void ItemTypes_AreKeyedByCode_WithRowIndexIds()
    {
        Assert.Equal(["hpot", "hpot2", "jave", "mpot", "none", "ques", "rpot"], Tables.ItemTypes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new ItemType { Id = 0, Name = "None", Code = "none" }, Tables.ItemTypes["none"]);
        Assert.Equal(new ItemType { Id = 1, Name = "Healing Potion", Code = "hpot", Beltable = true }, Tables.ItemTypes["hpot"]);
        Assert.Equal(3, Tables.ItemTypes["mpot"].Id);
        Assert.Equal(new ItemType { Id = 6, Name = "Javelin", Code = "jave", Throwable = true }, Tables.ItemTypes["jave"]);
    }

    [Fact]
    public void ItemDescriptions_UseOneRunningIdAcrossWeaponsArmorAndMisc()
    {
        Assert.Equal(Enumerable.Range(0, 9), Tables.ItemDescriptions.Keys.Order());
        Assert.Equal(
            new ItemDescription
            {
                Id = 0, Name = "Hierophant Axe", Code = "hax", NormalCode = "hax", UberCode = "9ha", UltraCode = "7ha",
                InventoryWidth = 1, InventoryHeight = 3, Type = "axe",
            },
            Tables.ItemDesc(SyntheticTables.HierophantAxe));
        Assert.Equal(
            new ItemDescription
            {
                Id = 2, Name = "Colossus Shield", Code = "uit", NormalCode = "buc", UberCode = "uit", UltraCode = "xuit",
                InventoryWidth = 2, InventoryHeight = 2, MinDefense = 5, MaxDefense = 10, Type = "shie",
            },
            Tables.ItemDesc(SyntheticTables.ColossusShield));
    }

    [Fact]
    public void ItemDescriptions_WithoutNormcode_HaveNoTierCodes_EvenWithAnUbercodeColumn()
    {
        var potion = Tables.ItemDesc(SyntheticTables.MinorHealingPotion);
        Assert.Equal(("hp1", "", "", ""), (potion.Code, potion.NormalCode, potion.UberCode, potion.UltraCode));
        Assert.Equal(0, potion.MinDefense);
        Assert.Equal(0, potion.MaxDefense);
    }

    [Fact]
    public void ItemDescriptions_GetNumbersTrimmed()
    {
        Assert.Equal(2, Tables.ItemDesc(SyntheticTables.Weird).InventoryWidth);
    }

    [Theory]
    [InlineData("Heirophant", "Hierophant")]
    [InlineData("Colossal Sword", "Colossus Sword")]
    [InlineData("Ancient Shield", "Kurast Shield")]
    [InlineData("Ornate Armor", "Ornate Plate")]
    [InlineData("Twisted Essense of Suffering", "Twisted Essence of Suffering")]
    [InlineData("Plain", "Plain")]
    [InlineData("", "")]
    public void FixItemName_AppliesGeneratorSpellingFixes(string name, string expected)
    {
        Assert.Equal(expected, GameTables.FixItemName(name));
        Assert.Equal("Essence of Ornate Plate", Tables.ItemDesc(SyntheticTables.EssenceOfOrnatePlate).Name);
    }

    [Fact]
    public void ItemDescriptions_WithoutNameColumn_HaveEmptyNames()
    {
        var provider = SyntheticTables.Provider().Add("data/global/excel/misc.txt", "code\tx\tinvwidth\tinvheight\ttype\nhp1\t1\t1\t1\thpot\n");
        Assert.Equal("", GameTables.Load(provider).ItemDesc(3).Name);
    }

    [Fact]
    public void Areas_AreKeyedByRowIndex()
    {
        Assert.Equal(
            [new AreaInfo { Id = AreaId.None, Name = "" }, new AreaInfo { Id = AreaId.RogueEncampment, Name = "Rogue Encampment" }, new AreaInfo { Id = AreaId.BloodMoor, Name = "Blood Moor" }],
            Tables.Areas.OrderBy(a => a.Key).Select(a => a.Value));
    }

    [Fact]
    public void Skills_AreKeyedByIdColumn_WithRowIndexIds()
    {
        Assert.Equal([SkillId.AttackSkill, SkillId.Kick, SkillId.Teleport], Tables.Skills.Keys.Order());
        Assert.Equal(new SkillInfo { Id = SkillId.AttackSkill, Name = "Attack", LeftSkill = true, RightSkill = true }, Tables.Skill(SkillId.AttackSkill));
        // ID is the row index (2), not the *Id column (54), as in d2go's template.
        Assert.Equal(new SkillInfo { Id = (SkillId)2, Name = "Teleport", RightSkill = true }, Tables.Skill(SkillId.Teleport));
    }

    [Fact]
    public void SkillDescriptions_AreKeyedByRowIndex()
    {
        Assert.Equal([(SkillId)0, (SkillId)1, (SkillId)2], Tables.SkillDescriptions.Keys.Order());
        Assert.Equal(new SkillDescription { Page = 0, Row = 0, Column = 0, ListRow = -1, IconCel = 0 }, Tables.SkillDesc((SkillId)1));
        Assert.Equal(new SkillDescription { Page = 3, Row = 4, Column = 2, ListRow = 0, IconCel = 18 }, Tables.SkillDesc((SkillId)2));
    }

    [Fact]
    public void ObjectDescriptions_AreKeyedByIdColumn()
    {
        Assert.Equal([0, 2], Tables.ObjectDescriptions.Keys.Order());
        Assert.Equal(new Object.ObjectDescription { Id = 2, Name = "Shrine", SizeX = 3, SizeY = 3, HasCollision = false }, Tables.ObjectDesc(2));
        Assert.True(Tables.ObjectDesc(0).HasCollision);
    }

    [Fact]
    public void Tables_AreImmutable()
    {
        Assert.Throws<NotSupportedException>(() => ((IDictionary<int, ItemDescription>)Tables.ItemDescriptions).Add(100, new ItemDescription()));
    }
}

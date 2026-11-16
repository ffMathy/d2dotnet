using System.Text;
using System.Text.Json;
using D2Dotnet.Data;
using D2Dotnet.Files;

namespace D2Dotnet.Tests.Data;

public class LocalizationTests
{
    private static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    private static readonly byte[] Levels = Json("""
        [
            { "id": 5055, "Key": "Blood Moor", "enUS": "Blood Moor", "deDE": "Blutmoor", "frFR": "  " },
            { "id": 5056, "Key": "Den of Evil", "enUS": "Den of Evil", "deDE": "Höhle des Bösen" },
            { "id": 5057, "Key": "Den of Evil", "enUS": "second entry", "deDE": "zweiter Eintrag" },
            { "id": 5058, "Key": "blank", "enUS": " ", "deDE": "" },
            { "id": 5059, "Key": "", "enUS": "no key" },
            { "id": 5060, "enUS": "missing key" },
            { "id": 5061, "Key": "notext" },
            { "id": 5062, "Key": "x", "enUS": "Ex" },
            "not an object",
        ]
        """);

    private static readonly byte[] Ui = Json("""
        // comments and a byte order mark are accepted
        [ { "id": 1, "Key": "Blood Moor", "enUS": "ui text", "deDE": "UI-Text" }, { "id": 2, "Key": "only-ui", "enUS": "Only UI" } ]
        """);

    private static Localization Parse(string language) => Localization.Parse(
        [("data/local/lng/strings/ui.json", [0xEF, 0xBB, 0xBF, .. Ui]), ("data/local/lng/strings/levels.json", Levels)], language);

    [Fact]
    public void ReadsTheSelectedLanguage()
    {
        var de = Parse("deDE");
        Assert.Equal("deDE", de.Language);
        Assert.Equal("Höhle des Bösen", de.Get("Den of Evil"));
        Assert.Equal("Höhle des Bösen", de["Den of Evil"]);
        Assert.Equal("Blutmoor", de.Get("Blood Moor", "levels.json"));

        var en = Parse("enUS");
        Assert.Equal("Den of Evil", en.Get("Den of Evil"));
        Assert.Equal("Ex", en.Get("x"));
    }

    [Fact]
    public void BlankValuesFallBackToEnglish()
    {
        Assert.Equal("Blood Moor", Parse("frFR").Get("Blood Moor", "levels.json"));
        Assert.Equal("Only UI", Parse("deDE").Get("only-ui"));
        // a field the files do not have at all
        Assert.Equal("Den of Evil", Parse("xxXX").Get("Den of Evil"));
    }

    [Fact]
    public void EntriesWithoutUsableTextOrKeyAreSkipped()
    {
        var en = Parse("enUS");
        Assert.Null(en.Get("blank"));
        Assert.Null(en.Get("notext"));
        Assert.Null(en.Get(""));
        Assert.Null(en.Get(null));
        Assert.Null(en.Get("missing"));
        // levels.json: Blood Moor, Den of Evil and x; ui.json: Blood Moor and only-ui
        Assert.Equal(5, en.Count);
    }

    [Fact]
    public void FirstEntryOfAKeyWins()
    {
        Assert.Equal("Höhle des Bösen", Parse("deDE").Get("Den of Evil"));
        Assert.Equal("Den of Evil", Parse("enUS").Get("Den of Evil", "levels.json"));
    }

    [Fact]
    public void PreferredFileIsSearchedFirstThenFilesInPathOrder()
    {
        var en = Parse("enUS");
        Assert.Equal(["data/local/lng/strings/levels.json", "data/local/lng/strings/ui.json"], en.Files);
        Assert.Equal("Blood Moor", en.Get("Blood Moor"));
        Assert.Equal("Blood Moor", en.Get("Blood Moor", "levels.json"));
        Assert.Equal("ui text", en.Get("Blood Moor", "ui.json"));
        Assert.Equal("Only UI", en.Get("only-ui", "levels.json"));
    }

    [Theory]
    [InlineData("Den of Evil", "Den of Evil")]
    [InlineData("  Den of Evil\u3000", "Den of Evil")] // the key is trimmed before the lookup
    [InlineData("no such key ", "no such key")] // no text: the key itself
    [InlineData(" ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Translate_TrimsAndFallsBackToTheKey(string? key, string? expected)
    {
        Assert.Equal(expected, Parse("enUS").Translate(key));
    }

    [Theory]
    [InlineData("enUS", "enUS")]
    [InlineData("ENUS", "enUS")]
    [InlineData(" dede ", "deDE")]
    [InlineData("ENG", "enUS")]
    [InlineData("deu", "deDE")]
    [InlineData("CHI", "zhTW")]
    [InlineData("SIN", "zhCN")]
    [InlineData("POR", "ptBR")]
    [InlineData("custom", "custom")]
    public void ResolvesLanguages(string input, string expected) => Assert.Equal(expected, Localization.ResolveLanguage(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ALanguageIsRequired(string? input) => Assert.Throws<ArgumentException>(() => Localization.ResolveLanguage(input));

    [Fact]
    public void InvalidJsonIsAnError()
    {
        Assert.ThrowsAny<JsonException>(() => Localization.Parse([("a.json", Json("{\"Key\":\"x\"}"))], "enUS"));
        Assert.ThrowsAny<JsonException>(() => Localization.Parse([("a.json", Json("[{"))], "enUS"));
    }

    [Fact]
    public void LoadsTheStringFolderOfAGameFileProvider()
    {
        var root = Directory.CreateTempSubdirectory("d2dotnet-strings-");
        try
        {
            var dir = Directory.CreateDirectory(Path.Join(root.FullName, "data", "local", "lng", "strings"));
            File.WriteAllBytes(Path.Join(dir.FullName, "Levels.json"), Levels);
            File.WriteAllBytes(Path.Join(dir.FullName, "readme.txt"), Json("not json"));
            Directory.CreateDirectory(Path.Join(root.FullName, "data", "global", "excel"));

            var strings = Localization.Load(new DirectoryFileProvider(root.FullName), "DEU");
            Assert.Equal("deDE", strings.Language);
            Assert.Equal(["data/local/lng/strings/levels.json"], strings.Files);
            Assert.Equal("Blutmoor", strings.Get("Blood Moor", "levels.json"));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void NoStringFolderGivesAnEmptyTable()
    {
        var strings = Localization.Load(new MemoryFiles(), "enUS");
        Assert.Equal(0, strings.Count);
        Assert.Null(strings.Get("Blood Moor"));
        Assert.Null(Localization.Empty.Get("Blood Moor"));
    }

    [Fact]
    public void IdLookupsNeedTheDatabase()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Parse("enUS")[AreaId.BloodMoor]);
        Assert.Contains("GameDatabase.Localization", error.Message);
        Assert.Throws<InvalidOperationException>(() => Localization.Empty.GetMonsterName(MonsterId.Zombie));
    }

    // ---- id lookups (level, npc and object names) ----

    [Fact]
    public void LevelNamesAreTranslatedTrimmedOrTheRawKey()
    {
        var strings = SyntheticGame.Database().Localization;
        Assert.Equal("Rogue Encampment", strings[AreaId.RogueEncampment]);
        Assert.Equal("Blood Moor", strings.GetLevelName(AreaId.BloodMoor)); // no string: the LevelName key itself
        Assert.Equal("Tal Rasha's Tömb <&+>", strings[AreaId.LutGholein]);
        Assert.Null(strings[AreaId.None]); // blank LevelName
        Assert.Null(strings[AreaId.TalRashasTomb1]); // no such level
    }

    [Fact]
    public void LanguageSelectsTheStringField()
    {
        var strings = SyntheticGame.Database("deDE").Localization;
        Assert.Equal("deDE", strings.Language);
        Assert.Equal("Lager der Jägerinnen", strings[AreaId.RogueEncampment]);
        // blank deDE text falls back to enUS
        Assert.Equal("Tal Rasha's Tömb <&+>", strings[AreaId.LutGholein]);
        Assert.Equal("Gefallener", strings[(MonsterId)1]);
        Assert.Equal("Kaltkrähe", strings.GetSuperUniqueName(2));
    }

    [Fact]
    public void MonsterAndSuperUniqueNames()
    {
        var strings = SyntheticGame.Database().Localization;
        Assert.Equal("Zombie", strings[(MonsterId)0]); // "Zombie " trimmed
        Assert.Equal("Fallen", strings.GetMonsterName((MonsterId)1));
        Assert.Null(strings[(MonsterId)2]); // blank NameStr
        Assert.Null(strings[(MonsterId)3]);
        Assert.Equal("Bishibosh", strings.GetSuperUniqueName(0));
        Assert.Equal("Coldcrow", strings.GetSuperUniqueName(2));
        Assert.Null(strings.GetSuperUniqueName(3));
    }

    [Fact]
    public void ObjectAndItemNames()
    {
        var strings = SyntheticGame.Database().Localization;
        Assert.Equal("Waypoint", strings[(ObjectId)119]); // " Waypoint " trimmed
        Assert.Equal("Shrine", strings.GetObjectName(ObjectId.Shrine));
        Assert.Null(strings[(ObjectId)0]);
        Assert.Equal("Hand Axe", strings[ItemId.HandAxe]); // namestr "hax" translated
        Assert.Equal("Minor Healing Potion", strings[(ItemId)3]); // misc.txt has no namestr: the name
        Assert.Null(strings[(ItemId)99]);
    }
}

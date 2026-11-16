using System.Text.Json;
using D2Dotnet.Map.GameData;
using D2Dotnet.Map.Generation;

namespace D2Dotnet.Map.Tests.Generation;

public class GameStringsTests
{
    private static readonly byte[] Levels = Synthetic.Json("""
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

    private static readonly byte[] Ui = Synthetic.Json("""
        // comments and a byte order mark are accepted
        [ { "id": 1, "Key": "Blood Moor", "enUS": "ui text", "deDE": "UI-Text" }, { "id": 2, "Key": "only-ui", "enUS": "Only UI" } ]
        """);

    private static GameStrings Parse(string language) => GameStrings.Parse(
        [("data/local/lng/strings/ui.json", [0xEF, 0xBB, 0xBF, .. Ui]), ("data/local/lng/strings/levels.json", Levels)], language);

    [Fact]
    public void ReadsTheSelectedLanguage()
    {
        var de = Parse("deDE");
        Assert.Equal("deDE", de.Language);
        Assert.Equal("Höhle des Bösen", de.Get("Den of Evil"));
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
    [InlineData("enUS", "enUS")]
    [InlineData("ENUS", "enUS")]
    [InlineData(" dede ", "deDE")]
    [InlineData("ENG", "enUS")]
    [InlineData("deu", "deDE")]
    [InlineData("CHI", "zhTW")]
    [InlineData("SIN", "zhCN")]
    [InlineData("POR", "ptBR")]
    [InlineData("custom", "custom")]
    public void ResolvesLanguages(string input, string expected) => Assert.Equal(expected, GameStrings.ResolveLanguage(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ALanguageIsRequired(string? input) => Assert.Throws<ArgumentException>(() => GameStrings.ResolveLanguage(input));

    [Fact]
    public void InvalidJsonIsAnError()
    {
        Assert.ThrowsAny<JsonException>(() => GameStrings.Parse([("a.json", Synthetic.Json("{\"Key\":\"x\"}"))], "enUS"));
        Assert.ThrowsAny<JsonException>(() => GameStrings.Parse([("a.json", Synthetic.Json("[{"))], "enUS"));
    }

    [Fact]
    public void LoadsTheStringFolderOfAGameFileProvider()
    {
        var root = Directory.CreateTempSubdirectory("d2map-strings-");
        try
        {
            var dir = Directory.CreateDirectory(Path.Join(root.FullName, "data", "local", "lng", "strings"));
            File.WriteAllBytes(Path.Join(dir.FullName, "Levels.json"), Levels);
            File.WriteAllBytes(Path.Join(dir.FullName, "readme.txt"), Synthetic.Json("not json"));
            Directory.CreateDirectory(Path.Join(root.FullName, "data", "global", "excel"));

            var strings = GameStrings.Load(new DirectoryFileProvider(root.FullName), "DEU");
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
        var root = Directory.CreateTempSubdirectory("d2map-strings-");
        try
        {
            var strings = GameStrings.Load(new DirectoryFileProvider(root.FullName), "enUS");
            Assert.Equal(0, strings.Count);
            Assert.Null(strings.Get("Blood Moor"));
            Assert.Null(GameStrings.Empty.Get("Blood Moor"));
        }
        finally
        {
            root.Delete(true);
        }
    }
}

namespace D2Dotnet.D2Go.Data.Tests;

/// <summary>Tiny hand-made excel tables (no game data) that exercise every rule of the loader.</summary>
internal static class SyntheticTables
{
    public const string ItemTypes =
        "ItemType\tCode\tThrowable\tBeltable\n" +
        "None\tnone\t0\t0\n" +
        "Expansion\t\t\t\n" + // skipped: second column empty
        "Healing Potion\thpot\t0\t1\n" +
        "Mana Potion\thpot2\t0\t1\n" +
        "Mana Potion\tmpot\t0\t1\n" +
        "Rejuv Potion\trpot\t0\t1\n" +
        "Quest\tques\t0\t0\n" +
        "Javelin\tjave\t1\t0\n";

    // No minac/maxac: defense defaults to 0. Second column is "type".
    public const string Weapons =
        "name\ttype\tcode\tnormcode\tubercode\tultracode\tinvwidth\tinvheight\n" +
        "Heirophant Axe\taxe\thax\thax\t9ha\t7ha\t1\t3\n" +
        "Unused\t\tzzz\tzzz\tzzz\tzzz\t1\t1\n" + // skipped
        "Elite Axe\taxe\t7ha\thax\t9ha\t7ha\t2\t3\n";

    public const string Armor =
        "name\tversion\tcode\tminac\tmaxac\tnormcode\tubercode\tultracode\tinvwidth\tinvheight\ttype\n" +
        "Colossal Shield\t100\tuit\t5\t10\tbuc\tuit\txuit\t2\t2\tshie\r\n";

    // No normcode: normcode, ubercode and ultracode are all empty even though ubercode exists.
    public const string Misc =
        "name\tcompactsave\tcode\tubercode\tinvwidth\tinvheight\ttype\n" +
        "Minor Healing Potion\t1\thp1\tzzz\t1\t1\thpot\n" +
        "Minor Mana Potion\t1\tmp1\t\t1\t1\tmpot\n" +
        "Rejuvenation Potion\t1\trvs\t\t1\t1\trpot\n" +
        "Horadric Cube\t1\tbox\t\t2\t2\tques\n" +
        "Essense of Ornate Armor\t1\tess\t\t1\t1\tques\n" +
        "Weird\t1\twrd\t\t 2 \t1\tnotype";  // no final newline

    public const string Levels =
        "Name\tId\tLevelName\n" +
        "Null\t0\t\n" +
        "Act 1 - Town\t1\tRogue Encampment\n" +
        "Expansion\t\t\n" + // skipped
        "Act 1 - Wilderness 1\t2\tBlood Moor\n";

    public const string Skills =
        "skill\t*Id\tleftskill\trightskill\n" +
        "Attack\t0\t1\t1\n" +
        "Kick\t1\t0\t0\n" +
        "Gap\t\t1\t1\n" + // skipped
        "Teleport\t54\t0\t1\n";

    public const string SkillDesc =
        "skilldesc\tSkillPage\tSkillRow\tSkillColumn\tListRow\tIconCel\n" +
        "attack\t0\t0\t0\t0\t2\n" +
        "kick\t0\t0\t0\t-1\t0\n" +
        "teleport\t3\t4\t2\t\t18\n";

    public const string Objects =
        "Class\tName\t*ID\tSizeX\tSizeY\tHasCollision0\n" +
        "dummy\tDummy\t0\t0\t0\t1\n" +
        "shrine\tShrine\t2\t3\t3\t0\n" +
        "noname\t\t5\t1\t1\t1\n"; // skipped

    public static MemoryProvider Provider() => new MemoryProvider()
        .Add("data/global/excel/itemtypes.txt", ItemTypes)
        .Add("data/global/excel/weapons.txt", Weapons)
        .Add("data/global/excel/armor.txt", Armor)
        .Add("data/global/excel/misc.txt", Misc)
        .Add("data/global/excel/levels.txt", Levels)
        .Add("data/global/excel/skills.txt", Skills)
        .Add("data/global/excel/skilldesc.txt", SkillDesc)
        .Add("data/global/excel/objects.txt", Objects);

    private static readonly Lazy<GameTables> _tables = new(() => GameTables.Load(Provider()));

    public static GameTables Tables => _tables.Value;

    // Item ids of the synthetic tables.
    public const int HierophantAxe = 0;
    public const int EliteAxe = 1;
    public const int ColossusShield = 2;
    public const int MinorHealingPotion = 3;
    public const int MinorManaPotion = 4;
    public const int RejuvenationPotion = 5;
    public const int HoradricCube = 6;
    public const int EssenceOfOrnatePlate = 7;
    public const int Weird = 8;
}

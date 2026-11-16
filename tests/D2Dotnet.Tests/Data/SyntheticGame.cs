using D2Dotnet.Data;

namespace D2Dotnet.Tests.Data;

/// <summary>Tiny hand-made excel and string tables (no game data) that exercise every rule of <see cref="GameDatabase"/>.</summary>
internal static class SyntheticGame
{
    public const string Levels =
        "Name\tId\tLevelName\tAct\tDrlgType\tLevelType\tWaypoint\n" +
        "Null\t0\t\t0\t0\t0\t255\n" +
        "Act 1 - Town\t1\tstrRogue\t0\t2\t1\t0\n" +
        "Act 1 - Wilderness 1\t2\tBlood Moor\t0\t3\t2\t255\n" +
        "Act 1 - Cave 1\t8\tDen of Evil\t0\t1\t3\t255\n" +
        "Expansion\t\t\t\t\t\t\n" + // divider: not a level
        "Act 2 - Town\t40\tLut Gholein\t1\t2\t5\t9\n";

    // Ids are game rows: the Expansion divider is not counted (unnamed is 2).
    public const string MonStats =
        "Id\thcIdx\tNameStr\n" +
        "zombie\t0\tZombie\n" +
        "fallen\t1\tFallen1\n" +
        "Expansion\t\t\n" +
        "unnamed\t2\t\n";

    public const string SuperUniques =
        "Superunique\tName\tClass\n" +
        "Bishibosh\tBishibosh\tfallenshaman1\n" +
        "Bonebreaker\tBonebreaker\tskeleton1\n" +
        "Expansion\t\t\n" +
        "Coldcrow\tColdcrow\tcr_archer1\n";

    public const string Objects =
        "Class\tName\t*Description\t*ID\tSizeX\tSizeY\tHasCollision0\tSelectable0\tOperateFn\n" +
        "testdata\t\t\t0\t0\t0\t1\t0\t0\n" +
        "shrine\tShrine\t\t2\t3\t3\t0\t1\t2\n" +
        "chest\tLargeChestR\t\t5\t2\t2\t1\t0\t4\n" +
        "Expansion\t\t\t\t\t\t\t\t\n" +
        "barrel\tbarrel\t\t7\t1\t1\t1\t1\t5\n" +
        "waypoint\t Waypoint \t\t119\t5\t5\t0\t1\t23\n";

    // No minac/maxac: defense is 0. The divider row is not an item.
    public const string Weapons =
        "name\ttype\tcode\tnamestr\tnormcode\tubercode\tultracode\tinvwidth\tinvheight\n" +
        "Heirophant Axe\taxe\thax\thax\thax\t9ha\t7ha\t1\t3\n" +
        "Expansion\t\t\t\t\t\t\t\t\n" +
        "Elite Axe\taxe\t7ha\t7ha\thax\t9ha\t7ha\t2\t3\n";

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
        "Weird\t1\twrd\t\t 2 \t1\tnotype"; // no final newline; a padded number

    public const string ItemTypes =
        "ItemType\tCode\tEquiv1\tEquiv2\tThrowable\tBeltable\n" +
        "None\tnone\t\t\t0\t0\n" +
        "Expansion\t\t\t\t\t\n" + // divider
        "Healing Potion\thpot\tpoti\t\t0\t1\n" +
        "Not Used\t\t\t\t0\t0\n" + // a game row without a code
        "Mana Potion\tmpot\tpoti\t\t0\t1\n" +
        "Javelin\tjave\tmiss\tthro\t1\t0\n";

    public const string Skills =
        "skill\t*Id\tcharclass\tskilldesc\tleftskill\trightskill\n" +
        "Attack\t0\t\tattack\t1\t1\n" +
        "Kick\t1\t\tkick\t0\t0\n" +
        "Expansion\t\t\t\t\t\n" +
        "Teleport\t54\tsor\tteleport\t0\t1\n";

    public const string SkillDesc =
        "skilldesc\tSkillPage\tSkillRow\tSkillColumn\tListRow\tIconCel\n" +
        "attack\t0\t0\t0\t0\t2\n" +
        "kick\t0\t0\t0\t-1\t0\n" +
        "teleport\t3\t4\t2\t\t18\n";

    public const string LevelStrings = """
        [
            { "id": 1, "Key": "strRogue", "enUS": "Rogue Encampment  ", "deDE": "Lager der Jägerinnen" },
            { "id": 2, "Key": "Lut Gholein", "enUS": "Tal Rasha's Tömb <&+>", "deDE": "" }
        ]
        """;

    public const string MonsterStrings = """
        [
            { "id": 3, "Key": "Zombie", "enUS": "Zombie ", "deDE": "Zombie" },
            { "id": 4, "Key": "Fallen1", "enUS": "Fallen", "deDE": "Gefallener" },
            { "id": 5, "Key": "Coldcrow", "enUS": "Coldcrow", "deDE": "Kaltkrähe" }
        ]
        """;

    public const string ItemStrings = """[ { "id": 6, "Key": "hax", "enUS": "Hand Axe" } ]""";

    /// <summary>Every table and string file, fresh (so a test can change it).</summary>
    public static MemoryFiles Files() => new MemoryFiles()
        .Add("data/global/excel/levels.txt", Levels)
        .Add("data/global/excel/monstats.txt", MonStats)
        .Add("data/global/excel/superuniques.txt", SuperUniques)
        .Add("data/global/excel/objects.txt", Objects)
        .Add("data/global/excel/weapons.txt", Weapons)
        .Add("data/global/excel/armor.txt", Armor)
        .Add("data/global/excel/misc.txt", Misc)
        .Add("data/global/excel/itemtypes.txt", ItemTypes)
        .Add("data/global/excel/skills.txt", Skills)
        .Add("data/global/excel/skilldesc.txt", SkillDesc)
        .Add("data/local/lng/strings/levels.json", LevelStrings)
        .Add("data/local/lng/strings/monsters.json", MonsterStrings)
        .Add("data/local/lng/strings/item-names.json", ItemStrings);

    public static GameDatabase Database(string language = "enUS") => new(Files(), language);
}

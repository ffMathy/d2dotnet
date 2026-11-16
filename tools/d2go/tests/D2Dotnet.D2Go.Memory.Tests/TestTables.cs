using System.Text;
using D2Dotnet.D2Go.Data.Item;
using D2Dotnet.D2Go.Data.Tests;

namespace D2Dotnet.D2Go.Memory.Tests;

/// <summary>
/// Hand-made excel tables (no game data) for the reader: the item ids of <see cref="BeltId"/> and
/// <see cref="BeltLikeNameId"/> get item types "belt" and "shie", every other item type "misc".
/// </summary>
internal static class TestTables
{
    /// <summary>An item id whose type is <see cref="ItemTypeCodes.TypeBelt"/>: the id of "Belt" in d2go's name list.</summary>
    public static int BeltId { get; } = ItemNames.GetIdByName("Belt");

    /// <summary>An item id of a non-belt type.</summary>
    public static int BeltLikeNameId { get; } = ItemNames.GetIdByName("Buckler");

    private static readonly Lazy<GameTables> _tables = new(() => GameTables.Load(Provider()));

    public static GameTables Tables => _tables.Value;

    public static MemoryProvider Provider()
    {
        // One running id over weapons, armor and misc: put every row in weapons.txt.
        var items = new StringBuilder("name\ttype\tcode\tnormcode\tubercode\tultracode\tinvwidth\tinvheight\n");
        var count = Math.Max(BeltId, BeltLikeNameId) + 1;
        for (var i = 0; i < count; i++)
        {
            var type = i == BeltId ? "belt" : i == BeltLikeNameId ? "shie" : "misc";
            items.Append(CultureInvariant($"Item{i}\t{type}\tc{i}\tc{i}\tc{i}\tc{i}\t1\t1\n"));
        }

        return new MemoryProvider()
            .Add("data/global/excel/itemtypes.txt", "ItemType\tCode\tThrowable\tBeltable\nBelt\tbelt\t0\t0\nShield\tshie\t0\t0\nMisc\tmisc\t0\t0\n")
            .Add("data/global/excel/weapons.txt", items.ToString())
            .Add("data/global/excel/armor.txt", "name\ttype\tcode\tnormcode\tubercode\tultracode\tinvwidth\tinvheight\n")
            .Add("data/global/excel/misc.txt", "name\ttype\tcode\tnormcode\tubercode\tultracode\tinvwidth\tinvheight\n")
            .Add("data/global/excel/levels.txt", "Name\tId\tLevelName\nNull\t0\t\n")
            .Add("data/global/excel/skills.txt", "skill\t*Id\tleftskill\trightskill\nAttack\t0\t1\t1\n")
            .Add("data/global/excel/skilldesc.txt", "skilldesc\tSkillPage\tSkillRow\tSkillColumn\tListRow\tIconCel\nattack\t0\t0\t0\t0\t2\n")
            .Add("data/global/excel/objects.txt", "Class\tName\t*ID\tSizeX\tSizeY\tHasCollision0\ndummy\tDummy\t0\t0\t0\t1\n");
    }

    private static string CultureInvariant(FormattableString s) => FormattableString.Invariant(s);
}

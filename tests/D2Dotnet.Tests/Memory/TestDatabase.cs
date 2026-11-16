using System.Globalization;
using System.Text;
using D2Dotnet.Data;

namespace D2Dotnet.Tests.Memory;

/// <summary>
/// Hand-made excel tables (no game data) for the reader: base items up to the super mana potion, whose types are
/// <c>belt</c> for the four belt kinds, the potion types for the potions, <c>ques</c> for the Golden Bird and
/// <c>misc</c> otherwise; and an objects.txt where <see cref="ObjectId.Shrine"/> is a shrine (OperateFn 2).
/// </summary>
internal static class TestDatabase
{
    private static readonly Lazy<GameDatabase> _database = new(() => new GameDatabase(Files()));

    public static GameDatabase Database => _database.Value;

    public static string TypeOf(ItemId id) => id switch
    {
        ItemId.Sash or ItemId.LightBelt or ItemId.Belt or ItemId.HeavyBelt => ItemTypeCode.Belt,
        >= ItemId.MinorHealingPotion and <= ItemId.SuperHealingPotion => ItemTypeCode.HealingPotion,
        >= ItemId.MinorManaPotion and <= ItemId.SuperManaPotion => ItemTypeCode.ManaPotion,
        ItemId.RejuvenationPotion or ItemId.FullRejuvenationPotion => ItemTypeCode.RejuvPotion,
        ItemId.TheGoldenBird => ItemTypeCode.Quest,
        _ => "misc",
    };

    public static MemoryFiles Files()
    {
        // One running id over weapons, armor and misc: every row goes in weapons.txt.
        var header = "name\ttype\tcode\tnormcode\tubercode\tultracode\tinvwidth\tinvheight\n";
        var items = new StringBuilder(header);
        for (var i = 0; i <= (int)ItemId.SuperManaPotion; i++)
            items.Append(CultureInfo.InvariantCulture, $"Item{i}\t{TypeOf((ItemId)i)}\tc{i}\tc{i}\tu{i}\tx{i}\t1\t1\n");

        return new MemoryFiles()
            .Add("data/global/excel/weapons.txt", items.ToString())
            .Add("data/global/excel/armor.txt", header)
            .Add("data/global/excel/misc.txt", header)
            .Add("data/global/excel/objects.txt",
                "Class\tName\t*ID\tOperateFn\n" +
                "dummy\tDummy\t0\t0\n" +
                "shrine\tShrine\t2\t2\n" +
                "urn\tLargeUrn1\t4\t3\n" +
                "portal\tTownPortal\t59\t0\n" +
                "waypoint\tWaypoint\t119\t23\n");
    }
}

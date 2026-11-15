using D2Dotnet.Map.Binary;

namespace D2Dotnet.Map.Bintools.Readers;

/// <summary>Item graphic files (upstream <c>ItemFiles</c>).</summary>
public sealed record ItemFiles(string Normal, string Unique, string Set);

/// <summary>Item codes per quality (upstream <c>ItemCodes</c>).</summary>
public sealed record ItemCodes(string Normal, string Unique, string Set);

/// <summary>Upgraded item codes (upstream <c>ItemUpgrades</c>).</summary>
public sealed record ItemUpgrades(string Nightmare, string Hell);

/// <summary>A Weapons.bin / Armor.bin / Misc.bin record (upstream <c>Item</c>).</summary>
public sealed record ItemNode(
    string Name,
    ItemFiles Files,
    // Code for the item, trimmed (eg "cap")
    string Code,
    ItemCodes Codes,
    string AltGfx,
    string BetterGem,
    uint WeaponClass,
    uint WeaponClass2Hand,
    uint Transmogrify,
    uint MinAc,
    uint MaxAc,
    uint GambleCost,
    uint Speed,
    uint BitField1,
    uint Cost,
    uint MinStack,
    uint MaxStack,
    uint SpawnStack,
    uint GemOffset,
    // Lang Id for the item name
    ushort NameId,
    ItemUpgrades Upgrade);

/// <summary>Reader for item tables (upstream <c>ItemFileParser</c>).</summary>
public static class ItemReader
{
    private const int Unk2 = 220 - 13 * 4 - 2;

    public static IReadOnlyList<ItemNode> Parse(byte[] bytes) => Parse(new BinaryCursor(bytes));

    public static IReadOnlyList<ItemNode> Parse(BinaryCursor bp)
    {
        var count = bp.Lu32();
        var items = new List<ItemNode>((int)Math.Min(count, 4096));
        for (var i = 0; i < count; i++) items.Add(ParseItem(bp));
        return items;
    }

    /// <summary>
    /// Generally item codes are its 3 letters plus a space eg <c>cap </c>; the code is trimmed to just the letters
    /// (upstream <c>ItemCodeParser</c>).
    /// </summary>
    private static string ItemCode(BinaryCursor bp) => D2Dotnet.Map.Data.JsNumber.TrimJs(bp.FixedString(4));

    private static ItemNode ParseItem(BinaryCursor bp)
    {
        var name = bp.FixedString(32);
        var files = new ItemFiles(bp.FixedString(32), bp.FixedString(32), bp.FixedString(32));
        var code = ItemCode(bp);
        var codes = new ItemCodes(ItemCode(bp), ItemCode(bp), ItemCode(bp));
        var altGfx = bp.FixedString(4);
        bp.Skip(40);
        var betterGem = bp.FixedString(4);
        var weaponClass = bp.Lu32();
        var weaponClass2Hand = bp.Lu32();
        var transmogrify = bp.Lu32();
        var minAc = bp.Lu32();
        var maxAc = bp.Lu32();
        var gambleCost = bp.Lu32();
        var speed = bp.Lu32();
        var bitField1 = bp.Lu32();
        var cost = bp.Lu32();
        var minStack = bp.Lu32();
        var maxStack = bp.Lu32();
        var spawnStack = bp.Lu32();
        var gemOffset = bp.Lu32();
        var nameId = bp.Lu16();
        bp.Skip(Unk2);
        var upgrade = new ItemUpgrades(ItemCode(bp), ItemCode(bp));
        bp.Skip(4);
        return new ItemNode(name, files, code, codes, altGfx, betterGem, weaponClass, weaponClass2Hand, transmogrify,
            minAc, maxAc, gambleCost, speed, bitField1, cost, minStack, maxStack, spawnStack, gemOffset, nameId, upgrade);
    }
}

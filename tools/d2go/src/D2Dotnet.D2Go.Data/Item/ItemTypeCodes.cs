// Port of d2go pkg/data/item/itemtypes.go (constants only) (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).
// Converted mechanically from the Go source; the oracle tests check every value against it.

namespace D2Dotnet.D2Go.Data.Item;

/// <summary>
/// Item type codes (the <c>Code</c> column of itemtypes.txt), named after the <c>ItemType</c> column with spaces
/// removed, as d2go's generated <c>item.Type*</c> constants. The rest of itemtypes.txt is loaded at run time, see
/// <see cref="GameTables.ItemTypes"/>.
/// </summary>
public static class ItemTypeCodes
{
    public const string TypeNone = "none";
    public const string TypeShield = "shie";
    public const string TypeArmor = "tors";
    public const string TypeGold = "gold";
    public const string TypeBowQuiver = "bowq";
    public const string TypeCrossbowQuiver = "xboq";
    public const string TypePlayerBodyPart = "play";
    public const string TypeHerb = "herb";
    public const string TypePotion = "poti";
    public const string TypeRing = "ring";
    public const string TypeElixir = "elix";
    public const string TypeAmulet = "amul";
    public const string TypeCharm = "char";
    public const string TypeBoots = "boot";
    public const string TypeGloves = "glov";
    public const string TypeBook = "book";
    public const string TypeBelt = "belt";
    public const string TypeGem = "gem";
    public const string TypeTorch = "torc";
    public const string TypeScroll = "scro";
    public const string TypeScepter = "scep";
    public const string TypeWand = "wand";
    public const string TypeStaff = "staf";
    public const string TypeBow = "bow";
    public const string TypeAxe = "axe";
    public const string TypeClub = "club";
    public const string TypeSword = "swor";
    public const string TypeHammer = "hamm";
    public const string TypeKnife = "knif";
    public const string TypeSpear = "spea";
    public const string TypePolearm = "pole";
    public const string TypeCrossbow = "xbow";
    public const string TypeMace = "mace";
    public const string TypeHelm = "helm";
    public const string TypeMissilePotion = "tpot";
    public const string TypeQuest = "ques";
    public const string TypeBodyPart = "body";
    public const string TypeKey = "key";
    public const string TypeThrowingKnife = "tkni";
    public const string TypeThrowingAxe = "taxe";
    public const string TypeJavelin = "jave";
    public const string TypeWeapon = "weap";
    public const string TypeMeleeWeapon = "mele";
    public const string TypeMissileWeapon = "miss";
    public const string TypeThrownWeapon = "thro";
    public const string TypeComboWeapon = "comb";
    public const string TypeAnyArmor = "armo";
    public const string TypeAnyShield = "shld";
    public const string TypeMiscellaneous = "misc";
    public const string TypeSocketFiller = "sock";
    public const string TypeSecondHand = "seco";
    public const string TypeStavesAndRods = "rod";
    public const string TypeMissile = "misl";
    public const string TypeBlunt = "blun";
    public const string TypeJewel = "jewl";
    public const string TypeClassSpecific = "clas";
    public const string TypeAmazonItem = "amaz";
    public const string TypeBarbarianItem = "barb";
    public const string TypeNecromancerItem = "necr";
    public const string TypePaladinItem = "pala";
    public const string TypeSorceressItem = "sorc";
    public const string TypeAssassinItem = "assn";
    public const string TypeDruidItem = "drui";
    public const string TypeHandtoHand = "h2h";
    public const string TypeOrb = "orb";
    public const string TypeVoodooHeads = "head";
    public const string TypeAuricShields = "ashd";
    public const string TypePrimalHelm = "phlm";
    public const string TypePelt = "pelt";
    public const string TypeCloak = "cloa";
    public const string TypeRune = "rune";
    public const string TypeCirclet = "circ";
    public const string TypeHealingPotion = "hpot";
    public const string TypeManaPotion = "mpot";
    public const string TypeRejuvPotion = "rpot";
    public const string TypeStaminaPotion = "spot";
    public const string TypeAntidotePotion = "apot";
    public const string TypeThawingPotion = "wpot";
    public const string TypeSmallCharm = "scha";
    public const string TypeMediumCharm = "mcha";
    public const string TypeLargeCharm = "lcha";
    public const string TypeAmazonBow = "abow";
    public const string TypeAmazonSpear = "aspe";
    public const string TypeAmazonJavelin = "ajav";
    public const string TypeHandtoHand2 = "h2h2";
    public const string TypeMagicBowQuiv = "mboq";
    public const string TypeMagicXbowQuiv = "mxbq";
    public const string TypeChippedGem = "gem0";
    public const string TypeFlawedGem = "gem1";
    public const string TypeStandardGem = "gem2";
    public const string TypeFlawlessGem = "gem3";
    public const string TypePerfectGem = "gem4";
    public const string TypeAmethyst = "gema";
    public const string TypeDiamond = "gemd";
    public const string TypeEmerald = "geme";
    public const string TypeRuby = "gemr";
    public const string TypeSapphire = "gems";
    public const string TypeTopaz = "gemt";
    public const string TypeSkull = "gemz";
    public const string TypeSwordsandKnives = "blde";
    public const string TypeSpearsandPolearms = "sppl";
}

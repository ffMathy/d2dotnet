// Values and names from d2go pkg/data/item/itemtypes.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).
// The oracle tests check every value against the pinned d2go source.

namespace D2Dotnet;

/// <summary>Item type codes: the <c>Code</c> column of itemtypes.txt, the key every other table uses to name an item type.</summary>
/// <remarks>
/// The constants and their names come from d2go's <c>item.Type*</c> constants (named after the <c>ItemType</c> column, with
/// spaces removed). The rest of itemtypes.txt is read from the game: see <see cref="Data.GameDatabase.ItemTypes"/>.
/// </remarks>
public static class ItemTypeCode
{
    /// <summary><c>none</c></summary>
    public const string None = "none";

    /// <summary><c>shie</c></summary>
    public const string Shield = "shie";

    /// <summary><c>tors</c></summary>
    public const string Armor = "tors";

    /// <summary><c>gold</c></summary>
    public const string Gold = "gold";

    /// <summary><c>bowq</c></summary>
    public const string BowQuiver = "bowq";

    /// <summary><c>xboq</c></summary>
    public const string CrossbowQuiver = "xboq";

    /// <summary><c>play</c></summary>
    public const string PlayerBodyPart = "play";

    /// <summary><c>herb</c></summary>
    public const string Herb = "herb";

    /// <summary><c>poti</c></summary>
    public const string Potion = "poti";

    /// <summary><c>ring</c></summary>
    public const string Ring = "ring";

    /// <summary><c>elix</c></summary>
    public const string Elixir = "elix";

    /// <summary><c>amul</c></summary>
    public const string Amulet = "amul";

    /// <summary><c>char</c></summary>
    public const string Charm = "char";

    /// <summary><c>boot</c></summary>
    public const string Boots = "boot";

    /// <summary><c>glov</c></summary>
    public const string Gloves = "glov";

    /// <summary><c>book</c></summary>
    public const string Book = "book";

    /// <summary><c>belt</c></summary>
    public const string Belt = "belt";

    /// <summary><c>gem</c></summary>
    public const string Gem = "gem";

    /// <summary><c>torc</c></summary>
    public const string Torch = "torc";

    /// <summary><c>scro</c></summary>
    public const string Scroll = "scro";

    /// <summary><c>scep</c></summary>
    public const string Scepter = "scep";

    /// <summary><c>wand</c></summary>
    public const string Wand = "wand";

    /// <summary><c>staf</c></summary>
    public const string Staff = "staf";

    /// <summary><c>bow</c></summary>
    public const string Bow = "bow";

    /// <summary><c>axe</c></summary>
    public const string Axe = "axe";

    /// <summary><c>club</c></summary>
    public const string Club = "club";

    /// <summary><c>swor</c></summary>
    public const string Sword = "swor";

    /// <summary><c>hamm</c></summary>
    public const string Hammer = "hamm";

    /// <summary><c>knif</c></summary>
    public const string Knife = "knif";

    /// <summary><c>spea</c></summary>
    public const string Spear = "spea";

    /// <summary><c>pole</c></summary>
    public const string Polearm = "pole";

    /// <summary><c>xbow</c></summary>
    public const string Crossbow = "xbow";

    /// <summary><c>mace</c></summary>
    public const string Mace = "mace";

    /// <summary><c>helm</c></summary>
    public const string Helm = "helm";

    /// <summary><c>tpot</c></summary>
    public const string MissilePotion = "tpot";

    /// <summary><c>ques</c></summary>
    public const string Quest = "ques";

    /// <summary><c>body</c></summary>
    public const string BodyPart = "body";

    /// <summary><c>key</c></summary>
    public const string Key = "key";

    /// <summary><c>tkni</c></summary>
    public const string ThrowingKnife = "tkni";

    /// <summary><c>taxe</c></summary>
    public const string ThrowingAxe = "taxe";

    /// <summary><c>jave</c></summary>
    public const string Javelin = "jave";

    /// <summary><c>weap</c></summary>
    public const string Weapon = "weap";

    /// <summary><c>mele</c></summary>
    public const string MeleeWeapon = "mele";

    /// <summary><c>miss</c></summary>
    public const string MissileWeapon = "miss";

    /// <summary><c>thro</c></summary>
    public const string ThrownWeapon = "thro";

    /// <summary><c>comb</c></summary>
    public const string ComboWeapon = "comb";

    /// <summary><c>armo</c></summary>
    public const string AnyArmor = "armo";

    /// <summary><c>shld</c></summary>
    public const string AnyShield = "shld";

    /// <summary><c>misc</c></summary>
    public const string Miscellaneous = "misc";

    /// <summary><c>sock</c></summary>
    public const string SocketFiller = "sock";

    /// <summary><c>seco</c></summary>
    public const string SecondHand = "seco";

    /// <summary><c>rod</c></summary>
    public const string StavesAndRods = "rod";

    /// <summary><c>misl</c></summary>
    public const string Missile = "misl";

    /// <summary><c>blun</c></summary>
    public const string Blunt = "blun";

    /// <summary><c>jewl</c></summary>
    public const string Jewel = "jewl";

    /// <summary><c>clas</c></summary>
    public const string ClassSpecific = "clas";

    /// <summary><c>amaz</c></summary>
    public const string AmazonItem = "amaz";

    /// <summary><c>barb</c></summary>
    public const string BarbarianItem = "barb";

    /// <summary><c>necr</c></summary>
    public const string NecromancerItem = "necr";

    /// <summary><c>pala</c></summary>
    public const string PaladinItem = "pala";

    /// <summary><c>sorc</c></summary>
    public const string SorceressItem = "sorc";

    /// <summary><c>assn</c></summary>
    public const string AssassinItem = "assn";

    /// <summary><c>drui</c></summary>
    public const string DruidItem = "drui";

    /// <summary><c>h2h</c></summary>
    public const string HandtoHand = "h2h";

    /// <summary><c>orb</c></summary>
    public const string Orb = "orb";

    /// <summary><c>head</c></summary>
    public const string VoodooHeads = "head";

    /// <summary><c>ashd</c></summary>
    public const string AuricShields = "ashd";

    /// <summary><c>phlm</c></summary>
    public const string PrimalHelm = "phlm";

    /// <summary><c>pelt</c></summary>
    public const string Pelt = "pelt";

    /// <summary><c>cloa</c></summary>
    public const string Cloak = "cloa";

    /// <summary><c>rune</c></summary>
    public const string Rune = "rune";

    /// <summary><c>circ</c></summary>
    public const string Circlet = "circ";

    /// <summary><c>hpot</c></summary>
    public const string HealingPotion = "hpot";

    /// <summary><c>mpot</c></summary>
    public const string ManaPotion = "mpot";

    /// <summary><c>rpot</c></summary>
    public const string RejuvPotion = "rpot";

    /// <summary><c>spot</c></summary>
    public const string StaminaPotion = "spot";

    /// <summary><c>apot</c></summary>
    public const string AntidotePotion = "apot";

    /// <summary><c>wpot</c></summary>
    public const string ThawingPotion = "wpot";

    /// <summary><c>scha</c></summary>
    public const string SmallCharm = "scha";

    /// <summary><c>mcha</c></summary>
    public const string MediumCharm = "mcha";

    /// <summary><c>lcha</c></summary>
    public const string LargeCharm = "lcha";

    /// <summary><c>abow</c></summary>
    public const string AmazonBow = "abow";

    /// <summary><c>aspe</c></summary>
    public const string AmazonSpear = "aspe";

    /// <summary><c>ajav</c></summary>
    public const string AmazonJavelin = "ajav";

    /// <summary><c>h2h2</c></summary>
    public const string HandtoHand2 = "h2h2";

    /// <summary><c>mboq</c></summary>
    public const string MagicBowQuiv = "mboq";

    /// <summary><c>mxbq</c></summary>
    public const string MagicXbowQuiv = "mxbq";

    /// <summary><c>gem0</c></summary>
    public const string ChippedGem = "gem0";

    /// <summary><c>gem1</c></summary>
    public const string FlawedGem = "gem1";

    /// <summary><c>gem2</c></summary>
    public const string StandardGem = "gem2";

    /// <summary><c>gem3</c></summary>
    public const string FlawlessGem = "gem3";

    /// <summary><c>gem4</c></summary>
    public const string PerfectGem = "gem4";

    /// <summary><c>gema</c></summary>
    public const string Amethyst = "gema";

    /// <summary><c>gemd</c></summary>
    public const string Diamond = "gemd";

    /// <summary><c>geme</c></summary>
    public const string Emerald = "geme";

    /// <summary><c>gemr</c></summary>
    public const string Ruby = "gemr";

    /// <summary><c>gems</c></summary>
    public const string Sapphire = "gems";

    /// <summary><c>gemt</c></summary>
    public const string Topaz = "gemt";

    /// <summary><c>gemz</c></summary>
    public const string Skull = "gemz";

    /// <summary><c>blde</c></summary>
    public const string SwordsandKnives = "blde";

    /// <summary><c>sppl</c></summary>
    public const string SpearsandPolearms = "sppl";
}

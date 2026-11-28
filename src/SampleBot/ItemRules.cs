using D2Dotnet;
using D2Dotnet.Data;
using D2Dotnet.Game;

/// <summary>Where an item is worn.</summary>
internal enum Slot
{
    None, Head, Neck, Torso, Weapon, Shield, Ring, Belt, Feet, Gloves,
}

/// <summary>
/// What the bot thinks of items: which slot an item is for, whether the character can wear it, how good it is for the
/// class, and what to pick up and keep.
/// </summary>
internal sealed class ItemRules(GameDatabase data)
{
    /// <summary>Total gold (carried and stashed) below which the bot picks up and sells magic items.</summary>
    public const int SellingBelowGold = 100_000;

    // itemtypes.txt codes, the most specific first where one is a kind of another.
    private static readonly (string Type, Slot Slot)[] SlotTypes =
    [
        ("helm", Slot.Head), ("circ", Slot.Head), ("tors", Slot.Torso), ("shie", Slot.Shield), ("glov", Slot.Gloves),
        ("boot", Slot.Feet), ("belt", Slot.Belt), ("amul", Slot.Neck), ("ring", Slot.Ring), ("weap", Slot.Weapon),
    ];

    /// <summary>The slot an item is worn in; <see cref="Slot.None"/> for potions, scrolls, gems, charms...</summary>
    public Slot SlotOf(Item item)
    {
        if (item.TypeCode is not { } type)
            return Slot.None;
        foreach (var (code, slot) in SlotTypes)
        {
            if (data.IsItemType(type, code))
                return slot;
        }

        return Slot.None;
    }

    /// <summary>True when the character meets the item's level, strength and dexterity (the base item's; magic changes to them are not counted).</summary>
    public bool CanWear(MainPlayer player, Item item) =>
        item.Info is { } info
        && IsForClass(item, player.Class)
        && player.Level >= info.RequiredLevel
        && (player.BaseStats.GetValue(StatId.Strength) ?? 0) >= info.RequiredStrength
        && (player.BaseStats.GetValue(StatId.Dexterity) ?? 0) >= info.RequiredDexterity;

    // itemtypes.txt class codes.
    private static readonly Dictionary<CharacterClass, string> ClassCodes = new()
    {
        [CharacterClass.Amazon] = "ama", [CharacterClass.Sorceress] = "sor", [CharacterClass.Necromancer] = "nec",
        [CharacterClass.Paladin] = "pal", [CharacterClass.Barbarian] = "bar", [CharacterClass.Druid] = "dru",
        [CharacterClass.Assassin] = "ass", [CharacterClass.Warlock] = "war",
    };

    /// <summary>True when the class may use the item: it has no class of its own (a Wolf Head is for druids only).</summary>
    public bool IsForClass(Item item, CharacterClass characterClass) =>
        item.TypeCode is not { } type || data.ClassOf(type) is var only && (only.Length == 0 || ClassCodes.GetValueOrDefault(characterClass) == only);

    /// <summary>
    /// How good the item is for the class, from its stats (null while it is not identified, when its magic is unknown).
    /// Casters weigh skills, cast rate, life, mana and resistances; the others damage and defense more.
    /// </summary>
    public double? Score(Item item, CharacterClass characterClass)
    {
        if (!item.IsIdentified && item.Quality is ItemQuality.Magic or ItemQuality.Rare or ItemQuality.Set or ItemQuality.Unique)
            return null;
        var caster = characterClass is CharacterClass.Sorceress or CharacterClass.Necromancer or CharacterClass.Warlock;
        double Stat(StatId id) => item.GetStat(id) ?? 0;
        // Life and mana bonuses on items are kept in 1/256 points.
        double Points(StatId id) => Math.Abs(Stat(id)) >= 256 ? Stat(id) / 256 : Stat(id);

        var score = 0.0;
        score += Stat(StatId.AllSkills) * 40;
        score += item.Stats.Where(s => s.Id == StatId.AddClassSkills && s.Layer == (ushort)characterClass).Sum(s => s.Value) * 40;
        score += item.Stats.Where(s => s.Id == StatId.AddSkillTab).Sum(s => s.Value) * 15;
        score += item.Stats.Where(s => s.Id == StatId.SingleSkill).Sum(s => s.Value) * 6;
        score += Stat(StatId.FasterCastRate) * (caster ? 1.5 : 0.2);
        score += Points(StatId.MaxLife) * 1.0 + Points(StatId.MaxMana) * (caster ? 0.8 : 0.3);
        score += (Stat(StatId.FireResist) + Stat(StatId.ColdResist) + Stat(StatId.LightningResist)) * 0.8 + Stat(StatId.PoisonResist) * 0.4;
        score += (Stat(StatId.Strength) + Stat(StatId.Dexterity) + Stat(StatId.Vitality)) * 1.5 + Stat(StatId.Energy) * (caster ? 1.0 : 0.3);
        score += Stat(StatId.FasterRunWalk) * 0.5 + Stat(StatId.FasterHitRecovery) * 0.4 + Stat(StatId.MagicFind) * 0.3;
        score += (Stat(StatId.LifeSteal) + Stat(StatId.ManaSteal)) * (caster ? 0.2 : 2);
        score += Stat(StatId.Defense) * (caster ? 0.15 : 0.3) + Stat(StatId.EnhancedDefense) * 0.05;

        if (item.Info is { } info)
        {
            var damage = info.IsTwoHanded ? (info.TwoHandedMinDamage + info.TwoHandedMaxDamage) / 2.0 : (info.MinDamage + info.MaxDamage) / 2.0;
            damage *= 1 + Stat(StatId.EnhancedDamage) / 100;
            score += damage * (caster ? 0.2 : 2);
            score += (info.MinDefense + info.MaxDefense) / 2.0 * (caster ? 0.1 : 0.25);
        }

        return score;
    }

    /// <summary>True for items picked up and kept whatever they are worth: gold, potions, the Identify and Town Portal scrolls.</summary>
    public static bool IsSupply(Item item) =>
        item.Class is ItemId.Gold or ItemId.ScrollOfIdentify or ItemId.ScrollOfTownPortal || item.IsPotion;

    /// <summary>True for items the bot never sells: tomes, quest items, keys.</summary>
    public static bool IsKept(Item item) =>
        item.Class is ItemId.TomeOfIdentify or ItemId.TomeOfTownPortal or ItemId.Key || item.IsQuestItem;

    /// <summary>
    /// True when the item on the ground is worth picking up: supplies; magic, rare, set and unique items while gold is
    /// below <see cref="SellingBelowGold"/> (to sell, or to wear); and a plain item only when it would be worn.
    /// </summary>
    public bool IsWorthPickingUp(Item item, MainPlayer player, IReadOnlyList<Item> equipped, int totalGold)
    {
        if (IsSupply(item) || item.IsQuestItem)
            return true;
        if (item.Quality is ItemQuality.Magic or ItemQuality.Rare or ItemQuality.Set or ItemQuality.Unique)
            return totalGold < SellingBelowGold || IsUpgrade(item, player, equipped);
        return IsUpgrade(item, player, equipped);
    }

    /// <summary>True when the item (identified) can be worn and scores better than what is worn in its slot (the worse ring for a ring).</summary>
    public bool IsUpgrade(Item item, MainPlayer player, IReadOnlyList<Item> equipped)
    {
        var slot = SlotOf(item);
        if (slot == Slot.None || !CanWear(player, item) || Score(item, player.Class) is not { } score)
            return false;
        var worn = equipped.Where(e => SlotOf(e) == slot).Select(e => Score(e, player.Class) ?? 0).ToList();
        // Anything wearable beats an empty slot (a belt adds potion rows even with no magic on it).
        if (worn.Count == 0 || (slot == Slot.Ring && worn.Count < 2))
            return true;
        return score > worn.Min() + 1;
    }

    /// <summary>The worn item an upgrade replaces: the one in its slot (the worse ring), or null for an empty slot.</summary>
    public Item? Replaces(Item upgrade, MainPlayer player, IReadOnlyList<Item> equipped)
    {
        var slot = SlotOf(upgrade);
        var worn = equipped.Where(e => SlotOf(e) == slot).ToList();
        if (slot == Slot.Ring && worn.Count < 2)
            return null;
        return worn.MinBy(e => Score(e, player.Class) ?? 0);
    }

    /// <summary>
    /// True when a carried item is to be sold: not worn, not kept, not a supply, and not an upgrade (unidentified magic
    /// items wait for identifying).
    /// </summary>
    public bool IsToSell(Item item, MainPlayer player, IReadOnlyList<Item> equipped) =>
        item.Location == ItemLocation.Inventory && !IsKept(item) && !IsSupply(item) && item.Class != ItemId.Gold
        && (item.IsIdentified || item.Quality is not (ItemQuality.Magic or ItemQuality.Rare or ItemQuality.Set or ItemQuality.Unique))
        && !IsUpgrade(item, player, equipped)
        && !IsGoodCharm(item, player);

    /// <summary>True for a charm: its bonuses count while it is carried in the inventory.</summary>
    public bool IsCharm(Item item) => item.TypeCode is { } type && data.IsItemType(type, "char");

    /// <summary>
    /// True for an identified charm worth carrying for the class (life, resistances, skills... scoring 5 or more), kept
    /// instead of sold.
    /// </summary>
    public bool IsGoodCharm(Item item, MainPlayer player) => IsCharm(item) && Score(item, player.Class) >= 5;
}

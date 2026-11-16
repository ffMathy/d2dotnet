// Port of d2go pkg/data/object/shrine_type.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Collections.Frozen;

namespace D2Dotnet.D2Go.Data.Object;

/// <summary>Shrine kinds (Go <c>object.ShrineType</c>); the values are the game's.</summary>
public enum ShrineType : uint
{
    RefillShrine = 0x01,
    HealthShrine = 0x02,
    ManaShrine = 0x03,
    HPXChangeShrine = 0x04,
    ManaXChangeShrine = 0x05,
    ArmorShrine = 0x06,
    CombatShrine = 0x07,
    ResistFireShrine = 0x08,
    ResistColdShrine = 0x09,
    ResistLightningShrine = 0x0A,
    ResistPoisonShrine = 0x0B,
    SkillShrine = 0x0C,
    ManaRegenShrine = 0x0D,
    StaminaShrine = 0x0E,
    ExperienceShrine = 0x0F,
    UnknownShrine = 0x10,
    PortalShrine = 0x11,
    GemShrine = 0x12,
    FireShrine = 0x13,
    MonsterShrine = 0x14,
    ExplosiveShrine = 0x15,
    PoisonShrine = 0x16,
}

/// <summary>The shrine of an object (Go <c>object.ShrineData</c>); the default instance is Go's zero value.</summary>
public sealed record ShrineData
{
    /// <summary>The shrine's name.</summary>
    public string ShrineName { get; init; } = "";

    /// <summary>The shrine's kind; 0 (not a member) when the object is not a shrine.</summary>
    public ShrineType ShrineType { get; init; }
}

/// <summary>Shrine names (Go <c>object.ShrineTypeNames</c>).</summary>
public static class ShrineTypes
{
    /// <summary>The display name of every shrine kind.</summary>
    public static IReadOnlyDictionary<ShrineType, string> ShrineTypeNames { get; } = new Dictionary<ShrineType, string>
    {
        [ShrineType.RefillShrine] = "Refill Shrine",
        [ShrineType.HealthShrine] = "Health Shrine",
        [ShrineType.ManaShrine] = "Mana Shrine",
        [ShrineType.HPXChangeShrine] = "HP XChange Shrine",
        [ShrineType.ManaXChangeShrine] = "Mana XChange Shrine",
        [ShrineType.ArmorShrine] = "Armor Shrine",
        [ShrineType.CombatShrine] = "Combat Shrine",
        [ShrineType.ResistFireShrine] = "Resist Fire Shrine",
        [ShrineType.ResistColdShrine] = "Resist Cold Shrine",
        [ShrineType.ResistLightningShrine] = "Resist Lightning Shrine",
        [ShrineType.ResistPoisonShrine] = "Resist Poison Shrine",
        [ShrineType.SkillShrine] = "Skill Shrine",
        [ShrineType.ManaRegenShrine] = "Mana Regen Shrine",
        [ShrineType.StaminaShrine] = "Stamina Shrine",
        [ShrineType.ExperienceShrine] = "Experience Shrine",
        [ShrineType.UnknownShrine] = "Unknown Shrine",
        [ShrineType.PortalShrine] = "Portal Shrine",
        [ShrineType.GemShrine] = "Gem Shrine",
        [ShrineType.FireShrine] = "Fire Shrine",
        [ShrineType.MonsterShrine] = "Monster Shrine",
        [ShrineType.ExplosiveShrine] = "Explosive Shrine",
        [ShrineType.PoisonShrine] = "Poison Shrine",
    }.ToFrozenDictionary();
}

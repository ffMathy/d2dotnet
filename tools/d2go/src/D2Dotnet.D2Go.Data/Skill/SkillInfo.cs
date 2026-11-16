// Port of d2go pkg/data/skill/skill.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data.Skill;

/// <summary>
/// The skill tree position and icon of a skill: a row of skilldesc.txt (Go <c>skill.Description</c>). Instances come
/// from <see cref="GameTables.SkillDescriptions"/>; the default instance is Go's zero value.
/// </summary>
public sealed record SkillDescription
{
    /// <summary>Never filled (d2go's generator does not set it either); always empty.</summary>
    public string Name { get; init; } = "";

    /// <summary>The <c>SkillPage</c> column.</summary>
    public int Page { get; init; }

    /// <summary>The <c>SkillRow</c> column.</summary>
    public int Row { get; init; }

    /// <summary>The <c>SkillColumn</c> column.</summary>
    public int Column { get; init; }

    /// <summary>The <c>ListRow</c> column.</summary>
    public int ListRow { get; init; }

    /// <summary>The <c>IconCel</c> column.</summary>
    public int IconCel { get; init; }
}

/// <summary>
/// A skill: a row of skills.txt (Go <c>skill.Skill</c>). Instances come from <see cref="GameTables.Skills"/>; the
/// default instance is Go's zero value.
/// </summary>
public sealed record SkillInfo
{
    /// <summary>The row index of the skill in skills.txt (Go's embedded <c>ID</c>; the table is keyed by <c>*Id</c>).</summary>
    public SkillId Id { get; init; }

    /// <summary>The <c>skill</c> column.</summary>
    public string Name { get; init; } = "";

    /// <summary>The <c>leftskill</c> column is <c>1</c>.</summary>
    public bool LeftSkill { get; init; }

    /// <summary>The <c>rightskill</c> column is <c>1</c>.</summary>
    public bool RightSkill { get; init; }

    /// <inheritdoc cref="SkillIdExtensions.Desc"/>
    public SkillDescription Desc(GameTables tables) => Id.Desc(tables);
}

/// <summary>A skill's points on a unit (Go <c>skill.Points</c>).</summary>
public readonly record struct SkillPoints(uint Level, uint Quantity, uint Charges);

/// <summary>The methods of Go's <c>skill.ID</c>.</summary>
public static class SkillIdExtensions
{
    /// <summary>
    /// Go <c>Desc[sk]</c>: the skilldesc.txt row whose <em>row index</em> equals the skill id (the zero value when
    /// missing). skilldesc.txt rows are not in skill id order, so for most skills this is another skill's row; d2go
    /// does the same, see the README.
    /// </summary>
    public static SkillDescription Desc(this SkillId skill, GameTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return tables.SkillDesc(skill);
    }
}

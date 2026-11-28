namespace D2Dotnet.Data;

/// <summary>A level: a row of levels.txt.</summary>
/// <param name="Id">The <c>Id</c> column.</param>
/// <param name="Name">The internal name (the <c>Name</c> column, such as <c>Act 1 - Town</c>).</param>
/// <param name="NameKey">The string key of the display name (the <c>LevelName</c> column; in D2R also the English name).
/// See <see cref="Localization.GetLevelName"/>.</param>
/// <param name="Act">The <c>Act</c> column.</param>
/// <param name="DrlgType">The <c>DrlgType</c> column: 1 maze, 2 preset, 3 wilderness.</param>
/// <param name="LevelType">The <c>LevelType</c> column (a lvltypes.txt <c>Id</c>).</param>
/// <param name="Waypoint">The <c>Waypoint</c> column: the waypoint index, 255 when the level has none.</param>
public sealed record LevelInfo(AreaId Id, string Name, string NameKey, ActId Act, int DrlgType, int LevelType, int Waypoint);

/// <summary>A monster or NPC class: a row of monstats.txt.</summary>
/// <param name="Id">The class id: the game row index (the <c>Expansion</c> divider is not counted).</param>
/// <param name="Code">The <c>Id</c> column (such as <c>skeleton1</c>), which other tables use to name the class.</param>
/// <param name="NameKey">The string key of the display name (the <c>NameStr</c> column). See <see cref="Localization.GetMonsterName"/>.</param>
public sealed record MonsterInfo(MonsterId Id, string Code, string NameKey);

/// <summary>A super unique monster: a row of superuniques.txt.</summary>
/// <param name="Index">The game row index (the <c>Expansion</c> divider is not counted).</param>
/// <param name="Code">The <c>Superunique</c> column (such as <c>Bishibosh</c>), which other tables use to name it.</param>
/// <param name="NameKey">The string key of the display name (the <c>Name</c> column). See <see cref="Localization.GetSuperUniqueName"/>.</param>
/// <param name="MonsterCode">The monster class it is (the <c>Class</c> column, a <see cref="MonsterInfo.Code"/>).</param>
public sealed record SuperUniqueInfo(int Index, string Code, string NameKey, string MonsterCode);

/// <summary>An object class: a row of objects.txt.</summary>
/// <param name="Id">The <c>*ID</c> column (classic <c>Id</c>); the game row index when the table has neither.</param>
/// <param name="NameKey">The string key of the display name (the <c>Name</c> column). See <see cref="Localization.GetObjectName"/>.</param>
/// <param name="SizeX">The <c>SizeX</c> column.</param>
/// <param name="SizeY">The <c>SizeY</c> column.</param>
/// <param name="HasCollision">The <c>HasCollision0</c> column is set.</param>
/// <param name="Selectable">The <c>Selectable0</c> column is set.</param>
/// <param name="OperateFn">The <c>OperateFn</c> column.</param>
public sealed record ObjectInfo(ObjectId Id, string NameKey, int SizeX, int SizeY, bool HasCollision, bool Selectable, int OperateFn);

/// <summary>The excel table a base item comes from.</summary>
public enum ItemKind
{
    /// <summary>weapons.txt.</summary>
    Weapon,

    /// <summary>armor.txt.</summary>
    Armor,

    /// <summary>misc.txt.</summary>
    Misc,
}

/// <summary>A base item: a row of weapons.txt, armor.txt or misc.txt.</summary>
/// <param name="Id">The item class id: the game row index across the three tables (weapons, then armor, then misc;
/// the <c>Expansion</c> dividers are not counted).</param>
/// <param name="Kind">The table the item comes from.</param>
/// <param name="Name">The <c>name</c> column (the English name).</param>
/// <param name="NameKey">The string key of the display name (the <c>namestr</c> column, empty when the table has none).
/// See <see cref="Localization.GetItemName"/>.</param>
/// <param name="Code">The <c>code</c> column.</param>
/// <param name="NormalCode">The <c>normcode</c> column; empty, like the other tier codes, when the table has no
/// <c>normcode</c> column.</param>
/// <param name="UberCode">The <c>ubercode</c> column (exceptional version).</param>
/// <param name="UltraCode">The <c>ultracode</c> column (elite version).</param>
/// <param name="Type">The <c>type</c> column (an <see cref="ItemTypeInfo.Code"/>).</param>
/// <param name="InventoryWidth">The <c>invwidth</c> column.</param>
/// <param name="InventoryHeight">The <c>invheight</c> column.</param>
/// <param name="MinDefense">The <c>minac</c> column (0 when the table has none).</param>
/// <param name="MaxDefense">The <c>maxac</c> column (0 when the table has none).</param>
public sealed record ItemInfo(
    ItemId Id,
    ItemKind Kind,
    string Name,
    string NameKey,
    string Code,
    string NormalCode,
    string UberCode,
    string UltraCode,
    string Type,
    int InventoryWidth,
    int InventoryHeight,
    int MinDefense,
    int MaxDefense)
{
    /// <summary>The <c>levelreq</c> column: the character level the item needs; 0 when the table has no such column.</summary>
    public int RequiredLevel { get; init; }

    /// <summary>The <c>reqstr</c> column: the strength the item needs.</summary>
    public int RequiredStrength { get; init; }

    /// <summary>The <c>reqdex</c> column: the dexterity the item needs.</summary>
    public int RequiredDexterity { get; init; }

    /// <summary>The <c>mindam</c> column: one-handed (or kick, for boots) minimum damage.</summary>
    public int MinDamage { get; init; }

    /// <summary>The <c>maxdam</c> column: one-handed (or kick) maximum damage.</summary>
    public int MaxDamage { get; init; }

    /// <summary>The <c>2handmindam</c> column: two-handed minimum damage.</summary>
    public int TwoHandedMinDamage { get; init; }

    /// <summary>The <c>2handmaxdam</c> column: two-handed maximum damage.</summary>
    public int TwoHandedMaxDamage { get; init; }

    /// <summary>The <c>2handed</c> column is set: the weapon takes both hands.</summary>
    public bool IsTwoHanded { get; init; }
}


/// <summary>An item type: a row of itemtypes.txt.</summary>
/// <param name="Index">The game row index (the <c>Expansion</c> divider is not counted).</param>
/// <param name="Name">The <c>ItemType</c> column.</param>
/// <param name="Code">The <c>Code</c> column; empty for the few unused rows. See <see cref="ItemTypeCode"/>.</param>
/// <param name="Equiv1">The <c>Equiv1</c> column: the parent type's code, or empty.</param>
/// <param name="Equiv2">The <c>Equiv2</c> column: a second parent type's code, or empty.</param>
/// <param name="Throwable">The <c>Throwable</c> column is set.</param>
/// <param name="Beltable">The <c>Beltable</c> column is set.</param>
public sealed record ItemTypeInfo(int Index, string Name, string Code, string Equiv1, string Equiv2, bool Throwable, bool Beltable);

/// <summary>A skill: a row of skills.txt.</summary>
/// <param name="Id">The <c>*Id</c> column (classic <c>Id</c>).</param>
/// <param name="Name">The <c>skill</c> column (the internal name).</param>
/// <param name="CharacterClass">The <c>charclass</c> column (<c>ama</c>, <c>sor</c>, ...; empty for monster and item skills).</param>
/// <param name="Description">The <c>skilldesc</c> column (a <see cref="SkillDescriptionInfo.Name"/>).</param>
/// <param name="LeftSkill">The <c>leftskill</c> column is set.</param>
/// <param name="RightSkill">The <c>rightskill</c> column is set (it can be put on the right mouse button); D2R only, false in the classic tables.</param>
public sealed record SkillInfo(SkillId Id, string Name, string CharacterClass, string Description, bool LeftSkill, bool RightSkill)
{
    /// <summary>The <c>mana</c> column: the cost at level 1, before <see cref="ManaShift"/>; 0 when the table has no such column.</summary>
    public int Mana { get; init; }

    /// <summary>The <c>lvlmana</c> column: what each level past the first adds to <see cref="Mana"/>.</summary>
    public int ManaPerLevel { get; init; }

    /// <summary>The <c>manashift</c> column: the cost is <c>(Mana + ManaPerLevel * (level - 1)) * 2^ManaShift / 256</c>.</summary>
    public int ManaShift { get; init; }

    /// <summary>The <c>minmana</c> column: the least the skill costs.</summary>
    public int MinimumMana { get; init; }

    /// <summary>The <c>reqlevel</c> column: the character level the first point needs; 0 when the table has no such column.</summary>
    public int RequiredLevel { get; init; }

    /// <summary>
    /// The skills a point needs first: the <c>reqskill1</c> to <c>reqskill3</c> columns (skill names), resolved to ids;
    /// names no row has are left out.
    /// </summary>
    public IReadOnlyList<SkillId> RequiredSkills { get; init; } = Array.Empty<SkillId>();

    /// <summary>
    /// The mana the skill costs at <paramref name="level"/> (hard points plus bonuses; 1 when below 1), in mana points:
    /// <c>(Mana + ManaPerLevel * (level - 1)) * 2^ManaShift / 256</c>, at least <see cref="MinimumMana"/>.
    /// Ring of Fire (16, 1, 7, 1) costs 8 at level 1 and 8.5 at level 2.
    /// </summary>
    public double ManaCost(int level)
    {
        var levels = Math.Max(level, 1) - 1;
        var cost = (Mana + (double)ManaPerLevel * levels) * Math.Pow(2, ManaShift) / 256;
        return Math.Max(cost, MinimumMana);
    }
}

/// <summary>How a skill is shown in the skill tree: a row of skilldesc.txt.</summary>
/// <param name="Index">The game row index.</param>
/// <param name="Name">The <c>skilldesc</c> column, which skills.txt refers to.</param>
/// <param name="Page">The <c>SkillPage</c> column (skill tree tab, 0 when not in the tree).</param>
/// <param name="Row">The <c>SkillRow</c> column.</param>
/// <param name="Column">The <c>SkillColumn</c> column.</param>
/// <param name="ListRow">The <c>ListRow</c> column.</param>
/// <param name="IconCel">The <c>IconCel</c> column.</param>
public sealed record SkillDescriptionInfo(int Index, string Name, int Page, int Row, int Column, int ListRow, int IconCel);

// Replaces the tables that d2go's cmd/txttocode generates into pkg/data (github.com/hectorgimenez/d2go, MIT,
// (c) 2023 Héctor Giménez): item/itemtypes.go, item/items.go, area/areas.go, skill/skills.go, skill/skilldesc.go
// and object/objects.go. The game data is read at run time; none of it is part of this repository.

using System.Collections.Frozen;
using D2Dotnet.D2Go.Data.Area;
using D2Dotnet.D2Go.Data.Item;
using D2Dotnet.D2Go.Data.Object;
using D2Dotnet.D2Go.Data.Skill;
using D2Dotnet.Map.GameData;

namespace D2Dotnet.D2Go.Data;

/// <summary>
/// The excel tables that d2go compiles into Go maps, loaded from the game files instead. Immutable and thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// Each table is built exactly as d2go's <c>cmd/txttocode</c> builds the Go source: same files, same columns, same
/// row filter (rows whose second column is empty are skipped) and the same keys and ids. The dictionaries are the Go
/// maps; the lookup methods (<see cref="ItemDesc"/>, <see cref="Area(AreaId)"/>, ...) are Go's map index expression,
/// which gives the zero value for a missing key.
/// </para>
/// <para>
/// Files: <c>data/global/excel/</c> <c>itemtypes.txt</c>, <c>weapons.txt</c>, <c>armor.txt</c>, <c>misc.txt</c>,
/// <c>levels.txt</c>, <c>skills.txt</c>, <c>skilldesc.txt</c> and <c>objects.txt</c>.
/// </para>
/// </remarks>
public sealed class GameTables
{
    /// <summary>Where the tables are, relative to the game's data root.</summary>
    public const string ExcelDirectory = "data/global/excel/";

    private static readonly ItemType ZeroItemType = new();
    private static readonly ItemDescription ZeroItemDescription = new();
    private static readonly AreaInfo ZeroArea = new();
    private static readonly SkillInfo ZeroSkill = new();
    private static readonly SkillDescription ZeroSkillDescription = new();
    private static readonly ObjectDescription ZeroObjectDescription = new();

    private GameTables(
        FrozenDictionary<string, ItemType> itemTypes,
        FrozenDictionary<int, ItemDescription> itemDescriptions,
        FrozenDictionary<AreaId, AreaInfo> areas,
        FrozenDictionary<SkillId, SkillInfo> skills,
        FrozenDictionary<SkillId, SkillDescription> skillDescriptions,
        FrozenDictionary<int, ObjectDescription> objectDescriptions)
    {
        ItemTypes = itemTypes;
        ItemDescriptions = itemDescriptions;
        Areas = areas;
        Skills = skills;
        SkillDescriptions = skillDescriptions;
        ObjectDescriptions = objectDescriptions;
    }

    /// <summary>Item types by code (Go <c>item.ItemTypes</c>), from itemtypes.txt.</summary>
    public IReadOnlyDictionary<string, ItemType> ItemTypes { get; }

    /// <summary>Base items by item id (Go <c>item.Desc</c>), from weapons.txt, armor.txt and misc.txt.</summary>
    public IReadOnlyDictionary<int, ItemDescription> ItemDescriptions { get; }

    /// <summary>Levels by id (Go <c>area.Areas</c>), from levels.txt.</summary>
    public IReadOnlyDictionary<AreaId, AreaInfo> Areas { get; }

    /// <summary>Skills by the <c>*Id</c> column (Go <c>skill.Skills</c>), from skills.txt.</summary>
    public IReadOnlyDictionary<SkillId, SkillInfo> Skills { get; }

    /// <summary>
    /// Skill descriptions by skilldesc.txt <em>row index</em>, typed as a skill id like Go's <c>skill.Desc</c>
    /// (<c>map[ID]Description</c>).
    /// </summary>
    public IReadOnlyDictionary<SkillId, SkillDescription> SkillDescriptions { get; }

    /// <summary>Object classes by the <c>*ID</c> column (Go <c>object.Desc</c>), from objects.txt.</summary>
    public IReadOnlyDictionary<int, ObjectDescription> ObjectDescriptions { get; }

    /// <summary>Go <c>item.ItemTypes[code]</c>: the item type, or the zero value.</summary>
    public ItemType ItemType(string code) => ItemTypes.GetValueOrDefault(code, ZeroItemType);

    /// <summary>Go <c>item.Desc[id]</c>: the base item, or the zero value.</summary>
    public ItemDescription ItemDesc(int id) => ItemDescriptions.GetValueOrDefault(id, ZeroItemDescription);

    /// <summary>Go <c>area.Areas[id]</c>: the level, or the zero value (whose id is <see cref="AreaId.None"/>).</summary>
    public AreaInfo Area(AreaId id) => Areas.GetValueOrDefault(id, ZeroArea);

    /// <summary>Go <c>skill.Skills[id]</c>: the skill, or the zero value.</summary>
    public SkillInfo Skill(SkillId id) => Skills.GetValueOrDefault(id, ZeroSkill);

    /// <summary>Go <c>skill.Desc[id]</c>: the skilldesc.txt row with this index, or the zero value.</summary>
    public SkillDescription SkillDesc(SkillId id) => SkillDescriptions.GetValueOrDefault(id, ZeroSkillDescription);

    /// <summary>Go <c>object.Desc[id]</c>: the object class, or the zero value.</summary>
    public ObjectDescription ObjectDesc(int id) => ObjectDescriptions.GetValueOrDefault(id, ZeroObjectDescription);

    /// <summary>Load every table from the game files.</summary>
    /// <exception cref="FileNotFoundException">A table is missing.</exception>
    /// <exception cref="InvalidDataException">A table lacks a column d2go reads, or has a malformed number.</exception>
    public static GameTables Load(IGameFileProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new GameTables(
            LoadItemTypes(Read(provider, "itemtypes.txt")),
            LoadItemDescriptions(Read(provider, "weapons.txt"), Read(provider, "armor.txt"), Read(provider, "misc.txt")),
            LoadAreas(Read(provider, "levels.txt")),
            LoadSkills(Read(provider, "skills.txt")),
            LoadSkillDescriptions(Read(provider, "skilldesc.txt")),
            LoadObjectDescriptions(Read(provider, "objects.txt")));
    }

    private static ExcelTable Read(IGameFileProvider provider, string file)
    {
        var path = ExcelDirectory + file;
        var bytes = provider.ReadFile(path) ?? throw new FileNotFoundException($"Game file '{path}' not found.", path);
        return ExcelTable.Parse(file, bytes);
    }

    // templateItemType: ItemTypes[Code] = {ID: row index, Name: ItemType, Code, Throwable, Beltable}.
    private static FrozenDictionary<string, ItemType> LoadItemTypes(ExcelTable table)
    {
        var types = new Dictionary<string, ItemType>(StringComparer.Ordinal);
        var id = 0;
        foreach (var row in table.GeneratorRows())
        {
            var code = table.Get(row, "Code");
            types[code] = new ItemType
            {
                Id = id++,
                Name = table.Get(row, "ItemType"),
                Code = code,
                Throwable = table.GetFlag(row, "Throwable"),
                Beltable = table.GetFlag(row, "Beltable"),
            };
        }

        return types.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // generateItems: one running id over weapons, armor and misc; name spelling fixes; minac/maxac default to 0; when
    // normcode is missing, normcode, ubercode and ultracode are all empty.
    private static FrozenDictionary<int, ItemDescription> LoadItemDescriptions(params ExcelTable[] tables)
    {
        var items = new Dictionary<int, ItemDescription>();
        var id = 0;
        foreach (var table in tables)
        {
            var hasTierCodes = table.HasColumn("normcode");
            foreach (var row in table.GeneratorRows())
            {
                items[id] = new ItemDescription
                {
                    Id = id,
                    Name = FixItemName(table.GetOptional(row, "name") ?? ""),
                    Code = table.Get(row, "code"),
                    NormalCode = hasTierCodes ? table.Get(row, "normcode") : "",
                    UberCode = hasTierCodes ? table.Get(row, "ubercode") : "",
                    UltraCode = hasTierCodes ? table.Get(row, "ultracode") : "",
                    InventoryWidth = table.GetInt(row, "invwidth"),
                    InventoryHeight = table.GetInt(row, "invheight"),
                    MinDefense = table.HasColumn("minac") ? table.GetInt(row, "minac") : 0,
                    MaxDefense = table.HasColumn("maxac") ? table.GetInt(row, "maxac") : 0,
                    Type = table.Get(row, "type"),
                };
                id++;
            }
        }

        return items.ToFrozenDictionary();
    }

    /// <summary>The spelling fixes d2go's generator applies to item names, in its order.</summary>
    internal static string FixItemName(string name) => name
        .Replace("Heirophant", "Hierophant", StringComparison.Ordinal)
        .Replace("Colossal", "Colossus", StringComparison.Ordinal)
        .Replace("Ancient Shield", "Kurast Shield", StringComparison.Ordinal)
        .Replace("Ornate Armor", "Ornate Plate", StringComparison.Ordinal)
        .Replace("Essense", "Essence", StringComparison.Ordinal);

    // templateLevels: Areas[row index] = {Name: LevelName, ID: row index}.
    private static FrozenDictionary<AreaId, AreaInfo> LoadAreas(ExcelTable table)
    {
        var areas = new Dictionary<AreaId, AreaInfo>();
        var id = 0;
        foreach (var row in table.GeneratorRows())
        {
            areas[(AreaId)id] = new AreaInfo { Id = (AreaId)id, Name = table.Get(row, "LevelName") };
            id++;
        }

        return areas.ToFrozenDictionary();
    }

    // templateSkills: Skills[*Id] = {Name: skill, ID: row index, LeftSkill, RightSkill}.
    private static FrozenDictionary<SkillId, SkillInfo> LoadSkills(ExcelTable table)
    {
        var skills = new Dictionary<SkillId, SkillInfo>();
        var index = 0;
        foreach (var row in table.GeneratorRows())
        {
            var key = (SkillId)checked((short)table.GetInt(row, "*Id"));
            skills[key] = new SkillInfo
            {
                Id = (SkillId)checked((short)index),
                Name = table.Get(row, "skill"),
                LeftSkill = table.GetFlag(row, "leftskill"),
                RightSkill = table.GetFlag(row, "rightskill"),
            };
            index++;
        }

        return skills.ToFrozenDictionary();
    }

    // templateSkillDesc: Desc[row index] = {Page: SkillPage, Row: SkillRow, Column: SkillColumn, ListRow, IconCel}.
    private static FrozenDictionary<SkillId, SkillDescription> LoadSkillDescriptions(ExcelTable table)
    {
        var descriptions = new Dictionary<SkillId, SkillDescription>();
        var index = 0;
        foreach (var row in table.GeneratorRows())
        {
            descriptions[(SkillId)checked((short)index)] = new SkillDescription
            {
                Page = table.GetInt(row, "SkillPage"),
                Row = table.GetInt(row, "SkillRow"),
                Column = table.GetInt(row, "SkillColumn"),
                ListRow = table.GetInt(row, "ListRow"),
                IconCel = table.GetInt(row, "IconCel"),
            };
            index++;
        }

        return descriptions.ToFrozenDictionary();
    }

    // templateObjects: Desc[*ID] = {Name, ID: *ID, SizeX, SizeY, HasCollision: HasCollision0}.
    private static FrozenDictionary<int, ObjectDescription> LoadObjectDescriptions(ExcelTable table)
    {
        var objects = new Dictionary<int, ObjectDescription>();
        foreach (var row in table.GeneratorRows())
        {
            var id = table.GetInt(row, "*ID");
            objects[id] = new ObjectDescription
            {
                Id = id,
                Name = table.Get(row, "Name"),
                SizeX = table.GetInt(row, "SizeX"),
                SizeY = table.GetInt(row, "SizeY"),
                HasCollision = table.GetFlag(row, "HasCollision0"),
            };
        }

        return objects.ToFrozenDictionary();
    }
}

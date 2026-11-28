using System.Collections.Concurrent;
using D2Dotnet.Files;

namespace D2Dotnet.Data;

/// <summary>
/// The game's data, read from its files: the excel tables (raw and strongly typed) and the string tables. Every table
/// is loaded the first time it is used, once; the instance is safe to use from several threads.
/// </summary>
/// <remarks>
/// <para>
/// Typed tables keep the <em>game rows</em> of their excel file in file order: rows whose first cell (the key column for
/// monstats.txt and superuniques.txt) is empty or is the <c>Expansion</c> divider are left out, so a record's index is
/// the id the game uses. Their numbers are read strictly (see <see cref="ExcelTable.ParseInt32"/>) and a missing key
/// column or file is an error; columns that 1.x and D2R name differently (<c>*ID</c>/<c>Id</c>) are found either way.
/// </para>
/// <para>
/// Exceptions thrown while a table loads (<see cref="FileNotFoundException"/> for a missing file,
/// <see cref="InvalidDataException"/> for a missing column or a malformed number) are thrown again on every later use.
/// </para>
/// </remarks>
public sealed class GameDatabase
{
    /// <summary>Where the excel tables are, relative to the game's data root.</summary>
    public const string ExcelDirectory = "data/global/excel/";

    private const LazyThreadSafetyMode Once = LazyThreadSafetyMode.ExecutionAndPublication;

    private readonly ConcurrentDictionary<string, Lazy<ExcelTable?>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<GameTable<AreaId, LevelInfo>> _levels;
    private readonly Lazy<GameTable<MonsterId, MonsterInfo>> _monsters;
    private readonly Lazy<GameTable<int, SuperUniqueInfo>> _superUniques;
    private readonly Lazy<GameTable<ObjectId, ObjectInfo>> _objects;
    private readonly Lazy<GameTable<ItemId, ItemInfo>> _items;
    private readonly Lazy<IReadOnlyDictionary<string, ItemInfo>> _itemsByCode;
    private readonly Lazy<GameTable<string, ItemTypeInfo>> _itemTypes;
    private readonly Lazy<GameTable<SkillId, SkillInfo>> _skills;
    private readonly Lazy<GameTable<string, SkillDescriptionInfo>> _skillDescriptions;
    private readonly Lazy<Localization> _localization;

    /// <summary>A database over the game files of <paramref name="files"/>.</summary>
    /// <param name="files">The game files.</param>
    /// <param name="language">The language of <see cref="Localization"/> (see <see cref="Localization.ResolveLanguage"/>).</param>
    /// <exception cref="ArgumentException"><paramref name="language"/> is blank.</exception>
    public GameDatabase(IGameFileProvider files, string language = Localization.DefaultLanguage)
    {
        Files = files ?? throw new ArgumentNullException(nameof(files));
        Language = Localization.ResolveLanguage(language);
        _levels = new(LoadLevels, Once);
        _monsters = new(LoadMonsters, Once);
        _superUniques = new(LoadSuperUniques, Once);
        _objects = new(LoadObjects, Once);
        _items = new(LoadItems, Once);
        _itemsByCode = new(() => new GameTable<string, ItemInfo>(Items, i => i.Code, StringComparer.Ordinal).ByKey, Once);
        _itemTypes = new(LoadItemTypes, Once);
        _skills = new(LoadSkills, Once);
        _skillDescriptions = new(LoadSkillDescriptions, Once);
        _localization = new(() => Localization.Create(Files, Language, this), Once);
    }

    /// <summary>The game files the tables are read from.</summary>
    public IGameFileProvider Files { get; }

    /// <summary>The D2R language field of <see cref="Localization"/>.</summary>
    public string Language { get; }

    /// <summary>levels.txt by <see cref="AreaId"/>.</summary>
    public GameTable<AreaId, LevelInfo> Levels => _levels.Value;

    /// <summary>monstats.txt by <see cref="MonsterId"/>.</summary>
    public GameTable<MonsterId, MonsterInfo> Monsters => _monsters.Value;

    /// <summary>superuniques.txt by <see cref="SuperUniqueInfo.Index"/>.</summary>
    public GameTable<int, SuperUniqueInfo> SuperUniques => _superUniques.Value;

    /// <summary>objects.txt by <see cref="ObjectId"/>.</summary>
    public GameTable<ObjectId, ObjectInfo> Objects => _objects.Value;

    /// <summary>weapons.txt, armor.txt and misc.txt (in that order) by <see cref="ItemId"/>.</summary>
    public GameTable<ItemId, ItemInfo> Items => _items.Value;

    /// <summary>itemtypes.txt by <see cref="ItemTypeInfo.Code"/> (see <see cref="ItemTypeCode"/>); the unused rows without a code are only in the list.</summary>
    public GameTable<string, ItemTypeInfo> ItemTypes => _itemTypes.Value;

    /// <summary>
    /// True when item type <paramref name="type"/> is <paramref name="ancestor"/> or one of its kinds: the
    /// <c>Equiv1</c> and <c>Equiv2</c> columns of itemtypes.txt, followed up (a staff is a weapon, a circlet a helm and
    /// armor). Codes compare exactly; an unknown type is no kind of anything but itself.
    /// </summary>
    public bool IsItemType(string type, string ancestor)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(ancestor);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([type]);
        while (pending.TryPop(out var code))
        {
            if (code == ancestor)
                return true;
            if (code.Length == 0 || !seen.Add(code) || ItemTypes.Find(code) is not { } info)
                continue;
            pending.Push(info.Equiv1);
            pending.Push(info.Equiv2);
        }

        return false;
    }

    /// <summary>skills.txt by <see cref="SkillId"/>.</summary>
    public GameTable<SkillId, SkillInfo> Skills => _skills.Value;

    /// <summary>skilldesc.txt by <see cref="SkillDescriptionInfo.Name"/> (what <see cref="SkillInfo.Description"/> names).</summary>
    public GameTable<string, SkillDescriptionInfo> SkillDescriptions => _skillDescriptions.Value;

    /// <summary>The string tables in <see cref="Language"/>, with lookups by id.</summary>
    public Localization Localization => _localization.Value;

    /// <summary>The base item with this <see cref="ItemInfo.Code"/>, or null (the first one when codes repeat).</summary>
    public ItemInfo? FindItem(string code) => _itemsByCode.Value.GetValueOrDefault(code);

    /// <summary>
    /// An excel table by name, such as <c>Levels</c> or <c>LvlPrest</c> (case-insensitive, <c>.txt</c> optional), read
    /// from <see cref="ExcelDirectory"/>; null when the game has no such file. Parsed once and cached.
    /// </summary>
    public ExcelTable? GetTable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var file = name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        return _tables.GetOrAdd(file, f => new Lazy<ExcelTable?>(() =>
        {
            var path = ExcelPath(f);
            var bytes = Files.ReadFile(path);
            return bytes == null ? null : ExcelTable.Parse(bytes, f.ToLowerInvariant() + ".txt");
        }, Once)).Value;
    }

    /// <summary>An excel table by name, like <see cref="GetTable"/>, that must exist.</summary>
    /// <exception cref="FileNotFoundException">The game has no such file.</exception>
    public ExcelTable GetRequiredTable(string name)
    {
        var table = GetTable(name);
        if (table != null)
            return table;
        var path = ExcelPath(name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name);
        throw new FileNotFoundException($"Game file '{path}' not found.", path);
    }

    private static string ExcelPath(string name) => ExcelDirectory + name.ToLowerInvariant() + ".txt";


    /// <summary>The game rows of a table: rows whose key cell is empty or is the <c>Expansion</c> divider are left out.</summary>
    private static IEnumerable<int> GameRows(ExcelTable table, int keyColumn = 0)
    {
        for (var row = 0; row < table.RowCount; row++)
        {
            var key = table.GetSpan(row, keyColumn);
            if (key.IsEmpty || key.SequenceEqual("Expansion"))
                continue;
            yield return row;
        }
    }

    private static int Optional(ExcelTable table, int row, int column, int missing = 0) =>
        column < 0 ? missing : table.ParseInt32(row, column);

    private static bool Flag(ExcelTable table, int row, int column) => column >= 0 && table.ParseInt32(row, column) != 0;

    private GameTable<AreaId, LevelInfo> LoadLevels()
    {
        var t = GetRequiredTable("levels");
        int id = t.GetRequiredColumnIndex("Id"), nameKey = t.GetRequiredColumnIndex("LevelName"), name = t.GetColumnIndex("Name");
        int act = t.GetColumnIndex("Act"), drlg = t.GetColumnIndex("DrlgType"), type = t.GetColumnIndex("LevelType"), wp = t.GetColumnIndex("Waypoint");
        var records = GameRows(t).Select(r => new LevelInfo(
            (AreaId)t.ParseInt32(r, id), t.GetString(r, name), t.GetString(r, nameKey), (ActId)Optional(t, r, act),
            Optional(t, r, drlg), Optional(t, r, type), Optional(t, r, wp, 255)));
        return new GameTable<AreaId, LevelInfo>(records, l => l.Id);
    }

    private GameTable<MonsterId, MonsterInfo> LoadMonsters()
    {
        var t = GetRequiredTable("monstats");
        int code = t.GetRequiredColumnIndex("Id"), nameKey = t.GetColumnIndex("NameStr");
        int ai = t.GetColumnIndex("AI"), spawn = t.GetColumnIndex("spawn");
        var skills = Enumerable.Range(1, 8).Select(n => t.GetColumnIndex("Skill" + n)).Where(c => c >= 0).ToArray();
        var records = GameRows(t, code).Select((r, i) =>
        {
            var spawned = t.GetString(r, spawn);
            return new MonsterInfo((MonsterId)i, t.GetString(r, code), t.GetString(r, nameKey))
            {
                Ai = t.GetString(r, ai),
                Spawn = spawned,
                IsSummoner = spawned.Length > 0 || skills.Any(c => SummoningSkills.Contains(t.GetString(r, c))),
                Resistances = ReadResistances(t, r, ""),
                ResistancesNightmare = ReadResistances(t, r, "(N)"),
                ResistancesHell = ReadResistances(t, r, "(H)"),
            };
        });
        return new GameTable<MonsterId, MonsterInfo>(records, m => m.Id);
    }

    // A row's resistances in one difficulty: the Res* columns with the difficulty's suffix ("", "(N)", "(H)").
    private static Resistances ReadResistances(ExcelTable t, int row, string suffix)
    {
        int Read(string name) => Optional(t, row, t.GetColumnIndex("Res" + name + suffix));
        return new Resistances(Read("Dm"), Read("Ma"), Read("Fi"), Read("Li"), Read("Co"), Read("Po"));
    }

    // Monster skills (skills.txt names) that make more monsters.
    private static readonly HashSet<string> SummoningSkills = new(StringComparer.OrdinalIgnoreCase) { "Resurrect", "Resurrect2", "Nest", "MagottLay" };

    private GameTable<int, SuperUniqueInfo> LoadSuperUniques()
    {
        var t = GetRequiredTable("superuniques");
        int code = t.GetRequiredColumnIndex("Superunique"), nameKey = t.GetColumnIndex("Name"), monster = t.GetColumnIndex("Class");
        int mod1 = t.GetColumnIndex("Mod1"), mod2 = t.GetColumnIndex("Mod2"), mod3 = t.GetColumnIndex("Mod3");
        var records = GameRows(t, code).Select((r, i) => new SuperUniqueInfo(i, t.GetString(r, code), t.GetString(r, nameKey), t.GetString(r, monster))
        {
            Mods = (Optional(t, r, mod1), Optional(t, r, mod2), Optional(t, r, mod3)),
        });
        return new GameTable<int, SuperUniqueInfo>(records, s => s.Index);
    }

    private GameTable<ObjectId, ObjectInfo> LoadObjects()
    {
        var t = GetRequiredTable("objects");
        int id = t.FindColumn("*ID", "Id"), nameKey = t.GetRequiredColumnIndex("Name");
        int sizeX = t.GetColumnIndex("SizeX"), sizeY = t.GetColumnIndex("SizeY"), collision = t.GetColumnIndex("HasCollision0");
        int selectable = t.GetColumnIndex("Selectable0"), operate = t.GetColumnIndex("OperateFn");
        var records = GameRows(t).Select((r, i) => new ObjectInfo(
            (ObjectId)(id < 0 ? i : t.ParseInt32(r, id)), t.GetString(r, nameKey), Optional(t, r, sizeX), Optional(t, r, sizeY),
            Flag(t, r, collision), Flag(t, r, selectable), Optional(t, r, operate)));
        return new GameTable<ObjectId, ObjectInfo>(records, o => o.Id);
    }

    private GameTable<ItemId, ItemInfo> LoadItems()
    {
        var records = new List<ItemInfo>();
        foreach (var (file, kind) in new[] { ("weapons", ItemKind.Weapon), ("armor", ItemKind.Armor), ("misc", ItemKind.Misc) })
        {
            var t = GetRequiredTable(file);
            int code = t.GetRequiredColumnIndex("code"), type = t.GetRequiredColumnIndex("type");
            int width = t.GetRequiredColumnIndex("invwidth"), height = t.GetRequiredColumnIndex("invheight");
            int name = t.GetColumnIndex("name"), nameKey = t.GetColumnIndex("namestr"), minac = t.GetColumnIndex("minac"), maxac = t.GetColumnIndex("maxac");
            // The tier codes come as a set: without normcode (misc.txt) the item has none.
            int norm = t.GetColumnIndex("normcode");
            int uber = norm < 0 ? -1 : t.GetColumnIndex("ubercode"), ultra = norm < 0 ? -1 : t.GetColumnIndex("ultracode");
            int levelReq = t.GetColumnIndex("levelreq"), strReq = t.GetColumnIndex("reqstr"), dexReq = t.GetColumnIndex("reqdex");
            int minDam = t.GetColumnIndex("mindam"), maxDam = t.GetColumnIndex("maxdam");
            int twoMinDam = t.GetColumnIndex("2handmindam"), twoMaxDam = t.GetColumnIndex("2handmaxdam"), twoHanded = t.GetColumnIndex("2handed");
            foreach (var r in GameRows(t))
            {
                records.Add(new ItemInfo(
                    (ItemId)records.Count, kind, t.GetString(r, name), t.GetString(r, nameKey), t.GetString(r, code),
                    t.GetString(r, norm), t.GetString(r, uber), t.GetString(r, ultra), t.GetString(r, type),
                    t.ParseInt32(r, width), t.ParseInt32(r, height), Optional(t, r, minac), Optional(t, r, maxac))
                {
                    RequiredLevel = Optional(t, r, levelReq),
                    RequiredStrength = Optional(t, r, strReq),
                    RequiredDexterity = Optional(t, r, dexReq),
                    MinDamage = Optional(t, r, minDam),
                    MaxDamage = Optional(t, r, maxDam),
                    TwoHandedMinDamage = Optional(t, r, twoMinDam),
                    TwoHandedMaxDamage = Optional(t, r, twoMaxDam),
                    IsTwoHanded = Flag(t, r, twoHanded),
                });
            }
        }

        return new GameTable<ItemId, ItemInfo>(records, i => i.Id);
    }

    private GameTable<string, ItemTypeInfo> LoadItemTypes()
    {
        var t = GetRequiredTable("itemtypes");
        int name = t.GetRequiredColumnIndex("ItemType"), code = t.GetRequiredColumnIndex("Code");
        int throwable = t.GetRequiredColumnIndex("Throwable"), beltable = t.GetRequiredColumnIndex("Beltable");
        int equiv1 = t.GetColumnIndex("Equiv1"), equiv2 = t.GetColumnIndex("Equiv2");
        var records = GameRows(t, name).Select((r, i) => new ItemTypeInfo(
            i, t.GetString(r, name), t.GetString(r, code), t.GetString(r, equiv1), t.GetString(r, equiv2),
            Flag(t, r, throwable), Flag(t, r, beltable)));
        return new GameTable<string, ItemTypeInfo>(records, it => it.Code.Length > 0 ? it.Code : null, StringComparer.Ordinal);
    }

    private GameTable<SkillId, SkillInfo> LoadSkills()
    {
        var t = GetRequiredTable("skills");
        int id = t.FindColumn("*Id", "Id");
        if (id < 0)
            t.GetRequiredColumnIndex("*Id"); // throws, naming the D2R column
        int name = t.GetRequiredColumnIndex("skill"), left = t.GetColumnIndex("leftskill"), right = t.GetColumnIndex("rightskill"); // rightskill is D2R only
        int charClass = t.GetColumnIndex("charclass"), desc = t.GetColumnIndex("skilldesc");
        int mana = t.GetColumnIndex("mana"), lvlMana = t.GetColumnIndex("lvlmana"), shift = t.GetColumnIndex("manashift"), minMana = t.GetColumnIndex("minmana");
        int reqLevel = t.GetColumnIndex("reqlevel");
        int[] reqSkill = [t.GetColumnIndex("reqskill1"), t.GetColumnIndex("reqskill2"), t.GetColumnIndex("reqskill3")];
        var rows = GameRows(t, name).ToArray();
        // reqskill columns name the skill column: resolve them once every row's id is known.
        var idsByName = new Dictionary<string, SkillId>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
            idsByName.TryAdd(t.GetString(r, name), SkillIdOf(t, r, id));
        var records = rows.Select(r => new SkillInfo(
            SkillIdOf(t, r, id), t.GetString(r, name), t.GetString(r, charClass), t.GetString(r, desc),
            Flag(t, r, left), Flag(t, r, right))
        {
            Mana = Optional(t, r, mana),
            ManaPerLevel = Optional(t, r, lvlMana),
            ManaShift = Optional(t, r, shift),
            MinimumMana = Optional(t, r, minMana),
            RequiredLevel = Optional(t, r, reqLevel),
            RequiredSkills = RequiredSkillsOf(t, r, reqSkill, idsByName),
        });
        return new GameTable<SkillId, SkillInfo>(records, s => s.Id);
    }

    // The skills a row's reqskill columns name; the shared empty array when there are none, so records with no
    // requirement stay equal to records built without the property.
    private static IReadOnlyList<SkillId> RequiredSkillsOf(ExcelTable t, int row, int[] columns, Dictionary<string, SkillId> idsByName)
    {
        var ids = columns.Where(c => c >= 0).Select(c => t.GetString(row, c).Trim())
            .Where(n => n.Length > 0 && idsByName.ContainsKey(n)).Select(n => idsByName[n]).ToArray();
        return ids.Length == 0 ? Array.Empty<SkillId>() : ids;
    }

    // The game keeps skill ids in 16 bits: a larger id is malformed for this column.
    private static SkillId SkillIdOf(ExcelTable t, int row, int column)
    {
        var value = t.ParseInt32(row, column);
        if (value is < short.MinValue or > short.MaxValue)
        {
            throw new InvalidDataException(
                $"{t.Name}: row {row}, column '{t.Columns[column]}' has '{t.GetString(row, column)}', which is not a 16-bit skill id.");
        }
        return (SkillId)value;
    }

    private GameTable<string, SkillDescriptionInfo> LoadSkillDescriptions()
    {
        var t = GetRequiredTable("skilldesc");
        int name = t.GetRequiredColumnIndex("skilldesc"), page = t.GetRequiredColumnIndex("SkillPage"), row = t.GetRequiredColumnIndex("SkillRow");
        int column = t.GetRequiredColumnIndex("SkillColumn"), listRow = t.GetRequiredColumnIndex("ListRow"), icon = t.GetRequiredColumnIndex("IconCel");
        var records = GameRows(t, name).Select((r, i) => new SkillDescriptionInfo(
            i, t.GetString(r, name), t.ParseInt32(r, page), t.ParseInt32(r, row), t.ParseInt32(r, column), t.ParseInt32(r, listRow), t.ParseInt32(r, icon)));
        return new GameTable<string, SkillDescriptionInfo>(records, d => d.Name, StringComparer.Ordinal);
    }
}

// Port of d2go pkg/data/npc.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using D2Dotnet.D2Go.Data.Npc;
using D2Dotnet.D2Go.Data.Stat;

namespace D2Dotnet.D2Go.Data;

/// <summary>An NPC preset of the current level and its positions (Go <c>data.NPC</c>).</summary>
public sealed record GameNpc
{
    public NpcId Id { get; init; }

    public string Name { get; init; } = "";

    public IReadOnlyList<Position> Positions { get; init; } = [];
}

/// <summary>NPC presets (Go <c>data.NPCs</c>).</summary>
[CollectionBuilder(typeof(GameNpcs), nameof(Create))]
public sealed class GameNpcs : ReadOnlyCollection<GameNpc>
{
    public GameNpcs()
        : base([])
    {
    }

    public GameNpcs(IEnumerable<GameNpc> npcs)
        : base([.. npcs])
    {
    }

    /// <summary>No NPCs (Go's nil slice).</summary>
    public static new GameNpcs Empty { get; } = new();

    /// <summary>Collection expression builder.</summary>
    public static GameNpcs Create(ReadOnlySpan<GameNpc> npcs) => new(npcs.ToArray());

    /// <summary>
    /// The first NPC with this id (Go <c>FindOne</c>). When there is none, returns false and <paramref name="npc"/>
    /// is the zero value.
    /// </summary>
    public bool TryFindOne(NpcId npcId, out GameNpc npc)
    {
        foreach (var np in this)
        {
            if (np.Id == npcId)
            {
                npc = np;
                return true;
            }
        }

        npc = new GameNpc();
        return false;
    }
}

/// <summary>A monster or other non-player unit (Go <c>data.Monster</c>).</summary>
public sealed record Monster
{
    // IsGoodNpc's ids, as numbers in Go.
    private static readonly FrozenSet<int> GoodNpcIds = new[]
    {
        146, 154, 147, 150, 155, 148, 244, 210, 175, 199, 198, 177, 178, 201, 202, 200, 331, 245, 264, 255,
        176, 252, 254, 253, 297, 246, 251, 367, 521, 257, 405, 265, 520, 512, 527, 515, 513, 511, 514, 266,
        408, 406,
    }.ToFrozenSet();

    /// <summary>The unit id (Go's embedded <c>UnitID</c>).</summary>
    public UnitId UnitId { get; init; }

    /// <summary>The monster class.</summary>
    public NpcId Name { get; init; }

    public bool IsHovered { get; init; }

    public Position Position { get; init; }

    /// <summary>Stat values (Go <c>map[stat.ID]int</c>; a missing stat reads as 0).</summary>
    public IReadOnlyDictionary<StatId, int> Stats { get; init; } = new Dictionary<StatId, int>();

    public MonsterType Type { get; init; }

    /// <summary>
    /// True when one of the stats matching <paramref name="resist"/> is at least 100: cold, fire, lightning, poison or
    /// magic resist for <see cref="Resist.ColdImmune"/>, <see cref="Resist.FireImmune"/>,
    /// <see cref="Resist.LightImmune"/>, <see cref="Resist.PoisonImmune"/> and <see cref="Resist.MagicImmune"/>.
    /// </summary>
    public bool IsImmune(Resist resist)
    {
        foreach (var (st, value) in Stats)
        {
            // We only want max resistance
            if (value < 100)
                continue;
            if (resist == Resist.ColdImmune && st == StatId.ColdResist)
                return true;
            if (resist == Resist.FireImmune && st == StatId.FireResist)
                return true;
            if (resist == Resist.LightImmune && st == StatId.LightningResist)
                return true;
            if (resist == Resist.PoisonImmune && st == StatId.PoisonResist)
                return true;
            if (resist == Resist.MagicImmune && st == StatId.MagicResist)
                return true;
        }

        return false;
    }

    /// <summary>A hireling: act 1 rogue, act 2 guard, act 3 iron wolf or an act 5 barbarian.</summary>
    public bool IsMerc() =>
        Name == NpcId.Guard || Name == NpcId.Act5Hireling1Hand || Name == NpcId.Act5Hireling2Hand || Name == NpcId.IronWolf || Name == NpcId.Rogue2;

    /// <summary>A player summon: druid summons and spirits, necromancer golems and skeletons, valkyrie, shadows.</summary>
    public bool IsPet() => Name switch
    {
        NpcId.DruHawk or NpcId.DruSpiritWolf or NpcId.DruFenris or NpcId.HeartOfWolverine
            or NpcId.OakSage or NpcId.DruBear or NpcId.DruPlaguePoppy or NpcId.VineCreature
            or NpcId.DruCycleOfLife or NpcId.ClayGolem or NpcId.BloodGolem or NpcId.IronGolem
            or NpcId.FireGolem or NpcId.NecroSkeleton or NpcId.NecroMage or NpcId.Valkyrie
            or NpcId.ShadowWarrior or NpcId.ShadowMaster => true,
        _ => false,
    };

    /// <summary>A friendly NPC (town NPCs and the like; d2go lists them by number).</summary>
    public bool IsGoodNpc() => GoodNpcIds.Contains((int)Name);

    /// <summary>A champion, minion, unique or super unique.</summary>
    public bool IsElite() =>
        Type == MonsterType.Minion || Type == MonsterType.Unique || Type == MonsterType.Champion || Type == MonsterType.SuperUnique;

    /// <summary>True if the monster is able to spawn new monsters (the fallen shamans).</summary>
    public bool IsMonsterRaiser() => Name switch
    {
        NpcId.FallenShaman or NpcId.CarverShaman or NpcId.DevilkinShaman or NpcId.DarkShaman or NpcId.WarpedShaman => true,
        _ => false,
    };

    /// <summary>A monster that can not be killed as a normal enemy, for example because it can not be targeted.</summary>
    public bool IsSkip() => Name switch
    {
        NpcId.WaterWatcherLimb or NpcId.WaterWatcherHead or NpcId.BaalTaunt or NpcId.Act5Combatant or NpcId.Act5Combatant2
            or NpcId.BarricadeTower => true,
        _ => false,
    };
}

/// <summary>A filter for <see cref="Monsters.Enemies"/> (Go <c>data.MonsterFilter</c>).</summary>
public delegate Monsters MonsterFilter(Monsters monsters);

/// <summary>The filters of Go's <c>data</c> package.</summary>
public static class MonsterFilters
{
    /// <summary>Keeps the elite monsters (<see cref="Monster.IsElite"/>).</summary>
    public static MonsterFilter MonsterEliteFilter() => monsters => new Monsters(monsters.Where(mo => mo.IsElite()));

    /// <summary>Keeps every monster: returns its argument.</summary>
    public static MonsterFilter MonsterAnyFilter() => monsters => monsters;
}

/// <summary>Monsters (Go <c>data.Monsters</c>).</summary>
[CollectionBuilder(typeof(Monsters), nameof(Create))]
public sealed class Monsters : ReadOnlyCollection<Monster>
{
    public Monsters()
        : base([])
    {
    }

    public Monsters(IEnumerable<Monster> monsters)
        : base([.. monsters])
    {
    }

    /// <summary>No monsters (Go's nil slice).</summary>
    public static new Monsters Empty { get; } = new();

    /// <summary>Collection expression builder.</summary>
    public static Monsters Create(ReadOnlySpan<Monster> monsters) => new(monsters.ToArray());

    /// <summary>
    /// The first monster of class <paramref name="id"/> whose type is <paramref name="type"/>, or of any type when
    /// <paramref name="type"/> is <see cref="MonsterType.None"/> (Go <c>FindOne</c>). When there is none, returns false
    /// and <paramref name="monster"/> is the zero value.
    /// </summary>
    public bool TryFindOne(NpcId id, MonsterType type, out Monster monster)
    {
        foreach (var m in this)
        {
            if (m.Name == id && (type == MonsterType.None || type == m.Type))
            {
                monster = m;
                return true;
            }
        }

        monster = new Monster();
        return false;
    }

    /// <summary>
    /// The living hostile monsters (not mercenaries, skipped monsters, good NPCs or pets, and life above 0), then
    /// passed through <paramref name="filters"/> in order.
    /// </summary>
    public Monsters Enemies(params ReadOnlySpan<MonsterFilter> filters)
    {
        var monsters = new Monsters(this.Where(mo =>
            !mo.IsMerc() && !mo.IsSkip() && !mo.IsGoodNpc() && !mo.IsPet() && mo.Stats.GetValueOrDefault(StatId.Life) > 0));

        foreach (var f in filters)
            monsters = f(monsters);

        return monsters;
    }

    /// <summary>
    /// The monster with this unit id (Go <c>FindByID</c>). When there is none, returns false and
    /// <paramref name="monster"/> is the zero value.
    /// </summary>
    public bool TryFindById(UnitId id, out Monster monster)
    {
        foreach (var m in this)
        {
            if (m.UnitId == id)
            {
                monster = m;
                return true;
            }
        }

        monster = new Monster();
        return false;
    }
}

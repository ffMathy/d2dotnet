using System.Diagnostics;
using System.Text;
using D2Dotnet.Maps;
using D2Dotnet.Maps.Engine;
using Xunit.Abstractions;
using Oracle = LibD2.Drlg;

namespace D2Dotnet.Tests.Maps;

/// <summary>Compares the port (fed by <see cref="LibD2BakedDataSource"/>) against the libd2 native
/// generator (the LibD2 NuGet) for many seeds × all difficulties × all acts: the level list with
/// names, types, origins and sizes, every room, the raw collision grid cell for cell, exits
/// (adjacents), preset units and outdoor shrines. Set D2_ORACLE_SEEDS=N for a larger seed set.
/// </summary>
/// <remarks>
/// The newest LibD2 NuGet (0.1.1) predates the pinned libd2 commit's three preset commits (see
/// <see cref="LegacyPresets"/>), so presets and shrines are compared through the old collector
/// rebuilt over the port's engine state; the pinned commit's own preset behaviour is checked
/// against the 1.14d engine data in <see cref="EnginePresetTests"/>. Every other field is
/// compared directly.
/// </remarks>
public sealed class OracleTests
{
    private readonly ITestOutputHelper _out;

    public OracleTests(ITestOutputHelper output) => _out = output;

    private static readonly uint[] DefaultSeeds = { 0, 1, 2, 1337, 12345, 0x29a, 305419896, 1033089920, 0xDEADBEEF, 0xFFFFFFFF };

    /// <summary>The seed set: the fixed default set, or D2_ORACLE_SEEDS=N seeds (the defaults
    /// followed by a deterministic spread).</summary>
    public static IReadOnlyList<uint> Seeds
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("D2_ORACLE_SEEDS");
            if (string.IsNullOrWhiteSpace(env) || !int.TryParse(env, out int n) || n <= DefaultSeeds.Length) return DefaultSeeds;
            var seeds = new List<uint>(DefaultSeeds);
            uint x = 0x9E3779B9;
            while (seeds.Count < n)
            {
                x = unchecked(x * 1664525 + 1013904223);
                uint s = x ^ (x >> 16);
                if (!seeds.Contains(s)) seeds.Add(s);
            }
            return seeds;
        }
    }

    public static IEnumerable<object[]> ActsAndDifficulties()
    {
        for (int act = 0; act < 5; act++)
            for (int d = 0; d < 3; d++)
                yield return new object[] { act, d };
    }

    [Theory]
    [MemberData(nameof(ActsAndDifficulties))]
    public unsafe void MatchesLibD2(int act, int difficulty)
    {
        var gen = LibD2Fixtures.Generator;
        using var oracle = new Oracle.MapGenerator();
        var diffs = new List<string>();
        var swMine = new Stopwatch();
        var swTheirs = new Stopwatch();
        int compared = 0;
        foreach (uint seed in Seeds)
        {
            var legacy = new Dictionary<int, LegacyLevel>();
            swMine.Start();
            var mine = gen.Generate(seed, (Difficulty)difficulty, (ActId)act, level =>
            {
                var pLevel = (D2DrlgLevelStrc*)level;
                legacy[pLevel->eD2LevelId] = new LegacyLevel(LegacyPresets.Collect(level),
                    MapGenerator.OutdoorShrines(pLevel, act1ShrineFilesOnly: false));
            });
            swMine.Stop();
            swTheirs.Start();
            var theirs = oracle.GenerateAct(seed, (Oracle.Difficulty)difficulty, act, includeCollision: true);
            swTheirs.Stop();
            CompareAct(seed, mine, theirs, legacy, oracle, diffs, swTheirs);
            compared++;
            if (diffs.Count > 200) break;
        }
        _out.WriteLine($"act {act} difficulty {difficulty}: {compared} seeds compared, {diffs.Count} differences " +
                       $"(port {swMine.ElapsedMilliseconds} ms, libd2 {swTheirs.ElapsedMilliseconds} ms)");
        if (diffs.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{diffs.Count} differences vs libd2 (act {act}, difficulty {difficulty}):");
            foreach (var d in diffs.Take(60)) sb.AppendLine("  " + d);
            Assert.Fail(sb.ToString());
        }
    }

    internal sealed record LegacyLevel(List<(int Type, int ClassId, int X, int Y)> Presets, Shrine[] Shrines);

    private static void CompareAct(uint seed, GeneratedAct mine, Oracle.Act theirs, Dictionary<int, LegacyLevel> legacy,
        Oracle.MapGenerator oracle, List<string> diffs, Stopwatch swTheirs)
    {
        var mIds = mine.Levels.Select(l => (int)l.Id).ToArray();
        var tIds = theirs.Levels.Select(l => l.Id).ToArray();
        if (!mIds.SequenceEqual(tIds))
        {
            diffs.Add($"seed {seed} act {(int)mine.Act}: level ids [{string.Join(",", mIds)}] vs [{string.Join(",", tIds)}]");
            return;
        }
        for (int i = 0; i < mine.Levels.Count; i++)
        {
            var m = mine.Levels[i];
            var t = theirs.Levels[i];
            string p = $"seed {seed} act {(int)mine.Act} L{(int)m.Id}";
            // The port's levels carry no names (they are in the game's tables): compare the level's
            // levels.txt name, which is what libd2 reports.
            var name = LibD2Fixtures.LevelName(m.Id);
            if (name != t.Name) diffs.Add($"{p}: name '{name}' vs '{t.Name}'");
            if ((int)m.Layout != (int)t.Type) diffs.Add($"{p}: drlg type {m.Layout} vs {t.Type}");
            if (m.IsPlaced != t.IsPlaced) diffs.Add($"{p}: placed {m.IsPlaced} vs {t.IsPlaced}");
            var b = m.Bounds;
            if ((b.X, b.Y, b.Width, b.Height) != (t.OriginX, t.OriginY, t.Width, t.Height))
                diffs.Add($"{p}: rect ({b.X},{b.Y} {b.Width}x{b.Height}) vs ({t.OriginX},{t.OriginY} {t.Width}x{t.Height})");

            CompareList(p, "rooms",
                m.Rooms.Select(r => (r.Bounds.X, r.Bounds.Y, r.Bounds.Width, r.Bounds.Height, r.Type, r.PresetType, r.PickedFile)).ToList(),
                t.Rooms.Select(r => (r.X, r.Y, r.Width, r.Height, r.Type, r.PresetType, r.PickedFile)).ToList(), diffs);
            CompareList(p, "adjacents",
                m.Exits.Select(e => ((int)e.Destination, e.Position.X, e.Position.Y)).ToList(),
                t.Adjacents.Select(e => (e.DestinationLevelId, e.X, e.Y)).ToList(), diffs);
            CompareCollision(p, m.Collision, t.Collision, diffs);

            var lg = legacy[(int)m.Id];
            CompareList(p, "presets (0.1.1 collector)", lg.Presets,
                t.Presets.Select(u => ((int)u.Kind, u.TxtFileNo, u.X, u.Y)).ToList(), diffs);

            // Outdoor shrines (d2drlg_level_shrines; world subtiles -> level-local). The port's
            // own list is the same roll gated to the Act I shrine files.
            if (m.Layout == LevelLayout.Wilderness)
            {
                swTheirs.Start();
#pragma warning disable CS0618 // GetShrines is the d2drlg_level_shrines entry point compared against
                var ts = oracle.GetShrines(seed, (int)m.Id, (Oracle.Difficulty)(int)mine.Difficulty);
#pragma warning restore CS0618
                swTheirs.Stop();
                CompareList(p, "shrines (0.1.1 roll)",
                    lg.Shrines.Select(s => ((int)s.Object, s.Position.X, s.Position.Y)).ToList(),
                    ts.Select(s => (s.ClassId, s.X - t.OriginX * 5, s.Y - t.OriginY * 5)).ToList(), diffs);
                if (m.Shrines.Count != 0 && !m.Shrines.SequenceEqual(lg.Shrines)) diffs.Add($"{p}: gated shrines are not the 0.1.1 roll");
            }
            else if (m.Shrines.Count != 0 || lg.Shrines.Length != 0)
            {
                diffs.Add($"{p}: shrines on a non-wilderness level");
            }
        }
    }

    private static void CompareList<T>(string p, string what, List<T> m, List<T> t, List<string> diffs) where T : IEquatable<T>
    {
        if (m.Count == t.Count && m.SequenceEqual(t)) return;
        int k = 0;
        while (k < m.Count && k < t.Count && m[k].Equals(t[k])) k++;
        string a = k < m.Count ? m[k].ToString()! : "<end>";
        string b = k < t.Count ? t[k].ToString()! : "<end>";
        var ms = new HashSet<T>(m);
        var ts = new HashSet<T>(t);
        string extra = string.Join(" ", m.Where(x => !ts.Contains(x)).Take(4));
        string missing = string.Join(" ", t.Where(x => !ms.Contains(x)).Take(4));
        diffs.Add($"{p}: {what} differ ({m.Count} vs {t.Count}); first at #{k}: {a} vs {b}; only port: [{extra}] only libd2: [{missing}]");
    }

    private static void CompareCollision(string p, CollisionMap? m, Oracle.CollisionGrid? t, List<string> diffs)
    {
        if (m == null && t == null) return;
        if (m == null || t == null)
        {
            diffs.Add($"{p}: collision {(m == null ? "missing" : $"{m.Width}x{m.Height}")} vs {(t == null ? "missing" : $"{t.Width}x{t.Height}")}");
            return;
        }
        if (m.Width != t.Width || m.Height != t.Height)
        {
            diffs.Add($"{p}: collision {m.Width}x{m.Height} vs {t.Width}x{t.Height}");
            return;
        }
        var tc = t.Cells;
        int bad = 0, first = -1;
        for (int i = 0; i < m.Cells.Length; i++)
        {
            if (m.Cells[i] != tc[i])
            {
                if (first < 0) first = i;
                bad++;
            }
        }
        if (bad > 0)
            diffs.Add($"{p}: {bad} collision cells differ; first at ({first % m.Width},{first / m.Width}): 0x{m.Cells[first]:X4} vs 0x{tc[first]:X4}");
    }
}

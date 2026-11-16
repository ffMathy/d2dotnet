// Port of d2go pkg/data/stat/stats.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace D2Dotnet.D2Go.Data.Stat;

/// <summary>A stat value on a unit, for a layer (Go <c>stat.Data</c>).</summary>
public readonly record struct StatData(StatId Id, int Value, int Layer);

/// <summary>A unit's stats (Go <c>stat.Stats</c>).</summary>
[CollectionBuilder(typeof(Stats), nameof(Create))]
public sealed class Stats : ReadOnlyCollection<StatData>
{
    public Stats()
        : base([])
    {
    }

    public Stats(IEnumerable<StatData> stats)
        : base([.. stats])
    {
    }

    /// <summary>No stats (Go's nil slice).</summary>
    public static new Stats Empty { get; } = new();

    /// <summary>Collection expression builder.</summary>
    public static Stats Create(ReadOnlySpan<StatData> stats) => new(stats.ToArray());

    /// <summary>
    /// The first stat with this id and layer (Go <c>FindStat</c>). When there is none, returns false and
    /// <paramref name="stat"/> is the zero value.
    /// </summary>
    public bool TryFindStat(StatId id, int layer, out StatData stat)
    {
        foreach (var s in this)
        {
            if (s.Id == id && s.Layer == layer)
            {
                stat = s;
                return true;
            }
        }

        stat = default;
        return false;
    }
}

/// <summary>The methods of Go's <c>stat.ID</c>.</summary>
public static class StatIdExtensions
{
    /// <summary>The lower-case stat name (Go <c>ID.String</c>: <c>StringStats[s]</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The id is not an index of <see cref="StatNames.StringStats"/> (Go panics).</exception>
    public static string ToStatString(this StatId id)
    {
        var index = (int)id;
        if (index < 0 || index >= StatNames.StringStats.Count)
            throw new ArgumentOutOfRangeException(nameof(id), id, "No stat name for this id.");
        return StatNames.StringStats[index];
    }
}

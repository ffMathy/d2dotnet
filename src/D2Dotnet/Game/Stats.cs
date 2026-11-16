using System.Collections;

namespace D2Dotnet.Game;

/// <summary>A stat value of a unit or item, for a layer (the layer tells apart, for example, the skill of a +skill stat).</summary>
/// <param name="Id">The stat.</param>
/// <param name="Value">
/// The value: the game's 32-bit field read as a signed number, so a resistance of -10 is -10. Unsigned stats above
/// <see cref="int.MaxValue"/> (experience near level 99) are negative here; <see cref="UnsignedValue"/> gives them back.
/// Life, mana, stamina and a few per-level stats are rescaled to the unit the game shows (see <c>docs/memory.md</c>).
/// </param>
/// <param name="Layer">The layer (16 bits, as the game stores it); 0 for most stats.</param>
public readonly record struct Stat(StatId Id, int Value, ushort Layer = 0)
{
    /// <summary><see cref="Value"/> reinterpreted as unsigned, for the stats the game keeps unsigned (experience, gold).</summary>
    public uint UnsignedValue => unchecked((uint)Value);
}

/// <summary>The stats of a unit or item, in the order the game lists them. Immutable.</summary>
public sealed class StatList : IReadOnlyList<Stat>, IEquatable<StatList>
{
    private readonly Stat[] _stats;

    /// <summary>A list of <paramref name="stats"/>, in order.</summary>
    public StatList(IEnumerable<Stat> stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        _stats = stats.ToArray();
    }

    /// <summary>No stats.</summary>
    public static StatList Empty { get; } = new([]);

    /// <inheritdoc/>
    public int Count => _stats.Length;

    /// <inheritdoc/>
    public Stat this[int index] => _stats[index];

    /// <summary>The first stat with this id and layer.</summary>
    /// <returns>False when there is none.</returns>
    public bool TryGet(StatId id, ushort layer, out Stat stat)
    {
        foreach (var s in _stats)
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

    /// <summary>The value of the first stat with this id and layer, or null when there is none.</summary>
    public int? GetValue(StatId id, ushort layer = 0) => TryGet(id, layer, out var stat) ? stat.Value : null;

    /// <summary>True when a stat has this id and layer.</summary>
    public bool Contains(StatId id, ushort layer = 0) => TryGet(id, layer, out _);

    /// <inheritdoc/>
    public IEnumerator<Stat> GetEnumerator() => ((IEnumerable<Stat>)_stats).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>True when both lists hold the same stats in the same order.</summary>
    public bool Equals(StatList? other) => other is not null && _stats.AsSpan().SequenceEqual(other._stats);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as StatList);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var s in _stats)
            hash.Add(s);
        return hash.ToHashCode();
    }
}

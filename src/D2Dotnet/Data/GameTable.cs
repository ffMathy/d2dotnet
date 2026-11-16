using System.Collections;
using System.Collections.Frozen;

namespace D2Dotnet.Data;

/// <summary>
/// A typed excel table: its records in file order, and a lookup by key. Immutable and thread-safe.
/// </summary>
/// <typeparam name="TKey">The key, such as <see cref="AreaId"/>.</typeparam>
/// <typeparam name="TRecord">The record type.</typeparam>
/// <remarks>When several records have the same key, the lookup finds the first one (the game links by file order).</remarks>
public sealed class GameTable<TKey, TRecord> : IReadOnlyList<TRecord>
    where TKey : notnull
    where TRecord : class
{
    private readonly TRecord[] _records;

    internal GameTable(IEnumerable<TRecord> records, Func<TRecord, TKey?> keyOf, IEqualityComparer<TKey>? comparer = null)
    {
        _records = records.ToArray();
        var byKey = new Dictionary<TKey, TRecord>(comparer);
        foreach (var record in _records)
        {
            if (keyOf(record) is { } key)
                byKey.TryAdd(key, record);
        }

        ByKey = byKey.ToFrozenDictionary(comparer);
    }

    /// <summary>The records by key.</summary>
    public IReadOnlyDictionary<TKey, TRecord> ByKey { get; }

    /// <summary>The number of records.</summary>
    public int Count => _records.Length;

    /// <summary>A record by position (file order).</summary>
    public TRecord this[int index] => _records[index];

    /// <summary>The record with this key, or null.</summary>
    public TRecord? Find(TKey key) => ByKey.GetValueOrDefault(key);

    /// <summary>The record with this key.</summary>
    public bool TryGet(TKey key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TRecord record) => ByKey.TryGetValue(key, out record);

    /// <summary>True when a record has this key.</summary>
    public bool Contains(TKey key) => ByKey.ContainsKey(key);

    /// <inheritdoc/>
    public IEnumerator<TRecord> GetEnumerator() => ((IEnumerable<TRecord>)_records).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

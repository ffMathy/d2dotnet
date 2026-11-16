namespace D2Dotnet.MapServer.Generation;

/// <summary>
/// Two generation "LRU" cache (upstream <c>lru.ts</c>): once <c>maxEntries</c> values are stored the current
/// generation becomes the old generation and a new one is started. Reading a value from the old generation
/// promotes it into the current one. At most <c>2 * maxEntries</c> values are held.
/// </summary>
/// <remarks>Thread safe.</remarks>
public sealed class LruCache<T>(int maxEntries)
    where T : class
{
    private readonly Lock _lock = new();
    private Dictionary<string, T> _values = [];
    private Dictionary<string, T> _valuesOld = [];

    public int MaxEntries { get; } = maxEntries;

    public T? Get(string key)
    {
        lock (_lock)
        {
            if (_values.TryGetValue(key, out var entry)) return entry;
            if (!_valuesOld.Remove(key, out entry)) return null;
            _values[key] = entry;
            return entry;
        }
    }

    public void Set(string key, T value)
    {
        lock (_lock)
        {
            if (_values.Count >= MaxEntries)
            {
                _valuesOld = _values;
                _values = [];
            }
            _values[key] = value;
        }
    }
}

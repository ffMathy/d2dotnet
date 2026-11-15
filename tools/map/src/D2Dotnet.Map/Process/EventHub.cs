using System.Text.Json;

namespace D2Dotnet.Map.Process;

/// <summary>
/// A tiny thread safe replacement for the node <c>EventEmitter</c> used by the map process:
/// handlers registered with <see cref="On"/> run for every event, <see cref="Once"/> handlers run once.
/// Handlers run synchronously on the emitting thread in registration order.
/// </summary>
public sealed class EventHub
{
    private readonly Lock _lock = new();
    private readonly List<(string Name, Action<JsonElement> Handler, bool Once)> _listeners = [];

    public void On(string name, Action<JsonElement> handler)
    {
        lock (_lock) _listeners.Add((name, handler, false));
    }

    public void Once(string name, Action<JsonElement> handler)
    {
        lock (_lock) _listeners.Add((name, handler, true));
    }

    /// <summary>Remove the first registration of <paramref name="handler"/> for <paramref name="name"/>.</summary>
    public void Off(string name, Action<JsonElement> handler)
    {
        lock (_lock)
        {
            var idx = _listeners.FindIndex(l => l.Name == name && l.Handler == handler);
            if (idx >= 0) _listeners.RemoveAt(idx);
        }
    }

    /// <summary>Number of handlers registered for an event.</summary>
    public int ListenerCount(string name)
    {
        lock (_lock) return _listeners.Count(l => l.Name == name);
    }

    public void Emit(string name, JsonElement data = default)
    {
        List<Action<JsonElement>> toRun;
        lock (_lock)
        {
            toRun = [];
            for (var i = 0; i < _listeners.Count; i++)
            {
                var l = _listeners[i];
                if (l.Name != name) continue;
                toRun.Add(l.Handler);
                if (l.Once)
                {
                    _listeners.RemoveAt(i);
                    i--;
                }
            }
        }
        foreach (var handler in toRun) handler(data);
    }
}

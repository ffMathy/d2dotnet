// Port of d2go pkg/data/state/states.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace D2Dotnet.D2Go.Data.State;

/// <summary>A unit's states (Go <c>state.States</c>).</summary>
[CollectionBuilder(typeof(States), nameof(Create))]
public sealed class States : ReadOnlyCollection<StateId>
{
    public States()
        : base([])
    {
    }

    public States(IEnumerable<StateId> states)
        : base([.. states])
    {
    }

    /// <summary>No states (Go's nil slice).</summary>
    public static new States Empty { get; } = new();

    /// <summary>Collection expression builder.</summary>
    public static States Create(ReadOnlySpan<StateId> states) => new(states.ToArray());

    /// <summary>True when <paramref name="state"/> is in the list.</summary>
    public bool HasState(StateId state)
    {
        foreach (var st in this)
        {
            if (st == state)
                return true;
        }

        return false;
    }
}

// Port of d2go pkg/memory/player_unit.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using D2Dotnet.D2Go.Data;
using D2Dotnet.D2Go.Data.Area;
using D2Dotnet.D2Go.Data.Stat;
using D2Dotnet.D2Go.Data.State;

namespace D2Dotnet.D2Go.Memory;

// Inside the namespace, so these win over the library's types of the same name in the enclosing D2Dotnet namespace.
using AreaId = D2Dotnet.D2Go.Data.Area.AreaId;

/// <summary>A player unit as read from the unit table (Go <c>memory.RawPlayerUnit</c>, which embeds <c>data.UnitID</c>).</summary>
public sealed record RawPlayerUnit
{
    /// <summary>The unit id (Go's embedded <c>UnitID</c>).</summary>
    public UnitId UnitId { get; init; }

    /// <summary>The unit's address in the game process.</summary>
    public nuint Address { get; init; }

    public string Name { get; init; } = "";

    public bool IsMainPlayer { get; init; }

    public bool IsCorpse { get; init; }

    public AreaId Area { get; init; }

    public Position Position { get; init; }

    public bool IsHovered { get; init; }

    public States States { get; init; } = States.Empty;

    public Stats Stats { get; init; } = Stats.Empty;

    public Stats BaseStats { get; init; } = Stats.Empty;
}

/// <summary>Player units (Go <c>memory.RawPlayerUnits</c>).</summary>
[CollectionBuilder(typeof(RawPlayerUnits), nameof(Create))]
public sealed class RawPlayerUnits : ReadOnlyCollection<RawPlayerUnit>
{
    public RawPlayerUnits()
        : base([])
    {
    }

    public RawPlayerUnits(IEnumerable<RawPlayerUnit> units)
        : base([.. units])
    {
    }

    /// <summary>No units.</summary>
    public static new RawPlayerUnits Empty { get; } = new();

    /// <summary>Collection expression builder.</summary>
    public static RawPlayerUnits Create(ReadOnlySpan<RawPlayerUnit> units) => new(units.ToArray());

    /// <summary>The first unit that <see cref="RawPlayerUnit.IsMainPlayer"/>, or the zero value.</summary>
    public RawPlayerUnit GetMainPlayer()
    {
        foreach (var p in this)
        {
            if (p.IsMainPlayer)
                return p;
        }

        return new RawPlayerUnit();
    }

    /// <summary>The first unit that <see cref="RawPlayerUnit.IsCorpse"/>, or the zero value.</summary>
    public RawPlayerUnit GetCorpse()
    {
        foreach (var p in this)
        {
            if (p.IsCorpse)
                return p;
        }

        return new RawPlayerUnit();
    }
}

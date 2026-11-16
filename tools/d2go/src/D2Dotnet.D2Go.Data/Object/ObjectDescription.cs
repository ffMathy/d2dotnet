// Port of d2go pkg/data/object/description.go, interact_type.go and ids.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

namespace D2Dotnet.D2Go.Data.Object;

/// <summary>
/// An object class: a row of objects.txt (Go <c>object.Description</c>). Instances come from
/// <see cref="GameTables.ObjectDescriptions"/>; the default instance is Go's zero value.
/// </summary>
public sealed record ObjectDescription
{
    /// <summary>The <c>*ID</c> column.</summary>
    public int Id { get; init; }

    /// <summary>The <c>Name</c> column.</summary>
    public string Name { get; init; } = "";

    /// <summary>The <c>SizeX</c> column.</summary>
    public int SizeX { get; init; }

    /// <summary>The <c>SizeY</c> column.</summary>
    public int SizeY { get; init; }

    /// <summary>The <c>HasCollision0</c> column is <c>1</c>.</summary>
    public bool HasCollision { get; init; }
}

/// <summary>How an object can be interacted with (Go <c>object.InteractType</c>).</summary>
public enum InteractType : uint
{
    None = 0x00,
    Trap = 0x04,
    Locked = 0x80,
}

/// <summary>The methods of Go's <c>object.Name</c>.</summary>
public static class ObjectNameExtensions
{
    /// <summary>The objects.txt row of this object (Go <c>Desc[int(n)]</c>: the zero value when missing).</summary>
    public static ObjectDescription Desc(this ObjectName name, GameTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return tables.ObjectDesc((int)name);
    }
}

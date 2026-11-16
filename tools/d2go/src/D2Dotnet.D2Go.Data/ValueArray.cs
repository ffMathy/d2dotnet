using System.Collections;

namespace D2Dotnet.D2Go.Data;

/// <summary>
/// An immutable copy of a Go array (<c>[N]T</c>) that, like the Go array, compares by value: equal when its elements
/// are equal in order. Records that hold one through an <see cref="IReadOnlyList{T}"/> property keep Go's <c>==</c>.
/// </summary>
internal sealed class ValueArray<T> : IReadOnlyList<T>, IEquatable<ValueArray<T>>
{
    private readonly T[] items;

    public ValueArray(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        this.items = [.. items];
    }

    public int Count => items.Length;

    public T this[int index] => items[index];

    public bool Equals(ValueArray<T>? other) =>
        other is not null && (ReferenceEquals(this, other) || items.AsSpan().SequenceEqual(other.items, EqualityComparer<T>.Default));

    public override bool Equals(object? obj) => Equals(obj as ValueArray<T>);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var item in items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => "[" + string.Join(", ", items) + "]";
}

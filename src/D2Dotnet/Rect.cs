namespace D2Dotnet;

/// <summary>An axis-aligned rectangle: its top-left corner and its size. <see cref="Right"/> and <see cref="Bottom"/> are exclusive.</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width; not negative.</param>
/// <param name="Height">The height; not negative.</param>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    /// <summary>A rectangle from its top-left corner and its size.</summary>
    public Rect(Point location, int width, int height)
        : this(location.X, location.Y, width, height)
    {
    }

    /// <summary>The left edge (inclusive), same as <see cref="X"/>.</summary>
    public int Left => X;

    /// <summary>The top edge (inclusive), same as <see cref="Y"/>.</summary>
    public int Top => Y;

    /// <summary>The right edge (exclusive): <see cref="X"/> + <see cref="Width"/>.</summary>
    public int Right => X + Width;

    /// <summary>The bottom edge (exclusive): <see cref="Y"/> + <see cref="Height"/>.</summary>
    public int Bottom => Y + Height;

    /// <summary>The top-left corner.</summary>
    public Point Location => new(X, Y);

    /// <summary>True when the rectangle covers no point.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>A rectangle from its edges; <paramref name="right"/> and <paramref name="bottom"/> are exclusive.</summary>
    public static Rect FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    /// <summary>True when <paramref name="point"/> is inside (left/top inclusive, right/bottom exclusive).</summary>
    public bool Contains(Point point) => Contains(point.X, point.Y);

    /// <summary>True when (<paramref name="x"/>, <paramref name="y"/>) is inside (left/top inclusive, right/bottom exclusive).</summary>
    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    /// <summary>True when the two rectangles share at least one point.</summary>
    public bool IntersectsWith(Rect other) =>
        !IsEmpty && !other.IsEmpty && other.X < Right && X < other.Right && other.Y < Bottom && Y < other.Bottom;

    /// <summary>The smallest rectangle that contains both; an empty rectangle does not count.</summary>
    public Rect Union(Rect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return FromEdges(Math.Min(X, other.X), Math.Min(Y, other.Y), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }
}

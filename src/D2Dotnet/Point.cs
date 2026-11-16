namespace D2Dotnet;

/// <summary>A position: an x/y pair in whatever unit the API that returns it documents (tiles or subtiles, world or level-local).</summary>
/// <param name="X">The x coordinate.</param>
/// <param name="Y">The y coordinate.</param>
public readonly record struct Point(int X, int Y)
{
    /// <summary>The origin, (0, 0).</summary>
    public static Point Zero => default;

    /// <summary>Component-wise sum.</summary>
    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);

    /// <summary>Component-wise difference.</summary>
    public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>The squared euclidean distance to <paramref name="other"/> (exact, no square root).</summary>
    public long DistanceSquaredTo(Point other)
    {
        long dx = (long)X - other.X;
        long dy = (long)Y - other.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>The euclidean distance to <paramref name="other"/>.</summary>
    public double DistanceTo(Point other) => Math.Sqrt(DistanceSquaredTo(other));
}

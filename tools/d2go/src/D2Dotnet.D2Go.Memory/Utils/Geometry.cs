// Port of d2go pkg/utils/utils.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using D2Dotnet.D2Go.Data;

namespace D2Dotnet.D2Go.Memory;

/// <summary>Distance helpers (Go <c>utils.DistanceFromPoint</c>).</summary>
public static class Geometry
{
    /// <summary>The euclidean distance between two points, truncated (Go <c>DistanceFromPoint</c>).</summary>
    public static int DistanceFromPoint(Position from, Position to)
    {
        // Go's int is 64-bit: subtract in long. Squares are computed as x*x, which is what Go's math.Pow(x, 2) returns.
        var dx = (double)((long)to.X - from.X);
        var dy = (double)((long)to.Y - from.Y);
        var first = dx * dx;
        var second = dy * dy;

        return GoInt.Narrow((long)Math.Sqrt(first + second));
    }
}

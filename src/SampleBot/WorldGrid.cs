using D2Dotnet;
using D2Dotnet.Maps;

/// <summary>
/// The walkable subtiles of a few levels in world subtiles, and paths across them (A*, eight directions, no cutting of
/// corners).
/// </summary>
internal sealed class WorldGrid
{
    private readonly (MapLevel Level, Point Origin)[] _levels;
    private readonly Rect _bounds;

    /// <param name="levels">Each level with its top-left corner in world subtiles.</param>
    public WorldGrid(params (MapLevel Level, Point Origin)[] levels)
    {
        _levels = levels;
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var (level, origin) in levels)
        {
            left = Math.Min(left, origin.X);
            top = Math.Min(top, origin.Y);
            right = Math.Max(right, origin.X + level.Collision!.Width);
            bottom = Math.Max(bottom, origin.Y + level.Collision.Height);
        }

        _bounds = new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>The top-left corner of a placed level, in world subtiles.</summary>
    public static Point OriginOf(MapLevel level) =>
        new(level.Bounds.X * MapLevel.SubtilesPerTile, level.Bounds.Y * MapLevel.SubtilesPerTile);

    public bool IsWalkable(Point p)
    {
        foreach (var (level, origin) in _levels)
        {
            var local = new Point(p.X - origin.X, p.Y - origin.Y);
            if (level.Collision!.Contains(local.X, local.Y) && level.Collision.IsCovered(local.X, local.Y))
                return level.Collision.IsWalkable(local);
        }

        return false;
    }

    /// <summary>The walkable subtile nearest to <paramref name="p"/> within <paramref name="radius"/>, or null.</summary>
    public Point? NearestWalkable(Point p, int radius = 30)
    {
        for (var r = 0; r <= radius; r++)
        {
            Point? best = null;
            var bestDistance = int.MaxValue;
            for (var dy = -r; dy <= r; dy++)
            for (var dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    continue;
                var q = new Point(p.X + dx, p.Y + dy);
                var distance = dx * dx + dy * dy;
                if (distance < bestDistance && IsWalkable(q))
                    (best, bestDistance) = (q, distance);
            }

            if (best is not null)
                return best;
        }

        return null;
    }

    /// <summary>A path from <paramref name="from"/> to <paramref name="to"/> (both moved to the nearest walkable subtile), or null.</summary>
    public List<Point>? FindPath(Point from, Point to)
    {
        if (NearestWalkable(from) is not { } start || NearestWalkable(to) is not { } goal)
            return null;

        int width = _bounds.Width, height = _bounds.Height;
        int Index(Point p) => (p.Y - _bounds.Y) * width + (p.X - _bounds.X);
        bool Inside(Point p) => p.X >= _bounds.X && p.Y >= _bounds.Y && p.X < _bounds.X + width && p.Y < _bounds.Y + height;
        if (!Inside(start) || !Inside(goal))
            return null;

        var cost = new float[width * height];
        Array.Fill(cost, float.PositiveInfinity);
        var parent = new int[width * height];
        var open = new PriorityQueue<Point, float>();
        cost[Index(start)] = 0;
        open.Enqueue(start, Heuristic(start, goal));

        ReadOnlySpan<(int Dx, int Dy, float Cost)> steps =
        [
            (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1),
            (1, 1, 1.414f), (1, -1, 1.414f), (-1, 1, 1.414f), (-1, -1, 1.414f),
        ];

        while (open.TryDequeue(out var current, out _))
        {
            if (current == goal)
                return Unwind(goal, start, parent, Index, width);

            var currentCost = cost[Index(current)];
            foreach (var (dx, dy, stepCost) in steps)
            {
                var next = new Point(current.X + dx, current.Y + dy);
                if (!Inside(next) || !IsWalkable(next))
                    continue;
                // No cutting through a corner: both orthogonal neighbours of a diagonal step must be walkable.
                if (dx != 0 && dy != 0 && (!IsWalkable(new Point(current.X + dx, current.Y)) || !IsWalkable(new Point(current.X, current.Y + dy))))
                    continue;

                var nextCost = currentCost + stepCost;
                var i = Index(next);
                if (nextCost >= cost[i])
                    continue;
                cost[i] = nextCost;
                parent[i] = Index(current);
                open.Enqueue(next, nextCost + Heuristic(next, goal));
            }
        }

        return null;
    }

    private static float Heuristic(Point a, Point b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return Math.Max(dx, dy) + 0.414f * Math.Min(dx, dy);
    }

    private List<Point> Unwind(Point goal, Point start, int[] parent, Func<Point, int> index, int width)
    {
        var path = new List<Point> { goal };
        var i = index(goal);
        var startIndex = index(start);
        while (i != startIndex)
        {
            i = parent[i];
            path.Add(new Point(_bounds.X + i % width, _bounds.Y + i / width));
        }

        path.Reverse();
        return path;
    }
}

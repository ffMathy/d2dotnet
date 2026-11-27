namespace D2Dotnet.Maps;

/// <summary>
/// The walkable subtiles of one or more levels, in world subtiles (the positions a running game reports), and paths
/// across them: levels that touch, such as a town and the wilderness around it, make one grid.
/// </summary>
/// <remarks>
/// A subtile is walkable when the first level whose collision map covers it says so (<see cref="CollisionMap.IsWalkable(Point)"/>).
/// Paths are A* over the eight neighbours of a subtile, diagonal steps costing √2, and never cut a corner: a diagonal
/// step needs both subtiles beside it to be walkable.
/// </remarks>
public sealed class MapGrid
{
    private static readonly (int Dx, int Dy, float Cost)[] Steps =
    [
        (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1),
        (1, 1, 1.4142135f), (1, -1, 1.4142135f), (-1, 1, 1.4142135f), (-1, -1, 1.4142135f),
    ];

    private readonly MapLevel[] _levels;

    /// <summary>A grid over <paramref name="levels"/>, each at its <see cref="MapLevel.Origin"/>.</summary>
    /// <exception cref="ArgumentException">No level is given, or a level has no collision map.</exception>
    public MapGrid(params IEnumerable<MapLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        _levels = [.. levels];
        if (_levels.Length == 0)
            throw new ArgumentException("A grid needs at least one level.", nameof(levels));
        if (_levels.FirstOrDefault(l => l.Collision is null) is { } empty)
            throw new ArgumentException($"{empty.Id} has no collision map.", nameof(levels));

        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var level in _levels)
        {
            left = Math.Min(left, level.Origin.X);
            top = Math.Min(top, level.Origin.Y);
            right = Math.Max(right, level.Origin.X + level.Collision!.Width);
            bottom = Math.Max(bottom, level.Origin.Y + level.Collision.Height);
        }

        Bounds = new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>The levels of the grid.</summary>
    public IReadOnlyList<MapLevel> Levels => _levels;

    /// <summary>The smallest rectangle around every level, in world subtiles.</summary>
    public Rect Bounds { get; }

    /// <summary>The level whose collision map covers <paramref name="world"/>, or null.</summary>
    public MapLevel? LevelAt(Point world)
    {
        foreach (var level in _levels)
        {
            var local = level.ToLocal(world);
            if (level.Collision!.IsCovered(local.X, local.Y))
                return level;
        }

        return null;
    }

    /// <summary>True when <paramref name="world"/> is walkable.</summary>
    public bool IsWalkable(Point world) => LevelAt(world) is { } level && level.Collision!.IsWalkable(level.ToLocal(world));

    /// <summary>
    /// The walkable subtile nearest to <paramref name="world"/>, searching outwards up to <paramref name="radius"/>
    /// subtiles (Chebyshev distance); null when there is none.
    /// </summary>
    public Point? NearestWalkable(Point world, int radius = 30)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        for (var r = 0; r <= radius; r++)
        {
            Point? best = null;
            var bestDistance = int.MaxValue;
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                        continue;
                    var p = new Point(world.X + dx, world.Y + dy);
                    var distance = dx * dx + dy * dy;
                    if (distance < bestDistance && IsWalkable(p))
                        (best, bestDistance) = (p, distance);
                }
            }

            if (best is not null)
                return best;
        }

        return null;
    }

    /// <summary>
    /// The shortest path from <paramref name="from"/> to <paramref name="to"/>, both first moved to the nearest walkable
    /// subtile (see <see cref="NearestWalkable"/>): every subtile on the way, the two ends included. Null when either end
    /// has no walkable subtile near it or no path joins them.
    /// </summary>
    public IReadOnlyList<Point>? FindPath(Point from, Point to)
    {
        if (NearestWalkable(from) is not { } start || NearestWalkable(to) is not { } goal)
            return null;

        int width = Bounds.Width, height = Bounds.Height;
        var cost = new float[width * height];
        Array.Fill(cost, float.PositiveInfinity);
        var parent = new int[width * height];
        var open = new PriorityQueue<Point, float>();
        cost[Index(start)] = 0;
        open.Enqueue(start, Heuristic(start, goal));

        while (open.TryDequeue(out var current, out _))
        {
            if (current == goal)
                return Unwind(parent, Index(start), Index(goal));

            var currentCost = cost[Index(current)];
            foreach (var (dx, dy, stepCost) in Steps)
            {
                var next = new Point(current.X + dx, current.Y + dy);
                if (!Inside(next) || !IsWalkable(next))
                    continue;
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

        int Index(Point p) => (p.Y - Bounds.Y) * width + (p.X - Bounds.X);
        bool Inside(Point p) => p.X >= Bounds.X && p.Y >= Bounds.Y && p.X < Bounds.X + width && p.Y < Bounds.Y + height;
    }

    // Octile distance: exact for eight-way steps on an empty grid, so A* stays optimal.
    private static float Heuristic(Point a, Point b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return Math.Max(dx, dy) + 0.41421356f * Math.Min(dx, dy);
    }

    private List<Point> Unwind(int[] parent, int start, int goal)
    {
        var path = new List<Point>();
        for (var i = goal; ; i = parent[i])
        {
            path.Add(new Point(Bounds.X + i % Bounds.Width, Bounds.Y + i / Bounds.Width));
            if (i == start)
                break;
        }

        path.Reverse();
        return path;
    }
}

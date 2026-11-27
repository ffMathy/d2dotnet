namespace D2Dotnet.Maps;

/// <summary>
/// The walkable subtiles of one or more levels, in world subtiles (the positions a running game reports), and paths
/// across them: levels that touch, such as a town and the wilderness around it, make one grid.
/// </summary>
/// <remarks>
/// A subtile is walkable when the first level whose collision map covers it says so (<see cref="CollisionMap.IsWalkable(Point)"/>).
/// Paths are A* over the eight neighbours of a subtile, diagonal steps costing √2, and never cut a corner: a diagonal
/// step needs both subtiles beside it to be walkable. <see cref="Block"/> adds obstacles the collision map does not
/// hold; a grid with blocks is not thread-safe.
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

    /// <summary>True when <paramref name="world"/> is walkable: the collision map says so and it is not <see cref="Block"/>ed.</summary>
    public bool IsWalkable(Point world) =>
        !_blocked.Contains(world) && LevelAt(world) is { } level && level.Collision!.IsWalkable(level.ToLocal(world));

    private readonly HashSet<Point> _blocked = [];

    /// <summary>The subtiles <see cref="Block"/> made unwalkable.</summary>
    public IReadOnlyCollection<Point> Blocked => _blocked;

    /// <summary>
    /// Makes the subtiles within <paramref name="radius"/> of <paramref name="world"/> (Chebyshev distance) unwalkable,
    /// for something the collision map does not hold, such as a unit or an object in the way; paths go round them.
    /// </summary>
    public void Block(Point world, int radius = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
                _blocked.Add(new Point(world.X + dx, world.Y + dy));
        }
    }

    /// <summary>Makes every <see cref="Block"/>ed subtile walkable again.</summary>
    public void ClearBlocks() => _blocked.Clear();

    /// <summary>
    /// True when nothing <paramref name="blocking"/> lies on the line from <paramref name="from"/> to
    /// <paramref name="to"/> (world subtiles): a missile cast from one reaches the other. The line is traced cell by cell
    /// as the engine traces it (Bresenham, along the major axis); the two end cells are not tested, so a unit standing
    /// against a wall can still be hit. A cell no level covers blocks.
    /// </summary>
    /// <param name="from">One end, such as the caster.</param>
    /// <param name="to">The other end, such as the target.</param>
    /// <param name="blocking">The collision flags that stop the line; <see cref="CollisionMap.MissileFlight"/> for a missile.</param>
    public bool IsLineClear(Point from, Point to, ushort blocking = CollisionMap.MissileFlight)
    {
        int dx = Math.Abs(to.X - from.X), dy = Math.Abs(to.Y - from.Y);
        int stepX = to.X >= from.X ? 1 : -1, stepY = to.Y >= from.Y ? 1 : -1;
        int x = from.X, y = from.Y, error = 0;
        var major = Math.Max(dx, dy);
        for (var i = 1; i < major; i++)
        {
            // Advance along the major axis; the minor one follows the error term.
            if (dx >= dy)
            {
                x += stepX;
                error += dy;
                if (2 * error >= dx)
                {
                    y += stepY;
                    error -= dx;
                }
            }
            else
            {
                y += stepY;
                error += dx;
                if (2 * error >= dy)
                {
                    x += stepX;
                    error -= dy;
                }
            }

            if (IsBlocked(new Point(x, y), blocking))
                return false;
        }

        return true;
    }

    // True when a cell holds one of the flags, or no level covers it.
    private bool IsBlocked(Point world, ushort flags)
    {
        if (LevelAt(world) is not { } level)
            return true;
        var local = level.ToLocal(world);
        return (level.Collision![local.X, local.Y] & flags) != 0;
    }

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

    /// <summary>
    /// The shortest path from <paramref name="from"/> (moved to the nearest walkable subtile) to the nearest walkable
    /// subtile <paramref name="isGoal"/> accepts, such as the first subtiles of the next level: every subtile on the way,
    /// both ends included. Null when no goal can be reached. The same steps as <see cref="FindPath"/>; no heuristic, so
    /// it searches outwards evenly.
    /// </summary>
    public IReadOnlyList<Point>? FindPathTo(Point from, Func<Point, bool> isGoal)
    {
        ArgumentNullException.ThrowIfNull(isGoal);
        if (NearestWalkable(from) is not { } start)
            return null;

        int width = Bounds.Width, height = Bounds.Height;
        var cost = new float[width * height];
        Array.Fill(cost, float.PositiveInfinity);
        var parent = new int[width * height];
        var open = new PriorityQueue<Point, float>();
        cost[Index(start)] = 0;
        open.Enqueue(start, 0);

        while (open.TryDequeue(out var current, out var currentCost))
        {
            if (currentCost > cost[Index(current)])
                continue; // a stale entry
            if (isGoal(current))
                return Unwind(parent, Index(start), Index(current));

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
                open.Enqueue(next, nextCost);
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

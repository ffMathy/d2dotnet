namespace D2Dotnet.Maps;

/// <summary>
/// A level's collision map: one cell per subtile, holding the game's raw collision flags (the engine's per-room
/// collision maps put together over the level). Cells no room covers hold <see cref="Uncovered"/>.
/// </summary>
/// <remarks>
/// Each covered cell is the game's <c>COLLIDE_*</c> flag word: <see cref="BlockPlayer"/> (0x01) blocks walking, and the
/// other bits block line of sight, missiles and so on. <see cref="IsWalkable(int, int)"/> is the common reading; read
/// the raw flags for anything finer. Coordinates are level-local subtiles. The map is immutable.
/// </remarks>
public sealed class CollisionMap
{
    /// <summary>The value of a subtile no room covers.</summary>
    public const ushort Uncovered = 0xFFFF;

    /// <summary>The flag that blocks walking (<c>COLLIDE_BLOCK_PLAYER</c>; the engine's <c>COLBIT_WALL</c>).</summary>
    public const ushort BlockPlayer = 0x01;

    /// <summary>The flag that blocks line of sight and light (<c>COLBIT_VISIBLE</c>).</summary>
    public const ushort BlockSight = 0x02;

    /// <summary>The flag that blocks missiles but not walking (<c>COLBIT_MISSILE_BARRIER</c>).</summary>
    public const ushort MissileBarrier = 0x04;

    /// <summary>
    /// What stops a missile in flight: <see cref="BlockPlayer"/> and <see cref="MissileBarrier"/> (the engine's
    /// <c>SKILL_CheckMissileCollisionAtTarget</c> tests a missile's cell with this mask, as libd2 documents it).
    /// </summary>
    public const ushort MissileFlight = BlockPlayer | MissileBarrier;

    private readonly ushort[] _cells;

    /// <summary>A map over row-major cells.</summary>
    /// <param name="width">The width, in subtiles.</param>
    /// <param name="height">The height, in subtiles.</param>
    /// <param name="cells">The cells, row by row (<paramref name="width"/> × <paramref name="height"/>); the map keeps
    /// this array, so do not change it afterwards.</param>
    /// <exception cref="ArgumentException"><paramref name="cells"/> does not hold width × height cells.</exception>
    public CollisionMap(int width, int height, ushort[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (cells.Length != (long)width * height)
            throw new ArgumentException("The map needs width × height cells.", nameof(cells));
        Width = width;
        Height = height;
        _cells = cells;
    }

    /// <summary>The width, in subtiles.</summary>
    public int Width { get; }

    /// <summary>The height, in subtiles.</summary>
    public int Height { get; }

    /// <summary>The cells, row by row.</summary>
    public ReadOnlySpan<ushort> Cells => _cells;

    /// <summary>The raw flags at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The subtile is outside the map.</exception>
    public ushort this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width)
                throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException(nameof(y));
            return _cells[y * Width + x];
        }
    }

    /// <summary>The raw flags at <paramref name="subtile"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The subtile is outside the map.</exception>
    public ushort this[Point subtile] => this[subtile.X, subtile.Y];

    /// <summary>True when (<paramref name="x"/>, <paramref name="y"/>) is inside the map.</summary>
    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    /// <summary>True when the subtile is inside the map and a room covers it.</summary>
    public bool IsCovered(int x, int y) => Contains(x, y) && _cells[y * Width + x] != Uncovered;

    /// <summary>True when the subtile is inside the map, covered, and <see cref="BlockPlayer"/> is clear.</summary>
    public bool IsWalkable(int x, int y)
    {
        if (!Contains(x, y))
            return false;
        var cell = _cells[y * Width + x];
        return cell != Uncovered && (cell & BlockPlayer) == 0;
    }

    /// <summary>True when <paramref name="subtile"/> is inside the map, covered, and <see cref="BlockPlayer"/> is clear.</summary>
    public bool IsWalkable(Point subtile) => IsWalkable(subtile.X, subtile.Y);

    /// <inheritdoc/>
    public override string ToString() => $"{Width}x{Height} subtiles";
}

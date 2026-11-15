namespace D2Dotnet.Map.Generator;

/// <summary>
/// Port of upstream map/map.c: a fixed 1500x1500 collision grid indexed <c>x * 1500 + y</c>
/// that tracks the largest x/y that received an even (walkable) value.
/// </summary>
public sealed class MapGrid
{
    public const int MapSize = 1500;
    public const int MapArraySize = 1500 * 1500;

    public const int MapDataInvalid = -1;       // Invalid
    public const int MapDataCleaned = 11110;    // Cleaned for start/end positions
    public const int MapDataFilled = 11111;     // Filled gaps
    public const int MapDataThickened = 11113;  // Wall thickened
    public const int MapDataAvoid = 11115;      // Should be avoided

    private readonly int[] _map = new int[MapArraySize];

    /// <summary><c>MAX_SIZE_X</c>.</summary>
    public int MaxX { get; private set; }

    /// <summary><c>MAX_SIZE_Y</c>.</summary>
    public int MaxY { get; private set; }

    /// <summary><c>map_offset</c>.</summary>
    public static int Offset(int x, int y) => x * MapSize + y;

    /// <summary>
    /// <c>map_value</c>. The C code does not bounds-check (out of range is undefined behaviour);
    /// here an out-of-range index throws <see cref="IndexOutOfRangeException"/>.
    /// </summary>
    public int Value(int x, int y) => _map[Offset(x, y)];

    /// <summary><c>map_set</c>.</summary>
    public void Set(int x, int y, int value)
    {
        if (value % 2 == 0)
        {
            if (x > MaxX) MaxX = x;
            if (y > MaxY) MaxY = y;
        }
        _map[Offset(x, y)] = value;
    }

    /// <summary><c>map_reset</c>.</summary>
    public void Reset()
    {
        MaxX = 0;
        MaxY = 0;
        Array.Fill(_map, MapDataInvalid);
    }
}

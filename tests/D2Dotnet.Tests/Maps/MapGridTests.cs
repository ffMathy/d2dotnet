using D2Dotnet.Maps;

namespace D2Dotnet.Tests.Maps;

/// <summary>World coordinates of levels, and paths across them, on hand-made levels.</summary>
public sealed class MapGridTests
{
    // A level whose top-left tile is (tileX, tileY), its collision map drawn row by row: '.' walkable, '#' blocked,
    // ' ' uncovered.
    private static MapLevel Level(AreaId id, int tileX, int tileY, params string[] rows)
    {
        var width = rows[0].Length;
        var cells = rows.SelectMany(r => r.Select(c => c switch
        {
            '.' => (ushort)0,
            '#' => CollisionMap.BlockPlayer,
            _ => CollisionMap.Uncovered,
        })).ToArray();
        return new MapLevel
        {
            Id = id,
            Bounds = new Rect(tileX, tileY, width / MapLevel.SubtilesPerTile, rows.Length / MapLevel.SubtilesPerTile),
            Collision = new CollisionMap(width, rows.Length, cells),
        };
    }

    // Subtiles 50..54 by 100..104.
    private static readonly MapLevel Town = Level(AreaId.RogueEncampment, 10, 20,
        ".....",
        ".###.",
        ".#...",
        ".#.#.",
        "...#.");

    // Right of the town: subtiles 55..64 by 100..104.
    private static readonly MapLevel Moor = Level(AreaId.BloodMoor, 11, 20,
        "..........",
        "########..",
        "..........",
        "..........",
        "    ......");

    [Fact]
    public void Origin_AndConversions_AreInSubtiles()
    {
        Assert.Equal(new Point(50, 100), Town.Origin);
        Assert.Equal(new Point(52, 103), Town.ToWorld(new Point(2, 3)));
        Assert.Equal(new Point(2, 3), Town.ToLocal(new Point(52, 103)));
    }

    [Fact]
    public void IsWalkable_AndLevelAt_LookAcrossLevels()
    {
        var grid = new MapGrid(Town, Moor);
        Assert.Equal(new Rect(50, 100, 15, 5), grid.Bounds);
        Assert.True(grid.IsWalkable(new Point(50, 100)));
        Assert.False(grid.IsWalkable(new Point(51, 101))); // a town wall
        Assert.True(grid.IsWalkable(new Point(60, 100)));  // the moor
        Assert.False(grid.IsWalkable(new Point(55, 104))); // uncovered in the moor
        Assert.False(grid.IsWalkable(new Point(49, 100))); // outside every level
        Assert.Same(Moor, grid.LevelAt(new Point(62, 102)));
        Assert.Null(grid.LevelAt(new Point(55, 104)));
    }

    [Fact]
    public void FindPath_CrossesLevels_AroundWalls_WithoutCuttingCorners()
    {
        var grid = new MapGrid(Town, Moor);
        var path = grid.FindPath(new Point(52, 103), new Point(60, 100))!;

        Assert.Equal(new Point(52, 103), path[0]);
        Assert.Equal(new Point(60, 100), path[^1]);
        Assert.All(path, p => Assert.True(grid.IsWalkable(p), $"{p} is not walkable"));
        for (var i = 1; i < path.Count; i++)
        {
            var (a, b) = (path[i - 1], path[i]);
            Assert.True(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) == 1, $"{a} -> {b} is not one step");
            if (a.X != b.X && a.Y != b.Y)
                Assert.True(grid.IsWalkable(new Point(b.X, a.Y)) && grid.IsWalkable(new Point(a.X, b.Y)), $"{a} -> {b} cuts a corner");
        }

        // The short way climbs the town's right column and follows the top row into the moor, rather than going round
        // the moor's wall on its far side: (52,103) (52,102) (53,102) (54,102) (54,101) (54,100) ... (60,100).
        Assert.Equal(12, path.Count);
        Assert.Contains(new Point(54, 101), path);
        Assert.Contains(new Point(55, 100), path);
    }

    [Fact]
    public void FindPath_MovesBlockedEnds_AndIsNullWhenNothingJoins()
    {
        var grid = new MapGrid(Town, Moor);
        var path = grid.FindPath(new Point(52, 101), new Point(50, 104))!; // starts in a wall
        Assert.Equal(grid.NearestWalkable(new Point(52, 101)), path[0]);
        Assert.NotEqual(new Point(52, 101), path[0]);

        var island = new MapGrid(Level(AreaId.DenOfEvil, 0, 0,
            ".#...",
            "##...",
            ".....",
            ".....",
            "....."));
        Assert.Null(island.FindPath(new Point(0, 0), new Point(4, 4)));
        Assert.Null(island.NearestWalkable(new Point(100, 100), radius: 3));
    }

    [Fact]
    public void Constructor_RejectsNoLevels_AndLevelsWithoutCollision()
    {
        Assert.Throws<ArgumentException>(() => new MapGrid());
        Assert.Throws<ArgumentException>(() => new MapGrid(new MapLevel { Id = AreaId.BloodMoor }));
    }
}

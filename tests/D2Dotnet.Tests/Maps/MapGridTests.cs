using D2Dotnet.Maps;

namespace D2Dotnet.Tests.Maps;

/// <summary>World coordinates of levels, and paths across them, on hand-made levels.</summary>
public sealed class MapGridTests
{
    // A level whose top-left tile is (tileX, tileY), its collision map drawn row by row: '.' walkable, '#' blocked,
    // 'm' a missile barrier (walkable), ' ' uncovered.
    private static MapLevel Level(AreaId id, int tileX, int tileY, params string[] rows)
    {
        var width = rows[0].Length;
        var cells = rows.SelectMany(r => r.Select(c => c switch
        {
            '.' => (ushort)0,
            '#' => CollisionMap.BlockPlayer,
            'm' => CollisionMap.MissileBarrier,
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
    public void IsLineClear_StopsAtWallsAndBarriers_NotAtTheEnds()
    {
        var grid = new MapGrid(Town, Moor);
        Assert.False(grid.IsLineClear(new Point(52, 100), new Point(52, 102))); // the town wall at (52,101)
        Assert.True(grid.IsLineClear(new Point(54, 100), new Point(54, 104)));  // the town's open right column
        Assert.True(grid.IsLineClear(new Point(50, 100), new Point(60, 100)));  // along the top row, into the moor
        Assert.False(grid.IsLineClear(new Point(57, 100), new Point(57, 103))); // the moor's wall
        Assert.True(grid.IsLineClear(new Point(51, 101), new Point(51, 101)));  // the same cell: nothing between
        Assert.True(grid.IsLineClear(new Point(52, 100), new Point(52, 101)));  // next to each other: nothing between
        Assert.False(grid.IsLineClear(new Point(56, 103), new Point(56, 106))); // off the levels

        var barrier = new MapGrid(Level(AreaId.DenOfEvil, 0, 0,
            ".....",
            "..m..",
            ".....",
            ".....",
            "....."));
        Assert.True(barrier.IsWalkable(new Point(2, 1)));
        Assert.False(barrier.IsLineClear(new Point(2, 0), new Point(2, 3)));                         // a missile stops
        Assert.True(barrier.IsLineClear(new Point(2, 0), new Point(2, 3), CollisionMap.BlockPlayer)); // a walker does not
        Assert.False(barrier.IsLineClear(new Point(0, 0), new Point(4, 2)));                          // a diagonal through it
    }

    [Fact]
    public void Constructor_RejectsNoLevels_AndLevelsWithoutCollision()
    {
        Assert.Throws<ArgumentException>(() => new MapGrid());
        Assert.Throws<ArgumentException>(() => new MapGrid(new MapLevel { Id = AreaId.BloodMoor }));
    }
}

/// <summary>Routes across an act's levels, on hand-made levels.</summary>
public sealed class RouteTests
{
    private static MapLevel L(AreaId id, params (AreaId To, ExitKind Kind)[] exits) => new()
    {
        Id = id,
        Exits = [.. exits.Select((e, i) => new LevelExit(e.To, new Point(i, i), e.Kind))],
    };

    private static readonly GeneratedAct Act = new()
    {
        Act = ActId.Act1,
        Levels =
        [
            L(AreaId.RogueEncampment, (AreaId.BloodMoor, ExitKind.Seam), (AreaId.BloodMoor, ExitKind.Seam)),
            L(AreaId.BloodMoor, (AreaId.RogueEncampment, ExitKind.Seam), (AreaId.ColdPlains, ExitKind.Seam), (AreaId.DenOfEvil, ExitKind.Warp)),
            L(AreaId.ColdPlains, (AreaId.BloodMoor, ExitKind.Seam), (AreaId.BurialGrounds, ExitKind.Seam), (AreaId.CaveLevel1, ExitKind.Warp)),
            L(AreaId.DenOfEvil, (AreaId.BloodMoor, ExitKind.Warp)),
            L(AreaId.BurialGrounds, (AreaId.ColdPlains, ExitKind.Seam)),
            L(AreaId.CaveLevel1),
            L(AreaId.StonyField),
        ],
    };

    [Fact]
    public void FindRoute_CrossesTheFewestLevels_WithEveryExitOfEachStep()
    {
        var route = Act.FindRoute(AreaId.RogueEncampment, AreaId.BurialGrounds)!;
        Assert.Equal([AreaId.BloodMoor, AreaId.ColdPlains, AreaId.BurialGrounds], route.Select(s => s.To.Id));
        Assert.Equal([AreaId.RogueEncampment, AreaId.BloodMoor, AreaId.ColdPlains], route.Select(s => s.From.Id));
        Assert.Equal(2, route[0].Exits.Count); // both points of the town's border with the moor
        Assert.All(route, s => Assert.All(s.Exits, e => Assert.Equal(s.To.Id, e.Destination)));

        Assert.Equal([AreaId.BloodMoor, AreaId.RogueEncampment], Act.FindRoute(AreaId.DenOfEvil, AreaId.RogueEncampment)!.Select(s => s.To.Id));
    }

    [Fact]
    public void FindRoute_IsEmptyForTheSameLevel_AndNullWithoutAWay()
    {
        Assert.Empty(Act.FindRoute(AreaId.BloodMoor, AreaId.BloodMoor)!);
        Assert.Null(Act.FindRoute(AreaId.CaveLevel1, AreaId.RogueEncampment)); // the cave has no exit back
        Assert.Null(Act.FindRoute(AreaId.RogueEncampment, AreaId.StonyField)); // nothing leads there
        Assert.Null(Act.FindRoute(AreaId.RogueEncampment, AreaId.LutGholein)); // not in the act
    }
}

/// <summary>Paths to the nearest subtile a goal accepts.</summary>
public sealed class PathToGoalTests
{
    [Fact]
    public void FindPathTo_ReachesTheNearestGoal_AroundWalls_OrNull()
    {
        var level = new MapLevel
        {
            Id = AreaId.RogueEncampment,
            Bounds = new Rect(0, 0, 1, 1),
            Collision = new CollisionMap(5, 5,
            [
                0, 1, 0, 0, 0,
                0, 1, 0, 1, 0,
                0, 1, 0, 1, 0,
                0, 1, 0, 1, 0,
                0, 0, 0, 1, 0,
            ]),
        };
        var grid = new MapGrid(level);

        // From the left column to the right one: down, through the gap at the bottom, up the middle, over the top.
        var path = grid.FindPathTo(new Point(0, 0), p => p.X == 4)!;
        Assert.Equal(new Point(0, 0), path[0]);
        Assert.Equal(4, path[^1].X);
        Assert.All(path, p => Assert.True(grid.IsWalkable(p)));
        Assert.Contains(new Point(1, 4), path);

        Assert.Equal([new Point(0, 0)], grid.FindPathTo(new Point(0, 0), p => p.X == 0)); // already there
        Assert.Null(grid.FindPathTo(new Point(0, 0), p => p.X == 5)); // no such subtile

        // The way round is 12 steps: a search limited to 10 gives up, one limited to 12 finds it.
        Assert.Null(grid.FindPathTo(new Point(0, 0), p => p.X == 4, maxLength: 10));
        Assert.NotNull(grid.FindPathTo(new Point(0, 0), p => p.X == 4, maxLength: 12));
    }
}

/// <summary>Obstacles added to a grid.</summary>
public sealed class BlockTests
{
    [Fact]
    public void Block_MakesSubtilesUnwalkable_AndPathsGoRound_UntilCleared()
    {
        var level = new MapLevel { Id = AreaId.BloodMoor, Bounds = new Rect(0, 0, 1, 1), Collision = new CollisionMap(5, 5, new ushort[25]) };
        var grid = new MapGrid(level);
        Assert.Equal(5, grid.FindPath(new Point(0, 2), new Point(4, 2))!.Count); // straight across

        grid.Block(new Point(2, 2), radius: 1);
        Assert.Equal(9, grid.Blocked.Count);
        Assert.False(grid.IsWalkable(new Point(3, 3)));
        var path = grid.FindPath(new Point(0, 2), new Point(4, 2))!;
        Assert.DoesNotContain(path, p => Math.Abs(p.X - 2) <= 1 && Math.Abs(p.Y - 2) <= 1);

        grid.ClearBlocks();
        Assert.True(grid.IsWalkable(new Point(2, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Block(new Point(0, 0), -1));
    }
}

/// <summary>Paths with an extra cost per subtile.</summary>
public sealed class CostlyPathTests
{
    [Fact]
    public void FindPath_WithAnExtraCost_GoesRoundTheCostlySubtiles()
    {
        var level = new MapLevel { Id = AreaId.BloodMoor, Bounds = new Rect(0, 0, 2, 2), Collision = new CollisionMap(10, 10, new ushort[100]) };
        var grid = new MapGrid(level);
        Point from = new(0, 5), to = new(9, 5);
        Assert.All(grid.FindPath(from, to)!, p => Assert.Equal(5, p.Y)); // straight along the middle

        // A monster at (5, 5): subtiles within 2 of it cost 50 more, so the path goes above or below them.
        var path = grid.FindPath(from, to, p => Math.Max(Math.Abs(p.X - 5), Math.Abs(p.Y - 5)) <= 2 ? 50 : 0)!;
        Assert.Equal(from, path[0]);
        Assert.Equal(to, path[^1]);
        Assert.DoesNotContain(path, p => Math.Max(Math.Abs(p.X - 5), Math.Abs(p.Y - 5)) <= 2);
        Assert.Equal(grid.FindPath(from, to), grid.FindPath(from, to, null));
    }
}

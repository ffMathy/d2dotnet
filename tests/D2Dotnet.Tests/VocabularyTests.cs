namespace D2Dotnet.Tests;

public class VocabularyTests
{
    [Fact]
    public void Point_Arithmetic()
    {
        var a = new Point(3, 4);
        Assert.Equal(new Point(4, 6), a + new Point(1, 2));
        Assert.Equal(new Point(2, 2), a - new Point(1, 2));
        Assert.Equal(25, a.DistanceSquaredTo(Point.Zero));
        Assert.Equal(5.0, a.DistanceTo(Point.Zero));
        Assert.Equal(2L * int.MaxValue * (long)int.MaxValue, new Point(int.MaxValue, int.MaxValue).DistanceSquaredTo(Point.Zero));
    }

    [Fact]
    public void Rect_EdgesAndContainment()
    {
        var r = new Rect(10, 20, 5, 3);
        Assert.Equal((10, 20, 15, 23), (r.Left, r.Top, r.Right, r.Bottom));
        Assert.Equal(new Point(10, 20), r.Location);
        Assert.Equal(r, new Rect(new Point(10, 20), 5, 3));
        Assert.Equal(r, Rect.FromEdges(10, 20, 15, 23));
        Assert.True(r.Contains(new Point(10, 20)));
        Assert.True(r.Contains(14, 22));
        Assert.False(r.Contains(15, 22)); // right edge is exclusive
        Assert.False(r.Contains(14, 23));
        Assert.False(r.Contains(9, 20));
        Assert.False(r.IsEmpty);
        Assert.True(new Rect(0, 0, 0, 5).IsEmpty);
    }

    [Fact]
    public void Rect_IntersectionAndUnion()
    {
        var r = new Rect(0, 0, 10, 10);
        Assert.True(r.IntersectsWith(new Rect(9, 9, 5, 5)));
        Assert.False(r.IntersectsWith(new Rect(10, 0, 5, 5))); // touching edges do not overlap
        Assert.False(r.IntersectsWith(new Rect(5, 5, 0, 0)));
        Assert.Equal(new Rect(-5, 0, 15, 12), r.Union(new Rect(-5, 2, 1, 10)));
        Assert.Equal(r, r.Union(default));
        Assert.Equal(r, default(Rect).Union(r));
    }

    [Fact]
    public void ActAndDifficulty_AreNumberedFromZero()
    {
        Assert.Equal([0, 1, 2, 3, 4], Enum.GetValues<ActId>().Select(a => (int)a));
        Assert.Equal([0, 1, 2], Enum.GetValues<Difficulty>().Select(d => (int)d));
    }

    [Fact]
    public void IdEnums_HaveTheGameWidths()
    {
        Assert.Equal(typeof(short), Enum.GetUnderlyingType(typeof(SkillId)));
        Assert.Equal(typeof(short), Enum.GetUnderlyingType(typeof(StatId)));
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(StateId)));
        Assert.Equal(-1, (int)ObjectId.None);
        Assert.Equal(-1, (int)SkillId.None);
    }
}

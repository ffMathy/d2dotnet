using System.Globalization;
using D2Dotnet.MapServer.Render;

namespace D2Dotnet.MapServer.Tests.Render;

/// <summary>Port of upstream <c>viewer/src/__tests__/bounds.test.ts</c>.</summary>
public class BoundsTests
{
    private const double HalfMap = LevelBounds.Size / 2;
    private const double Wm = LevelBounds.WebMercatorBounds;

    private static string Fixed(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

    [Fact]
    public void ShouldMapToWebMercatorProjection()
    {
        Assert.Equal((0d, 0d), LevelBounds.SourceToWebMercator(HalfMap, HalfMap));
        Assert.Equal((-Wm, Wm), LevelBounds.SourceToWebMercator(0, 0));
        Assert.Equal((Wm, Wm), LevelBounds.SourceToWebMercator(LevelBounds.Size, 0));
        Assert.Equal((Wm, -Wm), LevelBounds.SourceToWebMercator(LevelBounds.Size, LevelBounds.Size));
        Assert.Equal((-Wm, -Wm), LevelBounds.SourceToWebMercator(0, LevelBounds.Size));
    }

    [Fact]
    public void ShouldConvertToLatLng()
    {
        Assert.Equal((0d, 0d), LevelBounds.SourceToLatLng(HalfMap, HalfMap));

        var topLeft = LevelBounds.SourceToLatLng(0, 0);
        Assert.Equal("-180.00", Fixed(topLeft.Lng));
        Assert.Equal("85.05", Fixed(topLeft.Lat));

        var topRight = LevelBounds.SourceToLatLng(LevelBounds.Size, 0);
        Assert.Equal("180.00", Fixed(topRight.Lng));
        Assert.Equal("85.05", Fixed(topRight.Lat));

        var bottomRight = LevelBounds.SourceToLatLng(LevelBounds.Size, LevelBounds.Size);
        Assert.Equal("180.00", Fixed(bottomRight.Lng));
        Assert.Equal("-85.05", Fixed(bottomRight.Lat));

        var bottomLeft = LevelBounds.SourceToLatLng(0, LevelBounds.Size);
        Assert.Equal("-180.00", Fixed(bottomLeft.Lng));
        Assert.Equal("-85.05", Fixed(bottomLeft.Lat));
    }

    [Fact]
    public void TileBoundsAndScale()
    {
        Assert.Equal(128, LevelBounds.GetScale(0));
        Assert.Equal(new Bounds(256, 512, 256, 256), LevelBounds.TileToSourceBounds(1, 2, 7));
        Assert.Equal((512d, 768d), LevelBounds.TileToPixels(2, 3));
    }
}

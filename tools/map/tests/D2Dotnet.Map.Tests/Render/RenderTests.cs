using D2Dotnet.Map.Data;
using D2Dotnet.Map.Render;
using SkiaSharp;

namespace D2Dotnet.Map.Tests.Render;

public class RenderTests
{
    internal static Diablo2Level SyntheticLevel() => new()
    {
        Type = "map",
        Id = 1,
        Name = "Rogue Encampment",
        Offset = new LevelOffset(100, 200),
        Size = new LevelSize(40, 30),
        Objects =
        [
            new() { Id = 119, Type = "object", X = 20, Y = 20, Name = "Waypoint" },
            new() { Id = 2, Type = "exit", X = 35, Y = 10 },
            new() { Id = 0, Type = "npc", X = 3, Y = 20 },
        ],
        Map = [[1, 5, 1], [2, 3, 2], [1, 5, 1], []],
    };

    private static SKColor Pixel(SKBitmap bmp, int x, int y) => bmp.GetPixel(x, y);

    [Fact]
    public void RendersAPngOfALevel()
    {
        var png = MapImage.RenderPng(SyntheticLevel());
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);

        using var bmp = SKBitmap.Decode(png);
        Assert.Equal(40, bmp.Width);
        Assert.Equal(30 + MapImage.CaptionHeight, bmp.Height);

        // row 0: [1,5,1] -> collision, 5 walkable, collision, then the rest of the row is walkable
        Assert.Equal(SKColors.Black, Pixel(bmp, 0, 0));
        Assert.Equal(SKColors.White, Pixel(bmp, 1, 0));
        Assert.Equal(SKColors.White, Pixel(bmp, 5, 0));
        Assert.Equal(SKColors.Black, Pixel(bmp, 6, 0));
        Assert.Equal(SKColors.White, Pixel(bmp, 7, 0));
        Assert.Equal(SKColors.White, Pixel(bmp, 12, 0));
        // row 1: [2,3,2]
        Assert.Equal(SKColors.Black, Pixel(bmp, 1, 1));
        Assert.Equal(SKColors.White, Pixel(bmp, 2, 1));
        Assert.Equal(SKColors.Black, Pixel(bmp, 5, 1));
        // empty row and rows without data stay black
        Assert.Equal(SKColors.Black, Pixel(bmp, 10, 3));
        Assert.Equal(SKColors.Black, Pixel(bmp, 10, 5));

        // waypoint (blue) and exit (red) squares of 12px centred on the object, npcs are not drawn
        Assert.Equal(SKColors.Blue, Pixel(bmp, 20, 20));
        Assert.Equal(SKColors.Blue, Pixel(bmp, 14, 14));
        Assert.Equal(SKColors.Black, Pixel(bmp, 13, 13));
        Assert.Equal(SKColors.Red, Pixel(bmp, 35, 10));
        Assert.Equal(SKColors.Red, Pixel(bmp, 30, 5));
        Assert.Equal(SKColors.Black, Pixel(bmp, 3, 20));

        // caption strip is not filled
        Assert.Equal(0, Pixel(bmp, 39, bmp.Height - 1).Alpha);
    }

    [Fact]
    public void RenderHonoursBoundsScaleAndFillColor()
    {
        var level = SyntheticLevel();
        using var bmp = new SKBitmap(40, 30);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            var mapParams = new MapParams(1, 0, 0, 0, 0, 0, "0xff00ff");
            // bounds start one row below the level: row 0 is skipped
            LevelRender.Render(level, canvas, new Bounds(100, 201, 40, 30), 2, mapParams);
        }

        Assert.Equal(new SKColor(0xff, 0x00, 0xff), bmp.GetPixel(4, 0));
        Assert.Equal(new SKColor(0xff, 0x00, 0xff), bmp.GetPixel(5, 1));
        Assert.Equal(SKColors.Black, bmp.GetPixel(2, 0));
        Assert.Equal(SKColors.Black, bmp.GetPixel(10, 0));
    }

    [Fact]
    public void ObjectsOutsideTheBoundsAreSkipped()
    {
        var level = SyntheticLevel();
        using var bmp = new SKBitmap(40, 30);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            LevelRender.RenderObjects(level, canvas, new Bounds(130, 200, 10, 30), 1);
        }
        Assert.Equal(SKColors.Black, bmp.GetPixel(20, 20));
        Assert.Equal(SKColors.Red, bmp.GetPixel(35, 10));
    }

    [Theory]
    [InlineData("#ff0000", 0xff, 0x00, 0x00, 0xff)]
    [InlineData("#0f0", 0x00, 0xff, 0x00, 0xff)]
    [InlineData("#11223344", 0x11, 0x22, 0x33, 0x44)]
    [InlineData("blue", 0x00, 0x00, 0xff, 0xff)]
    [InlineData("not a color", 0xff, 0xff, 0xff, 0xff)]
    [InlineData("#12345", 0xff, 0xff, 0xff, 0xff)]
    public void ParsesCanvasColors(string value, byte r, byte g, byte b, byte a) =>
        Assert.Equal(new SKColor(r, g, b, a), CanvasColor.Parse(value, SKColors.White));
}

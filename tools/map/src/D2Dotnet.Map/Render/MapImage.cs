using D2Dotnet.Map.Data;
using SkiaSharp;

namespace D2Dotnet.Map.Render;

/// <summary>
/// Renders a single level to a PNG, the drawing part of upstream <c>routes/map.image.ts</c> (node-canvas there, SkiaSharp here).
/// </summary>
public static class MapImage
{
    /// <summary>Height of the caption strip below the level.</summary>
    public const int CaptionHeight = 32;

    private static readonly Lazy<SKTypeface> Typeface = new(() =>
        SKTypeface.FromFamilyName("Sans") ?? SKTypeface.Default);

    /// <summary>
    /// Draw the level on a black background with its name and offset/size in the caption strip below it.
    /// Like upstream the caption strip is left transparent and the caption text is drawn in black.
    /// </summary>
    public static byte[] RenderPng(Diablo2Level zone)
    {
        var width = zone.Size.Width;
        var height = zone.Size.Height + CaptionHeight;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException($"Unable to create a {width}x{height} canvas");
        var ctx = surface.Canvas;
        ctx.Clear(SKColors.Transparent);

        var bounds = new Bounds(zone.Offset.X, zone.Offset.Y, zone.Size.Width, zone.Size.Height);

        using (var black = CanvasColor.FillPaint(SKColors.Black))
        {
            LevelRender.FillRect(ctx, black, 0, 0, zone.Size.Width, zone.Size.Height);
        }
        LevelRender.Render(zone, ctx, bounds, 1);
        LevelRender.RenderObjects(zone, ctx, bounds, 1);

        using var textPaint = CanvasColor.FillPaint(SKColors.Black);
        var captionY = zone.Size.Height + 16;

        using (var font = new SKFont(Typeface.Value, 24))
        {
            FillText(ctx, zone.Name, 5, captionY, SKTextAlign.Left, font, textPaint);
        }

        using (var font = new SKFont(Typeface.Value, 16))
        {
            FillText(ctx, $"{zone.Offset.X}, {zone.Offset.Y} ({zone.Size.Width}x{zone.Size.Height})",
                zone.Size.Width - 5, captionY, SKTextAlign.Right, font, textPaint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Canvas <c>fillText</c> with <c>textBaseline = 'middle'</c>.</summary>
    private static void FillText(SKCanvas ctx, string text, float x, float y, SKTextAlign align, SKFont font, SKPaint paint)
    {
        var metrics = font.Metrics;
        // middle baseline: the middle of the em box sits on y
        var baseline = y - (metrics.Ascent + metrics.Descent) / 2;
        ctx.DrawText(text, x, baseline, align, font, paint);
    }
}

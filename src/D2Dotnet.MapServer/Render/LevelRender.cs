using D2Dotnet.MapServer.Data;
using SkiaSharp;

namespace D2Dotnet.MapServer.Render;

/// <summary>
/// Draws a level's collision map and important objects onto a canvas
/// (upstream <c>@diablo2/viewer</c> <c>render.ts</c>, ported from the HTML canvas API to SkiaSharp).
/// </summary>
public static class LevelRender
{
    public const double ExitSize = 12;

    private static bool IsInBounds(double x, double y, Bounds bounds)
    {
        if (x < bounds.X) return false;
        if (y < bounds.Y) return false;
        if (x > bounds.X + bounds.Width) return false;
        if (y > bounds.Y + bounds.Height) return false;
        return true;
    }

    /// <summary>Fill every walkable run of the collision map.</summary>
    public static void Render(Diablo2Level level, SKCanvas ctx, Bounds bounds, double scale = 0.5, MapParams? mapParams = null)
    {
        var fillStyle = SKColors.White;
        if (mapParams?.RasterFillColor is { } fill) fillStyle = CanvasColor.Parse(fill.Replace("0x", "#", StringComparison.Ordinal), fillStyle);

        using var paint = CanvasColor.FillPaint(fillStyle);
        var map = level.Map;

        for (var yOffset = 0; yOffset < map.Count; yOffset++)
        {
            var line = map[yOffset];
            var isFill = false;
            if (line.Count == 0) continue;

            if (level.Offset.Y + yOffset < bounds.Y) continue;

            var x = level.Offset.X - bounds.X;
            var y = level.Offset.Y - bounds.Y + yOffset;

            foreach (var xCount in line)
            {
                isFill = !isFill;
                if (!isFill)
                {
                    var width = xCount * scale;
                    if (width >= 0.2) FillRect(ctx, paint, x * scale, y * scale, width, scale);
                }
                x += xCount;
            }
            var xMax = level.Offset.X - bounds.X + level.Size.Width;
            if (isFill && x < xMax) FillRect(ctx, paint, x * scale, y * scale, (xMax - x) * scale, scale);
        }
    }

    /// <summary>Draw exits (red) and waypoints (blue) as squares.</summary>
    public static void RenderObjects(Diablo2Level zone, SKCanvas ctx, Bounds bounds, double scale = 0.5)
    {
        var size = ExitSize * scale;
        var halfSize = size / 2;
        using var exitPaint = CanvasColor.FillPaint(SKColors.Red);
        using var waypointPaint = CanvasColor.FillPaint(SKColors.Blue);
        foreach (var obj in zone.Objects)
        {
            if (!IsInBounds(obj.X + zone.Offset.X, obj.Y + zone.Offset.Y, bounds)) continue;

            if (obj.Type == Diablo2LevelObjectType.Exit)
                FillRect(ctx, exitPaint, obj.X - halfSize, obj.Y - halfSize, size, size);

            if (obj.Type == Diablo2LevelObjectType.Object && string.Equals(obj.Name, "waypoint", StringComparison.OrdinalIgnoreCase))
                FillRect(ctx, waypointPaint, obj.X - halfSize, obj.Y - halfSize, size, size);
        }
    }

    /// <summary>HTML canvas <c>ctx.fillRect(x, y, w, h)</c>.</summary>
    internal static void FillRect(SKCanvas ctx, SKPaint paint, double x, double y, double width, double height) =>
        ctx.DrawRect(SKRect.Create((float)x, (float)y, (float)width, (float)height), paint);
}

/// <summary>Minimal CSS color handling for canvas <c>fillStyle</c> values.</summary>
public static class CanvasColor
{
    private static readonly Dictionary<string, SKColor> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = SKColors.Black,
        ["white"] = SKColors.White,
        ["red"] = SKColors.Red,
        ["blue"] = SKColors.Blue,
        ["green"] = new SKColor(0x00, 0x80, 0x00),
        ["lime"] = SKColors.Lime,
        ["yellow"] = SKColors.Yellow,
        ["gray"] = new SKColor(0x80, 0x80, 0x80),
        ["grey"] = new SKColor(0x80, 0x80, 0x80),
        ["transparent"] = SKColors.Transparent,
    };

    /// <summary>
    /// Parse a CSS color (<c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c> or a basic name).
    /// Invalid values are ignored by canvas, so <paramref name="fallback"/> is returned.
    /// </summary>
    public static SKColor Parse(string value, SKColor fallback)
    {
        var v = value.Trim();
        if (Named.TryGetValue(v, out var named)) return named;
        if (v.StartsWith('#') && v.Length is 4 or 5 or 7 or 9 && v.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF") == false)
        {
            var hex = v[1..];
            if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(c => $"{c}{c}"));
            var r = Convert.ToByte(hex[..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            var a = hex.Length == 8 ? Convert.ToByte(hex[6..8], 16) : (byte)0xff;
            return new SKColor(r, g, b, a);
        }
        return fallback;
    }

    internal static SKPaint FillPaint(SKColor color) => new() { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
}

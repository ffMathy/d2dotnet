namespace D2Dotnet.MapServer.Render;

/// <summary>A rectangle in game world coordinates (upstream <c>Bounds</c>).</summary>
public readonly record struct Bounds(double X, double Y, double Width, double Height);

/// <summary>A latitude/longitude pair (upstream <c>LonLat</c>).</summary>
public readonly record struct LonLat(double Lat, double Lon);

/// <summary>Map location (upstream <c>MapLocation</c>).</summary>
public readonly record struct MapLocation(double Lat, double Lon, double Zoom, double Pitch, double Bearing);

/// <summary>Raster render parameters (upstream <c>MapParams</c> in <c>tile.ts</c>).</summary>
public sealed record MapParams(double Seed, int Difficulty, int Act, int X, int Y, int Z, string? RasterFillColor);

/// <summary>Projection helpers between game world coordinates and web mercator (upstream <c>LevelBounds</c>).</summary>
public static class LevelBounds
{
    public const double WebMercatorBounds = 20037500;
    public const double EarthRadius = 6378137;
    private const double RadToDeg = 180 / Math.PI;

    public const double Size = 1 << 15;
    public const double TileSize = 256;

    public static (double X, double Y) TileToPixels(double tX, double tY) => (tX * TileSize, tY * TileSize);

    public static Bounds TileToSourceBounds(double tX, double tY, double tZ)
    {
        var scale = GetScale(tZ);
        var size = scale * TileSize;
        return new Bounds(tX * size, tY * size, size, size);
    }

    public static (double Lat, double Lng) SourceToLatLng(double sX, double sY)
    {
        var wm = SourceToWebMercator(sX, sY);
        return WebMercatorToLatLong(wm.X, wm.Y);
    }

    /// <summary>Map the map bounding box into EPSG:3857 WebMercator so it can be transformed into a lat lng.</summary>
    public static (double X, double Y) SourceToWebMercator(double sX, double sY)
    {
        const double halfSize = Size / 2;
        var x = (sX - halfSize) / halfSize * WebMercatorBounds;
        var y = (halfSize - sY) / halfSize * WebMercatorBounds;
        return (x, y);
    }

    public static (double Lat, double Lng) WebMercatorToLatLong(double wX, double wY)
    {
        var lat = (Math.PI * 0.5 - 2.0 * Math.Atan(Math.Exp(-wY / EarthRadius))) * RadToDeg;
        return (lat, wX * RadToDeg / EarthRadius);
    }

    public static double GetScale(double z) => Size / (Math.Pow(2, z) * TileSize);
}

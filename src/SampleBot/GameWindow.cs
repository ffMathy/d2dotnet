using System.Diagnostics;
using D2Dotnet;

/// <summary>
/// The game window, and where a world position is on screen. The game draws the world isometrically around the player
/// at the centre of its client area: one subtile is 19.8 by 9.9 pixels at a client height of 720, in proportion at
/// other heights.
/// </summary>
internal sealed class GameWindow
{
    private const double SubtileWidth = 19.8, SubtileHeight = 9.9, ReferenceHeight = 720;

    private GameWindow(nint handle) => Handle = handle;

    public nint Handle { get; }

    /// <summary>Pixels above a unit's feet to aim at, so the cursor lands on its body (calibrated at start).</summary>
    public int AimHeight { get; set; }

    public static GameWindow Of(uint processId)
    {
        var handle = Process.GetProcessById((int)processId).MainWindowHandle;
        if (handle == 0)
            throw new InvalidOperationException("The game has no main window.");
        return new GameWindow(handle);
    }

    public bool IsForeground => Native.GetForegroundWindow() == Handle;

    public void BringToFront() => Native.SetForegroundWindow(Handle);

    /// <summary>The client area on screen: its top-left corner and size.</summary>
    public (int X, int Y, int Width, int Height) ClientArea()
    {
        Native.GetClientRect(Handle, out var rect);
        var origin = new Native.PointI();
        Native.ClientToScreen(Handle, ref origin);
        return (origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>Where <paramref name="target"/> (world subtiles) is on screen, seen from a player at <paramref name="player"/>.</summary>
    public (int X, int Y) ToScreen(Point player, Point target, int aimHeight = 0)
    {
        var (x, y, width, height) = ClientArea();
        var scale = height / ReferenceHeight;
        var dx = target.X - player.X;
        var dy = target.Y - player.Y;
        return (
            x + width / 2 + (int)Math.Round((dx - dy) * SubtileWidth * scale),
            y + height / 2 + (int)Math.Round((dx + dy) * SubtileHeight * scale) - (int)Math.Round(aimHeight * scale));
    }

    /// <summary>True when the screen point is in the client area, clear of its edges and of the bottom panel.</summary>
    public bool IsClickable((int X, int Y) point)
    {
        var (x, y, width, height) = ClientArea();
        return point.X > x + width * 0.05 && point.X < x + width * 0.95 && point.Y > y + height * 0.05 && point.Y < y + height * 0.82;
    }
}

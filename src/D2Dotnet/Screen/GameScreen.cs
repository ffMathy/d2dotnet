using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace D2Dotnet.Screen;

/// <summary>
/// Pictures of a running game's window, for seeing what the game shows (a bot can save one when something goes wrong).
/// The window is asked to draw itself into a bitmap (<c>PrintWindow</c> with full content), so it can be behind other
/// windows; a minimized window draws nothing (<see cref="Screenshot.IsBlank"/>). The picture is the client area in the
/// window's real pixels, whatever the display scaling.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class GameScreen
{
    private const uint PrintWindowClientOnly = 0x1, PrintWindowRenderFullContent = 0x2;
    private static readonly nint PerMonitorAwareV2 = -4;

    /// <summary>The client area of the main window of process <paramref name="processId"/>.</summary>
    /// <exception cref="InvalidOperationException">The process has no main window, or its client area is empty.</exception>
    /// <exception cref="Win32Exception">The picture could not be taken.</exception>
    public static Screenshot Capture(uint processId)
    {
        var window = Process.GetProcessById((int)processId).MainWindowHandle;
        if (window == 0)
            throw new InvalidOperationException($"Process {processId} has no main window.");
        return Capture(window);
    }

    /// <summary>The client area of <paramref name="window"/>.</summary>
    /// <exception cref="InvalidOperationException">The client area is empty.</exception>
    /// <exception cref="Win32Exception">The picture could not be taken.</exception>
    public static unsafe Screenshot Capture(nint window)
    {
        // Measure and draw in the window's real pixels, not the scaled ones a DPI-unaware caller sees.
        var previous = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        try
        {
            if (!GetClientRect(window, out var client))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            int width = client.Right - client.Left, height = client.Bottom - client.Top;
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("The window's client area is empty (is it minimized?).");

            var screen = GetDC(0);
            var memory = CreateCompatibleDC(screen);
            var info = new BitmapInfoHeader
            {
                Size = (uint)sizeof(BitmapInfoHeader),
                Width = width,
                Height = -height, // top-down rows
                Planes = 1,
                BitCount = 32,
            };
            var bitmap = CreateDIBSection(screen, &info, 0, out var bits, 0, 0);
            try
            {
                if (bitmap == 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                var old = SelectObject(memory, bitmap);
                var drawn = PrintWindow(window, memory, PrintWindowClientOnly | PrintWindowRenderFullContent);
                SelectObject(memory, old);
                if (!drawn)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());

                var pixels = new byte[width * height * 4];
                new ReadOnlySpan<byte>((void*)bits, pixels.Length).CopyTo(pixels);
                return new Screenshot(width, height, pixels);
            }
            finally
            {
                if (bitmap != 0)
                    DeleteObject(bitmap);
                DeleteDC(memory);
                ReleaseDC(0, screen);
            }
        }
        finally
        {
            SetThreadDpiAwarenessContext(previous);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClientRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out ClientRect rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint window, nint dc, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint SetThreadDpiAwarenessContext(nint context);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint window, nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static unsafe partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}

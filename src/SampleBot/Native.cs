using System.Runtime.InteropServices;

/// <summary>The user32 calls the bot needs: the game window, the cursor, and synthesized mouse and keyboard input.</summary>
internal static partial class Native
{
    public const int VkShift = 0x10;
    public const int VkEnd = 0x23;
    public const int VkEscape = 0x1B;
    public const int VkReturn = 0x0D;
    public const int VkUp = 0x26;
    public const int VkDown = 0x28;

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseLeftDown = 0x0002, MouseLeftUp = 0x0004, MouseRightDown = 0x0008, MouseRightUp = 0x0010;
    private const uint KeyUp = 0x0002, KeyScanCode = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PointI
    {
        public int X, Y;
    }

    // INPUT for 64-bit Windows: the type, then the mouse or keyboard union at offset 8; 40 bytes.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public int MouseDx;
        [FieldOffset(12)] public int MouseDy;
        [FieldOffset(16)] public uint MouseData;
        [FieldOffset(20)] public uint MouseFlags;
        [FieldOffset(8)] public ushort KeyVk;
        [FieldOffset(10)] public ushort KeyScan;
        [FieldOffset(12)] public uint KeyFlags;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint window, ref PointI point);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    public static partial nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);

    private const uint SmtoAbortIfHung = 0x0002;

    /// <summary>
    /// Sends a message and waits for the window to handle it, at most <paramref name="timeoutMs"/> (SendMessage waits
    /// for ever, and a game busy loading or minimised can keep it waiting); false when it was not handled in time.
    /// </summary>
    public static bool Send(nint window, uint message, nint wParam, nint lParam, uint timeoutMs = 1000) =>
        SendMessageTimeout(window, message, wParam, lParam, SmtoAbortIfHung, timeoutMs, out _) != 0;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out PointI point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint window, ref PointI point);

    // The window's DPI (96 at 100% scaling, 144 at 150%).
    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint code, uint mapType);

    [LibraryImport("user32.dll")]
    private static unsafe partial uint SendInput(uint count, Input* inputs, int size);

    public static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public static uint ScanCode(int virtualKey) => MapVirtualKeyW((uint)virtualKey, 0);

    public static void LeftClick() => Mouse(MouseLeftDown, MouseLeftUp);

    public static void RightClick() => Mouse(MouseRightDown, MouseRightUp);

    public static void KeyDown(int virtualKey) => Key(virtualKey, up: false);

    public static void KeyUpFor(int virtualKey) => Key(virtualKey, up: true);

    public static void PressKey(int virtualKey)
    {
        KeyDown(virtualKey);
        Thread.Sleep(30);
        KeyUpFor(virtualKey);
    }

    private static unsafe void Mouse(uint down, uint up)
    {
        var inputs = stackalloc Input[2];
        inputs[0] = new Input { Type = InputMouse, MouseFlags = down };
        inputs[1] = new Input { Type = InputMouse, MouseFlags = up };
        SendInput(1, inputs, sizeof(Input));
        Thread.Sleep(30);
        SendInput(1, inputs + 1, sizeof(Input));
    }

    // Games read scan codes, so the key is sent as one.
    private static unsafe void Key(int virtualKey, bool up)
    {
        var input = new Input
        {
            Type = InputKeyboard,
            KeyScan = (ushort)ScanCode(virtualKey),
            KeyFlags = KeyScanCode | (up ? KeyUp : 0),
        };
        SendInput(1, &input, sizeof(Input));
    }
}

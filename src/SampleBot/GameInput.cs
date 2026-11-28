/// <summary>How the bot aims, clicks and presses keys in the game.</summary>
internal interface IGameInput
{
    /// <summary>True when the game must be the foreground window for the input to reach it.</summary>
    bool NeedsForeground { get; }

    /// <summary>Where the next click or key aims, in screen pixels.</summary>
    void AimAt((int X, int Y) screen);

    void LeftClick();

    void RightClick();

    /// <summary>A left click with shift held (moves a potion from the inventory to the belt).</summary>
    void ShiftLeftClick();

    /// <summary>A right click while standing still (shift held), so a cast does not walk the player.</summary>
    void RightClickStandingStill();

    /// <summary>A key press, aimed (force move and skills act where the cursor is).</summary>
    void PressKey(int virtualKey);

    /// <summary>Puts the cursor at <paramref name="screen"/> for <paramref name="read"/> (to read what is hovered there).</summary>
    T Peek<T>((int X, int Y) screen, Func<T> read);
}

/// <summary>Synthesized input (<c>SendInput</c>) and the real cursor: the game must be in front.</summary>
internal sealed class ForegroundInput : IGameInput
{
    public bool NeedsForeground => true;

    public void AimAt((int X, int Y) screen) => Native.SetCursorPos(screen.X, screen.Y);

    public void LeftClick() => Native.LeftClick();

    public void RightClick() => Native.RightClick();

    public void ShiftLeftClick()
    {
        Native.KeyDown(Native.VkShift);
        Native.LeftClick();
        Native.KeyUpFor(Native.VkShift);
    }

    public void RightClickStandingStill()
    {
        Native.KeyDown(Native.VkShift);
        Native.RightClick();
        Native.KeyUpFor(Native.VkShift);
    }

    public void PressKey(int virtualKey) => Native.PressKey(virtualKey);

    public T Peek<T>((int X, int Y) screen, Func<T> read)
    {
        AimAt(screen);
        Thread.Sleep(150);
        return read();
    }
}

/// <summary>
/// Window messages sent to the game, which acts on them while another window is in front. The game aims at the cursor
/// (<c>GetCursorPos</c>), not at the position a message carries: with the <see cref="InputHook"/> the game is told the
/// aim for the action, and the player keeps the real cursor and keyboard. Without the hook the real cursor is
/// borrowed: moved to the aim, the messages sent (each waits until the game has handled it), and put back. Measured in
/// a running game: about 80 ms per click or key, plus a 50 ms rest so the game sees the unit under the cursor before
/// an attack (without it, clicks on monsters walked to them); giving the cursor back after the button-down alone is
/// too early.
/// </summary>
internal sealed class BackgroundInput(GameWindow window, InputHook? hook = null) : IGameInput
{
    private const uint MouseMove = 0x0200, LeftDown = 0x0201, LeftUp = 0x0202, RightDown = 0x0204, RightUp = 0x0205;
    private const uint KeyDown = 0x0100, KeyUp = 0x0101;
    private const nint LeftButton = 0x1, RightButton = 0x2, ShiftKey = 0x4; // MK_*

    private (int X, int Y) _aim;

    // The aim of the action under way: the position the messages carry (the real cursor is elsewhere with the hook).
    private (int X, int Y) _acting;

    // Where the messages say the cursor is: in the game's real pixels with the hook (as the hook tells the cursor),
    // else where the real cursor is.
    private nint Where() => hook is not null ? window.InClientPhysical(_acting) : window.InClient(_acting);

    public bool NeedsForeground => false;

    public void AimAt((int X, int Y) screen) => _aim = screen;

    public void LeftClick() => Borrowed(_aim, () => Click(LeftDown, LeftUp, LeftButton, 0));

    public void RightClick() => Borrowed(_aim, () => Click(RightDown, RightUp, RightButton, 0));

    // The game reads Shift from the keyboard's state, which a message does not change: Shift is held for real (like
    // the borrowed cursor) for the click only.
    public void ShiftLeftClick() => Borrowed(_aim, () =>
    {
        HoldShift(true);
        try
        {
            Key(Native.VkShift, down: true);
            Click(LeftDown, LeftUp, LeftButton, ShiftKey);
            Key(Native.VkShift, down: false);
        }
        finally
        {
            HoldShift(false);
        }
    });

    // Shift as the game reads it: the hook's, else the real key.
    private void HoldShift(bool down)
    {
        if (hook is not null)
            hook.Shift(down);
        else if (down)
            Native.KeyDown(Native.VkShift);
        else
            Native.KeyUpFor(Native.VkShift);
    }

    public void RightClickStandingStill() => Borrowed(_aim, () =>
    {
        Key(Native.VkShift, down: true);
        Click(RightDown, RightUp, RightButton, ShiftKey);
        Key(Native.VkShift, down: false);
    });

    public void PressKey(int virtualKey) => Borrowed(_aim, () =>
    {
        Send(MouseMove, 0, Where());
        Thread.Sleep(SettleMs);
        Key(virtualKey, down: true);
        Key(virtualKey, down: false);
    });

    public T Peek<T>((int X, int Y) screen, Func<T> read)
    {
        var result = default(T)!;
        Borrowed(screen, () =>
        {
            Send(MouseMove, 0, Where());
            Thread.Sleep(120); // the game updates what is hovered on its next frames
            result = read();
        });
        return result;
    }

    // The game sees the cursor at the aim for the action: through the hook, else the real cursor is moved there and
    // back to where the player left it.
    private void Borrowed((int X, int Y) aim, Action action)
    {
        var step = Bot.Step;
        Bot.Step = $"{step} (input at {aim})";
        _acting = aim;
        if (hook is not null)
        {
            hook.Aim(window.ToPhysical(aim));
            try
            {
                action();
                Thread.Sleep(HoldMs);
            }
            finally
            {
                hook.Release();
                Bot.Step = step;
            }

            return;
        }

        Native.GetCursorPos(out var own);
        Native.SetCursorPos(aim.X, aim.Y);
        try
        {
            action();
        }
        finally
        {
            Native.SetCursorPos(own.X, own.Y);
            Bot.Step = step;
        }
    }

    private void Click(uint down, uint up, nint button, nint modifiers)
    {
        var at = Where();
        Send(MouseMove, modifiers, at);
        Thread.Sleep(SettleMs); // the game updates what is hovered on its next frame; a click before that misses the unit
        Send(down, button | modifiers, at);
        Send(up, modifiers, at);
    }

    // How long the cursor rests on the aim before a click or key: about three frames at 60 frames a second.
    private const int SettleMs = 50;

    // How long the hook keeps telling the aim after the action (the game acts on a click on its next frames).
    private static readonly int HoldMs = int.TryParse(Environment.GetEnvironmentVariable("D2_HOLD"), out var hold) ? hold : 0;

    // lParam: repeat count 1, the scan code at bit 16, and for a release the previous-state and transition bits.
    private void Key(int virtualKey, bool down)
    {
        var lParam = 1 | ((nint)Native.ScanCode(virtualKey) << 16) | (down ? 0 : unchecked((nint)0xC000_0000));
        Send(down ? KeyDown : KeyUp, virtualKey, lParam);
    }

    private void Send(uint message, nint wParam, nint lParam)
    {
        if (!Native.Send(window.Handle, message, wParam, lParam))
            Bot.Log($"The game did not take message 0x{message:X4} within a second.");
    }
}

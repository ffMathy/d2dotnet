using D2Dotnet;

/// <summary>
/// Leaving the game and starting a new one with the same character (a new game brings the monsters back; a
/// single-player character keeps its map seed), through the
/// pause menu, the character screen's Play and the difficulty choice. Positions are from the HD layouts (a 2160-pixel
/// reference); each step is checked through memory, and where a layout leaves the position open the bot tries
/// points until the step works. In a running game the first points tried worked: Save and Exit 130 above the centre,
/// Play at x -730, Normal at (-32, -170).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>pauselayouthd.json: the buttons are a table at the window's centre from 340 above it, rows 140 high: Options,
/// Save and Exit Game (130 above the centre), Return to Game.</item>
/// <item>mainmenupanelhd.json: QuickPlayButton ("Play") at the window's horizontal centre plus x -1010, y 1865 (its
/// width is the image's, not given: the points along it are tried).</item>
/// <item>characterdifficultymodalhd.json: a modal at the window's centre; Normal, Nightmare and Hell are its first
/// buttons, 131 apart (where inside the frame is tried).</item>
/// </list>
/// </remarks>
internal static class GameSession
{
    private const double ReferenceHeight = 2160;

    /// <summary>Saves and exits, then starts a new game on Normal with the same character.</summary>
    public static void NewGame(Bot bot)
    {
        Bot.Log("Starting a new game, so the monsters come back.");
        SaveAndExit(bot);
        Play(bot);
        if (!WaitUntil(() => bot.Reader.IsInGame() && bot.Reader.ReadMapContext() is not null, seconds: 30))
            throw new BotStopped("The new game did not start.");
        Thread.Sleep(2000);
        Bot.Log($"In a new game: seed 0x{bot.Reader.ReadMapContext()!.Value.Seed:X8}.");
    }

    private static void SaveAndExit(Bot bot)
    {
        bot.CloseDialog();
        bot.Input.PressKey(Native.VkEscape);
        Thread.Sleep(700);
        Click(bot, x: 0, y: -130, anchorY: 0.5);
        if (!WaitUntil(() => !bot.Reader.IsInGame(), seconds: 15))
            throw new BotStopped("Save and Exit did not leave the game.");
        WaitUntil(() => bot.Reader.IsInCharacterSelection(), seconds: 15);
        Thread.Sleep(1500);
    }

    // Play, then Normal when the character can choose a difficulty.
    private static void Play(Bot bot)
    {
        foreach (var x in new[] { -730, -1010 + 150, -500, -900, -300 })
        {
            Click(bot, x, y: 1865 + 70, anchorY: 0);
            if (WaitUntil(() => bot.Reader.IsWidgetShown("CharacterDifficultyModal") || bot.Reader.IsInGame(), seconds: 3))
                break;
        }

        if (bot.Reader.IsInGame())
            return; // a character with Normal only goes straight in
        if (!bot.Reader.IsWidgetShown("CharacterDifficultyModal"))
            throw new BotStopped("Play did not open the difficulty choice.");

        foreach (var y in new[] { -170, -200, -140, -110, -230 })
        {
            Click(bot, x: -32, y, anchorY: 0.5);
            if (WaitUntil(() => !bot.Reader.IsWidgetShown("CharacterDifficultyModal"), seconds: 3))
                return;
        }

        throw new BotStopped("Could not choose Normal.");
    }

    // A click at the window's horizontal centre plus x, and anchorY of its height plus y (reference pixels).
    private static void Click(Bot bot, int x, int y, double anchorY)
    {
        var (left, top, width, height) = bot.Window.ClientArea();
        var s = height / ReferenceHeight;
        bot.Input.AimAt(((int)Math.Round(left + width / 2.0 + x * s), (int)Math.Round(top + height * anchorY + y * s)));
        bot.Input.LeftClick();
    }

    private static bool WaitUntil(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(250);
        }

        return condition();
    }
}

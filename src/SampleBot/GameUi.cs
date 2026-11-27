using D2Dotnet;
using D2Dotnet.Game;

/// <summary>
/// Clicks on the game's panels at positions from the game's own layout files (data/global/ui/layouts/*hd.json), and
/// checks every click through memory: a point is spent only when the stat or skill goes up.
/// </summary>
/// <remarks>
/// <para>
/// The HD layouts are drawn for a 2160-pixel-high screen. A panel is placed by an anchor (a fraction of the window)
/// plus an offset; its widgets by offsets within it:
/// </para>
/// <list type="bullet">
/// <item>left panels (the character stats): anchor (0, 0.397), offset (236, -651), 1162 by 1507 (_profilehd.json);</item>
/// <item>right panels (the skill tree): anchor (1, 0.397), offset (-1394, -651);</item>
/// <item>the stat buttons (characterstatspanelhd.json): x 436, y 381 / 638 / 895 / 1056 for strength, dexterity,
/// vitality and energy;</item>
/// <item>the skill tree (skillstreepanelhd.json): tabs at x 130, 428, 724 (y 160, 294 by 99) for skilldesc.txt pages
/// 3, 2 and 1; skill buttons at the skillColumn x (253, 515, 775) and skillRow y (296, 478, 654, 834, 1014, 1190) of
/// skilldesc.txt's column and row.</item>
/// </list>
/// <para>
/// How the game scales the layout to a window that is not 16:9, and whether a widget's position is its corner or its
/// centre, the files do not say: the first spent point tries the scales and the points inside a button, and the one
/// that works is kept.
/// </para>
/// </remarks>
internal sealed class GameUi(Bot bot)
{
    private const double ReferenceHeight = 2160, ReferenceWidth = 3840;
    private static readonly (double X, double Y) LeftAnchor = (0, 0.397), RightAnchor = (1, 0.397);
    private static readonly (int X, int Y) LeftPanel = (236, -651), RightPanel = (-1394, -651);
    private static readonly int[] StatButtonY = [381, 638, 895, 1056];
    private const int StatButtonX = 436;
    private static readonly int[] TabX = [130, 428, 724];
    private const int TabY = 160, TabWidth = 294, TabHeight = 99;
    private static readonly int[] SkillColumnX = [253, 515, 775];
    private static readonly int[] SkillRowY = [296, 478, 654, 834, 1014, 1190];

    // Where inside a button to click, in reference pixels from its position; the first that works is kept.
    private static readonly (int X, int Y)[] Insides = [(40, 40), (20, 20), (60, 60), (0, 0)];

    private (Func<double> Scale, (int X, int Y) Inside)? _calibration;
    private bool _loaded;

    // The calibration of a window size, kept between runs: "scale index;inside x;inside y".
    private string CalibrationFile()
    {
        var (_, _, width, height) = bot.Window.ClientArea();
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Dotnet", $"ui-{width}x{height}.txt");
    }

    private void LoadCalibration()
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            var parts = File.ReadAllText(CalibrationFile()).Split(';').Select(int.Parse).ToArray();
            _calibration = (Scales()[parts[0]], (parts[1], parts[2]));
        }
        catch (Exception e) when (e is IOException or FormatException or IndexOutOfRangeException or UnauthorizedAccessException)
        {
            // Not calibrated for this window size yet.
        }
    }

    private void SaveCalibration(int scaleIndex, (int X, int Y) inside)
    {
        var file = CalibrationFile();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, $"{scaleIndex};{inside.X};{inside.Y}");
    }

    /// <summary>Spends one stat point on <paramref name="stat"/> (strength, dexterity, vitality or energy).</summary>
    public bool SpendStatPoint(StatId stat)
    {
        var index = Array.IndexOf([StatId.Strength, StatId.Dexterity, StatId.Vitality, StatId.Energy], stat);
        if (index < 0)
            throw new ArgumentException($"{stat} has no button.", nameof(stat));
        if ((bot.Player().GetStat(StatId.StatPoints) ?? 0) == 0)
            return false;
        LoadCalibration();

        OpenPanel(() => bot.Reader.ReadMenus().Character, bot.Keys.CharacterScreen);
        var before = bot.Player().BaseStats.GetValue(stat) ?? 0;
        var done = ClickUntil(LeftAnchor, LeftPanel, (StatButtonX, StatButtonY[index]), () => (bot.Player().BaseStats.GetValue(stat) ?? 0) > before);
        Bot.Log(done ? $"Spent a stat point on {stat} ({before + 1})." : $"Could not spend a stat point on {stat}.");
        Thread.Sleep(150);
        return done;
    }

    /// <summary>Spends one skill point on <paramref name="skill"/>, which must be in the character's skill tree.</summary>
    public bool SpendSkillPoint(SkillId skill)
    {
        if ((bot.Player().GetStat(StatId.SkillPoints) ?? 0) == 0)
            return false;
        // A wrong guess in the skill tree can hit another skill; the stat buttons cannot, so they calibrate first.
        LoadCalibration();
        if (_calibration is null)
        {
            Bot.Log($"Not spending a skill point on {skill} before a stat point has calibrated the panels.");
            return false;
        }

        var description = bot.Data.Skills.Find(skill) is { } info ? bot.Data.SkillDescriptions.Find(info.Description) : null;
        if (description is not { Page: >= 1 and <= 3, Row: >= 1 and <= 6, Column: >= 1 and <= 3 })
            throw new ArgumentException($"{skill} is not in a skill tree.", nameof(skill));

        OpenPanel(() => bot.Reader.ReadMenus().SkillTree, bot.Keys.SkillTree);
        // The tab of the skill's page: the tabs run from page 3 to page 1 (the Sorceress's first tab is Cold Spells,
        // page 3 in skilldesc.txt; her last is Fire Spells, page 1). Clicking the open tab changes nothing.
        var tab = 3 - description.Page;
        ClickAt(RightAnchor, RightPanel, (TabX[tab] + TabWidth / 2, TabY + TabHeight / 2), inside: (0, 0));
        Thread.Sleep(150);

        var before = LevelOf(skill);
        var others = bot.Player().Skills.Where(k => k.Key != skill).ToDictionary(k => k.Key, k => k.Value.Level);
        var done = ClickUntil(RightAnchor, RightPanel, (SkillColumnX[description.Column - 1], SkillRowY[description.Row - 1]), () => LevelOf(skill) > before);
        var hit = bot.Player().Skills.Where(k => k.Key != skill && k.Value.Level > others.GetValueOrDefault(k.Key)).Select(k => k.Key).ToList();
        if (hit.Count > 0)
            throw new BotStopped($"A click meant for {skill} spent a point on {string.Join(", ", hit)}: the skill tree positions are off.");
        Bot.Log(done ? $"Spent a skill point on {skill} ({before + 1})." : $"Could not spend a skill point on {skill}.");
        Thread.Sleep(300);
        return done;
    }

    /// <summary>Closes the open panels with Esc.</summary>
    public void ClosePanels()
    {
        var menus = bot.Reader.ReadMenus();
        if (menus.Character || menus.SkillTree || menus.Inventory)
        {
            bot.Input.PressKey(Native.VkEscape);
            Thread.Sleep(200);
        }
    }

    private int LevelOf(SkillId skill) => bot.Player().Skills.TryGetValue(skill, out var points) ? points.Level : -1;

    private void OpenPanel(Func<bool> isOpen, KeyBinding key)
    {
        if (isOpen())
            return;
        bot.Press(key);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!isOpen() && DateTime.UtcNow < deadline)
            Thread.Sleep(100);
        if (!isOpen())
            throw new BotStopped("A panel did not open.");
        Thread.Sleep(200);
    }

    // Clicks a button and waits for done(); the first time, tries the scales and the points inside the button.
    private bool ClickUntil((double X, double Y) anchor, (int X, int Y) panel, (int X, int Y) button, Func<bool> done)
    {
        var scales = Scales();
        // A known calibration is tried twice: a click right after the previous point can go unnoticed.
        var tries = _calibration is { } known
            ? [(Index: -1, known.Scale, known.Inside), (Index: -1, known.Scale, known.Inside)]
            : Enumerable.Range(0, scales.Length).SelectMany(i => Insides.Select(inside => (Index: i, Scale: scales[i], Inside: inside))).ToArray();
        foreach (var (index, scale, inside) in tries)
        {
            ClickAt(anchor, panel, button, inside, scale());
            var deadline = DateTime.UtcNow.AddMilliseconds(700);
            while (DateTime.UtcNow < deadline)
            {
                if (done())
                {
                    if (_calibration is null)
                    {
                        _calibration = (scale, inside);
                        SaveCalibration(index, inside);
                    }

                    return true;
                }

                Thread.Sleep(100);
            }
        }

        return false;
    }

    private void ClickAt((double X, double Y) anchor, (int X, int Y) panel, (int X, int Y) button, (int X, int Y) inside, double? scale = null)
    {
        var (left, top, width, height) = bot.Window.ClientArea();
        var s = scale ?? (_calibration?.Scale() ?? height / ReferenceHeight);
        var x = left + width * anchor.X + (panel.X + button.X + inside.X) * s;
        var y = top + height * anchor.Y + (panel.Y + button.Y + inside.Y) * s;
        bot.Input.AimAt(((int)Math.Round(x), (int)Math.Round(y)));
        bot.Input.LeftClick();
    }

    // The scales a window can be drawn at: by its height, or by its tighter side when narrower than 16:9.
    private Func<double>[] Scales() =>
    [
        () => bot.Window.ClientArea().Height / ReferenceHeight,
        () => Math.Min(bot.Window.ClientArea().Width / ReferenceWidth, bot.Window.ClientArea().Height / ReferenceHeight),
    ];
}

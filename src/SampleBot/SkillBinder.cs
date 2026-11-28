using D2Dotnet;
using D2Dotnet.Game;
using D2Dotnet.Screen;

/// <summary>
/// Binds skills to the game's skill hotkeys the way a player does: opens the right skill's selection (a click on the
/// right skill button), points at a skill's icon and presses the key. The icons' places depend on the skills the
/// character has, so they are found in a picture of the open selection (what changed from before it opened, in icon
/// sized squares) and told apart by binding each in turn to a spare key and reading which skill that key got.
/// </summary>
internal static class SkillBinder
{
    // The right skill button on the HUD, as a share of the client's height from the centre's x and from the top:
    // measured at 2189x1387 (its centre at 1185, 1335).
    private const double RightButtonX = 0.0652, RightButtonY = 0.9625;

    // A skill icon in the selection: 98 of 2160 reference pixels (the HD layouts), so about 0.045 of the height.
    private const double IconShare = 98.0 / 2160;

    /// <summary>
    /// Binds every skill of <paramref name="skills"/> the character has and has not bound yet to a free hotkey slot.
    /// Returns how many were bound.
    /// </summary>
    public static int Bind(Bot bot, IEnumerable<SkillId> skills)
    {
        var player = bot.Player();
        var keys = bot.Reader.ReadKeyBindings();
        var wanted = skills.Where(s => player.Skills.ContainsKey(s) && keys.Skills.All(b => b.Skill != s)).Distinct().ToList();
        // The slots that have a key and no skill, to put them in.
        var free = keys.Skills.Select((b, i) => (Binding: b, Slot: i)).Where(b => b.Binding.Skill == SkillId.None && Bot.KeyOf(b.Binding.Keys) is not null).ToList();
        if (wanted.Count == 0 || free.Count < 2)
            return 0;

        bot.ClosePanels();
        var before = GameScreen.Capture(bot.Window.Handle);
        if (!Open(bot))
        {
            bot.Failure("Could not open the skill selection to bind skills.");
            return 0;
        }

        var after = GameScreen.Capture(bot.Window.Handle);
        var icons = FindIcons(before, after, bot.Window.ClientArea());
        Bot.Log($"Skill selection: {icons.Count} icons found.");

        // Each icon to the last free slot's key, to learn which skill it is.
        var probe = Bot.KeyOf(free[^1].Binding.Keys)!.Value;
        var found = new Dictionary<SkillId, (int X, int Y)>();
        foreach (var icon in icons)
        {
            bot.Input.AimAt(icon);
            bot.Input.PressKey(probe);
            Thread.Sleep(150);
            var skill = bot.Reader.ReadKeyBindings().Skills[free[^1].Slot].Skill;
            if (skill != SkillId.None)
                found.TryAdd(skill, icon);
        }

        var bound = 0;
        var slot = 0;
        foreach (var skill in wanted)
        {
            if (!found.TryGetValue(skill, out var icon) || slot >= free.Count)
                continue;
            bot.Input.AimAt(icon);
            bot.Input.PressKey(Bot.KeyOf(free[slot].Binding.Keys)!.Value);
            Thread.Sleep(150);
            if (bot.Reader.ReadKeyBindings().Skills[free[slot].Slot].Skill == skill)
            {
                Bot.Log($"Bound {skill} to slot {free[slot].Slot + 1}.");
                bound++;
                slot++;
            }
        }

        bot.ClosePanels();
        bot.RefreshKeys();
        return bound;
    }

    // Opens the right skill's selection: a click on its button; the game says so through the SkillSelect panel flag.
    private static bool Open(Bot bot)
    {
        var (x, y, width, height) = bot.Window.ClientArea();
        bot.Input.AimAt((x + width / 2 + (int)(RightButtonX * height), y + (int)(RightButtonY * height)));
        bot.Input.LeftClick();
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && !bot.Reader.ReadMenus().SkillSelect)
            Thread.Sleep(100);
        Thread.Sleep(300);
        return bot.Reader.ReadMenus().SkillSelect;
    }

    /// <summary>
    /// The screen centres of the icons that appeared between the two pictures: blocks of the right half above the HUD
    /// that changed, grouped into icon-sized squares.
    /// </summary>
    internal static List<(int X, int Y)> FindIcons(Screenshot before, Screenshot after, (int X, int Y, int Width, int Height) client)
    {
        var icon = IconShare * after.Height;
        var block = Math.Max(2, (int)(icon / 6));
        int columns = after.Width / block, rows = after.Height / block;
        var changed = new bool[columns, rows];
        for (var bx = columns / 2; bx < columns; bx++)
        {
            for (var by = 0; by < rows * 92 / 100; by++)
            {
                var difference = 0;
                for (var i = 0; i < block; i += 2)
                {
                    for (var j = 0; j < block; j += 2)
                    {
                        var (r1, g1, b1) = before[bx * block + i, by * block + j];
                        var (r2, g2, b2) = after[bx * block + i, by * block + j];
                        difference += Math.Abs(r1 - r2) + Math.Abs(g1 - g2) + Math.Abs(b1 - b2);
                    }
                }

                changed[bx, by] = difference > 30 * (block / 2) * (block / 2);
            }
        }

        // Connected groups of changed blocks that are about one icon wide and high (titles are wider, flat bars lower).
        var seen = new bool[columns, rows];
        var centres = new List<(int X, int Y)>();
        for (var bx = 0; bx < columns; bx++)
        {
            for (var by = 0; by < rows; by++)
            {
                if (!changed[bx, by] || seen[bx, by])
                    continue;
                var stack = new Stack<(int, int)>([(bx, by)]);
                seen[bx, by] = true;
                int minX = bx, maxX = bx, minY = by, maxY = by;
                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    (minX, maxX, minY, maxY) = (Math.Min(minX, cx), Math.Max(maxX, cx), Math.Min(minY, cy), Math.Max(maxY, cy));
                    foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                    {
                        if (nx >= 0 && ny >= 0 && nx < columns && ny < rows && changed[nx, ny] && !seen[nx, ny])
                        {
                            seen[nx, ny] = true;
                            stack.Push((nx, ny));
                        }
                    }
                }

                // A row of icons touching each other is one group: cut it into icon-sized cells.
                double width = (maxX - minX + 1) * block, height = (maxY - minY + 1) * block;
                if (height < icon * 0.6 || width < icon * 0.6)
                    continue;
                var across = Math.Max(1, (int)Math.Round(width / (icon + 4)));
                var down = Math.Max(1, (int)Math.Round(height / (icon + 4)));
                for (var i = 0; i < across; i++)
                {
                    for (var j = 0; j < down; j++)
                    {
                        var px = minX * block + (i + 0.5) * width / across;
                        var py = minY * block + (j + 0.5) * height / down;
                        centres.Add((client.X + (int)(px * client.Width / after.Width), client.Y + (int)(py * client.Height / after.Height)));
                    }
                }
            }
        }

        return centres;
    }
}

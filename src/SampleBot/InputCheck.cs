using D2Dotnet;
using D2Dotnet.Game;
using D2Dotnet.Maps;

/// <summary>
/// Checks the input in the game, starting in the Rogue Encampment or the Blood Moor: force-moves to three points, walks
/// by clicking, then (walking into the Blood Moor first) casts the right skill at the nearest monster, standing still.
/// </summary>
internal static class InputCheck
{
    public static void Run(Bot bot)
    {
        var player = bot.Player();
        if (player.Area is not (AreaId.RogueEncampment or AreaId.BloodMoor))
            throw new BotStopped($"Start the input check in the Rogue Encampment or the Blood Moor, not in {player.Area}.");
        Bot.Log($"Checking the input ({(bot.Input.NeedsForeground ? "synthesized; the game must stay in front" : "sent to the game window; the game can be in the background")}).");
        bot.Calibrate();

        var town = bot.Act[AreaId.RogueEncampment];
        var moor = bot.Act[AreaId.BloodMoor];
        var grid = new MapGrid(town, moor);

        // 1. Force move (a key) to three nearby points.
        var moved = 0;
        foreach (var (dx, dy) in new[] { (8, 0), (0, 8), (-8, -8) })
        {
            var from = bot.Player().Position;
            if (grid.NearestWalkable(new Point(from.X + dx, from.Y + dy), 3) is not { } point)
                continue;
            var ok = bot.MoveTo(point);
            moved += ok ? 1 : 0;
            Bot.Log($"Force move from {from} to {point}: {(ok ? "arrived" : "did not arrive")}, now at {bot.Player().Position}.");
        }

        // 2. A left click on the ground.
        {
            var from = bot.Player().Position;
            var point = grid.NearestWalkable(new Point(from.X - 8, from.Y), 3) ?? from;
            bot.Aim(from, point, aimHeight: 0);
            bot.Input.LeftClick();
            Thread.Sleep(1500);
            var now = bot.Player().Position;
            Bot.Log($"Click from {from} to {point}: now at {now} ({(Bot.Distance(now, point) <= 3 ? "arrived" : "did not arrive")}).");
        }

        // 3. Into the Blood Moor, then casts at the nearest monster.
        if (bot.Player().Area == AreaId.RogueEncampment)
            bot.TravelTo(AreaId.BloodMoor);

        var deadline = DateTime.UtcNow.AddSeconds(40);
        Monster? target = null;
        while (DateTime.UtcNow < deadline && (target = bot.Enemies(bot.Player().Position, 15).FirstOrDefault()) is null)
        {
            var nearest = bot.Enemies(bot.Player().Position, 100).FirstOrDefault();
            if (nearest is null || grid.FindPath(bot.Player().Position, nearest.Position) is not { Count: > 1 } path)
            {
                Thread.Sleep(300);
                continue;
            }

            bot.MoveTo(path[Math.Min(Bot.StepLength, path.Count - 1)]);
        }

        if (target is null)
        {
            Bot.Log($"Input check: {moved} of 3 force moves arrived; no monster came within 15 subtiles to cast at.");
            return;
        }

        var skill = bot.Player().RightSkill;
        var cost = bot.Data.Skills.Find(skill)?.ManaCost(bot.Player().Skills.TryGetValue(skill, out var points) ? points.Level : 1) ?? 0;
        var casts = 0;
        for (var i = 0; i < 3; i++)
        {
            var wait = DateTime.UtcNow.AddSeconds(40);
            while ((bot.Player().GetStat(StatId.Mana) ?? 0) < cost + 0.5 && DateTime.UtcNow < wait)
                Thread.Sleep(500);
            var start = bot.Player();
            var now = bot.Reader.ReadMonsters().FirstOrDefault(m => m.Id == target.Id && m.IsEnemy) ?? bot.Enemies(start.Position, 15).FirstOrDefault();
            if (now is null)
                break;
            bot.Aim(start.Position, now.Position, bot.Window.AimHeight);
            bot.Input.RightClickStandingStill();
            Thread.Sleep(1200);
            var end = bot.Player();
            var used = (start.GetStat(StatId.Mana) ?? 0) - (end.GetStat(StatId.Mana) ?? 0);
            var walked = Bot.Distance(start.Position, end.Position);
            if (used >= cost * 0.8 && walked <= 1)
                casts++;
            Bot.Log($"Cast {start.RightSkill} (costs {cost:0.#}) at {now.Class} {Bot.Distance(start.Position, now.Position):F0} subtiles away: mana {start.GetStat(StatId.Mana)} -> {end.GetStat(StatId.Mana)}, walked {walked:F0} subtiles.");
        }

        Bot.Log($"Input check: {moved} of 3 force moves arrived; {casts} casts used the mana while standing still.");
    }
}

using D2Dotnet;
using D2Dotnet.Data;
using D2Dotnet.Game;
using D2Dotnet.Maps;
using D2Dotnet.Memory;

/// <summary>
/// Clears the Den of Evil: walks from the Rogue Encampment through the Blood Moor to the cave, enters it, and visits
/// every room of the Den, fighting the monsters it meets with the right skill and drinking potions from the belt.
/// </summary>
/// <remarks>
/// It only reads the game's memory and sends the mouse and keys a player would: as window messages (the game can be in
/// the background; the real cursor is borrowed for about 80 ms per click or key, as the game aims at it) or synthesized
/// (the game must be in front, and the bot pauses while it is not). It stops when End is pressed, when life is low with
/// no potion left, or when it is stuck.
/// </remarks>
internal sealed class DenBot(GameReader reader, GameDatabase data, GeneratedAct act, GameWindow window, KeyBindings keys, IGameInput input)
{
    private const int FightRange = 18;     // subtiles: monsters nearer than this are fought
    private const int StepLength = 10;     // subtiles between the points the bot moves to
    private DateTime _lastPotion = DateTime.MinValue;

    public void Run()
    {
        var player = Player();
        Log($"{player.Name} in {player.Area}; End stops the bot.");
        Calibrate();

        if (player.Area is AreaId.RogueEncampment or AreaId.BloodMoor)
            GoIntoTheDen();
        else if (player.Area != AreaId.DenOfEvil)
            throw new BotStopped($"Start in the Rogue Encampment, the Blood Moor or the Den of Evil, not in {player.Area}.");

        ClearTheDen();
    }

    /// <summary>
    /// Checks the input in the game, starting in the Rogue Encampment or the Blood Moor: force-moves to three points,
    /// walks by clicking, then (walking into the Blood Moor first) casts the right skill once at the nearest monster,
    /// standing still.
    /// </summary>
    public void CheckInput()
    {
        var player = Player();
        if (player.Area is not (AreaId.RogueEncampment or AreaId.BloodMoor))
            throw new BotStopped($"Start the input check in the Rogue Encampment or the Blood Moor, not in {player.Area}.");
        Log($"Checking the input ({(input.NeedsForeground ? "synthesized; the game must stay in front" : "posted; the game can be in the background")}).");
        Calibrate();

        var town = act[AreaId.RogueEncampment];
        var moor = act[AreaId.BloodMoor];
        var grid = new MapGrid(town, moor);

        // 1. Force move (a key) to three nearby points.
        var moved = 0;
        foreach (var (dx, dy) in new[] { (8, 0), (0, 8), (-8, -8) })
        {
            var from = Player().Position;
            if (grid.NearestWalkable(new Point(from.X + dx, from.Y + dy), 3) is not { } point)
                continue;
            var ok = MoveTo(point);
            moved += ok ? 1 : 0;
            Log($"Force move from {from} to {point}: {(ok ? "arrived" : "did not arrive")}, now at {Player().Position}.");
        }

        // 2. A left click on the ground.
        {
            var from = Player().Position;
            var point = grid.NearestWalkable(new Point(from.X - 8, from.Y), 3) ?? from;
            Aim(from, point, aimHeight: 0);
            input.LeftClick();
            Thread.Sleep(1500);
            var now = Player().Position;
            Log($"Click from {from} to {point}: now at {now} ({(Distance(now, point) <= 3 ? "arrived" : "did not arrive")}).");
        }

        // 3. Into the Blood Moor, and one cast at the nearest monster.
        if (Player().Area == AreaId.RogueEncampment)
        {
            var here = Player().Position;
            var seam = town.Exits.Where(e => e.Destination == AreaId.BloodMoor).MinBy(e => Distance(here, town.ToWorld(e.Position)));
            var inside = moor.Exits.Where(e => e.Destination == AreaId.RogueEncampment).MinBy(e => Distance(town.ToWorld(seam.Position), moor.ToWorld(e.Position)));
            Log("Walking into the Blood Moor.");
            WalkTo(grid, moor.ToWorld(inside.Position), arriveWithin: 6, fight: false);
        }

        // Walk towards the nearest enemy until it is within 14 subtiles (or 40 s pass).
        var deadline = DateTime.UtcNow.AddSeconds(40);
        Monster? target = null;
        while (DateTime.UtcNow < deadline && (target = Enemies(Player().Position, 15).FirstOrDefault()) is null)
        {
            var nearest = Enemies(Player().Position, 100).FirstOrDefault();
            if (nearest is null || grid.FindPath(Player().Position, nearest.Position) is not { Count: > 1 } path)
            {
                Thread.Sleep(300);
                continue;
            }

            MoveTo(path[Math.Min(StepLength, path.Count - 1)]);
        }
        if (target is null)
        {
            Log($"Input check: {moved} of 3 force moves arrived; no monster came within 15 subtiles to cast at.");
            return;
        }

        var before = Player();
        var cost = data.Skills.Find(before.RightSkill)?.ManaCost(before.Skills.TryGetValue(before.RightSkill, out var points) ? points.Level : 1) ?? 0;
        var casts = 0;
        for (var i = 0; i < 3; i++)
        {
            var wait = DateTime.UtcNow.AddSeconds(40);
            while ((Player().GetStat(StatId.Mana) ?? 0) < cost + 0.5 && DateTime.UtcNow < wait)
                Thread.Sleep(500);
            var start = Player();
            if (reader.ReadMonsters().FirstOrDefault(m => m.Id == target.Id && m.IsEnemy) is not { } now)
                now = Enemies(start.Position, 15).FirstOrDefault();
            if (now is null)
                break;
            Aim(start.Position, now.Position, window.AimHeight);
            input.RightClickStandingStill();
            Thread.Sleep(1200);
            var end = Player();
            var used = (start.GetStat(StatId.Mana) ?? 0) - (end.GetStat(StatId.Mana) ?? 0);
            var walked = Distance(start.Position, end.Position);
            if (used >= cost * 0.8 && walked <= 1)
                casts++;
            Log($"Cast {start.RightSkill} (costs {cost:0.#}) at {now.Class} {Distance(start.Position, now.Position):F0} subtiles away: mana {start.GetStat(StatId.Mana)} -> {end.GetStat(StatId.Mana)}, walked {walked:F0} subtiles.");
        }

        Log($"Input check: {moved} of 3 force moves arrived; {casts} of 3 casts used the mana while standing still.");
    }

    /// <summary>Prints what the bot would do from where the player is, without sending any input.</summary>
    public void Plan()
    {
        var player = reader.ReadMainPlayer() ?? throw new BotStopped("Not in a game.");
        var den = act[AreaId.DenOfEvil];
        if (player.Area == AreaId.DenOfEvil)
        {
            var grid = new MapGrid(den);
            var reachable = den.Rooms.Count(r => grid.NearestWalkable(den.ToWorld(RoomCentre(den, r)), 25) is { } c
                && grid.FindPath(player.Position, c) is not null);
            Log($"{player.Name} at {player.Position} in the Den of Evil; walkable here: {grid.IsWalkable(player.Position)}; " +
                $"{reachable} of its {den.Rooms.Count} rooms can be reached.");
            return;
        }

        var town = act[AreaId.RogueEncampment];
        var moor = act[AreaId.BloodMoor];
        var route = new MapGrid(town, moor);
        var entrance = moor.ToWorld(moor.Exits.First(e => e.Destination == AreaId.DenOfEvil).Position);
        Log($"{player.Name} at {player.Position} in {player.Area}; walkable here: {route.IsWalkable(player.Position)}.");
        var path = route.FindPath(player.Position, entrance);
        Log(path is null
            ? $"No path to the Den entrance at {entrance}."
            : $"Path to the Den entrance at {entrance}: {path.Count} subtiles, about {path.Count / StepLength} moves; then {den.Rooms.Count} rooms.");
    }

    // ---- The route ----

    private void GoIntoTheDen()
    {
        var town = act[AreaId.RogueEncampment];
        var moor = act[AreaId.BloodMoor];
        var grid = new MapGrid(town, moor);
        var exit = moor.Exits.First(e => e.Destination == AreaId.DenOfEvil);
        var entrance = moor.ToWorld(exit.Position);

        Log($"Walking to the Den of Evil entrance at {entrance}.");
        WalkTo(grid, entrance, arriveWithin: 6, fight: true);
        EnterWarp(entrance, AreaId.DenOfEvil);
    }

    private void ClearTheDen()
    {
        var den = act[AreaId.DenOfEvil];
        // The Den keeps the fixed place levels.txt gives it (checked against the game's level structure: tile 1500,1000).
        var origin = den.Origin;
        var grid = new MapGrid(den);
        if (!grid.IsWalkable(Player().Position))
            throw new BotStopped($"The player at {Player().Position} is not on the generated Den: the map does not match the game.");
        Log($"In the Den of Evil; its top-left corner is at {origin}.");

        var rooms = den.Rooms
            .Select(r => grid.NearestWalkable(den.ToWorld(RoomCentre(den, r)), 25))
            .OfType<Point>()
            .ToList();
        var visited = new HashSet<int>();
        while (visited.Count < rooms.Count)
        {
            var here = Player().Position;
            var next = Enumerable.Range(0, rooms.Count).Where(i => !visited.Contains(i)).MinBy(i => Distance(here, rooms[i]));
            Log($"Room {visited.Count + 1} of {rooms.Count}.");
            WalkTo(grid, rooms[next], arriveWithin: 8, fight: true);
            // Every room the bot now stands near is seen.
            here = Player().Position;
            for (var i = 0; i < rooms.Count; i++)
            {
                if (Distance(here, rooms[i]) <= 20)
                    visited.Add(i);
            }

            visited.Add(next);
        }

        FightNearby(range: 30);
        Log("The Den of Evil is clear.");
    }

    // ---- Moving ----

    private void WalkTo(MapGrid grid, Point target, int arriveWithin, bool fight)
    {
        var failures = 0;
        while (Distance(Player().Position, target) > arriveWithin)
        {
            if (fight)
                FightNearby(FightRange);

            var path = grid.FindPath(Player().Position, target)
                ?? throw new BotStopped($"No path from {Player().Position} to {target}.");
            // Move to the point StepLength subtiles along the path (or the end), then plan again.
            var step = path[Math.Min(StepLength, path.Count - 1)];
            if (MoveTo(step))
            {
                failures = 0;
            }
            else if (++failures >= 6)
            {
                throw new BotStopped($"Stuck at {Player().Position} on the way to {target}.");
            }
        }
    }

    // Force-moves towards a nearby point; true when the player got close to it.
    private bool MoveTo(Point point)
    {
        var start = Player().Position;
        var deadline = DateTime.UtcNow.AddSeconds(2.5);
        while (DateTime.UtcNow < deadline)
        {
            var player = Player();
            if (Distance(player.Position, point) <= 3)
                return true;
            DrinkIfNeeded(player);
            Aim(player.Position, point, aimHeight: 0);
            Press(keys.ForceMove);
            Thread.Sleep(120);
        }

        return Distance(Player().Position, point) < Distance(start, point) - 2;
    }

    // Walks onto a level warp: sweeps the cursor around it until the cave entrance is hovered, then clicks it.
    private void EnterWarp(Point around, AreaId destination)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var player = Player();
            foreach (var spot in Spiral(around, radius: 14, step: 2))
            {
                var aim = Aim(player.Position, spot, aimHeight: 0);
                if (input.Peek(aim, reader.ReadHover) is { Type: UnitType.Tile })
                {
                    Log($"Entering {destination}.");
                    input.LeftClick();
                    if (WaitForArea(destination, seconds: 8))
                        return;
                    break;
                }
            }
        }

        throw new BotStopped($"Could not find the entrance to {destination} near {around}.");
    }

    private bool WaitForArea(AreaId area, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(200);
            if (reader.ReadMainPlayer() is { } p && p.Area == area && !reader.ReadMenus().LoadingScreen)
            {
                Thread.Sleep(800); // let the level's units load
                return true;
            }
        }

        return false;
    }

    // ---- Fighting ----

    private void FightNearby(int range)
    {
        while (true)
        {
            var player = Player();
            var target = Enemies(player.Position, range).FirstOrDefault();
            if (target is null)
                return;
            Fight(target.Id);
        }
    }

    private void Fight(UnitId id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            var player = Player();
            DrinkIfNeeded(player);
            var monster = reader.ReadMonsters().FirstOrDefault(m => m.Id == id);
            if (monster is null || !monster.IsEnemy)
                return;

            Aim(player.Position, monster.Position, window.AimHeight);
            if (CanCast(player, player.RightSkill))
            {
                // The right skill, standing still (shift) so a miss does not walk into the pack.
                input.RightClickStandingStill();
            }
            else
            {
                // Not enough mana for it: the left skill, walking up to the monster.
                input.LeftClick();
            }

            Thread.Sleep(250);
        }

        Log($"Gave up on monster {id.Value} after 12 s.");
    }

    // True when the player has the mana for the skill at its current level (the player's mana stat is in whole points).
    private bool CanCast(MainPlayer player, SkillId skill)
    {
        var mana = player.GetStat(StatId.Mana) ?? 0;
        var level = player.Skills.TryGetValue(skill, out var points) ? points.Level : 1;
        return data.Skills.Find(skill) is not { } info || mana >= info.ManaCost(level);
    }


    private IEnumerable<Monster> Enemies(Point from, int range) =>
        reader.ReadMonsters().Where(m => m.IsEnemy && Distance(from, m.Position) <= range).OrderBy(m => Distance(from, m.Position));

    // ---- Potions ----

    private void DrinkIfNeeded(MainPlayer player)
    {
        if (DateTime.UtcNow - _lastPotion < TimeSpan.FromSeconds(1))
            return;
        var life = player.LifePercent ?? 100;
        var mana = player.ManaPercent ?? 100;
        var belt = reader.ReadInventory().Belt;

        PotionType? need = life < 30 ? PotionType.Rejuvenation : life < 50 ? PotionType.Healing : mana < 15 ? PotionType.Mana : null;
        if (need is null)
            return;
        var potion = belt.FindPotion(need.Value) ?? (need == PotionType.Rejuvenation ? belt.FindPotion(PotionType.Healing) : null);
        if (potion is null)
        {
            if (life < 30)
                throw new BotStopped($"Life is at {life}% and the belt has no healing potion left.");
            return;
        }

        Log($"Life {life}%, mana {mana}%: drinking from belt column {potion.Position.X + 1}.");
        Press(keys.UseBelt[potion.Position.X]);
        _lastPotion = DateTime.UtcNow;
    }

    // ---- Calibration ----

    // Checks the screen mapping by putting the cursor on a nearby unit and reading back what is hovered, and finds how
    // far above a unit's feet to aim.
    private void Calibrate()
    {
        // Measured in a 1459x924 window: the cursor hovers a unit from about 20 to 100 pixels (at a height of 720) above
        // its feet. Some units cannot be hovered (the rogue on the palisade), so several are tried.
        var units = reader.ReadMonsters()
            .Where(m => Distance(Player().Position, m.Position) is > 2 and < 16)
            .OrderBy(m => Distance(Player().Position, m.Position))
            .Take(5)
            .ToList();
        if (units.Count == 0)
        {
            window.AimHeight = 55;
            Log("No unit nearby to check the screen mapping with; aiming 55 pixels above the feet.");
            return;
        }

        foreach (var unit in units)
        {
            foreach (var height in new[] { 55, 40, 75, 25, 90 })
            {
                var player = Player();
                if (reader.ReadMonsters().FirstOrDefault(m => m.Id == unit.Id) is not { } now)
                    break;
                var aim = Aim(player.Position, now.Position, height);
                if (input.Peek(aim, reader.ReadHover) is { } hover && hover.Id == unit.Id)
                {
                    window.AimHeight = height;
                    Log($"Screen mapping checked on {unit.Class}: aiming {height} pixels above the feet.");
                    return;
                }
            }
        }

        // Moving or unlit monsters are often not hovered in time; the mapping was checked on town NPCs, so go on.
        window.AimHeight = 55;
        Log($"Warning: the cursor hovered none of {string.Join(", ", units.Select(u => u.Class))}; aiming 55 pixels above the feet.");
    }

    // ---- Rooms ----

    private static Point RoomCentre(MapLevel level, MapRoom room) => new(
        (room.Bounds.X - level.Bounds.X) * MapLevel.SubtilesPerTile + room.Bounds.Width * MapLevel.SubtilesPerTile / 2,
        (room.Bounds.Y - level.Bounds.Y) * MapLevel.SubtilesPerTile + room.Bounds.Height * MapLevel.SubtilesPerTile / 2);

    // ---- Input and reading ----

    private MainPlayer Player()
    {
        WaitWhileNotForeground();
        if (Native.IsKeyDown(Native.VkEnd))
            throw new BotStopped("End pressed.");
        return reader.ReadMainPlayer() ?? throw new BotStopped("Not in a game any more.");
    }

    private void WaitWhileNotForeground()
    {
        var announced = false;
        while (input.NeedsForeground && !window.IsForeground)
        {
            if (!announced)
                Log("Paused: the game is not the foreground window.");
            announced = true;
            if (Native.IsKeyDown(Native.VkEnd))
                throw new BotStopped("End pressed.");
            Thread.Sleep(300);
        }
    }

    // Aims the next click or key at target (and returns where that is on screen).
    private (int X, int Y) Aim(Point player, Point target, int aimHeight)
    {
        var screen = window.ToScreen(player, target, aimHeight);
        if (!window.IsClickable(screen))
        {
            // Off screen or over the bottom panel: aim at the same direction, closer.
            var halfway = new Point((player.X + target.X) / 2, (player.Y + target.Y) / 2);
            if (halfway != target && halfway != player)
                return Aim(player, halfway, aimHeight);
        }

        input.AimAt(screen);
        return screen;
    }

    private void Press(KeyBinding binding)
    {
        var key = binding.Primary is { IsNone: false, First: not 0xFF } ? binding.Primary : binding.Secondary;
        if (key.IsNone || key.First == 0xFF)
            throw new BotStopped("A key the bot needs (force move or a belt slot) is not bound.");
        input.PressKey(key.First);
    }

    private static IEnumerable<Point> Spiral(Point centre, int radius, int step)
    {
        yield return centre;
        for (var r = step; r <= radius; r += step)
        {
            for (var dx = -r; dx <= r; dx += step)
            {
                yield return new Point(centre.X + dx, centre.Y - r);
                yield return new Point(centre.X + dx, centre.Y + r);
            }

            for (var dy = -r + step; dy <= r - step; dy += step)
            {
                yield return new Point(centre.X - r, centre.Y + dy);
                yield return new Point(centre.X + r, centre.Y + dy);
            }
        }
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}

/// <summary>The bot stopped on purpose, with the reason.</summary>
internal sealed class BotStopped(string reason) : Exception(reason);

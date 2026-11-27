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
/// It only reads the game's memory and sends the mouse and keys a player would. It pauses while the game is not the
/// foreground window and stops when End is pressed, when life is low with no potion left, or when it is stuck.
/// </remarks>
internal sealed class DenBot(GameReader reader, GameDatabase data, GeneratedAct act, GameWindow window, KeyBindings keys)
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

    /// <summary>Prints the route to the Den without sending any input.</summary>
    public void Plan()
    {
        var player = reader.ReadMainPlayer() ?? throw new BotStopped("Not in a game.");
        var town = act[AreaId.RogueEncampment];
        var moor = act[AreaId.BloodMoor];
        var grid = new WorldGrid((town, WorldGrid.OriginOf(town)), (moor, WorldGrid.OriginOf(moor)));
        var entrance = Add(WorldGrid.OriginOf(moor), moor.Exits.First(e => e.Destination == AreaId.DenOfEvil).Position);
        Log($"{player.Name} at {player.Position} in {player.Area}; walkable here: {grid.IsWalkable(player.Position)}.");
        var path = grid.FindPath(player.Position, entrance);
        Log(path is null
            ? $"No path to the Den entrance at {entrance}."
            : $"Path to the Den entrance at {entrance}: {path.Count} subtiles, about {path.Count / StepLength} moves.");
        var den = act[AreaId.DenOfEvil];
        Log($"The Den: {den.Rooms.Count} rooms, {den.Presets.Count(p => p.Kind == PresetKind.Object)} objects to place it by.");
    }

    // ---- The route ----

    private void GoIntoTheDen()
    {
        var town = act[AreaId.RogueEncampment];
        var moor = act[AreaId.BloodMoor];
        var grid = new WorldGrid((town, WorldGrid.OriginOf(town)), (moor, WorldGrid.OriginOf(moor)));
        var exit = moor.Exits.First(e => e.Destination == AreaId.DenOfEvil);
        var entrance = Add(WorldGrid.OriginOf(moor), exit.Position);

        Log($"Walking to the Den of Evil entrance at {entrance}.");
        WalkTo(grid, entrance, arriveWithin: 6, fight: true);
        EnterWarp(entrance, AreaId.DenOfEvil);
    }

    private void ClearTheDen()
    {
        var den = act[AreaId.DenOfEvil];
        // The Den keeps the fixed place levels.txt gives it (checked against the game's level structure: tile 1500,1000).
        var origin = WorldGrid.OriginOf(den);
        var grid = new WorldGrid((den, origin));
        if (!grid.IsWalkable(Player().Position))
            throw new BotStopped($"The player at {Player().Position} is not on the generated Den: the map does not match the game.");
        Log($"In the Den of Evil; its top-left corner is at {origin}.");

        var rooms = den.Rooms
            .Select(r => grid.NearestWalkable(Add(origin, RoomCentre(den, r)), 25))
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

    private void WalkTo(WorldGrid grid, Point target, int arriveWithin, bool fight)
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
                Aim(player.Position, spot, aimHeight: 0);
                Thread.Sleep(40);
                if (reader.ReadHover() is { Type: UnitType.Tile })
                {
                    Log($"Entering {destination}.");
                    Native.LeftClick();
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
                Native.KeyDown(Native.VkShift);
                Native.RightClick();
                Native.KeyUpFor(Native.VkShift);
            }
            else
            {
                // Not enough mana for it: the left skill, walking up to the monster.
                Native.LeftClick();
            }

            Thread.Sleep(250);
        }

        Log($"Gave up on monster {id.Value} after 12 s.");
    }

    // True when the player has the mana for the skill at its current level. The game keeps mana in 1/256 points; a
    // skill costs (mana + lvlmana * (level - 1)) * 2^manashift / 256, at least minmana (skills.txt).
    private bool CanCast(MainPlayer player, SkillId skill)
    {
        var mana = (player.GetStat(StatId.Mana) ?? 0) / 256.0;
        return mana >= ManaCost(skill, player.Skills.TryGetValue(skill, out var points) ? Math.Max(1, (int)points.Level) : 1);
    }

    private double ManaCost(SkillId skill, int level)
    {
        if (!_skillRows.TryGetValue(skill, out var found))
        {
            found = data.GetRequiredTable("skills").Rows.Where(r => r.GetInt64("*Id") == (long)skill).Cast<ExcelRow?>().FirstOrDefault();
            _skillRows[skill] = found;
        }

        if (found is not { } row)
            return 0;
        var cost = (row.GetInt64("mana") + row.GetInt64("lvlmana") * (level - 1)) * Math.Pow(2, row.GetInt64("manashift")) / 256;
        return Math.Max(cost, row.GetInt64("minmana"));
    }

    private readonly Dictionary<SkillId, ExcelRow?> _skillRows = [];

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
                Aim(player.Position, now.Position, height);
                Thread.Sleep(150);
                if (reader.ReadHover() is { } hover && hover.Id == unit.Id)
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
        while (!window.IsForeground)
        {
            if (!announced)
                Log("Paused: the game is not the foreground window.");
            announced = true;
            if (Native.IsKeyDown(Native.VkEnd))
                throw new BotStopped("End pressed.");
            Thread.Sleep(300);
        }
    }

    private void Aim(Point player, Point target, int aimHeight)
    {
        var screen = window.ToScreen(player, target, aimHeight);
        if (!window.IsClickable(screen))
        {
            // Off screen or over the bottom panel: aim at the same direction, closer.
            var halfway = new Point((player.X + target.X) / 2, (player.Y + target.Y) / 2);
            if (halfway != target && halfway != player)
            {
                Aim(player, halfway, aimHeight);
                return;
            }
        }

        Native.SetCursorPos(screen.X, screen.Y);
    }

    private static void Press(KeyBinding binding)
    {
        var key = binding.Primary is { IsNone: false, First: not 0xFF } ? binding.Primary : binding.Secondary;
        if (key.IsNone || key.First == 0xFF)
            throw new BotStopped("A key the bot needs (force move or a belt slot) is not bound.");
        Native.PressKey(key.First);
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

    private static Point Add(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}

/// <summary>The bot stopped on purpose, with the reason.</summary>
internal sealed class BotStopped(string reason) : Exception(reason);

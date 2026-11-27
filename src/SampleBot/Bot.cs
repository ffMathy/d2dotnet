using D2Dotnet;
using D2Dotnet.Data;
using D2Dotnet.Game;
using D2Dotnet.Maps;
using D2Dotnet.Memory;

/// <summary>
/// What every task of the bot uses: reading the game, aiming, walking, travelling between levels, fighting with the
/// class's combat, drinking potions and exploring a level.
/// </summary>
/// <remarks>
/// It only reads the game's memory and sends the mouse and keys a player would: as window messages (the game can be in
/// the background; the real cursor is borrowed for about 80 ms per click or key, as the game aims at it) or synthesized
/// (the game must be in front, and the bot pauses while it is not). It stops when End is pressed, when life is low with
/// no potion left, or when it is stuck.
/// </remarks>
internal sealed class Bot(GameReader reader, GameDatabase data, MapGenerator maps, GameWindow window, IGameInput input)
{
    public const int FightRange = 18; // subtiles: monsters nearer than this are fought
    public const int StepLength = 10; // subtiles between the points the bot moves to

    private DateTime _lastPotion = DateTime.MinValue;
    private bool _retreating;
    private GeneratedAct? _act;
    private KeyBindings? _keys;

    public GameReader Reader => reader;

    public GameDatabase Data => data;

    public GameWindow Window => window;

    public IGameInput Input => input;

    public ICombat Combat { get; set; } = new ClassCombat();

    /// <summary>The key bindings, read once.</summary>
    public KeyBindings Keys => _keys ??= reader.ReadKeyBindings();

    /// <summary>The generated map of the act the player is in, generated again when the act or the game changes.</summary>
    public GeneratedAct Act
    {
        get
        {
            var context = reader.ReadMapContext() ?? throw new BotStopped("Not in a game any more.");
            var act = data.Levels.Find(context.Area)?.Act ?? throw new BotStopped($"{context.Area} is in no act.");
            if (_act is null || _act.Act != act || _act.Seed != context.Seed || _act.Difficulty != context.Difficulty)
                _act = maps.Generate(context.Seed, context.Difficulty, act);
            return _act;
        }
    }

    // ---- Reading ----

    /// <summary>The main player; stops the bot when End is pressed or the game is gone, and waits while the game must be in front and is not.</summary>
    public MainPlayer Player()
    {
        WaitWhileNotForeground();
        if (Native.IsKeyDown(Native.VkEnd))
            throw new BotStopped("End pressed.");
        return reader.ReadMainPlayer() ?? throw new BotStopped("Not in a game any more.");
    }

    public IEnumerable<Monster> Enemies(Point from, int range) =>
        reader.ReadMonsters().Where(m => m.IsEnemy && Distance(from, m.Position) <= range).OrderBy(m => Distance(from, m.Position));

    // ---- Travel ----

    /// <summary>Goes to <paramref name="area"/> through the act's exits, fighting what comes near on the way when asked.</summary>
    public void TravelTo(AreaId area, bool fight = true)
    {
        var hops = 0;
        while (Player().Area != area)
        {
            if (++hops > 40)
                throw new BotStopped($"Travelling to {area} takes too many steps.");
            var here = Player().Area;
            var route = Act.FindRoute(here, area) ?? throw new BotStopped($"No route from {here} to {area}.");
            var step = route[0];
            Log($"{here} -> {step.To.Id}{(route.Count > 1 ? $" (then {string.Join(", ", route.Skip(1).Select(s => s.To.Id))})" : "")}.");
            if (step.Exits[0].Kind == ExitKind.Warp)
                TakeWarp(step, fight);
            else
                CrossSeam(step, fight);
        }
    }

    private void TakeWarp(RouteStep step, bool fight)
    {
        var grid = new MapGrid(step.From);
        var warp = step.From.ToWorld(step.Exits.MinBy(e => Distance(Player().Position, step.From.ToWorld(e.Position))).Position);
        WalkTo(grid, warp, arriveWithin: 6, fight);
        EnterWarp(warp, step.To.Id);
    }

    // Walks to the nearest point of the border, then into the next level until the game says the player is there.
    private void CrossSeam(RouteStep step, bool fight)
    {
        var grid = new MapGrid(step.From, step.To);
        var here = Player().Position;
        var border = step.Exits.MinBy(e => Distance(here, step.From.ToWorld(e.Position)));
        var borderWorld = step.From.ToWorld(border.Position);
        var inside = step.To.Exits
            .Where(e => e.Destination == step.From.Id)
            .Select(e => step.To.ToWorld(e.Position))
            .DefaultIfEmpty(borderWorld)
            .MinBy(p => Distance(p, borderWorld));
        WalkTo(grid, inside, arriveWithin: 6, fight, until: () => Player().Area == step.To.Id);
        if (Player().Area != step.To.Id && !WaitForArea(step.To.Id, seconds: 3))
            throw new BotStopped($"Walked to {inside} but the game does not say the player is in {step.To.Id}.");
    }

    /// <summary>Clicks a level warp: sweeps the cursor around it until a warp tile is hovered, then clicks it.</summary>
    public void EnterWarp(Point around, AreaId destination)
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

    public bool WaitForArea(AreaId area, int seconds)
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

    // ---- Moving ----

    /// <summary>Walks along a path to <paramref name="target"/>, a few subtiles at a time, fighting on the way when asked.</summary>
    public void WalkTo(MapGrid grid, Point target, int arriveWithin, bool fight, Func<bool>? until = null)
    {
        var failures = 0;
        while (Distance(Player().Position, target) > arriveWithin && until?.Invoke() != true)
        {
            if (fight)
                FightNearby(FightRange);

            var path = grid.FindPath(Player().Position, target)
                ?? throw new BotStopped($"No path from {Player().Position} to {target}.");
            var step = path[Math.Min(StepLength, path.Count - 1)];
            if (MoveTo(step))
                failures = 0;
            else if (++failures >= 6)
                throw new BotStopped($"Stuck at {Player().Position} on the way to {target}.");
        }
    }

    /// <summary>Force-moves towards a nearby point; true when the player got close to it.</summary>
    public bool MoveTo(Point point)
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
            Press(Keys.ForceMove);
            Thread.Sleep(120);
        }

        return Distance(Player().Position, point) < Distance(start, point) - 2;
    }

    // ---- Exploring ----

    /// <summary>Visits every room of the player's level (nearest first), fighting what it meets, until <paramref name="until"/> holds.</summary>
    public void ExploreLevel(Func<bool>? until = null)
    {
        var level = Act[Player().Area];
        var grid = new MapGrid(level);
        if (!grid.IsWalkable(Player().Position) && grid.NearestWalkable(Player().Position, 5) is null)
            throw new BotStopped($"The player at {Player().Position} is not on the generated {level.Id}: the map does not match the game.");

        var rooms = level.Rooms
            .Select(r => grid.NearestWalkable(level.ToWorld(RoomCentre(level, r)), 25))
            .OfType<Point>()
            .Where(p => grid.FindPath(Player().Position, p) is not null)
            .ToList();
        var visited = new HashSet<int>();
        while (visited.Count < rooms.Count && until?.Invoke() != true)
        {
            var here = Player().Position;
            var next = Enumerable.Range(0, rooms.Count).Where(i => !visited.Contains(i)).MinBy(i => Distance(here, rooms[i]));
            Log($"{level.Id}: room {visited.Count + 1} of {rooms.Count}.");
            WalkTo(grid, rooms[next], arriveWithin: 8, fight: true, until: until);
            here = Player().Position;
            for (var i = 0; i < rooms.Count; i++)
            {
                if (Distance(here, rooms[i]) <= 20)
                    visited.Add(i);
            }

            visited.Add(next);
        }

        if (until?.Invoke() != true)
            FightNearby(range: 30);
    }

    public static Point RoomCentre(MapLevel level, MapRoom room) => new(
        (room.Bounds.X - level.Bounds.X) * MapLevel.SubtilesPerTile + room.Bounds.Width * MapLevel.SubtilesPerTile / 2,
        (room.Bounds.Y - level.Bounds.Y) * MapLevel.SubtilesPerTile + room.Bounds.Height * MapLevel.SubtilesPerTile / 2);

    // ---- Fighting ----

    /// <summary>The walkable grid of the player's level, made once per level.</summary>
    public MapGrid GridHere()
    {
        var level = Act[Player().Area];
        if (_grid?.Levels[0] != level)
            _grid = new MapGrid(level);
        return _grid;
    }

    private MapGrid? _grid;

    /// <summary>
    /// Fights the enemies within <paramref name="range"/> until none is left: first those a missile can reach in a
    /// straight line, then the others the player can walk to within twice the range.
    /// </summary>
    public void FightNearby(int range)
    {
        var skipped = new HashSet<UnitId>();
        while (true)
        {
            var player = Player();
            var grid = GridHere();
            var enemies = Enemies(player.Position, range).Where(m => !skipped.Contains(m.Id)).ToList();
            var target = enemies.FirstOrDefault(m => grid.IsLineClear(player.Position, m.Position)) ?? enemies.FirstOrDefault();
            if (target is null)
                return;
            if (!grid.IsLineClear(player.Position, target.Position)
                && grid.FindPath(player.Position, target.Position) is not { Count: <= 2 * FightRange })
            {
                skipped.Add(target.Id); // behind walls and far round (or no way at all): leave it
                continue;
            }

            Fight(target.Id);
        }
    }

    /// <summary>Attacks one monster with the class's combat until it dies, leaves, or <paramref name="seconds"/> pass.</summary>
    public void Fight(UnitId id, int seconds = 12)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            var player = Player();
            DrinkIfNeeded(player);
            if (reader.ReadMonsters().FirstOrDefault(m => m.Id == id) is not { IsEnemy: true } monster)
                return;

            // A wall between them: walk round it towards the monster instead of casting into the wall.
            var grid = GridHere();
            if (!grid.IsLineClear(player.Position, monster.Position))
            {
                if (grid.FindPath(player.Position, monster.Position) is not { Count: > 1 } path)
                    return;
                MoveTo(path[Math.Min(4, path.Count - 1)]);
                continue;
            }

            Combat.Attack(this, player, monster);
        }

        Log($"Gave up on monster {id.Value} after {seconds} s.");
    }

    /// <summary>
    /// True when the player has the skill and the mana for it at its current level (mana is in whole points). A skill
    /// the player has at level 0 counts as level 1: a new Sorceress's Fire Bolt reads 0 and is cast.
    /// </summary>
    public bool CanCast(MainPlayer player, SkillId skill)
    {
        if (skill is SkillId.AttackSkill or SkillId.None)
            return true;
        if (!player.Skills.TryGetValue(skill, out var points))
            return false;
        var mana = player.GetStat(StatId.Mana) ?? 0;
        return data.Skills.Find(skill) is not { } info || mana >= info.ManaCost(Math.Max(1, (int)points.Level));
    }

    // ---- Potions ----

    public void DrinkIfNeeded(MainPlayer player)
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
            // In town there is nothing to flee: the healer is the way out.
            if (life < 30 && !_retreating && !Healers.ContainsKey(player.Area))
                throw new LowLife($"Life is at {life}% and the belt has no healing potion left.");
            return;
        }

        Log($"Life {life}%, mana {mana}%: drinking from belt column {potion.Position.X + 1}.");
        Press(Keys.UseBelt[potion.Position.X]);
        _lastPotion = DateTime.UtcNow;
    }

    // The healer of each act's town: talking to them heals.
    private static readonly Dictionary<AreaId, MonsterId> Healers = new()
    {
        [AreaId.RogueEncampment] = MonsterId.Akara,
        [AreaId.LutGholein] = MonsterId.Fara,
        [AreaId.KurastDocks] = MonsterId.Ormus,
        [AreaId.PandemoniumFortress] = MonsterId.Jamella,
        [AreaId.Harrogath] = MonsterId.Malah,
    };

    /// <summary>The act's town.</summary>
    public AreaId Town => Act.Levels.First(l => Healers.ContainsKey(l.Id)).Id;

    /// <summary>
    /// In town, talks to the healer when life or mana is below <paramref name="below"/> percent: walks up to them, clicks
    /// them (talking heals), and closes the dialog with Esc.
    /// </summary>
    public void HealInTown(int below = 90)
    {
        var player = Player();
        if (!Healers.TryGetValue(player.Area, out var healerClass) || (player.LifePercent >= below && player.ManaPercent >= below))
            return;

        var level = Act[player.Area];
        var grid = new MapGrid(level);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (reader.ReadMonsters().FirstOrDefault(m => m.Class == healerClass) is not { } healer)
            {
                // Out of sight: walk towards where the town's NPCs stand, its centre.
                WalkTo(grid, level.ToWorld(new Point(level.Collision!.Width / 2, level.Collision.Height / 2)), arriveWithin: 10, fight: false,
                    until: () => reader.ReadMonsters().Any(m => m.Class == healerClass));
                continue;
            }

            if (Distance(Player().Position, healer.Position) > 5)
                WalkTo(grid, healer.Position, arriveWithin: 4, fight: false);
            var now = reader.ReadMonsters().FirstOrDefault(m => m.Id == healer.Id) ?? healer;
            Aim(Player().Position, now.Position, Window.AimHeight);
            input.LeftClick();
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < deadline && !reader.ReadMenus().NpcInteract)
                Thread.Sleep(150);
            if (reader.ReadMenus().NpcInteract)
            {
                Thread.Sleep(300);
                input.PressKey(Native.VkEscape);
                Thread.Sleep(300);
                var after = Player();
                Log($"Talked to {healerClass}: life {after.LifePercent}%, mana {after.ManaPercent}%.");
                return;
            }
        }

        Log($"Could not talk to {healerClass}; going on with life {Player().LifePercent}%.");
    }

    /// <summary>Runs back to the act's town without fighting (low life and nothing to drink), and heals there.</summary>
    public void Retreat(string reason)
    {
        Log($"{reason} Running back to town.");
        _retreating = true;
        try
        {
            TravelTo(Town, fight: false);
        }
        finally
        {
            _retreating = false;
        }

        HealInTown();
    }

    // ---- Calibration ----

    /// <summary>
    /// Checks the screen mapping by putting the cursor on a nearby unit and reading back what is hovered, and finds how
    /// far above a unit's feet to aim (about 20 to 100 pixels at a height of 720 hover a unit).
    /// </summary>
    public void Calibrate()
    {
        var units = reader.ReadMonsters()
            .Where(m => Distance(Player().Position, m.Position) is > 2 and < 16)
            .OrderBy(m => Distance(Player().Position, m.Position))
            .Take(5)
            .ToList();
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
        Log(units.Count == 0
            ? "No unit nearby to check the screen mapping with; aiming 55 pixels above the feet."
            : $"Warning: the cursor hovered none of {string.Join(", ", units.Select(u => u.Class))}; aiming 55 pixels above the feet.");
    }

    // ---- Input ----

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

    /// <summary>Aims the next click or key at <paramref name="target"/>, and returns where that is on screen.</summary>
    public (int X, int Y) Aim(Point player, Point target, int aimHeight)
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

    /// <summary>Presses a binding's first usable key.</summary>
    public void Press(KeyBinding binding)
    {
        var key = KeyOf(binding) ?? throw new BotStopped("A key the bot needs (force move or a belt slot) is not bound.");
        input.PressKey(key);
    }

    /// <summary>The virtual key of a binding (its primary key, else its secondary), or null when it has none.</summary>
    public static int? KeyOf(KeyBinding binding)
    {
        foreach (var key in new[] { binding.Primary, binding.Secondary })
        {
            if (!key.IsNone && key.First is not (0 or 0xFF))
                return key.First;
        }

        return null;
    }

    public static IEnumerable<Point> Spiral(Point centre, int radius, int step)
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

    public static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    public static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}

/// <summary>The bot stopped on purpose, with the reason.</summary>
internal sealed class BotStopped(string reason) : Exception(reason);

/// <summary>Life is low and nothing is left to drink: the bot retreats to town (see <see cref="Bot.Retreat"/>).</summary>
internal sealed class LowLife(string reason) : Exception(reason);

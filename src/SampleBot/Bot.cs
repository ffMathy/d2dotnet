using D2Dotnet;
using D2Dotnet.Data;
using D2Dotnet.Game;
using D2Dotnet.Maps;
using D2Dotnet.Memory;
using D2Dotnet.Screen;

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

    /// <summary>What the bot thinks of items.</summary>
    public ItemRules Items { get; } = new(data);

    /// <summary>The key bindings, read once.</summary>
    public KeyBindings Keys => _keys ??= reader.ReadKeyBindings();

    /// <summary>Reads the key bindings again (after binding skills).</summary>
    public void RefreshKeys() => _keys = null;

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
        var player = reader.ReadMainPlayer() ?? throw new BotStopped("Not in a game any more.");
        if (!_respawning && (player.GetStat(StatId.Life) ?? 0) == 0) // a dead character has no life stat left, or 0
        {
            Proof("died");
            throw new Died(player.Area, player.Position);
        }

        if (player.Area != _lastArea)
        {
            _lastArea = player.Area;
            Proof($"arrived in {player.Area}");
        }

        return player;
    }

    private bool _respawning;

    /// <summary>
    /// After a death: presses Esc to come back to life in the act's town, heals, and goes back for the corpse (clicking
    /// it gives the character's items back).
    /// </summary>
    public void Respawn(Died death)
    {
        Screenshot($"died in {death.Area}");
        Log($"Died in {death.Area}. Pressing Esc to come back in town.");
        _respawning = true;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && reader.ReadMainPlayer() is { } p && (p.GetStat(StatId.Life) ?? 0) == 0)
            {
                input.PressKey(Native.VkEscape);
                Thread.Sleep(1500);
            }
        }
        finally
        {
            _respawning = false;
        }

        if (reader.ReadMainPlayer() is not { } back || (back.GetStat(StatId.Life) ?? 0) == 0)
            throw new BotStopped("Pressing Esc did not bring the character back.");
        WaitForArea(back.Area, seconds: 10);

        // In a single player game a new game brings the corpse, with all its gear, to town (only the gold dropped is
        // lost): quicker and safer than going back through what killed the character.
        Log("A new game, for the corpse to come to town.");
        GameSession.NewGame(this);
        PickUpCorpse();
        HealInTown(below: 100);
    }

    /// <summary>Picks up the character's corpse when one lies near (in town after a new game).</summary>
    public void PickUpCorpse()
    {
        var grid = GridHere();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (reader.ReadPlayers().FirstOrDefault(p => p.IsCorpse) is not { } corpse)
            {
                if (attempt > 0)
                    Log("The corpse is picked up.");
                return;
            }

            WalkTo(grid, corpse.Position, arriveWithin: 2, fight: true);
            foreach (var height in new[] { 10, 25, 0, 40 })
            {
                var aim = Aim(Player().Position, corpse.Position, height);
                if (input.Peek(aim, reader.ReadHover) is { Type: UnitType.Player } hover && hover.Id == corpse.Id)
                {
                    input.LeftClick();
                    Thread.Sleep(1000);
                    break;
                }
            }
        }

        if (reader.ReadPlayers().Any(p => p.IsCorpse))
            Failure("Could not pick up the corpse.");
        else
            Log("The corpse is picked up.");
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
            ActivateWaypointHere();
            if (TakePortalBack(area))
                continue;
            if (TakeWaypointTowards(area))
                continue;
            if (Act.FindRoute(here, area) is not { } route)
            {
                // Tristram and other levels a portal leads to have no exits: take the red portal back.
                if (reader.ReadObjects().FirstOrDefault(o => o.IsRedPortal) is { } portal)
                {
                    Log($"No way from {here} to {area} on foot: taking the red portal.");
                    UseObject(portal.Class, until: () => Player().Area != here);
                    WaitForArea(Player().Area, seconds: 8);
                    continue;
                }

                throw new BotStopped($"No route from {here} to {area}.");
            }

            var step = route[0];
            Log($"{here} -> {step.To.Id}{(route.Count > 1 ? $" (then {string.Join(", ", route.Skip(1).Select(s => s.To.Id))})" : "")}.");
            if (step.Exits[0].Kind == ExitKind.Warp)
                TakeWarp(step, fight);
            else
                CrossSeam(step, fight);
        }
    }

    // ---- Waypoints ----

    // The waypoint panel (waypointspaneloriginalhd.json and its expansion version, a left panel): five act tabs from
    // (148, 201), 174 by 70 with -1 between; the act's waypoints from (152, 264), 123 apart.
    private const int WaypointTabX = 148, WaypointTabY = 201, WaypointTabWidth = 173, WaypointTabHeight = 70;
    private const int WaypointRowX = 152, WaypointRowY = 264, WaypointRowStep = 123;

    /// <summary>
    /// Activates the waypoint of the level the player is in when it is not activated yet and close by (40 subtiles):
    /// a click on it opens the waypoint menu, which is closed again.
    /// </summary>
    private void ActivateWaypointHere()
    {
        // A level with a waypoint the character lacks: that first, before going on (it saves the walk next time).
        var player = Player();
        if (_retreating || Healers.ContainsKey(player.Area) || HasWaypoint(player, player.Area)
            || data.Levels.Find(player.Area) is not { Waypoint: < 255 } || PresetWaypoint() is not { } spot)
            return;
        Log($"Activating the {player.Area} waypoint.");
        WalkTo(GridHere(), spot, arriveWithin: 6, fight: true);
        if (reader.ReadObjects().FirstOrDefault(o => o.IsWaypoint)?.Class is { } waypoint
            && UseObject(waypoint, until: () => reader.ReadMenus().Waypoint))
        {
            RememberWaypoint(player.Area);
            Thread.Sleep(400);
            ClosePanels();
        }
        else
        {
            Failure($"Could not activate the {player.Area} waypoint.");
        }
    }

    // ---- The waypoints the character has: what memory says (it can miss them), and what the bot saw ----

    private HashSet<AreaId>? _knownWaypoints;
    private readonly HashSet<AreaId> _failedWaypoints = [];

    private string WaypointsFile(string character) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Dotnet", $"waypoints-{character}.txt");

    private HashSet<AreaId> KnownWaypoints(MainPlayer player)
    {
        if (_knownWaypoints is not null)
            return _knownWaypoints;
        _knownWaypoints = [];
        try
        {
            foreach (var line in File.Exists(WaypointsFile(player.Name)) ? File.ReadAllLines(WaypointsFile(player.Name)) : [])
            {
                if (Enum.TryParse<AreaId>(line, out var area))
                    _knownWaypoints.Add(area);
            }
        }
        catch (IOException)
        {
        }

        return _knownWaypoints;
    }

    /// <summary>True when the character has the waypoint: in memory's list, or activated or used by the bot before.</summary>
    public bool HasWaypoint(MainPlayer player, AreaId area) =>
        Healers.ContainsKey(area) || player.AvailableWaypoints.Contains(area) || KnownWaypoints(player).Contains(area);

    private void RememberWaypoint(AreaId area)
    {
        var player = Player();
        if (!KnownWaypoints(player).Add(area))
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WaypointsFile(player.Name))!);
            File.WriteAllLines(WaypointsFile(player.Name), KnownWaypoints(player).Select(a => a.ToString()));
        }
        catch (IOException)
        {
        }
    }

    private DateTime _waypointsLearned = DateTime.MinValue;

    /// <summary>
    /// Takes a waypoint towards <paramref name="area"/> when the player's level has one, the player has the waypoint
    /// that leaves the fewest levels to walk, and it saves at least two of them. True when it took one.
    /// </summary>
    private bool TakeWaypointTowards(AreaId area)
    {
        var player = Player();
        var act = Act;
        if (_retreating || act[player.Area].Waypoints.Count == 0 || act.Find(area) is null)
            return false;
        // Only a waypoint close by: walking across a level to one, through its monsters, is no shortcut.
        if (PresetWaypoint() is not { } waypoint || (Distance(player.Position, waypoint) > 60 && !Healers.ContainsKey(player.Area)))
            return false; // (in town there is nothing to walk through)
        // The game reads the activated waypoints into memory when the waypoint menu opens: a new game shows only the
        // town's until then. Open it once (in town, a short walk) to learn them.
        if (Healers.ContainsKey(player.Area) && player.AvailableWaypoints.Count <= 1 && DateTime.UtcNow - _waypointsLearned > TimeSpan.FromMinutes(10))
        {
            _waypointsLearned = DateTime.UtcNow;
            if (reader.ReadObjects().FirstOrDefault(o => o.IsWaypoint)?.Class is not { } here)
            {
                WalkTo(GridHere(), waypoint, arriveWithin: 6, fight: false);
                here = reader.ReadObjects().FirstOrDefault(o => o.IsWaypoint)?.Class ?? default;
            }

            if (here != default && UseObject(here, until: () => reader.ReadMenus().Waypoint))
            {
                Thread.Sleep(500);
                player = Player();
                var raw = reader.Process.ReadBytes(unchecked(reader.Process.ModuleBaseAddress + 0x1D705F0), 0x48);
                Log($"Waypoints: {string.Join(", ", player.AvailableWaypoints)}; raw {Convert.ToHexString(raw)}.");
                ClosePanels();
            }
        }

        var walking = act.FindRoute(player.Area, area)?.Count ?? int.MaxValue;
        var best = player.AvailableWaypoints.Concat(KnownWaypoints(player)).Distinct()
            .Where(w => w != player.Area && act.Find(w) is not null && !_failedWaypoints.Contains(w))
            .Select(w => (Area: w, Left: act.FindRoute(w, area)?.Count ?? int.MaxValue))
            .Where(w => w.Left != int.MaxValue)
            .OrderBy(w => w.Left)
            .FirstOrDefault();
        if (best.Area == default || best.Left > walking - 2)
            return false;

        Log($"Taking the waypoint to {best.Area} ({best.Left} levels from {area}, instead of {walking} on foot).");
        return UseWaypoint(best.Area);
    }

    /// <summary>Uses the level's waypoint to go to <paramref name="destination"/>, which the player must have.</summary>
    public bool UseWaypoint(AreaId destination)
    {
        if (data.Levels.Find(destination) is not { Waypoint: < 255 } info)
            return false;
        var first = data.Levels.Where(l => l.Act == info.Act && l.Waypoint < 255).Min(l => l.Waypoint);
        var row = info.Waypoint - first;
        var tab = (int)info.Act;

        var waypoint = reader.ReadObjects().FirstOrDefault(o => o.IsWaypoint)?.Class;
        if (waypoint is null)
        {
            if (PresetWaypoint() is { } spot)
                WalkTo(GridHere(), spot, arriveWithin: 6, fight: true);
            waypoint = reader.ReadObjects().FirstOrDefault(o => o.IsWaypoint)?.Class;
        }

        if (waypoint is null || !UseObject(waypoint.Value, until: () => reader.ReadMenus().Waypoint))
        {
            Failure("Could not open the waypoint.");
            return false;
        }

        Thread.Sleep(400);
        Ui.ClickLeftPanel((WaypointTabX + WaypointTabWidth * tab + WaypointTabWidth / 2, WaypointTabY + WaypointTabHeight / 2));
        Thread.Sleep(300);
        Ui.ClickLeftPanel((WaypointRowX + 300, WaypointRowY + WaypointRowStep * row + WaypointRowStep / 2));
        if (WaitForArea(destination, seconds: 10))
        {
            RememberWaypoint(destination);
            return true;
        }

        Log($"The waypoint did not take the player to {destination}.");
        _failedWaypoints.Add(destination); // not activated after all: not tried again
        if (_knownWaypoints?.Remove(destination) == true)
            Log($"Forgetting the {destination} waypoint.");
        CloseDialog();
        if (reader.ReadMenus().Waypoint)
        {
            input.PressKey(Native.VkEscape);
            Thread.Sleep(300);
        }

        return false;
    }

    private GameUi? _ui;

    /// <summary>The game's panels, for the bot's own use (waypoints, the inventory).</summary>
    public GameUi Ui => _ui ??= new GameUi(this);

    private DateTime _lastRefill = DateTime.MinValue;
    private DateTime _nextRepairTrip = DateTime.MinValue;

    // Moves potions from the inventory to the belt when the belt has room and no monster is near (every 20 s at most).
    private void RefillBeltWhenQuiet()
    {
        if (DateTime.UtcNow - _lastRefill < TimeSpan.FromSeconds(20))
            return;
        _lastRefill = DateTime.UtcNow;
        var inventory = reader.ReadInventory();
        if (inventory.Belt.Items.Count >= inventory.Belt.Rows * 4 || !inventory.Items.Any(i => i.Location == ItemLocation.Inventory && i.IsPotion))
            return;
        if (Enemies(Player().Position, 25).Any())
            return;
        InventoryPanel.RefillBelt(this, Ui);
    }

    // Where the player's level places its waypoint, in world subtiles.
    private Point? PresetWaypoint()
    {
        var level = Act[Player().Area];
        return level.Waypoints.Count > 0 ? level.ToWorld(level.Waypoints[0].Position) : null;
    }

    private void TakeWarp(RouteStep step, bool fight)
    {
        var grid = new MapGrid(step.From);
        var warp = step.From.ToWorld(step.Exits.MinBy(e => Distance(Player().Position, step.From.ToWorld(e.Position))).Position);
        WalkTo(grid, warp, arriveWithin: 6, fight);
        EnterWarp(warp, step.To.Id);
    }

    // Walks into the next level: along the shortest path to the first of its subtiles that is a little way past the
    // border (searched outwards from the player, so a fenced town is entered through its gate), until the game says
    // the player is there.
    private void CrossSeam(RouteStep step, bool fight)
    {
        var grid = new MapGrid(step.From, step.To);
        var path = grid.FindPathTo(Player().Position, p => grid.LevelAt(p) == step.To && DistanceToOtherLevel(grid, step.To, p) >= 6)
            ?? throw new BotStopped($"No way from {Player().Position} into {step.To.Id}.");
        var inside = path[^1];
        WalkTo(grid, inside, arriveWithin: 4, fight, until: () => Player().Area == step.To.Id);
        if (Player().Area != step.To.Id && !WaitForArea(step.To.Id, seconds: 3))
            throw new BotStopped($"Walked to {inside} but the game does not say the player is in {step.To.Id}.");
    }

    // How far a point is from the nearest subtile that is not in the level (up to 20), along the four axes.
    private static int DistanceToOtherLevel(MapGrid grid, MapLevel level, Point p)
    {
        for (var d = 1; d <= 20; d++)
        {
            foreach (var (dx, dy) in new[] { (d, 0), (-d, 0), (0, d), (0, -d) })
            {
                if (grid.LevelAt(new Point(p.X + dx, p.Y + dy)) != level)
                    return d;
            }
        }

        return 20;
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

        Failure($"Could not find the entrance to {destination} near {around}.");
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
        Step = $"walking to {target}";
        ClearCursor();
        ClosePanels();
        var failures = 0;
        var near = 0;
        while (Distance(Player().Position, target) > arriveWithin && until?.Invoke() != true)
        {
            // A click near an NPC can open their dialog, which then takes the clicks: close it.
            if (reader.ReadMenus().IsAnyPanelOpen)
                ClosePanels();

            // MoveTo counts 3 subtiles as there: closer than that, a few tries are enough (else this loops for ever).
            if (Distance(Player().Position, target) <= 3 && ++near > 3)
                return;

            if (fight)
            {
                FightNearby(FightRange);
                Step = $"walking to {target} (picking up)";
                PickUpNearby();
                Step = $"walking to {target} (chests and shrines)";
                UseNearbyObjects();
                Step = $"walking to {target} (refilling the belt)";
                RefillBeltWhenQuiet();
            }

            // A click can take a portal or stairs on the way (a town portal left standing): the walk is over.
            if (grid.LevelAt(Player().Position) is null)
                return;
            Step = $"walking to {target} (finding the path)";

            // Not fighting (a retreat): keep away from the monsters where the map leaves room.
            var dangers = fight ? [] : Enemies(Player().Position, 40).Select(m => m.Position).ToList();
            var path = (dangers.Count > 0 ? grid.FindPath(Player().Position, target, p => Danger(p, dangers)) : grid.FindPath(Player().Position, target))
                ?? throw new Stuck($"No path from {Player().Position} to {target}.");
            // An unwalkable target (in a wall, past the stairs) gets a path to the nearest walkable subtile: when that
            // ends where the character stands, it is as close as it gets (else this loops for ever, never "stuck").
            if (Distance(path[^1], Player().Position) <= 3)
                return;
            var step = path[Math.Min(StepLength, path.Count - 1)];
            Step = $"walking to {target} (moving to {step}, {path.Count} steps left, at {Player().Position})";
            if (MoveTo(step))
            {
                failures = 0;
            }
            else if (++failures >= 10)
            {
                Screenshot("stuck");
                throw new Stuck($"Stuck at {Player().Position} on the way to {target}.");
            }
            else if (failures % 3 == 0)
            {
                // Something the map does not show is in the way (a unit, a barrel): step back a little in a free
                // direction, and go round the spot.
                grid.Block(path[Math.Min(3, path.Count - 1)], radius: 1);
                var here = Player().Position;
                var back = new[] { (6, 0), (-6, 0), (0, 6), (0, -6), (5, 5), (-5, -5), (5, -5), (-5, 5) }
                    .Select(d => new Point(here.X + d.Item1, here.Y + d.Item2))
                    .Where(grid.IsWalkable)
                    .OrderBy(_ => Random.Shared.Next())
                    .FirstOrDefault();
                if (back != default)
                    MoveTo(back);
                Log($"Blocked at {here}: going round.");
            }
        }
    }

    // How much a subtile near monsters costs on a retreat: up to 60 for one next to a monster, nothing from 15 away.
    private static float Danger(Point p, List<Point> monsters)
    {
        var cost = 0f;
        foreach (var m in monsters)
        {
            var d = (float)Distance(p, m);
            if (d < 15)
                cost += (15 - d) * 4;
        }

        return cost;
    }

    /// <summary>
    /// Puts an item held on the cursor back in the inventory (the first free place it fits), or drops it when none:
    /// with an item held, every click to walk tries to put it down instead.
    /// </summary>
    public void ClearCursor()
    {
        if (reader.ReadInventory().Items.FirstOrDefault(i => i.Location == ItemLocation.Cursor) is not { } held)
            return;
        Log($"Holding {held.Class}: putting it back.");
        InventoryPanel.Open(this);
        if (InventoryPanel.FreePlace(reader.ReadInventory().Items, held.Info?.InventoryWidth ?? 1, held.Info?.InventoryHeight ?? 1) is { } place)
        {
            Ui.ClickRightPanel(InventoryPanel.CentreOfCells(place, held.Info?.InventoryWidth ?? 1, held.Info?.InventoryHeight ?? 1));
            Thread.Sleep(300);
        }

        InventoryPanel.Close(this);
        if (reader.ReadInventory().Items.Any(i => i.Location == ItemLocation.Cursor))
        {
            // No room: drop it at the player's feet.
            Aim(Player().Position, Player().Position, aimHeight: 0);
            input.LeftClick();
            Thread.Sleep(300);
            Log($"No room for {held.Class}: dropped it.");
        }
    }

    /// <summary>Closes the open panels (inventory, stash, shop...) with Escape: a panel covers half the screen, where clicks to walk would land on it.</summary>
    public void ClosePanels()
    {
        for (var i = 0; i < 3 && reader.ReadMenus().IsAnyPanelOpen; i++)
        {
            input.PressKey(Native.VkEscape);
            Thread.Sleep(250);
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
        var quietRooms = 0;
        while (visited.Count < rooms.Count && until?.Invoke() != true)
        {
            // While levelling, 15 rooms in a row without a monster in sight: this game's monsters here are dead.
            if (until is not null && quietRooms >= 15)
            {
                Log($"{level.Id}: no monster in the last {quietRooms} rooms; it looks cleared.");
                break;
            }

            var here = Player().Position;
            var next = Enumerable.Range(0, rooms.Count).Where(i => !visited.Contains(i)).MinBy(i => Distance(here, rooms[i]));
            Log($"{level.Id}: room {visited.Count + 1} of {rooms.Count}.");
            try
            {
                WalkTo(grid, rooms[next], arriveWithin: 8, fight: true, until: until);
            }
            catch (Stuck stuck)
            {
                Log($"{stuck.Message} Skipping the room.");
            }

            // Broken gear: to town for repairs, when there is gold for them (else the trip is for nothing), once in a while.
            if (LowestDurability() < 0.15 && !_retreating && DateTime.UtcNow > _nextRepairTrip
                && reader.ReadInventory() is var purse && purse.Gold + purse.StashGold.Sum() >= 200)
            {
                _nextRepairTrip = DateTime.UtcNow.AddMinutes(10);
                throw new LowLife($"Equipment is nearly broken ({LowestDurability():P0} durability left): to town for repairs.");
            }
            quietRooms = Enemies(Player().Position, 35).Any() ? 0 : quietRooms + 1;
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
    /// Fights the enemies within <paramref name="range"/> until none is left: summoners first (shamans, nests, Blood
    /// Raven: what makes more monsters), from a little further; then those a missile can reach in a straight line, then
    /// the others the player can walk to within twice the range.
    /// </summary>
    public void FightNearby(int range)
    {
        var skipped = new HashSet<UnitId>();
        var outer = Step;
        while (true)
        {
            Step = $"{outer} (looking for a fight)";
            if (!_retreating && Player() is { Level: < DeadlyBelowLevel } me
                && Enemies(me.Position, 25).FirstOrDefault(IsDeadly) is { } deadly)
                throw new TooDangerous($"{deadly.Class} (lightning enchanted, mods [{string.Join(", ", deadly.Mods)}]) is {Distance(me.Position, deadly.Position):F0} away; too dangerous below level {DeadlyBelowLevel}.");
            var player = Player();
            var grid = GridHere();
            var enemies = Enemies(player.Position, range + SummonerReach)
                .Where(m => !skipped.Contains(m.Id) && !GivenUp(m.Id) && !IsImmune(player, m) && (Distance(player.Position, m.Position) <= range || IsSummoner(m)))
                .ToList();
            var target = enemies
                .OrderBy(m => IsSummoner(m) ? 0 : 1)
                .ThenBy(m => grid.IsLineClear(player.Position, m.Position, MissileHalfWidth) ? 0 : 1)
                .ThenBy(m => Distance(player.Position, m.Position))
                .FirstOrDefault();
            if (target is null)
                return;
            // (A search limited to the fight distance: a full one across a big level, for a monster on the far side of
            // a wall, takes seconds each.)
            if (!grid.IsLineClear(player.Position, target.Position, MissileHalfWidth)
                && grid.FindPathTo(player.Position, p => Distance(p, target.Position) <= 2, maxLength: 2 * FightRange) is null)
            {
                skipped.Add(target.Id); // behind walls and far round (or no way at all): leave it
                continue;
            }

            Fight(target.Id, seconds: IsSummoner(target) || target.Type is not (MonsterType.Normal or MonsterType.Minion) ? 30 : 12);
            skipped.Add(target.Id); // one fight each per round: one that ended at once ("no way to it") is not picked again
        }
    }

    // How far a bolt is cast from, in subtiles: Fire Bolt and the like fade out further away, and an aim much further
    // is off the screen.
    private const int CastRange = 20;

    // Half a missile's width, in subtiles: a bolt clips a wall's end or corner the thin line between centres misses.
    private const float MissileHalfWidth = 0.75f;

    // Monsters given up on, left alone until the time: picked again at once, they eat the same 30 seconds again.
    private readonly Dictionary<UnitId, DateTime> _givenUp = [];

    private bool GivenUp(UnitId id) => _givenUp.TryGetValue(id, out var until) && DateTime.UtcNow < until;

    /// <summary>
    /// True when the monster resists the attack the character would make now so much (95% or more, such as
    /// Bishibosh against Fire Bolt: fire enchanted and magic resistant) that fighting it is a waste.
    /// </summary>
    public bool IsImmune(MainPlayer player, Monster monster) => !Combat.CanHurt(this, player, monster);

    /// <summary>
    /// True for a unique, champion or super unique that is lightning enchanted (Rakanishu, packs with it): every hit on it sends out charged bolts,
    /// which kill a low level character from full life within seconds.
    /// </summary>
    public bool IsDeadly(Monster monster) =>
        monster.Mods.Contains(LightningEnchanted)
        || (monster.Type == MonsterType.SuperUnique && data.Monsters.Find(monster.Class) is { } info
            && data.SuperUniques.Where(u => u.MonsterCode == info.Code).Any(u => u.Mods.First == LightningEnchanted || u.Mods.Second == LightningEnchanted || u.Mods.Third == LightningEnchanted));

    // monumod.txt: lightning enchanted (read on a unique Foul Crow; a unique Vile Hunter pack with it killed the
    // sorceress as she stepped out of a portal).
    private const int LightningEnchanted = 17;

    // The level below which deadly monsters are left alone.
    public const int DeadlyBelowLevel = 13;

    /// <summary>
    /// A monster's resistance: its class's for the difficulty (monstats.txt), with its unique modifiers (magic
    /// resistant +40 to fire, cold and lightning; fire, cold and lightning enchanted +50 to theirs) and what its stats
    /// add. A random unique's modifiers are not known, so they are not counted.
    /// </summary>
    public int Resistance(Monster monster, StatId resistance)
    {
        var info = data.Monsters.Find(monster.Class);
        var value = info?.ResistancesIn(_act?.Difficulty ?? Difficulty.Normal).Of(resistance) ?? 0;
        // The modifiers read from memory (any unique or champion), else a super unique's from superuniques.txt.
        IEnumerable<int> mods = monster.Mods;
        if (monster.Mods.Count == 0 && monster.Type == MonsterType.SuperUnique && info is not null
            && data.SuperUniques.FirstOrDefault(u => u.MonsterCode == info.Code) is { } unique)
            mods = [unique.Mods.First, unique.Mods.Second, unique.Mods.Third];
        {
            foreach (var mod in mods)
            {
                value += (mod, resistance) switch
                {
                    (8, StatId.FireResist or StatId.ColdResist or StatId.LightningResist) => 40,
                    (9, StatId.FireResist) or (17, StatId.LightningResist) or (18, StatId.ColdResist) => 50,
                    _ => 0,
                };
            }
        }

        return value + (monster.GetStat(resistance) ?? 0);
    }

    // How much further than the fight range summoners are gone for.
    private const int SummonerReach = 10;

    /// <summary>True when the monster makes more monsters (raises the dead, lets them out of a nest, lays eggs): it is fought first.</summary>
    public bool IsSummoner(Monster monster) => data.Monsters.Find(monster.Class)?.IsSummoner == true;

    /// <summary>
    /// The nearest other enemy standing in the line from <paramref name="from"/> to the target, which a bolt would hit
    /// instead; null when the line is free of monsters.
    /// </summary>
    public static Monster? InTheWay(Point from, Monster target, IEnumerable<Monster> enemies)
    {
        double dx = target.Position.X - from.X, dy = target.Position.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1)
            return null;
        Monster? nearest = null;
        var nearestAlong = double.MaxValue;
        foreach (var other in enemies)
        {
            if (other.Id == target.Id)
                continue;
            double ox = other.Position.X - from.X, oy = other.Position.Y - from.Y;
            var along = (ox * dx + oy * dy) / length; // how far along the line
            var aside = Math.Abs(ox * dy - oy * dx) / length; // how far from it
            if (along > 0.5 && along < length - 1 && aside < InTheWayWidth && along < nearestAlong)
                (nearest, nearestAlong) = (other, along);
        }

        return nearest;
    }

    // Subtiles either side of a missile's line where a monster is hit instead of the target (a monster is about 2 wide).
    private const double InTheWayWidth = 1.6;

    /// <summary>
    /// The way to the nearest place from which the target can be shot (no wall and no other monster in the line, no
    /// monster within 3 subtiles, within <paramref name="castRange"/> of it), at most 15 steps; null when there is none.
    /// </summary>
    public IReadOnlyList<Point>? WayToClearShot(MapGrid grid, Monster target, IReadOnlyList<Monster> enemies, int castRange = CastRange - 1)
    {
        var others = enemies.Where(e => e.Id != target.Id).ToList();
        return grid.FindPathTo(Player().Position, p =>
            Distance(p, target.Position) is var d && d <= castRange && d >= 8
            && others.All(e => Distance(p, e.Position) >= 3)
            && grid.IsLineClear(p, target.Position, MissileHalfWidth)
            && InTheWay(p, target, others) is null, maxLength: 15);
    }

    /// <summary>Attacks one monster with the class's combat until it dies, leaves, or <paramref name="seconds"/> pass.</summary>
    public void Fight(UnitId id, int seconds = 12)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var started = DateTime.UtcNow;
        var attacks = 0;
        var repositions = 0;
        var castsWithoutDamage = 0;
        var movesWithoutAttack = 0;
        var kites = 0;
        var lastLife = int.MaxValue;
        var startMana = Player().GetStat(StatId.Mana) ?? 0;
        int? startLife = reader.ReadMonsters().FirstOrDefault(m => m.Id == id)?.LifePercent;
        var first = reader.ReadMonsters().FirstOrDefault(m => m.Id == id);
        var name = first?.Class.ToString() ?? "?";
        Step = $"fighting {name} {id.Value}";
        // Bosses, uniques, champions and summoners are pictured: the start, every 5 seconds, and the end.
        var notable = first is not null && (first.Type is not (MonsterType.Normal or MonsterType.Minion) || IsSummoner(first));
        if (notable)
        {
            Proof($"fighting {name}");
            Log($"{name} {id.Value} is {first!.Type}, mods [{string.Join(", ", first.Mods)}].");
        }
        var nextPicture = DateTime.UtcNow.AddSeconds(5);
        string outcome;
        while (true)
        {
            if (notable && DateTime.UtcNow >= nextPicture)
            {
                Proof($"fighting {name}", log: false);
                nextPicture = DateTime.UtcNow.AddSeconds(5);
            }

            if (DateTime.UtcNow >= deadline)
            {
                outcome = "gave up";
                break;
            }

            var player = Player();
            DrinkIfNeeded(player);
            if (reader.ReadMonsters().FirstOrDefault(m => m.Id == id) is not { IsEnemy: true } monster)
            {
                outcome = reader.ReadCorpses().Any(m => m.Id == id) ? "killed" : "lost";
                break;
            }

            if (IsImmune(player, monster))
            {
                outcome = "immune to every attack";
                break;
            }

            // A summoner come into reach goes first (FightNearby picks it next).
            var grid = GridHere();
            if (!IsSummoner(monster)
                && Enemies(player.Position, FightRange + SummonerReach).Any(m => IsSummoner(m) && !GivenUp(m.Id) && !IsImmune(player, m) && grid.IsLineClear(player.Position, m.Position, MissileHalfWidth)))
            {
                outcome = "left for a summoner";
                break;
            }

            // A wall between them: to the nearest spot with a clear shot from a distance, not up to the monster (in
            // cluttered ground the line only clears right next to it); with none near, round the wall towards it.
            // After a few such moves without a cast (the wide check too strict among rocks and bushes), the thin line
            // is enough.
            var blocked = movesWithoutAttack < 4
                ? !grid.IsLineClear(player.Position, monster.Position, MissileHalfWidth)
                : !grid.IsLineClear(player.Position, monster.Position);
            if (blocked)
            {
                movesWithoutAttack++;
                if (movesWithoutAttack <= 4 && WayToClearShot(grid, monster, Enemies(player.Position, 40).ToList()) is { Count: > 1 } shot)
                {
                    MoveTo(shot[Math.Min(8, shot.Count - 1)]);
                    continue;
                }

                if (grid.FindPath(player.Position, monster.Position) is not { Count: > 1 } path)
                {
                    outcome = "no way to it";
                    break;
                }

                MoveTo(path[Math.Min(4, path.Count - 1)]);
                continue;
            }

            // Too far for a bolt to reach (summoners are gone for from further than the fight range, and an aim off
            // the screen is pulled in): walk closer first.
            if (Distance(player.Position, monster.Position) > CastRange
                && grid.FindPath(player.Position, monster.Position) is { Count: > 1 } nearer)
            {
                // Only as far as casting range: the first point of the path within it, at most 6 steps on.
                // (At least 4 steps: MoveTo counts 3 subtiles as there, so a nearer point would not move at all.)
                var stop = nearer.Select((p, i) => (p, i)).FirstOrDefault(x => Distance(x.p, monster.Position) <= CastRange - 1).i;
                MoveTo(nearer[Math.Min(nearer.Count - 1, Math.Clamp(stop == 0 ? 6 : stop, 4, 6))]);
                continue;
            }

            // A monster at arm's length hits every second while the target is cast at (and a bolt cast past it may hit
            // it instead): it goes first.
            // A monster at arm's length: step back out of reach first (a few times a fight), then deal with it.
            if (kites < 3 && Enemies(player.Position, 4).Any()
                && Enemies(player.Position, 30).ToList() is var near
                && grid.FindPathTo(player.Position, p => near.All(m => Distance(p, m.Position) >= 8)
                    && grid.IsLineClear(p, monster.Position, MissileHalfWidth), maxLength: 12) is { Count: > 1 } away)
            {
                kites++;
                MoveTo(away[^1]);
                continue;
            }

            if (Enemies(player.Position, 3).FirstOrDefault(m => m.Id != monster.Id) is { } close)
            {
                Combat.Attack(this, player, close);
                attacks++;
                continue;
            }

            // Another monster in the line (Blood Raven's zombies, a shaman's Fallen): a bolt would hit it instead. Step
            // to where the line is free; with no such place near, or after a few tries, clear the way by hitting the
            // one in front.
            if (Combat.StopsAtFirstHit(this, player)
                && Enemies(player.Position, 40).ToList() is var around
                && InTheWay(player.Position, monster, around) is { } blocker)
            {
                if (repositions < 4 && WayToClearShot(grid, monster, around) is { Count: > 1 } way)
                {
                    repositions++;
                    Log($"{blocker.Class} is in the way of {name}: moving {way.Count - 1} steps for a clear shot.");
                    MoveTo(way[Math.Min(8, way.Count - 1)]);
                    continue;
                }

                Combat.Attack(this, player, blocker);
                attacks++;
                continue;
            }

            // Casts that do nothing: something the map does not show is in the way (a low stone wall, a fence end).
            // Walk a few steps towards the monster and try again from there.
            if ((monster.LifePercent ?? 100) < lastLife)
            {
                (lastLife, castsWithoutDamage) = (monster.LifePercent ?? 100, 0);
                // Hurting it: keep at it a while longer (up to a minute and a half in all), not give up at 5%.
                var later = DateTime.UtcNow.AddSeconds(6);
                if (later > deadline && later < started.AddSeconds(90))
                    deadline = later;
            }
            else if (castsWithoutDamage >= 8 && grid.FindPath(player.Position, monster.Position) is { Count: > 3 } closer)
            {
                var others = Enemies(player.Position, 40).ToList();
                Log($"{castsWithoutDamage} attacks did not hurt {name}: moving closer. It is {Distance(player.Position, monster.Position):F1} away at {monster.Position} (me {player.Position}), " +
                    $"line {(grid.IsLineClear(player.Position, monster.Position, MissileHalfWidth) ? "clear" : "blocked")}, in the way {InTheWay(player.Position, monster, others)?.Class.ToString() ?? "nothing"}, " +
                    $"close {string.Join("/", Enemies(player.Position, 3).Select(m => m.Class))}, mana {player.GetStat(StatId.Mana)}, right skill {player.RightSkill}, " +
                    $"resistances fire {Resistance(monster, StatId.FireResist)} cold {Resistance(monster, StatId.ColdResist)} lightning {Resistance(monster, StatId.LightningResist)}, type {monster.Type}.");
                castsWithoutDamage = 0;
                // Another spot with a clear shot (not this one), else a few steps closer.
                var here = player.Position;
                var elsewhere = grid.FindPathTo(here, p =>
                    Distance(p, here) >= 3 && Distance(p, monster.Position) is var d && d >= 4 && d <= CastRange - 2
                    && grid.IsLineClear(p, monster.Position, MissileHalfWidth) && InTheWay(p, monster, others) is null, maxLength: 12);
                MoveTo(elsewhere is { Count: > 1 } ? elsewhere[^1] : closer[Math.Min(4, closer.Count - 2)]);
                continue;
            }

            Combat.Attack(this, player, monster);
            attacks++;
            castsWithoutDamage++;
            movesWithoutAttack = 0;
        }

        var endLife = reader.ReadMonsters().FirstOrDefault(m => m.Id == id)?.LifePercent ?? 0;
        if (outcome == "gave up")
        {
            _givenUp[id] = DateTime.UtcNow.AddMinutes(1);
            Screenshot($"gave up on {name}");
        }
        if (notable)
            Proof($"{name} {outcome}");
        Log($"{name} {id.Value}: {outcome} after {(DateTime.UtcNow - started).TotalSeconds:F1} s, {attacks} attacks, " +
            $"mana {startMana} -> {Player().GetStat(StatId.Mana)}, its life {startLife}% -> {(outcome == "killed" ? 0 : endLife)}%.");
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

    // ---- Objects and NPCs ----

    /// <summary>Where the player's level places an object (its first preset of that class), in world subtiles; null when it has none.</summary>
    public Point? PresetPosition(ObjectId objectClass, AreaId? area = null)
    {
        var level = Act[area ?? Player().Area];
        return level.Presets.FirstOrDefault(p => p.Kind == PresetKind.Object && p.ClassId == (int)objectClass) is { } preset && preset != default
            ? level.ToWorld(preset.Position)
            : null;
    }

    /// <summary>
    /// Walks up to the nearest object of the class (it must be in sight), and clicks it once the game says it is under
    /// the cursor; then waits up to 3 seconds for <paramref name="until"/>. False when there is no such object or it
    /// could not be clicked.
    /// </summary>
    public bool UseObject(ObjectId objectClass, Func<bool>? until = null)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var player = Player();
            DrinkIfNeeded(player);
            if (reader.ReadObjects().Where(o => o.Class == objectClass).MinBy(o => Distance(player.Position, o.Position)) is not { } target)
            {
                // Not in sight yet: walk to where the level places it, then look again.
                if (attempt == 0 && PresetPosition(objectClass) is { } preset)
                {
                    WalkTo(GridHere(), preset, arriveWithin: 6, fight: !_retreating,
                        until: () => reader.ReadObjects().Any(o => o.Class == objectClass && Distance(Player().Position, o.Position) < 15));
                    continue;
                }

                Failure($"No {objectClass} in sight.");
                return false;
            }
            if (attempt > 0)
            {
                // Something (an NPC, a monster) stood in front of it: come at it from another side.
                var grid = GridHere();
                var sides = new[] { new Point(4, 0), new Point(0, 4), new Point(-4, 0), new Point(0, -4) };
                var side = sides[(attempt - 1) % sides.Length];
                var spot = grid.NearestWalkable(new Point(target.Position.X + side.X, target.Position.Y + side.Y)) ?? target.Position;
                WalkTo(grid, spot, arriveWithin: 1, fight: !_retreating);
            }
            else if (Distance(player.Position, target.Position) > 5)
            {
                WalkTo(GridHere(), target.Position, arriveWithin: 4, fight: !_retreating);
            }

            // Points up and around the object, until the game says it is under the cursor.
            var offsets = new[] { new Point(0, 0), new Point(1, 0), new Point(0, 1), new Point(-1, 0), new Point(0, -1) };
            var clicked = false;
            foreach (var offset in offsets)
            {
                foreach (var height in new[] { 40, 20, 60, 5, 80 })
                {
                    var aim = Aim(Player().Position, new Point(target.Position.X + offset.X, target.Position.Y + offset.Y), height);
                    if (input.Peek(aim, reader.ReadHover) is not { Type: UnitType.Object } hover || hover.Id != target.Id)
                        continue;
                    input.LeftClick();
                    clicked = true;
                    var deadline = DateTime.UtcNow.AddSeconds(3);
                    while (until is not null && !until() && DateTime.UtcNow < deadline)
                        Thread.Sleep(150);
                    if (until is null || until())
                        return true;
                    break;
                }

                if (clicked)
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks up to the NPC and talks to them: opens their dialog, lets any quest speech run for up to
    /// <paramref name="speech"/> seconds, and closes it with Esc. False when the NPC is not in sight or the dialog did
    /// not open.
    /// </summary>
    public bool TalkTo(MonsterId npc, int speech = 2)
    {
        if (!OpenDialog(npc))
            return false;
        Thread.Sleep(speech * 1000);
        CloseDialog();
        return true;
    }

    /// <summary>Walks up to the NPC and clicks them, leaving their dialog open; false when it did not open.</summary>
    public bool OpenDialog(MonsterId npc)
    {
        var level = Act[Player().Area];
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Step = $"opening {npc}'s dialog, attempt {attempt}";
            if (reader.ReadMonsters().FirstOrDefault(m => m.Class == npc) is not { } who)
            {
                // Out of sight (units are in memory only near the player): walk to where the level places the NPC, else
                // towards the town's centre, where the NPCs stand.
                var home = level.Presets.FirstOrDefault(p => p.Kind == PresetKind.Npc && p.ClassId == (int)npc) is { } preset && preset != default
                    ? level.ToWorld(preset.Position)
                    : level.ToWorld(new Point(level.Collision!.Width / 2, level.Collision.Height / 2));
                Log($"{npc} is not in sight: walking to {home}.");
                WalkTo(GridHere(), home, arriveWithin: 5, fight: false, until: () => reader.ReadMonsters().Any(m => m.Class == npc));
                continue;
            }

            Log($"{npc}: attempt {attempt}, at {who.Position}, {Distance(Player().Position, who.Position):F1} away, line {(GridHere().IsLineClear(Player().Position, who.Position) ? "clear" : "blocked")}.");
            // Up to the NPC on a side with nothing between (not behind a tent wall): the nearest such spot, another
            // one after a failed try.
            var grid = GridHere();
            var here = Player().Position;
            if (Distance(here, who.Position) > 5 || !grid.IsLineClear(here, who.Position) || attempt > 0)
            {
                var tried = attempt;
                var way = grid.FindPathTo(here, p =>
                    Distance(p, who.Position) is var d && d <= 4 && d >= 2 && grid.IsLineClear(p, who.Position)
                    && (tried == 0 || Distance(p, here) >= 3), maxLength: 250);
                if (way is { Count: > 0 })
                    WalkTo(grid, way[^1], arriveWithin: 1, fight: false);
                else
                    WalkTo(grid, who.Position, arriveWithin: 4, fight: false);
            }

            // NPCs wander: aim at where they are now, at heights up their body, and click once the game says the NPC
            // is under the cursor (a click on the ground beside them walks instead).
            foreach (var height in new[] { Window.AimHeight, 40, 70, 25, 90 })
            {
                var now = reader.ReadMonsters().FirstOrDefault(m => m.Id == who.Id) ?? who;
                var aim = Aim(Player().Position, now.Position, height);
                if (input.Peek(aim, reader.ReadHover) is not { Type: UnitType.Monster } hover || hover.Id != who.Id)
                    continue;
                input.LeftClick();
                var deadline = DateTime.UtcNow.AddSeconds(4);
                while (DateTime.UtcNow < deadline && !reader.ReadMenus().NpcInteract)
                    Thread.Sleep(150);
                if (reader.ReadMenus().NpcInteract)
                    return true;
                break;
            }
        }

        Failure($"Could not open {npc}'s dialog.");
        return false;
    }

    // The area the last town portal was opened in: the portal stays in town, a way back there.
    private AreaId? _portalArea;

    private (AreaId Area, DateTime At)? _lastRetreat;
    private readonly Dictionary<AreaId, DateTime> _avoided = [];

    /// <summary>True while an area is avoided (the character had to leave it twice in a row).</summary>
    public bool IsAvoided(AreaId area) => _avoided.TryGetValue(area, out var until) && DateTime.UtcNow < until;

    /// <summary>Forgets the way back through the town portal (it leads somewhere too dangerous).</summary>
    public void ForgetPortal() => _portalArea = null;

    /// <summary>
    /// From town, goes back through the town portal the character came home by, when its area is the destination or
    /// on the way to it (fewer levels from there than from town). False when there is no such portal.
    /// </summary>
    private bool TakePortalBack(AreaId destination)
    {
        if (_portalArea is not { } there || Player().Area != Town || IsAvoided(there))
            return false;
        var fromThere = there == destination ? 0 : Act.FindRoute(there, destination)?.Count ?? int.MaxValue;
        var fromTown = Act.FindRoute(Town, destination)?.Count ?? int.MaxValue;
        if (fromThere >= fromTown || !reader.ReadObjects().Any(o => o.Class == ObjectId.TownPortal))
        {
            _portalArea = null;
            return false;
        }

        Log($"Back to {there} through the town portal.");
        _portalArea = null;
        if (!UseObject(ObjectId.TownPortal, until: () => reader.ReadMainPlayer() is { } p && p.Area == there))
        {
            Failure("Could not take the town portal back.");
            return false;
        }

        WaitWhileLoading();
        return true;
    }

    /// <summary>The town portal scrolls carried: the Tome of Town Portal's charges and loose scrolls.</summary>
    public int TownPortals() =>
        reader.ReadInventory().Items.Where(i => i.Location == ItemLocation.Inventory)
            .Sum(i => i.Class == ItemId.TomeOfTownPortal ? i.GetStat(StatId.Quantity) ?? 0 : i.Class == ItemId.ScrollOfTownPortal ? 1 : 0);

    /// <summary>
    /// Opens a town portal (a right click on the Tome of Town Portal, else a scroll) and steps through it: false when
    /// there is no scroll, or the portal did not take the player to town.
    /// </summary>
    public bool TownPortalHome()
    {
        var items = reader.ReadInventory().Items.Where(i => i.Location == ItemLocation.Inventory).ToList();
        var book = items.FirstOrDefault(i => i.Class == ItemId.TomeOfTownPortal && (i.GetStat(StatId.Quantity) ?? 0) > 0)
            ?? items.FirstOrDefault(i => i.Class == ItemId.ScrollOfTownPortal);
        if (book is null)
            return false;

        Step = "opening a town portal";
        var from = Player().Area;
        DrinkIfNeeded(Player());
        ClearCursor();
        InventoryPanel.Open(this);
        Ui.ClickRightPanel(InventoryPanel.CentreOf(book), right: true);
        Thread.Sleep(300);
        InventoryPanel.Close(this);
        ClosePanels();

        // The portal rises in front of the player within a second or so.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !reader.ReadObjects().Any(o => o.Class == ObjectId.TownPortal && Distance(Player().Position, o.Position) < 8))
        {
            DrinkIfNeeded(Player()); // the blows go on while the portal rises
            Thread.Sleep(150);
        }
        if (!reader.ReadObjects().Any(o => o.Class == ObjectId.TownPortal && Distance(Player().Position, o.Position) < 8))
        {
            Failure("The town portal did not open.");
            return false;
        }

        if (!UseObject(ObjectId.TownPortal, until: () => reader.ReadMainPlayer() is { } p && p.Area == Town))
        {
            Failure("Could not go through the town portal.");
            return false;
        }

        WaitWhileLoading();
        if (Player().Area == Town)
            _portalArea = from;
        return Player().Area == Town;
    }

    // Waits for the loading screen between areas to go.
    private void WaitWhileLoading()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        Thread.Sleep(300);
        while (DateTime.UtcNow < deadline && reader.ReadMenus().LoadingScreen)
            Thread.Sleep(200);
    }

    /// <summary>The worn items' lowest durability, as a share of their maximum (1 when nothing wears out).</summary>
    public double LowestDurability() =>
        reader.ReadInventory().Items.Where(i => i.Location == ItemLocation.Equipped)
            .Select(i => (Now: i.GetStat(StatId.Durability) ?? 0, Max: i.GetStat(StatId.MaxDurability) ?? 0))
            .Where(d => d.Max > 0).Select(d => (double)d.Now / d.Max).DefaultIfEmpty(1).Min();

    /// <summary>Logs a failure with a screenshot of it (in the trail when one runs), so it can be seen what went wrong.</summary>
    public void Failure(string message)
    {
        Log(message);
        if (TrailFolder is null)
            Screenshot(message);
        else
            Proof(message);
    }

    /// <summary>Closes an NPC dialog and anything it opened (a speech, a shop) with Esc.</summary>
    public void CloseDialog()
    {
        for (var i = 0; i < 3 && (reader.ReadMenus() is { NpcInteract: true } or { NpcShop: true }); i++)
        {
            input.PressKey(Native.VkEscape);
            Thread.Sleep(300);
        }
    }

    /// <summary>True when the player carries an item of the class (inventory, belt or cursor).</summary>
    public bool HasItem(ItemId item) =>
        reader.ReadInventory().Items.Any(i => i.Class == item && i.Location is ItemLocation.Inventory or ItemLocation.Cursor or ItemLocation.Equipped);

    // ---- Picking up ----

    private readonly HashSet<UnitId> _unreachableItems = [];
    private readonly Dictionary<UnitId, int> _pickTries = [];

    // What the bot picks up: supplies, items worth selling or wearing (ItemRules), quest items; what does not fit in
    // the inventory is left (gold and potions need no room: gold is counted, potions go to the belt first).
    private bool IsWanted(Item item, MainPlayer player, Inventory inventory)
    {
        var equipped = inventory.Items.Where(i => i.Location == ItemLocation.Equipped).ToList();
        if (!Items.IsWorthPickingUp(item, player, equipped, inventory.Gold + inventory.StashGold.Sum()))
            return false;
        if (item.Class == ItemId.Gold)
            return true;
        var fits = TownChores.Fits(inventory.Items, item.Info?.InventoryWidth ?? 1, item.Info?.InventoryHeight ?? 1);
        if (item.IsPotion)
        {
            // Into the belt when it has room; else into the inventory only up to a reserve, and only where it fits.
            var beltRoom = inventory.Belt.Rows * 4 - inventory.Belt.Items.Count;
            var carried = inventory.Items.Count(i => i.Location == ItemLocation.Inventory && i.IsPotion);
            return (beltRoom > 0 && inventory.Belt.Items.Any(b => b.Class == item.Class)) || (fits && carried < PotionReserve);
        }

        // A valuable item with no room for it: to town to sell (by town portal), once in a while.
        if (!fits && item.Quality is ItemQuality.Magic or ItemQuality.Rare or ItemQuality.Set or ItemQuality.Unique
            && !_retreating && DateTime.UtcNow > _nextSellingTrip && TownPortals() > 0)
        {
            _nextSellingTrip = DateTime.UtcNow.AddMinutes(5);
            throw new LowLife($"No room for the {item.Quality} {item.Class}: to town to sell.");
        }

        return fits;
    }

    // Potions carried in the inventory besides the belt, at most.
    private const int PotionReserve = 12;
    private DateTime _nextSellingTrip = DateTime.MinValue;

    /// <summary>Quest items the bot picks up too (the Scroll of Inifuss, the Horadric Malus...).</summary>
    public HashSet<ItemId> QuestItems { get; } = [];

    /// <summary>Picks up the potions, gold and quest items on the ground within <paramref name="range"/>, nearest first.</summary>
    public void PickUpNearby(int range = 15)
    {
        for (var picked = 0; picked < 12; picked++)
        {
            var player = Player();
            var inventory = reader.ReadInventory();
            var item = inventory.Items
                .Where(i => i.Location == ItemLocation.Ground && !_unreachableItems.Contains(i.Id) && Distance(player.Position, i.Position) <= range)
                .Where(i => QuestItems.Contains(i.Class) || IsWanted(i, player, inventory))
                .MinBy(i => Distance(player.Position, i.Position));
            if (item is null)
                return;

            var step = Step;
            if (Distance(player.Position, item.Position) > 3)
                WalkTo(GridHere(), item.Position, arriveWithin: 2, fight: false);
            Step = $"{step} (picking up {item.Class})";

            // Items lie on the ground: aim low, and click only once the game says the item is under the cursor.
            var clicked = false;
            foreach (var offset in new[] { new Point(0, 0), new Point(1, 0), new Point(0, 1), new Point(-1, 0), new Point(0, -1) })
            {
                foreach (var height in new[] { 5, 15, 0, 25 })
                {
                    var aim = Aim(Player().Position, new Point(item.Position.X + offset.X, item.Position.Y + offset.Y), height);
                    if (input.Peek(aim, reader.ReadHover) is { Type: UnitType.Item } hover && hover.Id == item.Id)
                    {
                        input.LeftClick();
                        clicked = true;
                        break;
                    }
                }

                if (clicked)
                    break;
            }

            // Gold is small and not always reported under the cursor: a plain low click on it (a miss only walks a step).
            if (!clicked && item.Class == ItemId.Gold)
            {
                Aim(Player().Position, item.Position, 5);
                input.LeftClick();
                clicked = true;
            }

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (clicked && DateTime.UtcNow < deadline && reader.ReadInventory().Items.Any(i => i.Id == item.Id && i.Location == ItemLocation.Ground))
                Thread.Sleep(100);
            Step = step;
            if (!clicked || reader.ReadInventory().Items.Any(i => i.Id == item.Id && i.Location == ItemLocation.Ground))
            {
                // Items lying on top of each other: the click can take the other one. Three tries, then leave it.
                _pickTries[item.Id] = _pickTries.GetValueOrDefault(item.Id) + 1;
                if (clicked && _pickTries[item.Id] < 3)
                    continue;
                _unreachableItems.Add(item.Id);
                Failure($"Could not pick up {item.Class} at {item.Position} ({(clicked ? "clicked, but it stayed" : "never under the cursor")}).");
            }
            else
                Log($"Picked up {(item.Quality is ItemQuality.Magic or ItemQuality.Rare or ItemQuality.Set or ItemQuality.Unique ? item.Quality + " " : "")}{item.Class}.");
        }
    }

    // ---- Chests and shrines ----

    private readonly HashSet<UnitId> _usedObjects = [];

    /// <summary>
    /// Opens the chests within 12 subtiles and uses the shrines that help now, when no monster is within 15: experience,
    /// skill, armor and combat always; health, mana and refill when life or mana is low; never the harmful ones (fire,
    /// monster, exploding, poison) or the exchange and portal ones. What a chest drops is picked up after.
    /// </summary>
    public void UseNearbyObjects()
    {
        var player = Player();
        if (_retreating || Healers.ContainsKey(player.Area) || Enemies(player.Position, 15).Any())
            return;
        var life = player.LifePercent ?? 100;
        var mana = player.ManaPercent ?? 100;
        bool Helps(ShrineType shrine) => shrine switch
        {
            ShrineType.ExperienceShrine or ShrineType.SkillShrine or ShrineType.ArmorShrine or ShrineType.CombatShrine
                or ShrineType.ResistFireShrine or ShrineType.ResistColdShrine or ShrineType.ResistLightningShrine
                or ShrineType.ResistPoisonShrine or ShrineType.GemShrine => true,
            ShrineType.HealthShrine => life < 75,
            ShrineType.ManaShrine or ShrineType.ManaRegenShrine => mana < 75,
            ShrineType.RefillShrine => life < 75 || mana < 75,
            _ => false,
        };

        var target = reader.ReadObjects()
            .Where(o => o.IsSelectable && !_usedObjects.Contains(o.Id) && Distance(player.Position, o.Position) <= 12
                        && (o.IsChest || o.IsSuperChest || (o.Shrine is { } shrine && Helps(shrine))))
            .MinBy(o => Distance(player.Position, o.Position));
        if (target is null)
            return;
        _usedObjects.Add(target.Id);
        Log(target.Shrine is { } kind ? $"Using a {kind.GetName() ?? kind.ToString()}." : $"Opening a {target.Class}.");
        if (UseObject(target.Class))
            PickUpNearby();
    }

    // ---- Potions ----

    public void DrinkIfNeeded(MainPlayer player)
    {
        if (DateTime.UtcNow - _lastPotion < TimeSpan.FromSeconds(1))
            return;
        var life = player.LifePercent ?? 100;
        var mana = player.ManaPercent ?? 100;
        var belt = reader.ReadInventory().Belt;

        // Close to dying with monsters about: out by town portal now; potions heal too slowly under a pack's blows.
        // In a crowd the blows come faster than a portal opens: leave earlier.
        // Below 60% that is when the belt has no healing potion left (drinking from the inventory is slow), a unique or
        // champion is at arm's length (they hit hard: Spine Brow with a Might aura), or five monsters crowd in.
        if (!_retreating && !Healers.ContainsKey(player.Area) && TownPortals() > 0 && Enemies(player.Position, 15).ToList() is { Count: > 0 } about)
        {
            var crowd = about.Count(m => Distance(player.Position, m.Position) <= 10) >= 5;
            var bossClose = about.Any(m => m.Type is MonsterType.Unique or MonsterType.SuperUnique or MonsterType.Champion && Distance(player.Position, m.Position) <= 5);
            // Dry: no healing potion in the belt, and fewer than two in the inventory to drink from.
            var beltDry = belt.FindPotion(PotionType.Healing) is null && belt.FindPotion(PotionType.Rejuvenation) is null
                && reader.ReadInventory().Items.Count(i => i.Location == ItemLocation.Inventory && (i.IsHealingPotion || i.IsRejuvenationPotion)) < 2;
            var limit = crowd || bossClose || beltDry ? 60 : 30;
            if (life < limit)
                throw new LowLife($"Life is at {life}% with {about.Count} monsters about{(bossClose ? ", a boss close" : "")}{(beltDry ? ", the belt dry" : "")}.");
        }

        PotionType? need = life < 40 ? PotionType.Rejuvenation : life < 55 ? PotionType.Healing : mana < 15 ? PotionType.Mana : null;
        if (need is null)
            return;
        // Out of mana with nothing to drink for it: a caster cannot fight. With no monster near, mana comes back on its
        // own (wait for it); with monsters within 10 subtiles, back to town (the healer restores mana).
        if (need == PotionType.Mana && belt.FindPotion(PotionType.Mana) is null && belt.FindPotion(PotionType.Rejuvenation) is null
            && mana < 5 && !_retreating && !Healers.ContainsKey(player.Area) && player.Class == CharacterClass.Sorceress)
        {
            if (Enemies(player.Position, 10).Any())
                throw new LowLife($"Mana is at {mana}% with monsters near, and the belt has no mana potion left.");
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < deadline && (Player().ManaPercent ?? 100) < 50 && !Enemies(Player().Position, 10).Any())
                Thread.Sleep(500);
            _lastPotion = DateTime.UtcNow;
            return;
        }
        var potion = belt.FindPotion(need.Value) ?? (need == PotionType.Rejuvenation ? belt.FindPotion(PotionType.Healing) : null);
        // None in the belt: one from the inventory (potions picked up while the belt was full wait there).
        if (potion is null && (InventoryPanel.Drink(this, Ui, need.Value) || (need == PotionType.Rejuvenation && InventoryPanel.Drink(this, Ui, PotionType.Healing))))
        {
            Log($"Life {life}%, mana {mana}%: drinking a {need} potion from the inventory.");
            _lastPotion = DateTime.UtcNow;
            return;
        }

        if (potion is null)
        {
            // In town there is nothing to flee: the healer is the way out.
            if (life < 40 && !_retreating && !Healers.ContainsKey(player.Area))
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
        if (!Healers.TryGetValue(player.Area, out var healer) || (player.LifePercent >= below && player.ManaPercent >= below))
            return;
        if (TalkTo(healer, speech: 0))
            Log($"Talked to {healer}: life {Player().LifePercent}%, mana {Player().ManaPercent}%.");
        else
            Failure($"Could not talk to {healer}; going on with life {Player().LifePercent}%.");
    }

    /// <summary>Runs back to the act's town without fighting (low life and nothing to drink), and heals there.</summary>
    public void Retreat(string reason)
    {
        // A second retreat from the same area within three minutes: what is there is too much for now; the area is
        // avoided for a while, and its portal not taken back.
        var leaving = Player().Area;
        var twice = _lastRetreat is { } last && last.Area == leaving && DateTime.UtcNow - last.At < TimeSpan.FromMinutes(3);
        _lastRetreat = (leaving, DateTime.UtcNow);
        if (twice && !Healers.ContainsKey(leaving))
        {
            _avoided[leaving] = DateTime.UtcNow.AddMinutes(10);
            Log($"{leaving}: retreating from it twice in a row; avoiding it for ten minutes.");
        }

        _retreating = true;
        try
        {
            Log($"{reason} Leaving for town.");
            if (Player().Area != Town && TownPortalHome())
            {
                Log("Back in town through a town portal.");
            }
            else
            {
                Log($"{reason} Running back to town.");
                TravelTo(Town, fight: false);
            }
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

    public static void Log(string message)
    {
        _lastLog = DateTime.UtcNow;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private static DateTime _lastLog = DateTime.UtcNow;

    /// <summary>What the bot is doing now, for the watchdog (see <see cref="StartWatchdog"/>).</summary>
    public static string Step { get; set; } = "starting";

    /// <summary>
    /// Logs, with a screenshot, when nothing was logged for <paramref name="seconds"/>: the step the bot is in and the
    /// main thread's place, so a hang shows where it is.
    /// </summary>
    public void StartWatchdog(int seconds = 45)
    {
        Point? lastPosition = null;
        _watchdog = new Timer(_ =>
        {
            // A long quiet walk is not a hang: only standing still with nothing logged is.
            var position = reader.ReadMainPlayer()?.Position;
            var moved = position is { } p && lastPosition is { } q && Distance(p, q) > 3;
            lastPosition = position;
            if (moved || DateTime.UtcNow - _lastLog < TimeSpan.FromSeconds(seconds))
                return;
            Failure($"Watchdog: nothing logged and no move for {seconds} s; the step is: {Step}.");
        }, null, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds));
    }

    private Timer? _watchdog;

    /// <summary>
    /// Saves a picture of the game window to %LOCALAPPDATA%\D2Dotnet\screenshots, named after the time and
    /// <paramref name="reason"/>, and logs where; for seeing afterwards what went wrong. Returns the path, or null when
    /// the picture could not be taken.
    /// </summary>
    public string? Screenshot(string reason) => Save(ScreenshotFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}", reason, log: true);

    private static readonly string ScreenshotFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Dotnet", "screenshots");

    // ---- The screenshot trail ----

    /// <summary>The folder of this run's screenshot trail, or null when there is none (see <see cref="StartTrail"/>).</summary>
    public string? TrailFolder { get; private set; }

    private int _trailCount;
    private Timer? _trailTimer;

    /// <summary>
    /// Starts a trail of screenshots for this run, as proof of what happened: one every <paramref name="seconds"/>, and
    /// one at each moment <see cref="Proof"/> is called (a boss found and killed, a quest done, a death...). They go in a
    /// folder of their own under screenshots/trail, numbered in order; trails older than two days are deleted.
    /// </summary>
    public void StartTrail(int seconds)
    {
        var root = Path.Combine(ScreenshotFolder, "trail");
        Directory.CreateDirectory(root);
        foreach (var old in new DirectoryInfo(root).GetDirectories().Where(d => d.CreationTimeUtc < DateTime.UtcNow.AddDays(-2)))
        {
            try
            {
                old.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }

        TrailFolder = Path.Combine(root, $"{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(TrailFolder);
        Log($"Screenshot trail: {TrailFolder} (every {seconds} s and at each key moment).");
        _trailTimer = new Timer(_ => Proof("", log: false), null, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds));
    }

    /// <summary>A screenshot in the trail, named after the moment (nothing when no trail is running).</summary>
    public void Proof(string moment, bool log = true)
    {
        if (TrailFolder is not { } folder)
            return;
        var number = Interlocked.Increment(ref _trailCount);
        var area = _lastArea is { } a ? a.ToString() : "";
        Save(folder, $"{number:D4}-{DateTime.Now:HHmmss}-{area}", moment, log);
    }

    /// <summary>Opens the quest log, pictures it in the trail (or as a plain screenshot without one), and closes it: proof of a quest's state.</summary>
    public void ProofQuestLog(string moment)
    {
        ClosePanels();
        Press(Keys.QuestLog);
        Thread.Sleep(700);
        if (TrailFolder is null)
            Screenshot(moment);
        else
            Proof(moment);
        ClosePanels();
    }

    private AreaId? _lastArea;
    private readonly object _captureLock = new();

    private string? Save(string folder, string prefix, string reason, bool log)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var name = string.Concat(reason.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
            var path = Path.Combine(folder, name.Length == 0 ? $"{prefix}.png" : $"{prefix}-{name[..Math.Min(name.Length, 60)]}.png");
            lock (_captureLock)
                GameScreen.Capture(window.Handle).SavePng(path);
            if (log)
                Log($"Screenshot: {path}");
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log($"No screenshot: {e.Message}");
            return null;
        }
    }
}

/// <summary>The bot stopped on purpose, with the reason.</summary>
internal class BotStopped(string reason) : Exception(reason);

/// <summary>Life is low and nothing is left to drink: the bot retreats to town (see <see cref="Bot.Retreat"/>).</summary>
internal sealed class LowLife(string reason) : Exception(reason);

/// <summary>Something too dangerous for the character is near: the bot leaves the area (see <see cref="Bot.IsDeadly"/>).</summary>
internal sealed class TooDangerous(string reason) : Exception(reason);

/// <summary>The bot could not get past something on the way (see <see cref="Bot.WalkTo"/>).</summary>
internal sealed class Stuck(string reason) : BotStopped(reason);

/// <summary>The character died, here (see <see cref="Bot.Respawn"/>).</summary>
internal sealed class Died(AreaId area, Point position) : Exception($"Died in {area} at {position}.")
{
    public AreaId Area => area;

    public Point Position => position;
}

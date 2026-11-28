namespace D2Dotnet.Game;

/// <summary>Which panels of the game's user interface are open.</summary>
public readonly record struct OpenMenus
{
    /// <summary>The inventory.</summary>
    public bool Inventory { get; init; }

    /// <summary>The loading screen (between areas or games).</summary>
    public bool LoadingScreen { get; init; }

    /// <summary>The dialog of an NPC.</summary>
    public bool NpcInteract { get; init; }

    /// <summary>An NPC's shop.</summary>
    public bool NpcShop { get; init; }

    /// <summary>The stash.</summary>
    public bool Stash { get; init; }

    /// <summary>The waypoint menu.</summary>
    public bool Waypoint { get; init; }

    /// <summary>The automap.</summary>
    public bool MapShown { get; init; }

    /// <summary>The skill tree.</summary>
    public bool SkillTree { get; init; }

    /// <summary>The character screen.</summary>
    public bool Character { get; init; }

    /// <summary>The quit (escape) menu.</summary>
    public bool QuitMenu { get; init; }

    /// <summary>The Horadric Cube.</summary>
    public bool Cube { get; init; }

    /// <summary>The skill selection of the mouse buttons.</summary>
    public bool SkillSelect { get; init; }

    /// <summary>Larzuk's or Charsi's anvil (imbue, socket).</summary>
    public bool Anvil { get; init; }

    /// <summary>The quest log.</summary>
    public bool QuestLog { get; init; }

    /// <summary>
    /// True when a panel that takes the mouse is open. The loading screen and the automap are not panels: they do not
    /// count.
    /// </summary>
    public bool IsAnyPanelOpen =>
        Inventory || NpcInteract || NpcShop || Stash || Waypoint || SkillTree || Character || QuitMenu || Cube || SkillSelect || Anvil || QuestLog;
}

/// <summary>A widget of the game's user interface (a child of its panel manager).</summary>
/// <param name="Name">The widget's name, such as <c>LobbyBackgroundPanel</c>.</param>
/// <param name="IsActive">True when the widget is active.</param>
/// <param name="IsVisible">True when the widget is visible.</param>
public sealed record Widget(string Name, bool IsActive, bool IsVisible)
{
    /// <summary>True when the widget is both active and visible.</summary>
    public bool IsShown => IsActive && IsVisible;
}

/// <summary>The current (or last) online game.</summary>
/// <param name="LastGameName">The last game's name.</param>
/// <param name="LastGamePassword">The last game's password.</param>
/// <param name="Fps">The frame rate.</param>
public sealed record OnlineGameInfo(string LastGameName, string LastGamePassword, uint Fps);

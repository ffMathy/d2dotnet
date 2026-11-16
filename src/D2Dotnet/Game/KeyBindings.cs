namespace D2Dotnet.Game;

/// <summary>A key as the game stores it in its key binding table: two bytes (the first is the virtual key code).</summary>
/// <param name="First">The first byte.</param>
/// <param name="Second">The second byte.</param>
public readonly record struct Key(byte First, byte Second)
{
    /// <summary>True when no key is set (both bytes are 0).</summary>
    public bool IsNone => First == 0 && Second == 0;
}

/// <summary>The two keys an action can be bound to.</summary>
/// <param name="Primary">The first key.</param>
/// <param name="Secondary">The second key.</param>
public readonly record struct KeyBinding(Key Primary, Key Secondary)
{
    /// <summary>True when neither key is set.</summary>
    public bool IsEmpty => Primary.IsNone && Secondary.IsNone;
}

/// <summary>A skill hotkey: the skill and its keys.</summary>
/// <param name="Skill">The skill.</param>
/// <param name="Keys">The keys.</param>
public readonly record struct SkillBinding(SkillId Skill, KeyBinding Keys);

/// <summary>The game's key bindings. Two instances are equal when every binding is, including each skill and belt key.</summary>
public sealed record KeyBindings
{
    private readonly SkillBinding[] _skills = new SkillBinding[SkillSlots];
    private readonly KeyBinding[] _useBelt = new KeyBinding[BeltSlots];

    /// <summary>The number of skill hotkeys.</summary>
    public const int SkillSlots = 16;

    /// <summary>The number of belt column keys.</summary>
    public const int BeltSlots = 4;

    /// <summary>The keys of the "character screen" action.</summary>
    public KeyBinding CharacterScreen { get; init; }

    /// <summary>The keys of the "inventory" action.</summary>
    public KeyBinding Inventory { get; init; }

    /// <summary>The keys of the "horadric cube" action.</summary>
    public KeyBinding HoradricCube { get; init; }

    /// <summary>The keys of the "party screen" action.</summary>
    public KeyBinding PartyScreen { get; init; }

    /// <summary>The keys of the "mercenary screen" action.</summary>
    public KeyBinding MercenaryScreen { get; init; }

    /// <summary>The keys of the "message log" action.</summary>
    public KeyBinding MessageLog { get; init; }

    /// <summary>The keys of the "quest log" action.</summary>
    public KeyBinding QuestLog { get; init; }

    /// <summary>The keys of the "help screen" action.</summary>
    public KeyBinding HelpScreen { get; init; }

    /// <summary>The keys of the "skill tree" action.</summary>
    public KeyBinding SkillTree { get; init; }

    /// <summary>The keys of the "skill speed bar" action.</summary>
    public KeyBinding SkillSpeedBar { get; init; }

    /// <summary>The keys of the "select previous skill" action.</summary>
    public KeyBinding SelectPreviousSkill { get; init; }

    /// <summary>The keys of the "select next skill" action.</summary>
    public KeyBinding SelectNextSkill { get; init; }

    /// <summary>The keys of the "show belt" action.</summary>
    public KeyBinding ShowBelt { get; init; }

    /// <summary>The keys of the "swap weapons" action.</summary>
    public KeyBinding SwapWeapons { get; init; }

    /// <summary>The keys of the "chat" action.</summary>
    public KeyBinding Chat { get; init; }

    /// <summary>The keys of the "run" action.</summary>
    public KeyBinding Run { get; init; }

    /// <summary>The keys of the "toggle run walk" action.</summary>
    public KeyBinding ToggleRunWalk { get; init; }

    /// <summary>The keys of the "stand still" action.</summary>
    public KeyBinding StandStill { get; init; }

    /// <summary>The keys of the "force move" action.</summary>
    public KeyBinding ForceMove { get; init; }

    /// <summary>The keys of the "show items" action.</summary>
    public KeyBinding ShowItems { get; init; }

    /// <summary>The keys of the "show portraits" action.</summary>
    public KeyBinding ShowPortraits { get; init; }

    /// <summary>The keys of the "automap" action.</summary>
    public KeyBinding Automap { get; init; }

    /// <summary>The keys of the "center automap" action.</summary>
    public KeyBinding CenterAutomap { get; init; }

    /// <summary>The keys of the "fade automap" action.</summary>
    public KeyBinding FadeAutomap { get; init; }

    /// <summary>The keys of the "party on automap" action.</summary>
    public KeyBinding PartyOnAutomap { get; init; }

    /// <summary>The keys of the "names on automap" action.</summary>
    public KeyBinding NamesOnAutomap { get; init; }

    /// <summary>The keys of the "toggle mini map" action.</summary>
    public KeyBinding ToggleMiniMap { get; init; }

    /// <summary>The keys of the "say help" action.</summary>
    public KeyBinding SayHelp { get; init; }

    /// <summary>The keys of the "say follow me" action.</summary>
    public KeyBinding SayFollowMe { get; init; }

    /// <summary>The keys of the "say this is for you" action.</summary>
    public KeyBinding SayThisIsForYou { get; init; }

    /// <summary>The keys of the "say thanks" action.</summary>
    public KeyBinding SayThanks { get; init; }

    /// <summary>The keys of the "say sorry" action.</summary>
    public KeyBinding SaySorry { get; init; }

    /// <summary>The keys of the "say bye" action.</summary>
    public KeyBinding SayBye { get; init; }

    /// <summary>The keys of the "say now you die" action.</summary>
    public KeyBinding SayNowYouDie { get; init; }

    /// <summary>The keys of the "say retreat" action.</summary>
    public KeyBinding SayRetreat { get; init; }

    /// <summary>The keys of the "clear screen" action.</summary>
    public KeyBinding ClearScreen { get; init; }

    /// <summary>The keys of the "clear messages" action.</summary>
    public KeyBinding ClearMessages { get; init; }

    /// <summary>The keys of the "zoom" action.</summary>
    public KeyBinding Zoom { get; init; }

    /// <summary>The keys of the "legacy toggle" action.</summary>
    public KeyBinding LegacyToggle { get; init; }

    /// <summary>The <see cref="SkillSlots"/> skill hotkeys. The list given is copied; it must have that many entries.</summary>
    public IReadOnlyList<SkillBinding> Skills
    {
        get => _skills;
        init => _skills = Copy(value, SkillSlots);
    }

    /// <summary>The <see cref="BeltSlots"/> keys that use the belt's columns. The list given is copied; it must have that many entries.</summary>
    public IReadOnlyList<KeyBinding> UseBelt
    {
        get => _useBelt;
        init => _useBelt = Copy(value, BeltSlots);
    }

    /// <summary>The keys of the first hotkey bound to <paramref name="skill"/> that has a key; null when none has.</summary>
    public KeyBinding? FindSkill(SkillId skill)
    {
        foreach (var binding in _skills)
        {
            if (binding.Skill == skill && !binding.Keys.IsEmpty)
                return binding.Keys;
        }

        return null;
    }

    /// <inheritdoc/>
    public bool Equals(KeyBindings? other) =>
        other is not null && EqualityContract == other.EqualityContract
        && NamedBindings().SequenceEqual(other.NamedBindings())
        && _skills.AsSpan().SequenceEqual(other._skills) && _useBelt.AsSpan().SequenceEqual(other._useBelt);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var b in NamedBindings())
            hash.Add(b);
        foreach (var s in _skills)
            hash.Add(s);
        foreach (var b in _useBelt)
            hash.Add(b);
        return hash.ToHashCode();
    }

    private IEnumerable<KeyBinding> NamedBindings() =>
    [
        CharacterScreen, Inventory, HoradricCube, PartyScreen, MercenaryScreen, MessageLog, QuestLog, HelpScreen, SkillTree, SkillSpeedBar, SelectPreviousSkill, SelectNextSkill, ShowBelt, SwapWeapons, Chat, Run, ToggleRunWalk, StandStill, ForceMove, ShowItems, ShowPortraits, Automap, CenterAutomap, FadeAutomap, PartyOnAutomap, NamesOnAutomap, ToggleMiniMap, SayHelp, SayFollowMe, SayThisIsForYou, SayThanks, SaySorry, SayBye, SayNowYouDie, SayRetreat, ClearScreen, ClearMessages, Zoom, LegacyToggle,
    ];

    private static T[] Copy<T>(IReadOnlyList<T> value, int count)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Count != count)
            throw new ArgumentException($"Expected {count} entries, got {value.Count}.", nameof(value));
        return [.. value];
    }
}

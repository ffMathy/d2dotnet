// Port of d2go pkg/data/keybindings.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using D2Dotnet.D2Go.Data.Skill;

namespace D2Dotnet.D2Go.Data;

// Inside the namespace, so these win over the library's types of the same name in the enclosing D2Dotnet namespace.
using SkillId = D2Dotnet.D2Go.Data.Skill.SkillId;

/// <summary>
/// The game's key bindings (Go <c>data.KeyBindings</c>). Like the Go struct, which holds only arrays and structs, two
/// key bindings are equal when every binding is, including each entry of <see cref="Skills"/> and
/// <see cref="UseBelt"/>.
/// </summary>
public sealed record KeyBindings
{
    // Go arrays are values: copied on assignment and compared element by element.
    private readonly ValueArray<SkillBinding> skills = new(new SkillBinding[16]);
    private readonly ValueArray<KeyBinding> useBelt = new(new KeyBinding[4]);

    public KeyBinding CharacterScreen { get; init; }

    public KeyBinding Inventory { get; init; }

    public KeyBinding HoradricCube { get; init; }

    public KeyBinding PartyScreen { get; init; }

    public KeyBinding MercenaryScreen { get; init; }

    public KeyBinding MessageLog { get; init; }

    public KeyBinding QuestLog { get; init; }

    public KeyBinding HelpScreen { get; init; }

    public KeyBinding SkillTree { get; init; }

    public KeyBinding SkillSpeedBar { get; init; }

    /// <summary>
    /// The 16 skill hotkeys (Go <c>[16]SkillBinding</c>); unset entries are zero, that is skill
    /// <see cref="SkillId.AttackSkill"/>. The list given is copied, and compares by value.
    /// </summary>
    public IReadOnlyList<SkillBinding> Skills { get => skills; init => skills = new(value); }

    public KeyBinding SelectPreviousSkill { get; init; }

    public KeyBinding SelectNextSkill { get; init; }

    public KeyBinding ShowBelt { get; init; }

    /// <summary>The 4 belt column keys (Go <c>[4]KeyBinding</c>). The list given is copied, and compares by value.</summary>
    public IReadOnlyList<KeyBinding> UseBelt { get => useBelt; init => useBelt = new(value); }

    public KeyBinding SwapWeapons { get; init; }

    public KeyBinding Chat { get; init; }

    public KeyBinding Run { get; init; }

    public KeyBinding ToggleRunWalk { get; init; }

    public KeyBinding StandStill { get; init; }

    public KeyBinding ForceMove { get; init; }

    public KeyBinding ShowItems { get; init; }

    public KeyBinding ShowPortraits { get; init; }

    public KeyBinding Automap { get; init; }

    public KeyBinding CenterAutomap { get; init; }

    public KeyBinding FadeAutomap { get; init; }

    public KeyBinding PartyOnAutomap { get; init; }

    public KeyBinding NamesOnAutomap { get; init; }

    public KeyBinding ToggleMiniMap { get; init; }

    public KeyBinding SayHelp { get; init; }

    public KeyBinding SayFollowMe { get; init; }

    public KeyBinding SayThisIsForYou { get; init; }

    public KeyBinding SayThanks { get; init; }

    public KeyBinding SaySorry { get; init; }

    public KeyBinding SayBye { get; init; }

    public KeyBinding SayNowYouDie { get; init; }

    public KeyBinding SayRetreat { get; init; }

    public KeyBinding ClearScreen { get; init; }

    public KeyBinding ClearMessages { get; init; }

    public KeyBinding Zoom { get; init; }

    public KeyBinding LegacyToggle { get; init; }

    /// <summary>
    /// The binding of the first of <see cref="Skills"/> whose skill is <paramref name="skillId"/> (Go
    /// <c>KeyBindingForSkill</c>). When there is none, returns false and <paramref name="keyBinding"/> is the zero value.
    /// As in Go, unset entries hold <see cref="SkillId.AttackSkill"/> (0), so looking up that skill finds the first unset
    /// entry when it is not bound.
    /// </summary>
    public bool TryGetKeyBindingForSkill(SkillId skillId, out KeyBinding keyBinding)
    {
        foreach (var sk in Skills)
        {
            if (sk.SkillId == skillId)
            {
                keyBinding = sk.KeyBinding;
                return true;
            }
        }

        keyBinding = default;
        return false;
    }

    /// <summary>The binding of <paramref name="skillId"/>, or the zero value when it is not bound (Go <c>MustKBForSkill</c>).</summary>
    public KeyBinding MustKBForSkill(SkillId skillId)
    {
        TryGetKeyBindingForSkill(skillId, out var k);

        return k;
    }
}

/// <summary>The two keys bound to an action (Go <c>data.KeyBinding</c>).</summary>
public readonly record struct KeyBinding(KeyBytes Key1, KeyBytes Key2);

/// <summary>A key as the game stores it: two bytes (Go <c>[2]byte</c>).</summary>
public readonly record struct KeyBytes(byte First, byte Second)
{
    /// <summary>Always 2, like Go's <c>len</c> of a <c>[2]byte</c>.</summary>
    public int Length => 2;

    /// <summary>Byte 0 or 1.</summary>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is not 0 or 1 (Go panics).</exception>
    public byte this[int index] => index switch
    {
        0 => First,
        1 => Second,
        _ => throw new IndexOutOfRangeException("A KeyBytes has two bytes."),
    };
}

/// <summary>A skill and its hotkey (Go <c>data.SkillBinding</c>, which embeds <c>KeyBinding</c>).</summary>
public readonly record struct SkillBinding(SkillId SkillId, KeyBinding KeyBinding)
{
    public KeyBytes Key1 => KeyBinding.Key1;

    public KeyBytes Key2 => KeyBinding.Key2;
}

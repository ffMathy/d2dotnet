// Memory layout from d2go pkg/memory/process.go (widgets), roster.go, quests.go, keybindings.go and terrorzones.go
// (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Buffers.Binary;
using D2Dotnet.Game;

namespace D2Dotnet.Memory;

public sealed partial class GameReader
{
    internal const string LobbyPanel = "LobbyBackgroundPanel";
    internal const string CharacterSelectPanel = "CharacterSelectPanel";
    internal const string CharacterCreatePanel = "CharacterCreatePanel";

    // keybindings.go, relative to the module base: the key binding blob and the skill hotkey blob.
    internal const nuint KeyBindingsAddress = 0x1DFA914;
    internal const nuint SkillKeyBindingsAddress = 0x21E29B0;

    // Each quest's two status bytes in the quest buffer (after its first 3 bytes).
    internal static readonly (QuestId Quest, int Start)[] QuestBytes =
    [
        (QuestId.Act1DenOfEvil, 0),
        (QuestId.Act1SistersBurialGrounds, 2),
        (QuestId.Act1ToolsOfTheTrade, 4),
        (QuestId.Act1TheSearchForCain, 6),
        (QuestId.Act1TheForgottenTower, 8),
        (QuestId.Act1SistersToTheSlaughter, 10),
        (QuestId.Act2RadamentsLair, 16),
        (QuestId.Act2TheHoradricStaff, 18),
        (QuestId.Act2TaintedSun, 20),
        (QuestId.Act2ArcaneSanctuary, 22),
        (QuestId.Act2TheSummoner, 24),
        (QuestId.Act2TheSevenTombs, 26),
        (QuestId.Act3LamEsensTome, 32),
        (QuestId.Act3KhalimsWill, 34),
        (QuestId.Act3BladeOfTheOldReligion, 36),
        (QuestId.Act3TheGoldenBird, 38),
        (QuestId.Act3TheBlackenedTemple, 40),
        (QuestId.Act3TheGuardian, 42),
        (QuestId.Act4TheFallenAngel, 48),
        (QuestId.Act4TerrorsEnd, 50),
        (QuestId.Act4HellForge, 52),
        (QuestId.Act5SiegeOnHarrogath, 68),
        (QuestId.Act5RescueOnMountArreat, 70),
        (QuestId.Act5PrisonOfIce, 72),
        (QuestId.Act5BetrayalOfHarrogath, 74),
        (QuestId.Act5RiteOfPassage, 76),
        (QuestId.Act5EveOfDestruction, 78),
    ];

    /// <summary>The party: the main player first (when in a game), then the party list linked at <c>+0x148</c>.</summary>
    /// <remarks>
    /// The game does not update a member's area and position while they are in town, so a member that is also a player
    /// unit of the game, with the same name, takes them from that unit.
    /// </remarks>
    public IReadOnlyList<PartyMember> ReadParty() => ReadParty(ReadPlayers(null));

    internal IReadOnlyList<PartyMember> ReadParty(IReadOnlyList<PlayerUnit> players)
    {
        var party = new List<PartyMember>();
        if (players.FirstOrDefault(p => p.IsMainPlayer) is { } main)
            party.Add(new PartyMember(main.Name, main.Area, main.Position));

        unchecked
        {
            // The first entry is the main player's: skip it.
            var member = Process.ReadPointer(Process.ReadPointer(Module(Offsets.Roster)) + 0x148);
            for (var n = 0; member != 0 && n < MaxListLength; n++)
            {
                var name = Process.ReadString(member, 16);
                var unit = players.FirstOrDefault(p => p.Name == name);
                party.Add(unit is not null
                    ? new PartyMember(name, unit.Area, unit.Position)
                    : new PartyMember(
                        name,
                        (AreaId)Process.ReadUInt32(member + 0x5C),
                        new Point((int)Process.ReadUInt32(member + 0x60), (int)Process.ReadUInt32(member + 0x64))));
                member = Process.ReadPointer(member + 0x148);
            }
        }

        return party;
    }

    /// <summary>The status of every quest, from the quest buffer.</summary>
    public IReadOnlyDictionary<QuestId, QuestStatus> ReadQuests()
    {
        var buffer = Process.ReadPointer(Process.ReadPointer(Module(QuestsAddress)));
        return ParseQuests(Process.ReadBytes(buffer, 85).AsSpan(3));
    }

    /// <summary>The status of every quest from the quest bytes (the quest buffer after its first 3 bytes, 80 bytes or more).</summary>
    internal static IReadOnlyDictionary<QuestId, QuestStatus> ParseQuests(ReadOnlySpan<byte> bytes)
    {
        var quests = new Dictionary<QuestId, QuestStatus>();
        foreach (var (quest, start) in QuestBytes)
            quests[quest] = (QuestStatus)BinaryPrimitives.ReadUInt16BigEndian(bytes[start..]);
        return Freeze(quests);
    }

    /// <summary>The terrorized areas: the non-zero entries of the eight area ids at the online or offline terror zone address.</summary>
    public IReadOnlyList<AreaId> ReadTerrorZones() => ReadTerrorZones(IsOnline());

    internal IReadOnlyList<AreaId> ReadTerrorZones(bool online)
    {
        var buffer = Process.ReadBytes(Module(online ? GameOffsets.TerrorZonesOnline : GameOffsets.TerrorZonesOffline), 8 * 4).AsSpan();
        var areas = new List<AreaId>();
        for (var i = 0; i < 8; i++)
        {
            var area = BinaryPrimitives.ReadUInt32LittleEndian(buffer[(i * 4)..]);
            if (area != 0)
                areas.Add((AreaId)area);
        }

        return areas;
    }

    /// <summary>The key bindings. Each key is two bytes; the offsets in the 0x500-byte binding blob are d2go's.</summary>
    public KeyBindings ReadKeyBindings()
    {
        var blob = Process.ReadBytes(Module(KeyBindingsAddress), 0x500);
        var skillBlob = Process.ReadBytes(Module(SkillKeyBindingsAddress), 0x500);

        // The skill id is the low 16 bits of a 32-bit value.
        SkillId SkillAt(int slot) => (SkillId)BinaryPrimitives.ReadInt16LittleEndian(skillBlob.AsSpan(slot * 0x1c));

        var skills = new SkillBinding[KeyBindings.SkillSlots];
        for (var i = 0; i < 7; i++)
            skills[i] = new SkillBinding(SkillAt(i), KB(blob, 0x118 + (i * 0x14), 0x119 + (i * 0x14), 0x122 + (i * 0x14), 0x123 + (i * 0x14)));
        for (var i = 0; i < 9; i++)
            skills[i + 7] = new SkillBinding(SkillAt(i + 7), KB(blob, 0x384 + (i * 0x14), 0x385 + (i * 0x14), 0x38e + (i * 0x14), 0x38f + (i * 0x14)));

        var belt = new KeyBinding[KeyBindings.BeltSlots];
        for (var i = 0; i < belt.Length; i++)
            belt[i] = KB(blob, 0x1b8 + (i * 0x14), 0x1b9 + (i * 0x14), 0x1c2 + (i * 0x14), 0x1c3 + (i * 0x14));

        return new KeyBindings
        {
            CharacterScreen = KB(blob, 0x00, 0x01, 0xa, 0xb),
            Inventory = KB(blob, 0x14, 0x15, 0x1e, 0x1f),
            HoradricCube = KB(blob, 0x4b0, 0x4b1, 0x4ba, 0x4bb),
            PartyScreen = KB(blob, 0x28, 0x29, 0x32, 0x33),
            MercenaryScreen = KB(blob, 0x438, 0x439, 0x442, 0x443),
            MessageLog = KB(blob, 0x3c, 0x3d, 0x46, 0x47),
            QuestLog = KB(blob, 0x50, 0x51, 0x5a, 0x5b),
            HelpScreen = KB(blob, 0x78, 0x79, 0x82, 0x83),
            SkillTree = KB(blob, 0xf0, 0xf1, 0xfa, 0xfb),
            SkillSpeedBar = KB(blob, 0x104, 0x105, 0x10e, 0x10f),
            Skills = skills,
            SelectPreviousSkill = KB(blob, 0x2f8, 0x2f9, 0x302, 0x303),
            SelectNextSkill = KB(blob, 0x30c, 0x30d, 0x316, 0x317),
            ShowBelt = KB(blob, 0x1a4, 0x1a5, 0x1ae, 0x1af),
            UseBelt = belt,
            SwapWeapons = KB(blob, 0x35c, 0x35d, 0x366, 0x367),
            Chat = KB(blob, 0x64, 0x65, 0x6e, 0x6f),
            Run = KB(blob, 0x294, 0x295, 0x29e, 0x29f),
            ToggleRunWalk = KB(blob, 0x2a8, 0x2a9, 0x2b2, 0x2b3),
            StandStill = KB(blob, 0x2bc, 0x2bd, 0x2c6, 0x2c7),
            ForceMove = KB(blob, 0x49c, 0x49d, 0x4a6, 0x4a7),
            ShowItems = KB(blob, 0x2d0, 0x2d1, 0x2da, 0x2db),
            ShowPortraits = KB(blob, 0x348, 0x349, 0x352, 0x353),
            Automap = KB(blob, 0x8c, 0x8d, 0x96, 0x97),
            CenterAutomap = KB(blob, 0xa0, 0xa1, 0xaa, 0xab),
            FadeAutomap = KB(blob, 0xb4, 0xb5, 0xbe, 0xbf),
            PartyOnAutomap = KB(blob, 0xc8, 0xc9, 0xd2, 0xd3),
            NamesOnAutomap = KB(blob, 0xdc, 0xdd, 0xe6, 0xe7),
            ToggleMiniMap = KB(blob, 0x370, 0x371, 0x37a, 0x37b),
            SayHelp = KB(blob, 0x208, 0x209, 0x212, 0x213),
            SayFollowMe = KB(blob, 0x21c, 0x21d, 0x226, 0x227),
            SayThisIsForYou = KB(blob, 0x230, 0x231, 0x23a, 0x23b),
            SayThanks = KB(blob, 0x244, 0x245, 0x24e, 0x24f),
            SaySorry = KB(blob, 0x258, 0x259, 0x262, 0x263),
            SayBye = KB(blob, 0x26c, 0x26d, 0x276, 0x277),
            SayNowYouDie = KB(blob, 0x280, 0x281, 0x28a, 0x28b),
            SayRetreat = KB(blob, 0x44c, 0x44d, 0x456, 0x457),
            ClearScreen = KB(blob, 0x2e4, 0x2e5, 0x2ee, 0x2ef),
            ClearMessages = KB(blob, 0x320, 0x321, 0x32a, 0x32b),
            Zoom = KB(blob, 0x474, 0x475, 0x47e, 0x47f),
            LegacyToggle = KB(blob, 0x488, 0x489, 0x492, 0x493),
        };
    }

    // The two keys of a binding: bytes a and b, then c and d, of the blob.
    private static KeyBinding KB(byte[] blob, int a, int b, int c, int d) =>
        new(new Key(blob[a], blob[b]), new Key(blob[c], blob[d]));

    /// <summary>
    /// The child widgets of the panel manager, by name; empty when the panel manager cannot be read. Children without a
    /// name are left out; of two children with the same name, the later one is kept.
    /// </summary>
    public IReadOnlyDictionary<string, Widget> ReadWidgets()
    {
        var widgets = new Dictionary<string, Widget>(StringComparer.Ordinal);
        var panelManager = Process.ReadPointer(Module(Offsets.PanelManager));
        if (ReadWidgetContainer(panelManager, full: true) is not { } container || container.ChildCount > MaxListLength)
            return Freeze(widgets);

        for (var i = 0u; i < container.ChildCount; i++)
        {
            var child = Process.ReadPointer(unchecked(container.ChildList + (i * 8)));
            if (ReadWidgetContainer(child, full: false) is { } w)
                widgets[w.Widget.Name] = w.Widget;
        }

        return Freeze(widgets);
    }

    /// <summary>True when the widget <paramref name="name"/> is active and visible.</summary>
    public bool IsWidgetShown(string name) => IsShown(ReadWidgets(), name);

    /// <summary>True in the lobby.</summary>
    public bool IsInLobby() => IsWidgetShown(LobbyPanel);

    /// <summary>True in the character selection screen.</summary>
    public bool IsInCharacterSelection() => IsWidgetShown(CharacterSelectPanel);

    /// <summary>True in the character creation screen.</summary>
    public bool IsInCharacterCreation() => IsWidgetShown(CharacterCreatePanel);

    private static bool IsShown(IReadOnlyDictionary<string, Widget> widgets, string name) =>
        widgets.TryGetValue(name, out var widget) && widget.IsShown;

    /// <summary>A widget container as the game lays it out (the lists other than the child list are not used).</summary>
    internal sealed record WidgetContainer(
        Widget Widget, uint NameLength, nuint ChildList, uint ChildCount, nuint List, uint ListCount, nuint List2, uint List2Count);

    /// <summary>
    /// The widget container at <paramref name="address"/>: name pointer at <c>+0x8</c>, name length at <c>+0x10</c>, active
    /// at <c>+0x50</c>, visible at <c>+0x51</c>; with <paramref name="full"/> also the lists of the name structure. Null
    /// when the name is empty.
    /// </summary>
    internal WidgetContainer? ReadWidgetContainer(nuint address, bool full)
    {
        unchecked
        {
            var namePtr = Process.ReadPointer(address + 0x8);
            var nameLength = Process.ReadUInt32(address + 0x10);
            var name = Process.ReadString(namePtr, (int)Math.Min(nameLength, (uint)GameProcess.MaxStringLength));
            if (name.Length == 0)
                return null;

            var widget = new Widget(name, IsActive: Process.ReadByte(address + 0x50) == 1, IsVisible: Process.ReadByte(address + 0x51) == 1);
            if (!full)
                return new WidgetContainer(widget, nameLength, 0, 0, 0, 0, 0, 0);

            return new WidgetContainer(
                widget,
                nameLength,
                ChildList: Process.ReadPointer(namePtr + 0x38),
                ChildCount: Process.ReadUInt32(namePtr + 0x40),
                List: Process.ReadPointer(namePtr + 0x68),
                ListCount: Process.ReadUInt32(namePtr + 0x78),
                List2: Process.ReadPointer(namePtr + 0x80),
                List2Count: Process.ReadUInt32(namePtr + 0x90));
        }
    }
}

// Port of d2go pkg/memory/keybindings.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Buffers.Binary;
using D2Dotnet.D2Go.Data;
using D2Dotnet.D2Go.Data.Skill;

namespace D2Dotnet.D2Go.Memory;

public sealed partial class GameReader
{
    // keybindings.go, relative to the module base: the key binding blob and the skill hotkey blob.
    internal const nuint KeyBindingsAddress = 0x1DFA914;
    internal const nuint SkillKeyBindingsAddress = 0x21E29B0;

    /// <summary>
    /// The key bindings (Go <c>GetKeyBindings</c>). Each key is two bytes; the offsets in the 0x500-byte binding blob
    /// are d2go's.
    /// </summary>
    public KeyBindings GetKeyBindings()
    {
        var blob = ReadBytesFromMemory(unchecked(ModuleBaseAddress + KeyBindingsAddress), 0x500);
        var blobSkills = ReadBytesFromMemory(unchecked(ModuleBaseAddress + SkillKeyBindingsAddress), 0x500);

        var skillsKB = new SkillBinding[16];
        for (var i = 0; i < 7; i++)
        {
            skillsKB[i] = new SkillBinding(
                SkillIdAt(blobSkills, i * 0x1c),
                new KeyBinding(
                    Key1: new KeyBytes(blob[0x118 + (i * 0x14)], blob[0x119 + (i * 0x14)]),
                    Key2: new KeyBytes(blob[0x122 + (i * 0x14)], blob[0x123 + (i * 0x14)])));
        }

        for (var i = 0; i < 9; i++)
        {
            var skillIdx = i + 7;
            skillsKB[skillIdx] = new SkillBinding(
                SkillIdAt(blobSkills, skillIdx * 0x1c),
                new KeyBinding(
                    Key1: new KeyBytes(blob[0x384 + (i * 0x14)], blob[0x385 + (i * 0x14)]),
                    Key2: new KeyBytes(blob[0x38e + (i * 0x14)], blob[0x38f + (i * 0x14)])));
        }

        var belt = new KeyBinding[4];
        for (var i = 0; i < 4; i++)
        {
            belt[i] = new KeyBinding(
                Key1: new KeyBytes(blob[0x1b8 + (i * 0x14)], blob[0x1b9 + (i * 0x14)]),
                Key2: new KeyBytes(blob[0x1c2 + (i * 0x14)], blob[0x1c3 + (i * 0x14)]));
        }

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
            Skills = skillsKB,
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

    // Go data.KeyBinding{Key1: [2]byte{blob[a], blob[b]}, Key2: [2]byte{blob[c], blob[d]}}.
    private static KeyBinding KB(byte[] blob, int a, int b, int c, int d) =>
        new(Key1: new KeyBytes(blob[a], blob[b]), Key2: new KeyBytes(blob[c], blob[d]));

    // Go skill.ID(binary.LittleEndian.Uint32(...)): the low 16 bits as a signed skill id.
    private static SkillId SkillIdAt(byte[] blobSkills, int offset) =>
        (SkillId)unchecked((short)BinaryPrimitives.ReadUInt32LittleEndian(blobSkills.AsSpan(offset, 4)));
}

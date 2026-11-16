using D2Dotnet.D2Go.Data.Area;
using D2Dotnet.D2Go.Data.Npc;
using D2Dotnet.D2Go.Data.Object;
using D2Dotnet.D2Go.Data.Quest;
using D2Dotnet.D2Go.Data.Skill;
using D2Dotnet.D2Go.Data.Stat;

namespace D2Dotnet.D2Go.Memory.Tests;

/// <summary>game_reader.go, roster.go, quests.go, keybindings.go and terrorzones.go.</summary>
public sealed class GameReaderTests
{
    [Fact]
    public void HoveredData_ReadsTypeAndIdWhenTheFlagIsSet()
    {
        var image = new GameImage();
        using var reader = image.Reader();
        Assert.Equal(default, reader.HoveredData());

        image.SetHover(0, 4, 9);
        Assert.Equal(default, reader.HoveredData());

        image.SetHover(2, 4, 0xFFFF_FFFF);
        Assert.Equal(new HoverData(true, new UnitId(-1), 4), reader.HoveredData());
    }

    [Theory]
    [InlineData(0x01, "Inventory")]
    [InlineData(0x16C, "LoadingScreen")]
    [InlineData(0x08, "NpcInteract")]
    [InlineData(0x0B, "NpcShop")]
    [InlineData(0x18, "Stash")]
    [InlineData(0x13, "Waypoint")]
    [InlineData(0x0A, "MapShown")]
    [InlineData(0x04, "SkillTree")]
    [InlineData(0x02, "Character")]
    [InlineData(0x09, "QuitMenu")]
    [InlineData(0x19, "Cube")]
    [InlineData(0x03, "SkillSelect")]
    [InlineData(0x0D, "Anvil")]
    public void OpenMenus_ReadsEachFlagFromItsUIByte(int index, string menu)
    {
        var image = new GameImage();
        image.SetUIByte(index, 0x80);
        using var reader = image.Reader();

        var menus = reader.GetOpenMenus();
        foreach (var p in typeof(OpenMenus).GetProperties())
            Assert.Equal(p.Name == menu, (bool)p.GetValue(menus)!);
    }

    [Fact]
    public void OpenMenus_IgnoresTheOtherUIBytes()
    {
        var image = new GameImage();
        int[] used = [0x01, 0x16C, 0x08, 0x0B, 0x18, 0x13, 0x0A, 0x04, 0x02, 0x09, 0x19, 0x03, 0x0D];
        for (var i = 0; i < 0x16D; i++)
        {
            if (!used.Contains(i))
                image.SetUIByte(i, 1);
        }

        using var reader = image.Reader();
        Assert.Equal(default, reader.GetOpenMenus());
    }

    [Fact]
    public void Roster_StartsWithTheMainPlayer_AndPrefersPlayerUnitPositions()
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = 1, Name = "Me", Area = 1, X = 10, Y = 11, IsMain = true });
        image.AddPlayer(new PlayerSpec { UnitId = 2, Name = "Near", Area = 3, X = 30, Y = 31 });
        image.SetRoster(("Near", 99, 1, 1), ("FarAwayMember123XYZ", 5, 0x8000_0000, 51));
        using var reader = image.Reader();

        var roster = reader.GetRoster(reader.GetRawPlayerUnits());

        Assert.Equal(3, roster.Count);
        Assert.Equal(new RosterMember { Name = "Me", Area = AreaId.RogueEncampment, Position = new Position(10, 11) }, roster[0]);
        Assert.Equal(new RosterMember { Name = "Near", Area = AreaId.ColdPlains, Position = new Position(30, 31) }, roster[1]);
        // Names are read as 16 bytes; coordinates as uint32 (wrapping to int).
        Assert.Equal(new RosterMember { Name = "FarAwayMember123", Area = AreaId.DarkWood, Position = new Position(int.MinValue, 51) }, roster[2]);
    }

    [Fact]
    public void Roster_WithoutAPartyList_IsTheMainPlayerAlone()
    {
        using var reader = new GameImage().Reader();
        var roster = reader.GetRoster(RawPlayerUnits.Empty);
        Assert.Equal([new RosterMember()], roster);
    }

    [Fact]
    public void GetQuestStatus_ReadsBigEndianBitsHighestFirst()
    {
        Assert.Empty(GameReader.GetQuestStatus([0, 0]));
        Assert.Equal([QuestStatus.Unknown2, QuestStatus.RewardGranted], GameReader.GetQuestStatus([0x80, 0x01]));
        Assert.Equal([QuestStatus.UpdateQuestLogCompleted, QuestStatus.InProgress1], GameReader.GetQuestStatus([0x10, 0x20]));
        Assert.Equal(Enumerable.Range(0, 16).Reverse().Select(i => (QuestStatus)i), GameReader.GetQuestStatus([0xFF, 0xFF]));
    }

    [Fact]
    public void GetQuests_ReadsEveryQuestFromItsBytes()
    {
        var bytes = new byte[82];
        bytes[0] = 0x10;  // Den of Evil: completed
        bytes[11] = 0x01; // Sisters to the Slaughter: reward granted
        bytes[79] = 0x02; // Eve of Destruction: reward pending
        bytes[12] = 0xFF; // between the acts: not a quest
        var quests = GameReader.GetQuests(bytes);

        Assert.Equal(27, quests.Count);
        Assert.True(quests[QuestId.Act1DenOfEvil].Completed());
        Assert.Equal([QuestStatus.RewardGranted], quests[QuestId.Act1SistersToTheSlaughter]);
        Assert.Equal([QuestStatus.RewardPending], quests[QuestId.Act5EveOfDestruction]);
        Assert.Equal(24, quests.Values.Count(s => s.Count == 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameReader.GetQuests(new byte[79]));
    }

    [Fact]
    public void TerrorZones_AreTheNonZeroAreasOfEightSlots()
    {
        var image = new GameImage();
        var tz = GameImage.Base + 0x29BD4D8;
        image.Memory.WriteU32(tz + 4, (uint)AreaId.BloodMoor);
        image.Memory.WriteU32(tz + 7 * 4, (uint)AreaId.DarkWood);
        image.Memory.WriteU32(tz + 8 * 4, (uint)AreaId.BlackMarsh); // a ninth slot is not read
        image.Memory.WriteU32(GameImage.Base + 0x22D6D44, (uint)AreaId.StonyField); // the offline address is not used
        using var reader = image.Reader();

        Assert.Equal([AreaId.BloodMoor, AreaId.DarkWood], reader.TerrorZones());
    }

    [Fact]
    public void FixedAddresses_FlagsAndStrings()
    {
        var image = new GameImage();
        var m = image.Memory;
        using var reader = image.Reader();
        Assert.False(reader.LegacyGraphics());
        Assert.False(reader.IsOnline());
        Assert.False(reader.IsIngame());
        Assert.Equal(0, reader.Fps());
        Assert.Equal("", reader.LastGameName());

        m.WriteU64(GameImage.Base + 0x21E2308, 1);
        m.WriteU8(GameImage.Base + 0x2140ED0, 1);
        m.WriteU8(GameImage.Base + 0x22D5D78, 1);
        m.WriteU32(GameImage.Base + 0x2140DF4, 144);
        m.WriteCString(GameImage.Base + 0x29DBD10 + 0x8, "game-01");
        m.WriteCString(GameImage.Base + 0x29DBD10 + 0x60, "pw");
        m.WriteCString(GameImage.Base + 0x2A01A50, "MySorc");
        Assert.True(reader.LegacyGraphics());
        Assert.True(reader.IsOnline());
        Assert.True(reader.IsIngame());
        Assert.Equal(144, reader.Fps());
        Assert.Equal("game-01", reader.LastGameName());
        Assert.Equal("pw", reader.LastGamePass());
        Assert.Equal("MySorc", reader.GetSelectedCharacterName());

        // Exactly 1, not just non-zero; LegacyGraphics reads 8 bytes, IsOnline and IsIngame 1.
        m.WriteU64(GameImage.Base + 0x21E2308, 0x101);
        m.WriteU64(GameImage.Base + 0x2140ED0, 0x201);
        m.WriteU8(GameImage.Base + 0x22D5D78, 2);
        Assert.False(reader.LegacyGraphics());
        Assert.True(reader.IsOnline());
        Assert.False(reader.IsIngame());
    }

    [Fact]
    public void Widgets_ScreensNeedActiveAndVisible()
    {
        var image = new GameImage();
        image.SetPanels(
            image.Widget("LobbyBackgroundPanel", active: true, visible: true),
            image.Widget("CharacterSelectPanel", active: true, visible: false),
            image.Widget("CharacterCreatePanel", active: true, visible: true));
        using var reader = image.Reader();

        var widgets = reader.UpdateWidgets();
        Assert.Equal(["CharacterCreatePanel", "CharacterSelectPanel", "LobbyBackgroundPanel"], widgets.Keys.Order());
        Assert.True(reader.IsInLobby());
        Assert.False(reader.IsInCharacterSelectionScreen());
        Assert.False(reader.InCharacterSelectionScreen());
        Assert.True(reader.IsInCharacterCreationScreen());
        Assert.True(reader.IsWidgetVisible("LobbyBackgroundPanel"));
        Assert.False(reader.IsWidgetVisible("Missing"));
    }

    [Fact]
    public void Widgets_AnUnnamedChildOrPanel_GivesNoWidgets()
    {
        var image = new GameImage();
        image.SetPanels(image.Widget("LobbyBackgroundPanel", true, true), image.Widget("", true, true));
        using var reader = image.Reader();
        Assert.Empty(reader.UpdateWidgets());
        Assert.False(reader.IsInLobby());

        var noPanel = new GameImage();
        using var reader2 = noPanel.Reader();
        Assert.Empty(reader2.UpdateWidgets());
    }

    [Fact]
    public void KeyBindings_ReadTwoBytesPerKey()
    {
        var image = new GameImage();
        var blob = GameImage.Base + 0x1DFA914;
        var skills = GameImage.Base + 0x21E29B0;
        image.Memory.Write(blob + 0x14, [0x49, 0x00]);           // Inventory Key1: I
        image.Memory.Write(blob + 0x1e, [0x09, 0x01]);           // Inventory Key2
        image.Memory.Write(blob + 0x118 + 0x14, [0x70, 0x00]);   // skill 1 Key1: F1
        image.Memory.WriteU32(skills + 0x1c, (uint)SkillId.Teleport);
        image.Memory.WriteU32(skills + 15 * 0x1c, 0x0001_FFFF);  // skill 15: uint32 truncated to int16 (-1)
        image.Memory.Write(blob + 0x384 + 8 * 0x14, [0x31, 0x32]); // skill 15 Key1
        image.Memory.Write(blob + 0x1c2 + 3 * 0x14, [0x34, 0x00]); // belt 4 Key2
        using var reader = image.Reader();

        var kb = reader.GetKeyBindings();
        Assert.Equal(new KeyBinding(new KeyBytes(0x49, 0), new KeyBytes(0x09, 0x01)), kb.Inventory);
        Assert.Equal(16, kb.Skills.Count);
        Assert.Equal(new SkillBinding(SkillId.Teleport, new KeyBinding(new KeyBytes(0x70, 0), default)), kb.Skills[1]);
        Assert.Equal(new SkillBinding((SkillId)(-1), new KeyBinding(new KeyBytes(0x31, 0x32), default)), kb.Skills[15]);
        Assert.Equal(4, kb.UseBelt.Count);
        Assert.Equal(new KeyBinding(default, new KeyBytes(0x34, 0)), kb.UseBelt[3]);
        Assert.Equal(kb.Skills[1].KeyBinding, kb.MustKBForSkill(SkillId.Teleport));
    }

    [Fact]
    public void GetData_ReadsTheWholeState()
    {
        var image = new GameImage();
        var m = image.Memory;
        image.AddPlayer(new PlayerSpec
        {
            UnitId = 1, Name = "Hero", Area = (uint)AreaId.BloodMoor, X = 100, Y = 100, IsMain = true, Class = 3,
            BaseStats = [new RawStat(StatId.Gold, 42)],
        });
        image.AddPlayer(new PlayerSpec { UnitId = 2, Name = "Hero", Area = 2, X = 90, Y = 90, CorpseFlag = 1, Bucket = 4 });
        image.AddMonster((uint)NpcId.Zombie, 50, 101, 100);
        image.AddMonster((uint)NpcId.Zombie, 51, 120, 100, corpse: true);
        image.AddObject((uint)ObjectName.TownPortal, 60, 105, 100, owner: "Hero");
        image.AddItem(1, 70, 3, 0, x: 102, y: 100);
        image.SetHover(1, 1, 50);
        image.SetUIByte(0x01, 1);
        image.SetRoster(("Friend", 4, 7, 8));
        image.SetPanels(image.Widget("LobbyBackgroundPanel", false, true));
        var questBytes = new byte[82];
        questBytes[1] = 0x01;
        image.SetQuestBytes(questBytes);
        m.WriteU32(GameImage.Base + 0x29BD4D8, (uint)AreaId.DarkWood);
        m.Write(GameImage.Base + 0x1DFA914, [0x43, 0x00]);
        m.WriteU64(GameImage.Base + 0x21E2308, 1);
        m.WriteU8(GameImage.Base + 0x2140ED0, 1);
        m.WriteU8(GameImage.Base + 0x22D5D78, 1);
        m.WriteU32(GameImage.Base + 0x2140DF4, 60);
        m.WriteCString(GameImage.Base + 0x29DBD10 + 0x8, "g");
        m.WriteCString(GameImage.Base + 0x29DBD10 + 0x60, "p");
        using var reader = image.Reader();

        var d = reader.GetData();

        Assert.Equal("Hero", d.PlayerUnit.Name);
        Assert.Equal(Class.Paladin, d.PlayerUnit.Class);
        Assert.Equal(new Corpse(true, false, new Position(90, 90)), d.Corpse);
        Assert.Equal(new OnlineGame { LastGameName = "g", LastGamePassword = "p", Fps = 60 }, d.Game);
        var monster = Assert.Single(d.Monsters);
        Assert.True(monster.IsHovered);
        Assert.Equal(51, Assert.Single(d.Corpses).UnitId.Value);
        Assert.Equal(42, d.Inventory.Gold);
        Assert.Equal(70, Assert.Single(d.Inventory.AllItems).UnitId.Value);
        Assert.Equal("Hero", Assert.Single(d.Objects).Owner);
        Assert.True(d.OpenMenus.Inventory);
        Assert.Single(d.Widgets);
        Assert.Equal(["Hero", "Friend"], d.Roster.Select(r => r.Name));
        Assert.Equal(new HoverData(true, new UnitId(50), 1), d.HoverData);
        Assert.Equal([AreaId.DarkWood], d.TerrorZones);
        Assert.Equal([QuestStatus.RewardGranted], d.Quests[QuestId.Act1DenOfEvil]);
        Assert.Equal(new KeyBytes(0x43, 0), d.KeyBindings.CharacterScreen.Key1);
        Assert.True(d.LegacyGraphics);
        Assert.True(d.IsOnline);
        Assert.True(d.IsIngame);
        Assert.False(d.IsInLobby); // visible but not active
        Assert.False(d.IsInCharSelectionScreen);
        Assert.False(d.IsInCharCreationScreen);
        // Not read by d2go.
        Assert.Empty(d.Npcs);
        Assert.Empty(d.Rooms);
        Assert.Empty(d.AdjacentLevels);
    }

    [Fact]
    public void GetData_OfAnEmptyImage_IsMostlyZero()
    {
        using var reader = new GameImage().Reader();
        var d = reader.GetData();
        Assert.Equal(default, d.Corpse);
        Assert.Empty(d.Monsters);
        Assert.Equal([new RosterMember()], d.Roster);
        Assert.Equal(27, d.Quests.Count);
        Assert.Empty(d.TerrorZones);
        Assert.Empty(d.Widgets);
    }
}

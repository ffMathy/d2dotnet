using D2Dotnet.Game;
using D2Dotnet.Memory;

namespace D2Dotnet.Tests.Memory;

/// <summary>game_reader.go, roster.go, quests.go, keybindings.go, terrorzones.go and the widgets of process.go.</summary>
public sealed class GameReaderTests
{
    [Fact]
    public void ReadHover_ReadsTypeAndIdWhenTheFlagIsSet()
    {
        var image = new GameImage();
        var reader = image.Reader();
        Assert.Null(reader.ReadHover());

        image.SetHover(0, 4, 9);
        Assert.Null(reader.ReadHover());

        image.SetHover(2, 4, 0xFFFF_FFFF);
        Assert.Equal(new HoveredUnit(UnitType.Item, new UnitId(0xFFFF_FFFF)), reader.ReadHover());
    }

    [Theory]
    [InlineData(0x01, nameof(OpenMenus.Inventory))]
    [InlineData(0x168, nameof(OpenMenus.LoadingScreen))]
    [InlineData(0x08, nameof(OpenMenus.NpcInteract))]
    [InlineData(0x0B, nameof(OpenMenus.NpcShop))]
    [InlineData(0x18, nameof(OpenMenus.Stash))]
    [InlineData(0x13, nameof(OpenMenus.Waypoint))]
    [InlineData(0x0A, nameof(OpenMenus.MapShown))]
    [InlineData(0x04, nameof(OpenMenus.SkillTree))]
    [InlineData(0x02, nameof(OpenMenus.Character))]
    [InlineData(0x09, nameof(OpenMenus.QuitMenu))]
    [InlineData(0x19, nameof(OpenMenus.Cube))]
    [InlineData(0x03, nameof(OpenMenus.SkillSelect))]
    [InlineData(0x0D, nameof(OpenMenus.Anvil))]
    [InlineData(0x0E, nameof(OpenMenus.QuestLog))]
    public void ReadMenus_ReadsEachFlagFromItsUIByte(int index, string menu)
    {
        var image = new GameImage();
        image.SetUIByte(index, 0x80);

        var menus = image.Reader().ReadMenus();
        foreach (var p in typeof(OpenMenus).GetProperties().Where(p => p.Name != nameof(OpenMenus.IsAnyPanelOpen)))
            Assert.Equal(p.Name == menu, (bool)p.GetValue(menus)!);
    }

    [Fact]
    public void ReadMenus_IgnoresTheOtherUIBytes()
    {
        var image = new GameImage();
        int[] used = [0x01, 0x168, 0x08, 0x0B, 0x18, 0x13, 0x0A, 0x04, 0x02, 0x09, 0x19, 0x03, 0x0D, 0x0E];
        for (var i = 0; i < 0x16D; i++)
        {
            if (!used.Contains(i))
                image.SetUIByte(i, 1);
        }

        Assert.Equal(default, image.Reader().ReadMenus());
    }

    [Fact]
    public void ReadParty_StartsWithTheMainPlayer_AndPrefersPlayerUnitPositions()
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = 1, Name = "Me", Area = 1, X = 10, Y = 11, IsMain = true });
        image.AddPlayer(new PlayerSpec { UnitId = 2, Name = "Near", Area = 3, X = 30, Y = 31 });
        image.SetRoster(("Near", 99, 1, 1), ("FarAwayMember123XYZ", 5, 0x8000_0000, 51));

        var party = image.Reader().ReadParty();

        Assert.Equal(
            [
                new PartyMember("Me", AreaId.RogueEncampment, new Point(10, 11)),
                new PartyMember("Near", AreaId.ColdPlains, new Point(30, 31)),
                // Names are 16-byte fields; coordinates 32-bit values.
                new PartyMember("FarAwayMember123", AreaId.DarkWood, new Point(int.MinValue, 51)),
            ],
            party);
    }

    [Fact]
    public void ReadParty_OutOfAGame_IsEmpty()
    {
        // d2go listed a zero member for the missing main player.
        Assert.Empty(new GameImage().Reader().ReadParty());
    }

    [Fact]
    public void ParseQuests_ReadsALittleEndianWordPerQuest()
    {
        // The layout of a save file's quest block, as Reign of the Warlock's memory holds it.
        var bytes = new byte[82];
        bytes[1] = 0x10;  // Den of Evil: completed
        bytes[10] = 0x01; // Sisters to the Slaughter: reward granted
        bytes[78] = 0x02; // Eve of Destruction: reward pending
        bytes[12] = 0xFF; // between the acts: not a quest
        bytes[50] = 0x01; bytes[51] = 0x80; // Terror's End: bits 0 and 15
        var quests = GameReader.ParseQuests(bytes);

        Assert.Equal(27, quests.Count);
        Assert.True(quests[QuestId.Act1DenOfEvil].IsCompleted());
        Assert.Equal(QuestStatus.UpdateQuestLogCompleted, quests[QuestId.Act1DenOfEvil]);
        Assert.Equal(QuestStatus.RewardGranted, quests[QuestId.Act1SistersToTheSlaughter]);
        Assert.Equal(QuestStatus.RewardPending, quests[QuestId.Act5EveOfDestruction]);
        Assert.Equal(QuestStatus.Unknown2 | QuestStatus.RewardGranted, quests[QuestId.Act4TerrorsEnd]);
        Assert.Equal(23, quests.Values.Count(s => s == QuestStatus.None));
        Assert.ThrowsAny<ArgumentException>(() => GameReader.ParseQuests(new byte[79]));
    }

    [Fact]
    public void ReadQuests_FollowsTwoPointersAndSkipsTwoBytes()
    {
        var image = new GameImage();
        var bytes = new byte[82];
        bytes[0] = 0x01;
        image.SetQuestBytes(bytes);
        Assert.Equal(QuestStatus.RewardGranted, image.Reader().ReadQuests()[QuestId.Act1DenOfEvil]);
    }

    [Fact]
    public void ReadTerrorZones_AreTheNonZeroAreasOfEightSlots_OnlineOrOffline()
    {
        // d2go always read the online address.
        var image = new GameImage();
        var online = GameImage.Base + 0x29BD4D8;
        var offline = GameImage.Base + 0x22D6D44;
        image.Memory.WriteU32(online + 4, (uint)AreaId.BloodMoor);
        image.Memory.WriteU32(online + 7 * 4, (uint)AreaId.DarkWood);
        image.Memory.WriteU32(online + 8 * 4, (uint)AreaId.BlackMarsh); // a ninth slot is not read
        image.Memory.WriteU32(offline, (uint)AreaId.StonyField);
        var reader = image.Reader();

        Assert.Equal([AreaId.StonyField], reader.ReadTerrorZones());
        image.Memory.WriteU8(GameImage.Base + 0x2140ED0, 1);
        Assert.Equal([AreaId.BloodMoor, AreaId.DarkWood], reader.ReadTerrorZones());
    }

    [Fact]
    public void FixedAddresses_FlagsAndStrings()
    {
        var image = new GameImage();
        var m = image.Memory;
        var reader = image.Reader();
        Assert.False(reader.IsLegacyGraphics());
        Assert.False(reader.IsOnline());
        Assert.False(reader.IsInGame());
        Assert.Equal(new OnlineGameInfo("", "", 0), reader.ReadOnlineGame());

        m.WriteU64(GameImage.Base + 0x21E2308, 1);
        m.WriteU8(GameImage.Base + 0x2140ED0, 1);
        m.WriteU32(GameImage.Base + 0x2140DF4, 0x8000_0090);
        m.WriteCString(GameImage.Base + 0x29DBD10 + 0x8, "game-01");
        m.WriteCString(GameImage.Base + 0x29DBD10 + 0x60, "pw");
        m.WriteCString(GameImage.Base + GameReader.SelectedCharacterNameAddress, "MySorc");
        Assert.True(reader.IsLegacyGraphics());
        Assert.True(reader.IsOnline());
        Assert.False(reader.IsInGame()); // no main player unit
        Assert.Equal(new OnlineGameInfo("game-01", "pw", 0x8000_0090), reader.ReadOnlineGame());
        Assert.Equal("MySorc", reader.ReadSelectedCharacterName());

        // Exactly 1, not just non-zero; legacy graphics is 8 bytes, the other two flags 1.
        m.WriteU64(GameImage.Base + 0x21E2308, 0x101);
        m.WriteU64(GameImage.Base + 0x2140ED0, 0x201);
        Assert.False(reader.IsLegacyGraphics());
        Assert.True(reader.IsOnline());
    }

    [Fact]
    public void Widgets_ScreensNeedActiveAndVisible()
    {
        var image = new GameImage();
        image.SetPanels(
            image.Widget("LobbyBackgroundPanel", active: true, visible: true),
            image.Widget("CharacterSelectPanel", active: true, visible: false),
            image.Widget("CharacterCreatePanel", active: true, visible: true));
        var reader = image.Reader();

        var widgets = reader.ReadWidgets();
        Assert.Equal(["CharacterCreatePanel", "CharacterSelectPanel", "LobbyBackgroundPanel"], widgets.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new Widget("CharacterSelectPanel", true, false), widgets["CharacterSelectPanel"]);
        Assert.True(reader.IsInLobby());
        Assert.False(reader.IsInCharacterSelection());
        Assert.True(reader.IsInCharacterCreation());
        Assert.True(reader.IsWidgetShown("LobbyBackgroundPanel"));
        Assert.False(reader.IsWidgetShown("Missing"));
    }

    [Fact]
    public void Widgets_UnnamedChildrenAreSkipped_LaterNamesWin()
    {
        // d2go dropped every widget when one child had no name.
        var image = new GameImage();
        image.SetPanels(
            image.Widget("LobbyBackgroundPanel", true, true),
            image.Widget("", true, true),
            image.Widget("LobbyBackgroundPanel", false, true));
        var widgets = image.Reader().ReadWidgets();
        Assert.Equal(new Widget("LobbyBackgroundPanel", false, true), Assert.Single(widgets).Value);
    }

    [Fact]
    public void Widgets_WithoutAPanelManager_AreEmpty()
    {
        Assert.Empty(new GameImage().Reader().ReadWidgets());

        var image = new GameImage();
        var panel = image.Widget("PanelManager", true, true, childList: 0x10, childCount: GameReader.MaxListLength + 1);
        image.Memory.WriteU64(GameImage.Base + GameImage.PanelOffset, panel);
        Assert.Empty(image.Reader().ReadWidgets());
    }

    [Fact]
    public void WidgetContainer_ReadsTheFullFormFromTheNameStruct()
    {
        var image = new GameImage();
        var widget = image.Widget("PanelX", active: false, visible: true, nameLength: 6, childList: 0xABC0, childCount: 3);
        var nameStruct = image.Memory.ReadU64(widget + 0x08);
        image.Memory.WriteU64(nameStruct + 0x68, 0x1111);
        image.Memory.WriteU32(nameStruct + 0x78, 4);
        image.Memory.WriteU64(nameStruct + 0x80, 0x2222);
        image.Memory.WriteU32(nameStruct + 0x90, 5);
        var reader = image.Reader();

        Assert.Equal(
            new GameReader.WidgetContainer(new Widget("PanelX", false, true), 6, 0xABC0, 3, 0x1111, 4, 0x2222, 5),
            reader.ReadWidgetContainer((nuint)widget, full: true));
        Assert.Equal(
            new GameReader.WidgetContainer(new Widget("PanelX", false, true), 6, 0, 0, 0, 0, 0, 0),
            reader.ReadWidgetContainer((nuint)widget, full: false));
        Assert.Null(reader.ReadWidgetContainer(0x10, full: false));
    }

    [Fact]
    public void ReadKeyBindings_ReadTwoBytesPerKey()
    {
        var image = new GameImage();
        var blob = GameImage.Base + GameReader.KeyBindingsAddress;
        var skills = GameImage.Base + GameReader.SkillKeyBindingsAddress;
        image.Memory.Write(blob + 0x14, [0x49, 0x00]);             // Inventory primary: I
        image.Memory.Write(blob + 0x1e, [0x09, 0x01]);             // Inventory secondary
        image.Memory.Write(blob + 0x118 + 0x14, [0x70, 0x00]);     // skill 1 primary: F1
        image.Memory.WriteU32(skills + 0x1c, (uint)SkillId.Teleport);
        image.Memory.WriteU32(skills + 15 * 0x1c, 0x0001_FFFF);    // skill 15: the low 16 bits (-1)
        image.Memory.Write(blob + 0x384 + 8 * 0x14, [0x31, 0x32]); // skill 15 primary
        image.Memory.Write(blob + 0x1c2 + 3 * 0x14, [0x34, 0x00]); // belt 4 secondary
        var reader = image.Reader();

        var kb = reader.ReadKeyBindings();
        Assert.Equal(new KeyBinding(new Key(0x49, 0), new Key(0x09, 0x01)), kb.Inventory);
        Assert.Equal(16, kb.Skills.Count);
        Assert.Equal(new SkillBinding(SkillId.Teleport, new KeyBinding(new Key(0x70, 0), default)), kb.Skills[1]);
        Assert.Equal(new SkillBinding(SkillId.None, new KeyBinding(new Key(0x31, 0x32), default)), kb.Skills[15]);
        Assert.Equal(4, kb.UseBelt.Count);
        Assert.Equal(new KeyBinding(default, new Key(0x34, 0)), kb.UseBelt[3]);
        Assert.Equal(kb.Skills[1].Keys, kb.FindSkill(SkillId.Teleport));

        // Two reads of the same memory are equal.
        Assert.Equal(kb, reader.ReadKeyBindings());
    }

    [Fact]
    public void ReadSnapshot_ReadsTheWholeState()
    {
        var image = new GameImage();
        var m = image.Memory;
        image.AddPlayer(new PlayerSpec
        {
            UnitId = 1, Name = "Hero", Area = (uint)AreaId.BloodMoor, X = 100, Y = 100, IsMain = true, Class = 3,
            BaseStats = [new RawStat(StatId.Gold, 42)],
        });
        image.AddPlayer(new PlayerSpec { UnitId = 2, Name = "Hero", Area = 2, X = 90, Y = 90, CorpseFlag = 1, Bucket = 4 });
        image.AddMonster((uint)MonsterId.Zombie, 50, 101, 100);
        image.AddMonster((uint)MonsterId.Zombie, 51, 120, 100, corpse: true);
        image.AddObject((uint)ObjectId.TownPortal, 60, 105, 100, owner: "Hero");
        image.AddItem(1, 70, 3, 0, x: 102, y: 100);
        image.SetHover(1, 1, 50);
        image.SetUIByte(0x01, 1);
        image.SetRoster(("Friend", 4, 7, 8));
        image.SetPanels(image.Widget("LobbyBackgroundPanel", false, true));
        var questBytes = new byte[82];
        questBytes[0] = 0x01;
        image.SetQuestBytes(questBytes);
        m.WriteU32(GameImage.Base + 0x29BD4D8, (uint)AreaId.DarkWood);
        m.Write(GameImage.Base + GameReader.KeyBindingsAddress, [0x43, 0x00]);
        m.WriteU64(GameImage.Base + 0x21E2308, 1);
        m.WriteU8(GameImage.Base + 0x2140ED0, 1);
        m.WriteU32(GameImage.Base + 0x2140DF4, 60);
        m.WriteCString(GameImage.Base + 0x29DBD10 + 0x8, "g");
        m.WriteCString(GameImage.Base + 0x29DBD10 + 0x60, "p");

        var d = image.Reader().ReadSnapshot();

        Assert.NotNull(d.Player);
        Assert.Equal("Hero", d.Player.Name);
        Assert.Equal(CharacterClass.Paladin, d.Player.Class);
        Assert.Equal(2, d.Players.Count);
        Assert.Equal(new PlayerCorpse(new Point(90, 90), false), d.Corpse);
        Assert.Equal(new OnlineGameInfo("g", "p", 60), d.Game);
        Assert.True(Assert.Single(d.Monsters).IsHovered);
        Assert.Equal(51u, Assert.Single(d.Corpses).Id.Value);
        Assert.Equal(42, d.Inventory.Gold);
        Assert.Equal(70u, Assert.Single(d.Inventory.Items).Id.Value);
        Assert.Equal("Hero", Assert.Single(d.Objects).Owner);
        Assert.True(d.Menus.Inventory);
        Assert.Single(d.Widgets);
        Assert.Equal(["Hero", "Friend"], d.Party.Select(r => r.Name));
        Assert.Equal(new HoveredUnit(UnitType.Monster, new UnitId(50)), d.Hover);
        Assert.Equal([AreaId.DarkWood], d.TerrorZones);
        Assert.Equal(QuestStatus.RewardGranted, d.Quests[QuestId.Act1DenOfEvil]);
        Assert.Equal(new Key(0x43, 0), d.KeyBindings.CharacterScreen.Primary);
        Assert.True(d.LegacyGraphics);
        Assert.True(d.IsOnline);
        Assert.True(d.IsInGame);
        Assert.False(d.IsInLobby); // visible but not active
        Assert.False(d.IsInCharacterSelection);
        Assert.False(d.IsInCharacterCreation);
        Assert.Same(d.Monsters[0], d.FindMonster(new UnitId(50)));
        Assert.Same(d.Corpses[0], d.FindMonster(new UnitId(51)));
    }

    [Fact]
    public void ReadSnapshot_OfAnEmptyImage_HasNoPlayer()
    {
        var d = new GameImage().Reader().ReadSnapshot();
        Assert.Null(d.Player);
        Assert.Null(d.Corpse);
        Assert.Null(d.Hover);
        Assert.Empty(d.Monsters);
        Assert.Empty(d.Party);
        Assert.Equal(27, d.Quests.Count);
        Assert.Empty(d.TerrorZones);
        Assert.Empty(d.Widgets);
    }
}

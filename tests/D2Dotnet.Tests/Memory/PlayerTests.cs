using D2Dotnet.Game;
using D2Dotnet.Memory;

namespace D2Dotnet.Tests.Memory;

/// <summary>player.go, player_unit.go and the stat lists of game_reader.go.</summary>
public sealed class PlayerTests
{
    [Fact]
    public void ReadPlayers_WalksEveryBucketAndChain()
    {
        var image = new GameImage();
        var a = image.AddPlayer(new PlayerSpec { UnitId = 10, Name = "Alice", Area = 1, X = 5000, Y = 6000, IsMain = true, Bucket = 0 });
        var b = image.AddPlayer(new PlayerSpec { UnitId = 11, Name = "Bob", Area = 2, X = 7, Y = 8, Bucket = 0 });
        var c = image.AddPlayer(new PlayerSpec { UnitId = 12, Name = "Carol", Area = 132, X = 9, Y = 10, Bucket = 127 });

        var units = image.Reader().ReadPlayers();

        Assert.Equal(["Alice", "Bob", "Carol"], units.Select(u => u.Name));
        Assert.Equal([(nuint)a, (nuint)b, (nuint)c], units.Select(u => u.Address));
        Assert.Equal([new UnitId(10), new UnitId(11), new UnitId(12)], units.Select(u => u.Id));
        Assert.Equal([AreaId.RogueEncampment, AreaId.BloodMoor, (AreaId)132], units.Select(u => u.Area));
        Assert.Equal(new Point(5000, 6000), units[0].Position);
        Assert.Equal([true, false, false], units.Select(u => u.IsMainPlayer));
    }

    [Fact]
    public void ReadPlayers_AUnitListThatLoops_Ends()
    {
        var image = new GameImage();
        var a = image.AddPlayer(new PlayerSpec { UnitId = 1, Name = "Loop" });
        image.Memory.WriteU64(a + 0x150, a);

        Assert.Equal(GameReader.MaxListLength, image.Reader().ReadPlayers().Count);
    }

    [Fact]
    public void MainPlayerFlag_DependsOnTheExpansionCharacter()
    {
        var image = new GameImage();
        var unit = image.AddPlayer(new PlayerSpec { UnitId = 1, Name = "P" });
        var inventory = image.Memory.ReadU64(unit + 0x90);
        image.Memory.WriteU16(inventory + 0x30, 1);
        image.Memory.WriteU16(inventory + 0x70, 0);
        var reader = image.Reader();

        // Classic character (expansion flag 0): +0x30.
        Assert.True(reader.ReadPlayers()[0].IsMainPlayer);

        // Expansion character: +0x70.
        image.Memory.WriteU16(image.ExpansionChar + 0x5C, 0x100);
        Assert.False(reader.ReadPlayers()[0].IsMainPlayer);
        Assert.Null(reader.ReadMainPlayer());
        image.Memory.WriteU16(inventory + 0x70, 2);
        Assert.True(reader.ReadPlayers()[0].IsMainPlayer);
    }

    [Theory]
    [InlineData(1, true, 3, 4, true)]
    [InlineData(1, true, 0, 4, false)]
    [InlineData(1, true, 3, 0, false)]
    [InlineData(1, false, 3, 4, false)]
    [InlineData(2, true, 3, 4, false)]
    [InlineData(0, true, 3, 4, false)]
    public void Corpse_NeedsFlag1AnInventoryAndAPosition(byte flag, bool inventory, ushort x, ushort y, bool expected)
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = 3, Name = "Dead", CorpseFlag = flag, HasInventory = inventory, X = x, Y = y });

        Assert.Equal(expected, image.Reader().ReadPlayers()[0].IsCorpse);
        var snapshot = image.Reader().ReadSnapshot();
        Assert.Equal(expected ? new PlayerCorpse(new Point(x, y), false) : null, snapshot.Corpse);
    }

    [Theory]
    [InlineData((ushort)1, 0u, 5u, true)]
    [InlineData((ushort)0x100, 0u, 5u, true)]
    [InlineData((ushort)0, 0u, 5u, false)]
    [InlineData((ushort)1, 1u, 5u, false)]
    [InlineData((ushort)1, 0u, 6u, false)]
    public void Hover_NeedsUnitType0AndTheId(ushort isHovered, uint type, uint id, bool expected)
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = 5, Name = "H" });
        image.SetHover(isHovered, type, id);

        Assert.Equal(expected, image.Reader().ReadPlayers()[0].IsHovered);
    }

    [Fact]
    public void ReadPlayers_ReadsStatsBaseStatsAndStates()
    {
        var image = new GameImage();
        var words = new uint[6];
        words[0] = 0x8000_0003; // states 0, 1, 31
        words[1] = 0x1;         // state 32
        words[5] = 1u << 26;    // state 186 (SharedStash)
        image.AddPlayer(new PlayerSpec
        {
            UnitId = 1,
            Name = "S",
            Stats = [new RawStat(StatId.Strength, 25), new RawStat(StatId.Life, 300 << 8)],
            BaseStats = [new RawStat(StatId.Gold, 1234), new RawStat(StatId.Strength, 20, Layer: 3)],
            StateWords = words,
        });

        var unit = image.Reader().ReadPlayers()[0];
        Assert.Equal([new Stat(StatId.Strength, 25), new Stat(StatId.Life, 300)], unit.Stats);
        Assert.Equal([new Stat(StatId.Gold, 1234), new Stat(StatId.Strength, 20, 3)], unit.BaseStats);
        Assert.Equal([StateId.None, StateId.Freeze, (StateId)31, (StateId)32, StateId.SharedStash], unit.States);
        Assert.True(unit.HasState(StateId.SharedStash));
        Assert.Equal(25, unit.GetStat(StatId.Strength));
        Assert.Equal(20, unit.GetStat(StatId.Strength, 3));
    }

    [Fact]
    public void StatesOfWord_AreBitsOfTheWordsPlace()
    {
        Assert.Equal(Enumerable.Range(32, 32).Select(i => (StateId)i), GameReader.StatesOfWord(0xFFFF_FFFF, 1));
        Assert.Empty(GameReader.StatesOfWord(0, 3));
        Assert.Equal([(StateId)0, (StateId)5], GameReader.StatesOfWord(0x21, 0));
        Assert.Equal([(StateId)191], GameReader.StatesOfWord(0x8000_0000, 5));
    }

    [Fact]
    public void Stats_AreSigned32BitValues()
    {
        // d2go kept the unsigned field in Go's 64-bit int: a resistance of -10 read as 4294967286.
        var image = new GameImage();
        var header = image.Memory.Alloc(0x10);
        image.WriteStatList(header, [
            new RawStat((StatId)(-1), 0xFFFF_FFF6, 7),
            new RawStat(StatId.FireResist, 0x8000_0000, 0xFFFF),
            new RawStat(StatId.Experience, 3_520_485_254),
        ]);

        var stats = image.Reader().ReadStatList((nuint)header);
        Assert.Equal([new Stat((StatId)(-1), -10, 7), new Stat(StatId.FireResist, int.MinValue, 0xFFFF), new Stat(StatId.Experience, unchecked((int)3_520_485_254))], stats);
        Assert.Equal(3_520_485_254u, stats[2].UnsignedValue);
    }

    [Fact]
    public void StatLists_EmptyUnreadableAndImplausibleListsAreEmpty()
    {
        var image = new GameImage();
        var reader = image.Reader();
        Assert.Empty(reader.ReadStatList(0x10));

        var header = image.Memory.Alloc(0x10);
        image.Memory.WriteU64(header, 0x1234_0000); // count 0
        Assert.Same(StatList.Empty, reader.ReadStatList((nuint)header));

        image.Memory.WriteU64(header + 8, GameReader.MaxStats + 1UL);
        Assert.Same(StatList.Empty, reader.ReadStatList((nuint)header));
    }

    public static TheoryData<StatId, uint, int> StatScaling => new()
    {
        { StatId.Strength, 77, 77 },
        { StatId.Life, 0x12345, 0x123 },
        { StatId.MaxLife, 255, 0 },
        { StatId.Mana, 512, 2 },
        { StatId.MaxMana, 1000, 3 },
        { StatId.Stamina, 0xFFFF_FFFF, 0x00FF_FFFF },
        { StatId.MaxStamina, 256, 1 },
        { StatId.ColdLength, 250, 10 },
        { StatId.PoisonLength, 49, 1 },
        { StatId.DeadlyStrikePerLevel, 4, 5 },
        { StatId.DeadlyStrikePerLevel, 7, 8 },
        { StatId.HitCausesMonsterToFlee, 32, 25 },
        { StatId.HitCausesMonsterToFlee, 1, 0 },
        { StatId.AttackRatingUndeadPerLevel, 9, 4 },
        { StatId.MagicFindPerLevel, 16, 2 },
        { StatId.MagicFindPerLevel, 7, 1 },
        { StatId.MagicFindPerLevel, 0, 1 },
        { StatId.ExtraGoldPerLevel, 80, 10 },
        { StatId.DamageDemonPerLevel, 8, 1 },
        { StatId.DamageUndeadPerLevel, 24, 3 },
        { StatId.DefensePerLevel, 12, 1 },
        { StatId.MaxDamagePerLevel, 64, 8 },
        { StatId.MaxDamagePercentPerLevel, 9, 1 },
        { StatId.StrengthPerLevel, 40, 5 },
        { StatId.DexterityPerLevel, 41, 5 },
        { StatId.VitalityPerLevel, 47, 5 },
        { StatId.ThornsPerLevel, 3, 1 },
        { StatId.LifePerLevel, 4096, 2 },
        { StatId.LifePerLevel, 2047, 1 },
        { StatId.ManaPerLevel, 6144, 3 },
        { StatId.ReplenishDurability, 1, 2 },
        { StatId.ReplenishDurability, 2, 1 },
        { StatId.ReplenishQuantity, 50, 1 },
        { StatId.ReplenishQuantity, 0, 0 }, // d2go divided by zero
        { StatId.RegenStaminaPerLevel, 7, 70 },
        { StatId.LevelRequirePercent, 20, -20 },
        { StatId.AttackRatingPerLevel, 3, 15 },
        { StatId.AttackRatingPerLevel, 40, 40 },
        // Results outside the int range saturate.
        { StatId.DeadlyStrikePerLevel, 0xFFFF_FFFF, int.MaxValue },
        { StatId.RegenStaminaPerLevel, 0xFFFF_FFFF, int.MaxValue },
        { StatId.LevelRequirePercent, 0xFFFF_FFFF, int.MinValue },
    };

    [Theory]
    [MemberData(nameof(StatScaling))]
    public void StatLists_RescaleLikeD2go(StatId id, uint raw, int expected)
    {
        var image = new GameImage();
        var header = image.Memory.Alloc(0x10);
        image.WriteStatList(header, [new RawStat(id, raw, 2)]);

        Assert.Equal([new Stat(id, expected, 2)], image.Reader().ReadStatList((nuint)header));
    }

    [Fact]
    public void ReadMainPlayer_ReadsSkillsSelectedSkillsClassAndWaypoints()
    {
        var image = new GameImage();
        var unit = image.AddPlayer(new PlayerSpec
        {
            UnitId = 9,
            Name = "Sorc",
            Area = (uint)AreaId.ColdPlains,
            X = 100,
            Y = 200,
            IsMain = true,
            Class = 1,
            Stats = [new RawStat(StatId.Level, 80)],
            BaseStats = [new RawStat(StatId.Strength, 10)],
            StateWords = [0x2, 0, 0, 0, 0, 0],
        });
        var m = image.Memory;

        // Skill list: first skill at +0, left skill at +0x08, right skill at +0x10; skills linked at +0x08.
        ulong Txt(ushort id)
        {
            var t = m.Alloc(0x10);
            m.WriteU16(t, id);
            return t;
        }

        ulong Skill(ushort id, ushort level, ushort quantity, ushort charges, ulong next)
        {
            var s = m.Alloc(0x60);
            m.WriteU64(s, Txt(id));
            m.WriteU64(s + 0x08, next);
            m.WriteU16(s + 0x38, level);
            m.WriteU16(s + 0x40, quantity);
            m.WriteU16(s + 0x48, charges);
            return s;
        }

        var dup = Skill((ushort)SkillId.Teleport, 20, 0, 0, 0);
        var s2 = Skill((ushort)SkillId.FireBolt, 1, 2, 3, dup);
        var s1 = Skill((ushort)SkillId.Teleport, 1, 0, 0, s2);
        var neg = Skill(0xFFFF, 4, 0, 0, s1);
        var skillList = m.Alloc(0x20);
        m.WriteU64(skillList, neg);
        m.WriteU64(skillList + 0x08, s2);
        m.WriteU64(skillList + 0x10, dup);
        m.WriteU64(unit + 0x100, skillList);

        // Waypoint list: 9 (area, active) pairs.
        var wp = GameImage.Base + GameReader.WaypointsAddress;
        uint[] wps = [1, 1, 3, 0, 4, 1, 5, 0, 6, 0, 0, 0, 0, 1, 0, 0, 7, 1];
        for (var i = 0; i < wps.Length; i++)
            m.WriteU32(wp + (ulong)(i * 4), wps[i]);

        var reader = image.Reader();
        var raw = reader.ReadPlayers()[0];
        var pu = reader.ReadMainPlayer();

        Assert.NotNull(pu);
        Assert.Equal((nuint)unit, pu.Address);
        Assert.Equal("Sorc", pu.Name);
        Assert.Equal(new UnitId(9), pu.Id);
        Assert.Equal(AreaId.ColdPlains, pu.Area);
        Assert.Equal(new Point(100, 200), pu.Position);
        Assert.Equal(raw.Stats, pu.Stats);
        Assert.Equal(raw.BaseStats, pu.BaseStats);
        Assert.Equal(raw.States, pu.States);
        Assert.Equal(CharacterClass.Sorceress, pu.Class);
        Assert.Equal(80, pu.Level);
        Assert.Equal(SkillId.FireBolt, pu.LeftSkill);
        Assert.Equal(SkillId.Teleport, pu.RightSkill);
        Assert.Equal(3, pu.Skills.Count);
        Assert.Equal(new SkillPoints(20, 0, 0), pu.Skills[SkillId.Teleport]); // the later entry wins
        Assert.Equal(new SkillPoints(1, 2, 3), pu.Skills[SkillId.FireBolt]);
        Assert.Equal(new SkillPoints(4, 0, 0), pu.Skills[SkillId.None]);
        // Active (1) only, not the current area (3, Cold Plains); area 0 is no waypoint even when active.
        Assert.Equal([AreaId.RogueEncampment, AreaId.StonyField, (AreaId)7], pu.AvailableWaypoints);
    }

    [Fact]
    public void ReadMainPlayer_WithoutAMainPlayer_IsNull()
    {
        var image = new GameImage();
        Assert.Null(image.Reader().ReadMainPlayer());
        image.AddPlayer(new PlayerSpec { UnitId = 1, Name = "Other" });
        Assert.Null(image.Reader().ReadMainPlayer());
        Assert.Null(image.Reader().ReadSnapshot().Player);
    }

    [Fact]
    public void ReadMainPlayer_KeepsUnsignedExperience()
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec
        {
            UnitId = 0x9000_0000, Name = "Hc", X = 1, Y = 1, Area = 1, IsMain = true,
            BaseStats = [new RawStat(StatId.Experience, 3_520_485_254), new RawStat(StatId.Level, 99)],
        });
        var reader = image.Reader();

        var pu = reader.ReadMainPlayer();
        Assert.NotNull(pu);
        Assert.Equal(new UnitId(0x9000_0000), pu.Id);
        Assert.Equal(3_520_485_254u, pu.Experience);
        Assert.Equal(990_000L, pu.MaxGold);
        Assert.True(reader.IsPlayerInWorld());
    }

    [Theory]
    [InlineData(1u, (ushort)1, (ushort)1, 1u, true)]
    [InlineData(0u, (ushort)1, (ushort)1, 1u, false)]
    [InlineData(1u, (ushort)0, (ushort)1, 1u, false)]
    [InlineData(1u, (ushort)1, (ushort)0, 1u, false)]
    [InlineData(1u, (ushort)1, (ushort)1, 0u, false)]
    [InlineData(0xFFFF_FFFFu, (ushort)1, (ushort)1, 1u, true)]
    public void IsPlayerInWorld_NeedsAnIdAPositionAndAnArea(uint id, ushort x, ushort y, uint area, bool expected)
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = id, Name = "M", X = x, Y = y, Area = area, IsMain = true });
        Assert.Equal(expected, image.Reader().IsPlayerInWorld());
    }

    [Fact]
    public void IsPlayerInWorld_WithoutAMainPlayer_IsFalse() => Assert.False(new GameImage().Reader().IsPlayerInWorld());
}

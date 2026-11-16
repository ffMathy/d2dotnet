using D2Dotnet.D2Go.Data.Area;
using D2Dotnet.D2Go.Data.Skill;
using D2Dotnet.D2Go.Data.Stat;
using D2Dotnet.D2Go.Data.State;

namespace D2Dotnet.D2Go.Memory.Tests;

// Inside the namespace, so these win over the library's types of the same name in the enclosing D2Dotnet namespace.
using AreaId = D2Dotnet.D2Go.Data.Area.AreaId;
using SkillId = D2Dotnet.D2Go.Data.Skill.SkillId;
using StatId = D2Dotnet.D2Go.Data.Stat.StatId;
using StateId = D2Dotnet.D2Go.Data.State.StateId;

/// <summary>player.go, player_unit.go and the stats list of game_reader.go.</summary>
public sealed class PlayerTests
{
    [Fact]
    public void GetRawPlayerUnits_WalksEveryBucketAndChain()
    {
        var image = new GameImage();
        var a = image.AddPlayer(new PlayerSpec { UnitId = 10, Name = "Alice", Area = 1, X = 5000, Y = 6000, IsMain = true, Bucket = 0 });
        var b = image.AddPlayer(new PlayerSpec { UnitId = 11, Name = "Bob", Area = 2, X = 7, Y = 8, Bucket = 0 });
        var c = image.AddPlayer(new PlayerSpec { UnitId = 12, Name = "Carol", Area = 132, X = 9, Y = 10, Bucket = 127 });
        using var reader = image.Reader();

        var units = reader.GetRawPlayerUnits();

        Assert.Equal(["Alice", "Bob", "Carol"], units.Select(u => u.Name));
        Assert.Equal([(nuint)a, (nuint)b, (nuint)c], units.Select(u => u.Address));
        Assert.Equal([new UnitId(10), new UnitId(11), new UnitId(12)], units.Select(u => u.UnitId));
        Assert.Equal([AreaId.RogueEncampment, AreaId.BloodMoor, (AreaId)132], units.Select(u => u.Area));
        Assert.Equal(new Position(5000, 6000), units[0].Position);
        Assert.Equal([true, false, false], units.Select(u => u.IsMainPlayer));
        Assert.Equal("Alice", units.GetMainPlayer().Name);
    }

    [Fact]
    public void GetRawPlayerUnits_MainPlayerFlag_DependsOnTheExpansionCharacter()
    {
        var image = new GameImage();
        var unit = image.AddPlayer(new PlayerSpec { UnitId = 1, Name = "P" });
        var inventory = image.Memory.ReadU64(unit + 0x90);
        image.Memory.WriteU16(inventory + 0x30, 1);
        image.Memory.WriteU16(inventory + 0x70, 0);
        using var reader = image.Reader();

        // Classic character (expansion flag 0): +0x30.
        Assert.True(reader.GetRawPlayerUnits()[0].IsMainPlayer);

        // Expansion character: +0x70.
        image.Memory.WriteU16(image.ExpansionChar + 0x5C, 0x100);
        Assert.False(reader.GetRawPlayerUnits()[0].IsMainPlayer);
        image.Memory.WriteU16(inventory + 0x70, 2);
        Assert.True(reader.GetRawPlayerUnits()[0].IsMainPlayer);
    }

    [Theory]
    [InlineData(1, true, 3, 4, true)]
    [InlineData(1, true, 0, 4, false)]
    [InlineData(1, true, 3, 0, false)]
    [InlineData(1, false, 3, 4, false)]
    [InlineData(2, true, 3, 4, false)]
    [InlineData(0, true, 3, 4, false)]
    public void GetRawPlayerUnits_Corpse_NeedsFlag1AnInventoryAndAPosition(byte flag, bool inventory, ushort x, ushort y, bool expected)
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = 3, Name = "Dead", CorpseFlag = flag, HasInventory = inventory, X = x, Y = y });
        using var reader = image.Reader();

        var units = reader.GetRawPlayerUnits();
        Assert.Equal(expected, units[0].IsCorpse);
        Assert.Equal(expected ? "Dead" : "", units.GetCorpse().Name);
    }

    [Theory]
    [InlineData((ushort)1, 0u, 5u, true)]
    [InlineData((ushort)0x100, 0u, 5u, true)]
    [InlineData((ushort)0, 0u, 5u, false)]
    [InlineData((ushort)1, 1u, 5u, false)]
    [InlineData((ushort)1, 0u, 6u, false)]
    public void GetRawPlayerUnits_Hover_NeedsUnitType0AndTheId(ushort isHovered, uint type, uint id, bool expected)
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = 5, Name = "H" });
        image.SetHover(isHovered, type, id);
        using var reader = image.Reader();

        Assert.Equal(expected, reader.GetRawPlayerUnits()[0].IsHovered);
    }

    [Fact]
    public void GetRawPlayerUnits_ReadsStatsBaseStatsAndStates()
    {
        var image = new GameImage();
        var words = new uint[6];
        words[0] = 0x8000_0003; // states 0, 1, 31
        words[1] = 0x1;         // state 32
        words[5] = 1u << 26;    // state 186 (Sharedstash)
        image.AddPlayer(new PlayerSpec
        {
            UnitId = 1,
            Name = "S",
            Stats = [new RawStat(StatId.Strength, 25), new RawStat(StatId.Life, 300 << 8)],
            BaseStats = [new RawStat(StatId.Gold, 1234), new RawStat(StatId.Strength, 20, Layer: 3)],
            StateWords = words,
        });
        using var reader = image.Reader();

        var unit = reader.GetRawPlayerUnits()[0];
        Assert.Equal([new StatData(StatId.Strength, 25, 0), new StatData(StatId.Life, 300, 0)], unit.Stats);
        Assert.Equal([new StatData(StatId.Gold, 1234, 0), new StatData(StatId.Strength, 20, 3)], unit.BaseStats);
        Assert.Equal([StateId.None, StateId.Freeze, (StateId)31, (StateId)32, StateId.Sharedstash], unit.States);
    }

    [Fact]
    public void GetPlayerUnit_KeepsUnsigned32BitStatsAndIdsPositive_LikeGo()
    {
        // Level 99 experience (3,520,485,254) and a unit id of 2^31 or more: Go's 64-bit int keeps both positive.
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec
        {
            UnitId = 0x9000_0000,
            Name = "Hc",
            X = 1,
            Y = 1,
            Area = 1,
            IsMain = true,
            Stats = [new RawStat(StatId.Experience, 3_520_485_254)],
            BaseStats = [new RawStat(StatId.Experience, 3_520_485_254), new RawStat(StatId.Level, 99)],
        });
        using var reader = image.Reader();

        var raw = reader.GetRawPlayerUnits().GetMainPlayer();
        var pu = reader.GetPlayerUnit(raw);
        Assert.Equal(new UnitId(0x9000_0000L), pu.Id);
        Assert.True(pu.TryFindStat(StatId.Experience, 0, out var xp));
        Assert.Equal(3_520_485_254L, xp.Value);
        Assert.Equal(new StatData(StatId.Experience, 3_520_485_254L, 0), pu.BaseStats[0]);
        Assert.Equal(990_000L, pu.MaxGold());
        Assert.True(reader.InGame());
    }

    [Fact]
    public void CalculateStates_GivesBitPlusOnePlusOffset_LowestBitFirst()
    {
        var all = GameReader.CalculateStates(0xFFFF_FFFF, 31);
        Assert.Equal(Enumerable.Range(32, 32).Select(i => (StateId)i), all);
        Assert.Empty(GameReader.CalculateStates(0, 31));
        Assert.Empty(GameReader.CalculateStates(0x1_0000_0000, 0)); // only 32 bits are tested
        // The first word's offset is uint(-1): bit 0 wraps to state 0.
        Assert.Equal([(StateId)0, (StateId)5], GameReader.CalculateStates(0x21, ulong.MaxValue));
    }

    [Fact]
    public void GetStatsList_EmptyAndUnreadableListsAreEmpty()
    {
        var image = new GameImage();
        using var reader = image.Reader();
        Assert.Empty(reader.GetStatsList(0x10));

        var header = image.Memory.Alloc(0x10);
        image.Memory.WriteU64(header, 0x1234_0000); // count 0
        Assert.Same(Stats.Empty, reader.GetStatsList((nuint)header));
    }

    [Fact]
    public void GetStatsList_WrapsIdsAndValuesLikeTheGameStoresThem()
    {
        var image = new GameImage();
        var header = image.Memory.Alloc(0x10);
        image.WriteStatList(header, [new RawStat((StatId)(-1), 0xFFFF_FFF6, 7), new RawStat(StatId.FireResist, 0x8000_0000, 0xFFFF)]);
        using var reader = image.Reader();

        var stats = reader.GetStatsList((nuint)header);
        // Id 0xFFFF is stat.ID(-1); values are Go's int(uint32), never negative: a resist of -10 as the game stores it
        // reads as 4294967286, as in Go.
        Assert.Equal([new StatData((StatId)(-1), 4294967286L, 7), new StatData(StatId.FireResist, 0x8000_0000L, 0xFFFF)], stats);
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
        { StatId.RegenStaminaPerLevel, 7, 70 },
        { StatId.LevelRequirePercent, 20, -20 },
        { StatId.AttackRatingPerLevel, 3, 15 },
        { StatId.AttackRatingPerLevel, 40, 40 },
    };

    [Theory]
    [MemberData(nameof(StatScaling))]
    public void GetStatsList_RescalesLikeGo(StatId id, uint raw, int expected)
    {
        var image = new GameImage();
        var header = image.Memory.Alloc(0x10);
        image.WriteStatList(header, [new RawStat(id, raw, 2)]);
        using var reader = image.Reader();

        Assert.Equal([new StatData(id, expected, 2)], reader.GetStatsList((nuint)header));
    }

    [Fact]
    public void StatValue_ComputesWithGo64BitInts()
    {
        // 0xFFFFFFFF / 0.8 and * 10 overflow 32 bits; the port narrows Go's 64-bit result.
        Assert.Equal(5368709118L, GameReader.StatValue(StatId.DeadlyStrikePerLevel, 0xFFFF_FFFF));
        Assert.Equal(42949672950L, GameReader.StatValue(StatId.RegenStaminaPerLevel, 0xFFFF_FFFF));
        Assert.Equal(-4294967295L, GameReader.StatValue(StatId.LevelRequirePercent, 0xFFFF_FFFF));
    }

    [Fact]
    public void GetStatsList_ReplenishZero_DividesByZeroLikeGo()
    {
        var image = new GameImage();
        var header = image.Memory.Alloc(0x10);
        image.WriteStatList(header, [new RawStat(StatId.ReplenishQuantity, 0)]);
        using var reader = image.Reader();

        Assert.Throws<DivideByZeroException>(() => reader.GetStatsList((nuint)header));
    }

    [Fact]
    public void GetPlayerUnit_ReadsSkillsSelectedSkillsClassAndWaypoints()
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

        // Waypoint list: 9 (area, available) pairs.
        var wp = GameImage.Base + 0x2140E20;
        uint[] wps = [1, 1, 3, 0, 4, 1, 5, 0, 6, 0, 0, 0, 0, 0, 0, 0, 7, 1];
        for (var i = 0; i < wps.Length; i++)
            m.WriteU32(wp + (ulong)(i * 4), wps[i]);

        using var reader = image.Reader();
        var raw = reader.GetRawPlayerUnits().GetMainPlayer();
        var pu = reader.GetPlayerUnit(raw);

        Assert.Equal((nuint)unit, pu.Address);
        Assert.Equal("Sorc", pu.Name);
        Assert.Equal(new UnitId(9), pu.Id);
        Assert.Equal(AreaId.ColdPlains, pu.Area);
        Assert.Equal(new Position(100, 200), pu.Position);
        Assert.Same(raw.Stats, pu.Stats);
        Assert.Same(raw.BaseStats, pu.BaseStats);
        Assert.Same(raw.States, pu.States);
        Assert.Equal(Class.Sorceress, pu.Class);
        Assert.Equal(SkillId.FireBolt, pu.LeftSkill);
        Assert.Equal(SkillId.Teleport, pu.RightSkill);
        Assert.Equal(3, pu.Skills.Count);
        Assert.Equal(new SkillPoints(20, 0, 0), pu.Skills[SkillId.Teleport]); // the later entry wins
        Assert.Equal(new SkillPoints(1, 2, 3), pu.Skills[SkillId.FireBolt]);
        Assert.Equal(new SkillPoints(4, 0, 0), pu.Skills[(SkillId)(-1)]);
        // Available (1), or the current area (3, Cold Plains) even when not available.
        Assert.Equal([AreaId.RogueEncampment, AreaId.ColdPlains, AreaId.StonyField, (AreaId)7], pu.AvailableWaypoints);
    }

    [Fact]
    public void GetPlayerUnit_OfTheZeroUnit_ReadsNothingUseful()
    {
        var image = new GameImage();
        using var reader = image.Reader();
        var pu = reader.GetPlayerUnit(new RawPlayerUnit());
        Assert.Empty(pu.Skills);
        Assert.Equal(SkillId.AttackSkill, pu.LeftSkill);
        // Unmapped waypoint list: 9 zero areas, and area 0 is the zero unit's area.
        Assert.Equal(Enumerable.Repeat(AreaId.None, 9), pu.AvailableWaypoints);
        Assert.Throws<ArgumentNullException>(() => reader.GetPlayerUnit(null!));
    }

    [Theory]
    [InlineData(1u, (ushort)1, (ushort)1, 1u, true)]
    [InlineData(0u, (ushort)1, (ushort)1, 1u, false)]
    [InlineData(1u, (ushort)0, (ushort)1, 1u, false)]
    [InlineData(1u, (ushort)1, (ushort)0, 1u, false)]
    [InlineData(1u, (ushort)1, (ushort)1, 0u, false)]
    [InlineData(0x8000_0000u, (ushort)1, (ushort)1, 1u, true)] // Go's UnitID(uint32) is never negative
    [InlineData(0xFFFF_FFFFu, (ushort)1, (ushort)1, 1u, true)]
    public void InGame_NeedsAnIdAPositionAndAnArea(uint id, ushort x, ushort y, uint area, bool expected)
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = id, Name = "M", X = x, Y = y, Area = area, IsMain = true });
        using var reader = image.Reader();

        Assert.Equal(expected, reader.InGame());
    }

    [Fact]
    public void RawPlayerUnits_WithoutMatches_GiveTheZeroUnit()
    {
        RawPlayerUnits units = [new RawPlayerUnit { Name = "x" }];
        Assert.Equal("", units.GetMainPlayer().Name);
        Assert.Equal("", units.GetCorpse().Name);
        Assert.Empty(RawPlayerUnits.Empty);
        Assert.Equal(2, new RawPlayerUnits([new RawPlayerUnit(), new RawPlayerUnit { IsMainPlayer = true, Name = "m" }]).Count);
    }
}

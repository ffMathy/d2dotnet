using D2Dotnet.D2Go.Data.Npc;
using D2Dotnet.D2Go.Data.Object;
using D2Dotnet.D2Go.Data.Stat;

namespace D2Dotnet.D2Go.Memory.Tests;

/// <summary>monsters.go and object.go.</summary>
public sealed class UnitTests
{
    private static readonly HoverData NoHover = default;

    [Fact]
    public void Monsters_ReadsLivingMonsters_NearestFirst_StableOnTies()
    {
        var image = new GameImage();
        image.AddMonster((uint)NpcId.Zombie, 100, 50, 50, bucket: 0);        // distance 50
        image.AddMonster((uint)NpcId.Andariel, 101, 10, 0, typeFlag: 8, bucket: 3); // distance 10
        image.AddMonster((uint)NpcId.Duriel, 102, 0, 10, bucket: 3);        // distance 10, after Andariel
        image.AddMonster((uint)NpcId.Zombie, 103, 3, 4, bucket: 127);       // distance 5
        using var reader = image.Reader();

        var monsters = reader.Monsters(new Position(0, 0), NoHover);

        Assert.Equal([103, 101, 102, 100], monsters.Select(m => m.UnitId.Value));
        Assert.Equal(NpcId.Andariel, monsters[1].Name);
        Assert.Equal(MonsterType.Unique, monsters[1].Type);
        Assert.Equal(MonsterType.None, monsters[0].Type);
        Assert.Equal(new Position(10, 0), monsters[1].Position);
    }

    [Fact]
    public void Monsters_LeaveOutCorpsesAndIgnoredNpcsWithoutExperience()
    {
        var image = new GameImage();
        image.AddMonster((uint)NpcId.Zombie, 1, 1, 1, corpse: true);
        image.AddMonster((uint)NpcId.Chicken, 2, 1, 1);
        image.AddMonster((uint)NpcId.Guard, 3, 1, 1, stats: [new RawStat(StatId.Experience, 5)]); // act 2 merc
        image.AddMonster((uint)NpcId.Rogue2, 4, 1, 1, stats: [new RawStat(StatId.Experience, 0)]);
        image.AddMonster((uint)NpcId.Hydra, 5, 1, 1, corpse: true, stats: [new RawStat(StatId.Experience, 1)]);
        using var reader = image.Reader();

        Assert.Equal([3], reader.Monsters(default, NoHover).Select(m => m.UnitId.Value));
        Assert.Equal([1, 5], reader.Corpses(default, NoHover).Select(m => m.UnitId.Value));
    }

    [Fact]
    public void Monsters_ReadStatsAsAMap_LaterEntriesWin()
    {
        var image = new GameImage();
        image.AddMonster((uint)NpcId.Zombie, 1, 1, 1, stats:
        [
            new RawStat(StatId.Life, 0x4000),
            new RawStat(StatId.FireResist, 100, Layer: 9),
            new RawStat(StatId.Life, 0x8000),
            new RawStat(StatId.ColdResist, 0xFFFF_FFF6),
        ]);
        using var reader = image.Reader();

        var m = Assert.Single(reader.Monsters(default, NoHover));
        // Monster stats are not rescaled, and the layer is not read.
        Assert.Equal(3, m.Stats.Count);
        Assert.Equal(0x8000, m.Stats[StatId.Life]);
        Assert.Equal(100, m.Stats[StatId.FireResist]);
        Assert.Equal(-10, m.Stats[StatId.ColdResist]);
    }

    [Theory]
    [InlineData((byte)10, "SuperUnique")]
    [InlineData((byte)4, "Champion")]
    [InlineData((byte)12, "Champion")]
    [InlineData((byte)8, "Unique")]
    [InlineData((byte)16, "Minion")]
    [InlineData((byte)0, "None")]
    [InlineData((byte)2, "None")]
    [InlineData((byte)24, "None")]
    public void GetMonsterType_MapsTheFlag(byte flag, string expected) =>
        Assert.Equal(new MonsterType(expected), GameReader.GetMonsterType(flag));

    [Theory]
    [InlineData((ushort)1, 1u, 7u, true)]
    [InlineData((ushort)1, 0u, 7u, false)]
    [InlineData((ushort)1, 1u, 8u, false)]
    [InlineData((ushort)0, 1u, 7u, false)]
    public void MonstersAndCorpses_Hover_NeedsUnitType1(ushort isHovered, uint type, uint id, bool expected)
    {
        var image = new GameImage();
        image.AddMonster((uint)NpcId.Zombie, 7, 1, 1);
        image.AddMonster((uint)NpcId.Zombie, 7, 1, 1, corpse: true);
        image.SetHover(isHovered, type, id);
        using var reader = image.Reader();
        var hover = reader.HoveredData();

        Assert.Equal(expected, Assert.Single(reader.Monsters(default, hover)).IsHovered);
        Assert.Equal(expected, Assert.Single(reader.Corpses(default, hover)).IsHovered);
    }

    [Fact]
    public void Corpses_AreSortedAndTyped()
    {
        var image = new GameImage();
        image.AddMonster((uint)NpcId.Zombie, 1, 100, 100, typeFlag: 16, corpse: true);
        image.AddMonster((uint)NpcId.FallenShaman, 2, 5, 5, typeFlag: 10, corpse: true, stats: [new RawStat(StatId.Strength, 3)]);
        using var reader = image.Reader();

        var corpses = reader.Corpses(new Position(0, 0), NoHover);
        Assert.Equal([2, 1], corpses.Select(c => c.UnitId.Value));
        Assert.Equal(MonsterType.SuperUnique, corpses[0].Type);
        Assert.Equal(MonsterType.Minion, corpses[1].Type);
        Assert.Equal(3, corpses[0].Stats[StatId.Strength]);
        Assert.Empty(corpses[1].Stats);
    }

    [Fact]
    public void ShouldBeIgnored_ListsCrittersAndTraps()
    {
        Assert.True(GameReader.ShouldBeIgnored((ulong)NpcId.Chicken));
        Assert.True(GameReader.ShouldBeIgnored((ulong)NpcId.DemonHole));
        Assert.False(GameReader.ShouldBeIgnored((ulong)NpcId.Zombie));
        Assert.False(GameReader.ShouldBeIgnored(uint.MaxValue));
    }

    [Fact]
    public void Objects_ShrineBranch_IsTakenWhenTheBytesAt0x0AAreNotZero()
    {
        var image = new GameImage();
        // Mode 1 makes the bytes at +0x0A non-zero.
        image.AddObject((uint)ObjectName.Shrine, 5, 10, 10, mode: 1, typeByte: (byte)ShrineType.ExperienceShrine, owner: "");
        // Unit id >= 65536 too.
        image.AddObject((uint)ObjectName.Shrine, 0x10000, 20, 20, typeByte: 0x7F);
        using var reader = image.Reader();

        var objects = reader.Objects(default, NoHover);
        Assert.Equal(2, objects.Count);
        Assert.Equal(new ShrineData { ShrineName = "Experience Shrine", ShrineType = ShrineType.ExperienceShrine }, objects[0].Shrine);
        Assert.Equal(InteractType.None, objects[0].InteractType);
        Assert.False(objects[0].Selectable);
        // An unknown shrine type has no name.
        Assert.Equal(new ShrineData { ShrineName = "", ShrineType = (ShrineType)0x7F }, objects[1].Shrine);
        Assert.True(objects[1].Selectable);
    }

    [Fact]
    public void Objects_InteractBranch_WhenTheBytesAt0x0AAreZero()
    {
        var image = new GameImage();
        image.AddObject((uint)ObjectName.LargeUrn1, 6, 1, 2, typeByte: (byte)InteractType.Locked, owner: "Owner name that is long!!", nonShrineLayout: true);
        using var reader = image.Reader();

        var o = Assert.Single(reader.Objects(default, NoHover));
        Assert.Equal(new ShrineData(), o.Shrine);
        Assert.Equal(InteractType.Locked, o.InteractType);
        Assert.Equal(new UnitId(6), o.Id);
        Assert.Equal(ObjectName.LargeUrn1, o.Name);
        Assert.Equal(new Position(1, 2), o.Position);
        Assert.True(o.Selectable);
        Assert.Equal("Owner name that is long!!", o.Owner);
    }

    [Fact]
    public void Objects_OwnerIs32BytesTrimmed()
    {
        var image = new GameImage();
        image.AddObject((uint)ObjectName.TownPortal, 1, 1, 1, owner: "0123456789abcdefghijklmnopqrstuvwxyz");
        using var reader = image.Reader();

        Assert.Equal("0123456789abcdefghijklmnopqrstuv", Assert.Single(reader.Objects(default, NoHover)).Owner);
    }

    [Fact]
    public void Objects_SkipsOtherUnitTypes_SortsAndReadsHover()
    {
        var image = new GameImage();
        image.AddObject((uint)ObjectName.TownPortal, 1, 30, 40, bucket: 2);
        image.AddObject((uint)ObjectName.WaypointPortal, 2, 3, 4, unitType: 1, bucket: 2);
        image.AddObject((uint)ObjectName.WaypointPortal, 3, 6, 8, bucket: 9);
        image.SetHover(1, 2, 1);
        using var reader = image.Reader();

        var objects = reader.Objects(new Position(0, 0), reader.HoveredData());
        Assert.Equal([3, 1], objects.Select(o => o.Id.Value));
        Assert.Equal([false, true], objects.Select(o => o.IsHovered));
    }

    [Fact]
    public void Objects_HoverOfAnotherUnitType_IsNotThisObject()
    {
        var image = new GameImage();
        image.AddObject((uint)ObjectName.TownPortal, 1, 30, 40);
        image.SetHover(1, 1, 1);
        using var reader = image.Reader();

        Assert.False(Assert.Single(reader.Objects(default, reader.HoveredData())).IsHovered);
    }

    [Fact]
    public void EmptyTables_GiveEmptyCollections()
    {
        using var reader = new GameImage().Reader();
        Assert.Empty(reader.Monsters(default, NoHover));
        Assert.Empty(reader.Corpses(default, NoHover));
        Assert.Empty(reader.Objects(default, NoHover));
        Assert.Empty(reader.GetRawPlayerUnits());
    }
}

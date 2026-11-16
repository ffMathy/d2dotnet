using D2Dotnet.Game;
using D2Dotnet.Memory;

namespace D2Dotnet.Tests.Memory;

/// <summary>monsters.go and object.go.</summary>
public sealed class UnitTests
{
    private static GameImage WithPlayerAt(ushort x, ushort y)
    {
        var image = new GameImage();
        image.AddPlayer(new PlayerSpec { UnitId = 1, Name = "Me", X = x, Y = y, Area = 1, IsMain = true });
        return image;
    }

    [Fact]
    public void Monsters_AreNearestFirst_ByExactDistance_StableOnTies()
    {
        var image = WithPlayerAt(0, 0);
        image.AddMonster((uint)MonsterId.Zombie, 100, 50, 50, bucket: 0);                  // 70.7
        image.AddMonster((uint)MonsterId.Andariel, 101, 10, 0, typeFlag: 8, bucket: 3);    // 10
        image.AddMonster((uint)MonsterId.Duriel, 102, 0, 10, bucket: 3);                   // 10, after Andariel
        image.AddMonster((uint)MonsterId.Zombie, 103, 3, 4, bucket: 127);                  // 5
        // d2go compared truncated distances: 5.4 and 5 tied and kept table order. Exact distances do not tie.
        image.AddMonster((uint)MonsterId.Zombie, 104, 5, 2, bucket: 0);                    // 5.39

        var monsters = image.Reader().ReadMonsters();

        Assert.Equal([103u, 104u, 101u, 102u, 100u], monsters.Select(m => m.Id.Value));
        Assert.Equal(MonsterId.Andariel, monsters[2].Class);
        Assert.Equal(MonsterType.Unique, monsters[2].Type);
        Assert.Equal(MonsterType.Normal, monsters[0].Type);
        Assert.Equal(new Point(10, 0), monsters[2].Position);
    }

    [Fact]
    public void Monsters_LeaveOutCorpsesAndIgnoredClassesWithoutExperience()
    {
        var image = new GameImage();
        image.AddMonster((uint)MonsterId.Zombie, 1, 1, 1, corpse: true);
        image.AddMonster((uint)MonsterId.Chicken, 2, 1, 1);
        image.AddMonster((uint)MonsterId.Guard, 3, 1, 1, stats: [new RawStat(StatId.Experience, 5)]); // act 2 mercenary
        image.AddMonster((uint)MonsterId.Rogue2, 4, 1, 1, stats: [new RawStat(StatId.Experience, 0)]);
        image.AddMonster((uint)MonsterId.Hydra, 5, 1, 1, corpse: true, stats: [new RawStat(StatId.Experience, 1)]);
        var reader = image.Reader();

        Assert.Equal([3u], reader.ReadMonsters().Select(m => m.Id.Value));
        Assert.Equal([1u, 5u], reader.ReadCorpses().Select(m => m.Id.Value));
    }

    [Fact]
    public void MonsterStats_AreSigned_NotRescaled_LaterEntriesWin()
    {
        var image = new GameImage();
        image.AddMonster((uint)MonsterId.Zombie, 1, 1, 1, stats:
        [
            new RawStat(StatId.Life, 0x4000),
            new RawStat(StatId.FireResist, 100, Layer: 9),
            new RawStat(StatId.Life, 0x8000),
            new RawStat(StatId.ColdResist, 0xFFFF_FFF6),
        ]);

        var m = Assert.Single(image.Reader().ReadMonsters());
        Assert.Equal(3, m.Stats.Count);
        Assert.Equal(0x8000, m.Stats[StatId.Life]);
        Assert.Equal(100, m.Stats[StatId.FireResist]);
        // d2go read -10 as 4294967286 and so reported the monster as cold immune.
        Assert.Equal(-10, m.Stats[StatId.ColdResist]);
        Assert.False(m.IsImmune(Element.Cold));
        Assert.True(m.IsImmune(Element.Fire));
    }

    [Fact]
    public void MonsterStats_ImplausibleCount_IsNoStats()
    {
        var image = new GameImage();
        var unit = image.AddMonster((uint)MonsterId.Zombie, 1, 1, 1, stats: [new RawStat(StatId.Life, 5)]);
        var statsEx = image.Memory.ReadU64(unit + 0x88);
        image.Memory.WriteU64(statsEx + 0x38, GameReader.MaxStats + 1UL);

        Assert.Empty(Assert.Single(image.Reader().ReadMonsters()).Stats);
    }

    [Theory]
    [InlineData((byte)10, MonsterType.SuperUnique)]
    [InlineData((byte)4, MonsterType.Champion)]
    [InlineData((byte)12, MonsterType.Champion)]
    [InlineData((byte)8, MonsterType.Unique)]
    [InlineData((byte)16, MonsterType.Minion)]
    [InlineData((byte)0, MonsterType.Normal)]
    [InlineData((byte)2, MonsterType.Normal)]
    [InlineData((byte)24, MonsterType.Normal)]
    public void MonsterTypeOf_MapsTheFlag(byte flag, MonsterType expected) => Assert.Equal(expected, GameReader.MonsterTypeOf(flag));

    [Theory]
    [InlineData((ushort)1, 1u, 7u, true)]
    [InlineData((ushort)1, 0u, 7u, false)]
    [InlineData((ushort)1, 1u, 8u, false)]
    [InlineData((ushort)0, 1u, 7u, false)]
    public void MonstersAndCorpses_Hover_NeedsUnitType1(ushort isHovered, uint type, uint id, bool expected)
    {
        var image = new GameImage();
        image.AddMonster((uint)MonsterId.Zombie, 7, 1, 1);
        image.AddMonster((uint)MonsterId.Zombie, 7, 1, 1, corpse: true);
        image.SetHover(isHovered, type, id);
        var reader = image.Reader();

        Assert.Equal(expected, Assert.Single(reader.ReadMonsters()).IsHovered);
        Assert.Equal(expected, Assert.Single(reader.ReadCorpses()).IsHovered);
    }

    [Fact]
    public void Corpses_AreSortedAndTyped()
    {
        var image = WithPlayerAt(0, 0);
        image.AddMonster((uint)MonsterId.Zombie, 1, 100, 100, typeFlag: 16, corpse: true);
        image.AddMonster((uint)MonsterId.FallenShaman, 2, 5, 5, typeFlag: 10, corpse: true, stats: [new RawStat(StatId.Strength, 3)]);

        var corpses = image.Reader().ReadCorpses();
        Assert.Equal([2u, 1u], corpses.Select(c => c.Id.Value));
        Assert.Equal(MonsterType.SuperUnique, corpses[0].Type);
        Assert.Equal(MonsterType.Minion, corpses[1].Type);
        Assert.Equal(3, corpses[0].Stats[StatId.Strength]);
        Assert.Empty(corpses[1].Stats);
    }

    [Fact]
    public void Objects_ShrineClasses_ReadTheShrineType()
    {
        // d2go decided "shrine" from the 8 bytes at unit +0x0A (the unit id's high half, the mode and the unit data
        // pointer), so nearly every object was a shrine; the object's class decides here.
        var image = new GameImage();
        image.AddObject((uint)ObjectId.Shrine, 5, 10, 10, mode: 1, typeByte: (byte)ShrineType.ExperienceShrine);
        image.AddObject((uint)ObjectId.LargeUrn1, 0x10000, 20, 20, mode: 1, typeByte: (byte)InteractType.Locked);
        image.AddObject((uint)ObjectId.Shrine, 7, 30, 30, typeByte: 0x7F);

        var objects = image.Reader().ReadObjects();
        Assert.Equal(3, objects.Count);
        Assert.Equal(ShrineType.ExperienceShrine, objects[0].Shrine);
        Assert.True(objects[0].IsShrine);
        Assert.Equal(InteractType.None, objects[0].InteractType);
        Assert.False(objects[0].IsSelectable);

        Assert.Null(objects[1].Shrine);
        Assert.False(objects[1].IsShrine);
        Assert.Equal(InteractType.Locked, objects[1].InteractType);

        // An unknown shrine kind is not a shrine.
        Assert.Equal((ShrineType)0x7F, objects[2].Shrine);
        Assert.False(objects[2].IsShrine);
        Assert.True(objects[2].IsSelectable);
    }

    [Fact]
    public void Objects_ReadTheirFields()
    {
        var image = new GameImage();
        image.AddObject((uint)ObjectId.LargeUrn1, 6, 1, 2, typeByte: (byte)InteractType.Trap, owner: "Owner name that is long!!", nonShrineLayout: true);

        var o = Assert.Single(image.Reader().ReadObjects());
        Assert.Equal(InteractType.Trap, o.InteractType);
        Assert.Equal(new UnitId(6), o.Id);
        Assert.Equal(ObjectId.LargeUrn1, o.Class);
        Assert.Equal(new Point(1, 2), o.Position);
        Assert.True(o.IsSelectable);
        Assert.Equal("Owner name that is long!!", o.Owner);
    }

    [Fact]
    public void Objects_OwnerIsA32ByteField()
    {
        var image = new GameImage();
        image.AddObject((uint)ObjectId.TownPortal, 1, 1, 1, owner: "0123456789abcdefghijklmnopqrstuvwxyz");
        Assert.Equal("0123456789abcdefghijklmnopqrstuv", Assert.Single(image.Reader().ReadObjects()).Owner);
    }

    [Fact]
    public void Objects_SkipOtherUnitTypes_SortAndReadHover()
    {
        var image = WithPlayerAt(0, 0);
        image.AddObject((uint)ObjectId.TownPortal, 1, 30, 40, bucket: 2);
        image.AddObject((uint)ObjectId.WaypointPortal, 2, 3, 4, unitType: 1, bucket: 2);
        image.AddObject((uint)ObjectId.WaypointPortal, 3, 6, 8, bucket: 9);
        image.SetHover(1, 2, 1);

        var objects = image.Reader().ReadObjects();
        Assert.Equal([3u, 1u], objects.Select(o => o.Id.Value));
        Assert.Equal([false, true], objects.Select(o => o.IsHovered));

        image.SetHover(1, 1, 1);
        Assert.All(image.Reader().ReadObjects(), o => Assert.False(o.IsHovered));
    }

    [Fact]
    public void EmptyTables_GiveEmptyLists()
    {
        var reader = new GameImage().Reader();
        Assert.Empty(reader.ReadMonsters());
        Assert.Empty(reader.ReadCorpses());
        Assert.Empty(reader.ReadObjects());
        Assert.Empty(reader.ReadPlayers());
    }
}

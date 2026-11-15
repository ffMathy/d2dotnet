using D2Dotnet.Map.Generator.Data;
using D2Dotnet.Map.Generator.Game;
using D2Dotnet.Map.Generator.Native;
using D2Dotnet.Map.Generator.Tests.Fakes;

namespace D2Dotnet.Map.Generator.Tests;

public class D2ClientTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(39, 0)]
    [InlineData(40, 1)]
    [InlineData(74, 1)]
    [InlineData(75, 2)]
    [InlineData(102, 2)]
    [InlineData(103, 3)]
    [InlineData(108, 3)]
    [InlineData(109, 4)]
    [InlineData(199, 4)]
    [InlineData(200, -1)]
    [InlineData(-5, 0)]
    public void GetAct(int level, int act) => Assert.Equal(act, D2Client.GetAct(level));

    [Theory]
    [InlineData(1, "casket")]
    [InlineData(2, "shrine")]
    [InlineData(3, "urn")]
    [InlineData(4, "chest")]
    [InlineData(5, "barrel")]
    [InlineData(7, "barrel-exploding")]
    [InlineData(8, "door")]
    [InlineData(9, "quest")]
    [InlineData(14, "bolder")]
    [InlineData(18, "door")]
    [InlineData(19, "rack-armor")]
    [InlineData(20, "rack-weapon")]
    [InlineData(22, "well")]
    [InlineData(23, "waypoint")]
    [InlineData(24, "quest")]
    [InlineData(25, "quest")]
    [InlineData(28, "quest")]
    [InlineData(29, "door")]
    [InlineData(30, "chest-exploding")]
    [InlineData(40, "chest")]
    [InlineData(41, "chest")]
    [InlineData(45, "quest")]
    [InlineData(49, "quest")]
    [InlineData(52, "quest")]
    [InlineData(53, "quest")]
    [InlineData(54, "quest")]
    [InlineData(55, "quest")]
    [InlineData(56, "quest")]
    [InlineData(58, "chest")]
    [InlineData(59, "chest")]
    [InlineData(68, "urn-evil")]
    [InlineData(6, null)]
    [InlineData(-1, null)]
    public void GetObjectClassByOperateFn(int operateFn, string? expected) => Assert.Equal(expected, D2Client.GetObjectClass(100, null, operateFn));

    [Fact]
    public void GetObjectClassSuperChestsOnlyWithoutOperateFnMatch()
    {
        Assert.Equal("chest-super", D2Client.GetObjectClass(580, null, -1));
        Assert.Equal("chest-super", D2Client.GetObjectClass(581, null, 6));
        Assert.Equal("shrine", D2Client.GetObjectClass(580, null, 2));
        Assert.Null(D2Client.GetObjectClass(582, null, -1));
    }

    [Fact]
    public void GetObjectTypeSkipsUselessObjects()
    {
        Assert.Null(D2Client.GetObjectType((int)GameObject.Barrel));
        Assert.Equal("object", D2Client.GetObjectType((int)GameObject.Shrine));
    }

    [Theory]
    [InlineData(AreaLevel.BloodMoor, AreaLevel.DenOfEvil, true)]
    [InlineData(AreaLevel.BloodMoor, AreaLevel.ColdPlains, false)]
    [InlineData(AreaLevel.TamoeHighland, AreaLevel.PitLevel1, true)]
    [InlineData(AreaLevel.BlackMarsh, AreaLevel.ForgottenTower, true)]
    [InlineData(AreaLevel.FarOasis, AreaLevel.MaggotLairLevel1, true)]
    [InlineData(AreaLevel.ValleyOfSnakes, AreaLevel.ClawViperTempleLevel1, true)]
    [InlineData(AreaLevel.RockyWaste, AreaLevel.StonyTombLevel1, true)]
    [InlineData(AreaLevel.LostCity, AreaLevel.AncientTunnels, true)]
    [InlineData(AreaLevel.SpiderForest, AreaLevel.SpiderCavern, true)]
    [InlineData(AreaLevel.FlayerJungle, AreaLevel.FlayerDungeonLevel1, true)]
    [InlineData(AreaLevel.KurastBazaar, AreaLevel.RuinedTemple, true)]
    [InlineData(AreaLevel.CrystallinePassage, AreaLevel.FrozenRiver, true)]
    [InlineData(AreaLevel.CanyonOfTheMagi, AreaLevel.TalRashasTomb3, true)] // staff tomb level
    [InlineData(AreaLevel.CanyonOfTheMagi, AreaLevel.TalRashasTomb4, false)]
    public void IsGoodExit(AreaLevel from, AreaLevel to, bool expected)
    {
        var h = new Harness();
        var m = h.Game.Memory;
        var misc = m.New(new ActMisc { dwStaffTombLevel = (uint)AreaLevel.TalRashasTomb3 });
        var act = m.New(new Act { pMisc = misc });
        var level = m.New(new Level { dwLevelNo = (uint)from });
        Assert.Equal(expected, new D2Client(h.Context).IsGoodExit(act, level, (int)to));
    }

    [Fact]
    public void AddCollisionDataMaps1024ToBlockedAndTracksEvenMaxima()
    {
        var h = new Harness();
        var client = new D2Client(h.Context);
        client.Map.Reset();
        var coll = h.Game.AddCollMap(103, 207, [[1024, 4], [0, 7]]);
        client.AddCollisionData(coll, 100, 200);
        Assert.Equal(1, client.Map.Value(3, 7));
        Assert.Equal(4, client.Map.Value(4, 7));
        Assert.Equal(0, client.Map.Value(3, 8));
        Assert.Equal(7, client.Map.Value(4, 8));
        Assert.Equal(-1, client.Map.Value(5, 8));
        Assert.Equal((4, 8), (client.Map.MaxX, client.Map.MaxY));
        client.AddCollisionData(0, 0, 0); // NULL collision map is ignored
    }

    [Fact]
    public void MapGridOffsetsAndReset()
    {
        var map = new MapGrid();
        Assert.Equal(1500 * 3 + 4, MapGrid.Offset(3, 4));
        map.Reset();
        map.Set(10, 20, 11111);
        Assert.Equal((0, 0), (map.MaxX, map.MaxY));
        map.Set(10, 20, 2);
        Assert.Equal((10, 20), (map.MaxX, map.MaxY));
        map.Reset();
        Assert.Equal(MapGrid.MapDataInvalid, map.Value(10, 20));
        Assert.Equal((0, 0), (map.MaxX, map.MaxY));
    }

    [Fact]
    public void MagicJumpTargetIsTheCConstantAtThePreferredBase()
    {
        Assert.Equal(0x6faf559bu, D2Pointers.MagicJumpTarget(D2Pointers.D2CLIENT_Pod_InitGameMisc_I_P, 0x6FAB0000));
        Assert.Equal(0x6faf454bu, D2Pointers.MagicJumpTarget(D2Pointers.D2CLIENT_Pd2_InitGameMisc_I_P, 0x6FAB0000));
        Assert.Equal(0x1000000u + 0x4454B, D2Pointers.MagicJumpTarget(D2Pointers.D2CLIENT_Pd2_InitGameMisc_I_P, 0x1000000));
        var values = new uint[D2Pointers.All.Count];
        values[(int)D2Ptr.D2CLIENT_Pd2_LoadAct_1] = 0x6FAB0000 + 0x62AA0;
        Assert.Equal(0x6FAB0000u, D2Pointers.D2ClientBase(new D2Addresses(values)));
    }
}

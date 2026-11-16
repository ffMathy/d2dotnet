using D2Dotnet.Data;
using D2Dotnet.Maps.Engine;
using D2Dotnet.Maps.Formats;

namespace D2Dotnet.Tests.Maps;

/// <summary>Ports of libd2's own format unit tests (packages/formats/src/ds1.zig, dt1.zig and
/// packages/drlg/src/txt.zig) over libd2's raw fixtures.</summary>
public sealed class FormatUnitTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(LibD2Fixtures.PathOf("packages", "formats", "src", "maps", name));

    [Fact]
    public void ParseTownETransFixture()
    {
        var bytes = Fixture("TownETrans.ds1");
        var d = Ds1File.Parse(bytes);
        Assert.True(d.Width >= 1 && d.Width <= 256);
        Assert.True(d.Height >= 1 && d.Height <= 256);
        Assert.True(d.WallLayers.Length >= 1);
        Assert.True(d.FloorLayers.Length >= 1);
        int cells = (int)(d.Width * d.Height);
        foreach (var wl in d.WallLayers)
        {
            Assert.Equal(cells, wl.Wall.Length);
            Assert.Equal(cells, wl.Orient.Length);
        }
        foreach (var fl in d.FloorLayers) Assert.Equal(cells, fl.Length);
        Assert.Equal(cells, d.Shadow.Length);
        foreach (var o in d.Objects)
        {
            Assert.True(o.X >= -1 && o.X <= d.Width + 1);
            Assert.True(o.Y >= -1 && o.Y <= d.Height + 1);
        }
        // TownETrans is version 13: the recon returns at `version <= 13` (just before the
        // NPC-path stream), so it leaves a 4-byte tail — the unread numNpcs field, zero-filled.
        Assert.Equal(13, d.Version);
        Assert.Equal(bytes.Length - 4, d.BytesConsumed);
        Assert.All(bytes[d.BytesConsumed..], b => Assert.Equal(0, b));
        Assert.False(d.NpcPathsDeferred);
    }

    [Theory]
    [InlineData("Act1_Town_TownN1.ds1")]
    [InlineData("Act2_Town_LutW.ds1")]
    [InlineData("Act3_Docktown_DockTown3.ds1")]
    [InlineData("Act4_Fort_Fortress.ds1")]
    [InlineData("Expansion_Town_townWest.ds1")]
    public void ParseRealTownDs1FromEveryAct(string name)
    {
        var bytes = Fixture(name);
        var d = Ds1File.Parse(bytes);
        Assert.True(d.Version > 0 && d.Version <= 20);
        Assert.True(d.Width >= 1 && d.Width <= 256);
        Assert.True(d.Height >= 1 && d.Height <= 256);
        Assert.InRange(d.WallLayers.Length, 1, 4);
        Assert.InRange(d.FloorLayers.Length, 1, 2);
        int cells = (int)(d.Width * d.Height);
        foreach (var wl in d.WallLayers)
        {
            Assert.Equal(cells, wl.Wall.Length);
            Assert.Equal(cells, wl.Orient.Length);
        }
        foreach (var fl in d.FloorLayers) Assert.Equal(cells, fl.Length);
        Assert.Equal(cells, d.Shadow.Length);
        foreach (var g in d.SubstGroups)
        {
            Assert.True(g.X >= -1 && g.X <= d.Width + 1);
            Assert.True(g.Y >= -1 && g.Y <= d.Height + 1);
        }
        Assert.Equal(bytes.Length, d.BytesConsumed);
        Assert.False(d.NpcPathsDeferred);
    }

    [Fact]
    public void ParseTownFloorDt1HeaderAndSubtileFlags()
    {
        var d = Dt1File.Parse(Fixture("Act1_Town_Floor.dt1"));
        Assert.Equal(7, d.VersionMajor);
        Assert.Equal(6, d.VersionMinor);
        Assert.Equal(144, d.Tiles.Length);
        // Tile 0: plain floor (orient 0), fully walkable.
        Assert.Equal(0, d.Tiles[0].Orientation);
        Assert.All(d.Tiles[0].Flags, f => Assert.Equal(0, f));
        // A blocking floor variant exists in this library.
        Assert.Contains(d.Tiles, t => t.Flags.Any(f => (f & 0x01) != 0));
    }

    [Fact]
    public void ExcelParseHeaderAndIntegerAccess()
    {
        var t = ExcelTable.Parse("Name\tId\tVal\nFoo\t3\t10\nBar\t7\t\n");
        Assert.Equal(2, t.RowCount);
        Assert.Equal("Foo", t.GetString(0, "Name"));
        Assert.Equal(3, t.GetInt64(0, "Id"));
        Assert.Equal(0, t.GetInt64(1, "Val")); // empty -> 0
        Assert.Equal(1, t.FindRow("Id", 7));
        Assert.Null(t.FindRow("Id", 99));
    }

    [Fact]
    public void ExcelColumnsAreLookedUpByName()
    {
        // A D2R-style table with an extra column in front: lookups by header name are unaffected.
        var t = ExcelTable.Parse("*Extra\tName\tId\nx\tFoo\t3\n");
        Assert.Equal("Foo", t.GetString(0, "Name"));
        Assert.Equal(3, t.GetInt64(0, "Id"));
        Assert.False(t.HasColumn("Missing"));
    }
}

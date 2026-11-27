using D2Dotnet.Maps;
using D2Dotnet.Memory;

namespace D2Dotnet.Tests.Memory;

/// <summary><see cref="GameReader.ReadMapContext()"/>: player unit -> act -> act misc (difficulty and seed hashes).</summary>
public sealed class MapContextTests
{
    private const uint Seed = 0x3607656C;
    private const uint InitHash = 0xA5A5A5A5;

    /// <summary>Points the player unit at an act whose act misc holds the difficulty and the two seed hashes.</summary>
    private static void WriteAct(GameImage image, ulong unit, ushort difficulty, uint initHash, uint endHash)
    {
        var act = image.Memory.Alloc(0x100);
        var actMisc = image.Memory.Alloc(0x900);
        image.Memory.WriteU64(unit + 0x20, act);
        image.Memory.WriteU64(act + 0x70, actMisc);
        image.Memory.WriteU16(actMisc + 0x828, difficulty);
        image.Memory.WriteU32(actMisc + 0x838, initHash);
        image.Memory.WriteU32(actMisc + 0x860, endHash);
    }

    private static (GameImage Image, ulong Unit) Game(uint area = 46)
    {
        var image = new GameImage();
        var unit = image.AddPlayer(new PlayerSpec { UnitId = 7, Name = "Hero", Area = area, X = 5000, Y = 5000, IsMain = true });
        return (image, unit);
    }

    [Fact]
    public void ReadsSeedDifficultyAndArea()
    {
        var (image, unit) = Game(area: 46);
        WriteAct(image, unit, difficulty: 2, InitHash, MapSeed.Hash(Seed));

        Assert.Equal(new MapContext(Seed, Difficulty.Hell, AreaId.CanyonOfTheMagi), image.Reader().ReadMapContext());
    }

    [Fact]
    public void Snapshot_CarriesTheContext()
    {
        var (image, unit) = Game(area: 1);
        WriteAct(image, unit, difficulty: 1, InitHash, MapSeed.Hash(Seed));

        var snapshot = image.Reader().ReadSnapshot();

        Assert.Equal(new MapContext(Seed, Difficulty.Nightmare, AreaId.RogueEncampment), snapshot.Map);
        Assert.Equal(ActId.Act1, snapshot.Map!.Value.Act);
    }

    [Fact]
    public void OnlyTheMainPlayerCounts()
    {
        var image = new GameImage();
        var other = image.AddPlayer(new PlayerSpec { UnitId = 8, Name = "Other", Area = 1, X = 1, Y = 1 });
        WriteAct(image, other, 0, InitHash, MapSeed.Hash(Seed));

        Assert.Null(image.Reader().ReadMapContext());
    }

    [Fact]
    public void NotInAGame_IsNull()
    {
        Assert.Null(new GameImage().Reader().ReadMapContext());
        Assert.Null(new GameImage().Reader().ReadSnapshot().Map);
    }

    [Fact]
    public void NoAct_IsNull()
    {
        var (image, _) = Game();
        Assert.Null(image.Reader().ReadMapContext());
    }

    [Fact]
    public void NoActMisc_IsNull()
    {
        var (image, unit) = Game();
        image.Memory.WriteU64(unit + 0x20, image.Memory.Alloc(0x100));
        Assert.Null(image.Reader().ReadMapContext());
    }

    [Fact]
    public void AreaZero_IsNull()
    {
        var (image, unit) = Game(area: 0);
        WriteAct(image, unit, 0, InitHash, MapSeed.Hash(Seed));
        Assert.Null(image.Reader().ReadMapContext());
    }

    [Theory]
    [InlineData((ushort)3)]
    [InlineData((ushort)0xFFFF)]
    public void UnknownDifficulty_IsNull(ushort difficulty)
    {
        var (image, unit) = Game();
        WriteAct(image, unit, difficulty, InitHash, MapSeed.Hash(Seed));
        Assert.Null(image.Reader().ReadMapContext());
    }

    [Fact]
    public void InitHashEqualToTheSeed_IsNull()
    {
        // TryGetMapSeed rejects an init hash that XORs with the seed to 0 (the hashes are not set yet).
        var (image, unit) = Game();
        WriteAct(image, unit, 0, Seed, MapSeed.Hash(Seed));
        Assert.Null(image.Reader().ReadMapContext());
    }

    [Fact]
    public void Offsets_AreTheDocumentedOnes()
    {
        Assert.Equal((nuint)0x20, GameReader.UnitActOffset);
        Assert.Equal((nuint)0x70, GameReader.ActMiscOffset);
        Assert.Equal((nuint)0x828, GameReader.DifficultyOffset);
        Assert.Equal((nuint)0x838, GameReader.InitSeedHashOffset);
        Assert.Equal((nuint)0x860, GameReader.EndSeedHashOffset);
    }
}

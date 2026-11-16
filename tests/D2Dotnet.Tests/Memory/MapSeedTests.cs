using D2Dotnet.Memory;

namespace D2Dotnet.Tests.Memory;

/// <summary>map_seed_hash.go.</summary>
public sealed class MapSeedTests
{
    // seed -> (seed * 0x6AC690C5 + 666) mod 2^32, worked out separately from the formula.
    [Theory]
    [InlineData(0x0u, 0x29Au)]
    [InlineData(0x1u, 0x6AC6935Fu)]
    [InlineData(0x2u, 0xD58D2424u)]
    [InlineData(0xFFFFu, 0x25FE71D5u)]
    [InlineData(0x10000u, 0x90C5029Au)]
    [InlineData(0x12345678u, 0x03BA0CF2u)]
    [InlineData(0xDEADBEEFu, 0xB9F46085u)]
    [InlineData(0xFFFFFFFFu, 0x953971D5u)]
    [InlineData(1234567890u, 0x24724E34u)]
    public void HashAndReverse_AreInverses(uint seed, uint hash)
    {
        Assert.Equal(hash, MapSeed.Hash(seed));
        Assert.Equal(seed, MapSeed.Reverse(hash));
        Assert.True(MapSeed.TryGetMapSeed(0xA5A5A5A5, hash, out var mapSeed));
        Assert.Equal(seed, mapSeed);
    }

    [Fact]
    public void Reverse_RoundTripsRandomSeeds()
    {
        var random = new Random(1234);
        for (var i = 0; i < 200; i++)
        {
            var seed = (uint)random.NextInt64(0, 1L << 32);
            Assert.Equal(seed, MapSeed.Reverse(MapSeed.Hash(seed)));
        }
    }

    [Fact]
    public void MultiplierInverse_IsTheInverseModulo2To32() =>
        Assert.Equal(1u, unchecked(MapSeed.Multiplier * MapSeed.MultiplierInverse));

    [Theory]
    [InlineData(0x0u)]
    [InlineData(0x29Au)]
    [InlineData(0x1234u)]
    [InlineData(0x90C5029Au)]
    [InlineData(0xFFFFFFFFu)]
    public void Reverse_MatchesD2gosSearch(uint hash)
    {
        // d2go reverseMapSeedHash: try every value until the low 16 bits of the hash match, then step by 2^16.
        ulong increment = 1, expected = 0;
        for (ulong start = 0; ; start += increment)
        {
            var result = ((start * 0x6AC690C5) + 666) & 0xFFFFFFFF;
            if (result == hash) { expected = start; break; }
            if (increment == 1 && result % 65536 == hash % 65536) increment = 65536;
        }

        Assert.Equal(expected, MapSeed.Reverse(hash));
    }

    [Fact]
    public void TryGetMapSeed_FailsWhenTheInitHashEqualsTheSeed()
    {
        Assert.False(MapSeed.TryGetMapSeed(0x12345678, 0x03BA0CF2, out var seed));
        Assert.Equal(0u, seed);
        Assert.False(MapSeed.TryGetMapSeed(0, 0x29A, out seed));
        Assert.True(MapSeed.TryGetMapSeed(1, 0x29A, out seed));
        Assert.Equal(0u, seed);
    }
}

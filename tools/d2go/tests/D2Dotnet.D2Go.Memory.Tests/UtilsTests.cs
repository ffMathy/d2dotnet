namespace D2Dotnet.D2Go.Memory.Tests;

/// <summary>pkg/utils: map seed reversal and distance.</summary>
public sealed class UtilsTests
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
    public void ReverseMapSeedHash_FindsTheSeed(uint seed, uint hash)
    {
        Assert.Equal(seed, MapSeed.ReverseMapSeedHash(hash));

        Assert.True(MapSeed.TryGetMapSeed(0xA5A5A5A5, hash, out var mapSeed));
        Assert.Equal(seed, mapSeed);
    }

    [Fact]
    public void ReverseMapSeedHash_RoundTripsRandomSeeds()
    {
        var random = new Random(1234);
        for (var i = 0; i < 200; i++)
        {
            var seed = (uint)random.NextInt64(0, 1L << 32);
            var hash = unchecked((seed * 0x6AC690C5u) + 666u);
            Assert.Equal(seed, MapSeed.ReverseMapSeedHash(hash));
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
    public void ReverseMapSeedHash_MatchesGosSearch(uint hash)
    {
        // Go reverseMapSeedHash: try every value until the low 16 bits of the hash match, then step by 2^16.
        ulong increment = 1, expected = 0;
        for (ulong start = 0; ; start += increment)
        {
            var result = ((start * 0x6AC690C5) + 666) & 0xFFFFFFFF;
            if (result == hash) { expected = start; break; }
            if (increment == 1 && result % 65536 == hash % 65536) increment = 65536;
        }

        Assert.Equal(expected, MapSeed.ReverseMapSeedHash(hash));
    }

    [Fact]
    public void GetMapSeed_FailsWhenTheInitHashEqualsTheSeed()
    {
        // XOR of the init hash and the seed must not be 0.
        Assert.False(MapSeed.TryGetMapSeed(0x12345678, 0x03BA0CF2, out var seed));
        Assert.Equal(0u, seed);
        // Seed 0 (hash 666) with init hash 0.
        Assert.False(MapSeed.TryGetMapSeed(0, 0x29A, out seed));
        Assert.Equal(0u, seed);
        Assert.True(MapSeed.TryGetMapSeed(1, 0x29A, out seed));
        Assert.Equal(0u, seed);
    }

    [Theory]
    [InlineData(0, 0, 3, 4, 5)]
    [InlineData(3, 4, 0, 0, 5)]
    [InlineData(0, 0, 1, 1, 1)]          // sqrt(2) truncated
    [InlineData(10, 10, 10, 10, 0)]
    [InlineData(-5, 0, 5, 0, 10)]
    [InlineData(0, 0, 65535, 65535, 92680)]
    public void DistanceFromPoint_IsTruncatedEuclidean(int x1, int y1, int x2, int y2, int expected) =>
        Assert.Equal(expected, Geometry.DistanceFromPoint(new Position(x1, y1), new Position(x2, y2)));

    [Fact]
    public void DistanceFromPoint_UsesGo64BitIntsBeforeNarrowing()
    {
        // The difference does not overflow (Go's int is 64-bit); the result 4294967295 is then narrowed to -1.
        Assert.Equal(-1, Geometry.DistanceFromPoint(new Position(int.MinValue, 0), new Position(int.MaxValue, 0)));
    }
}

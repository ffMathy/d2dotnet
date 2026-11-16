using D2Dotnet.Map.Drlg.Engine;

namespace D2Dotnet.Map.Drlg.Tests;

/// <summary>Ports of libd2's rng unit tests (packages/core/src/rng.zig and
/// packages/drlg/src/drlg/rng.zig).</summary>
public sealed unsafe class RngTests
{
    private static D2SeedStrc S(uint lo, uint hi) => new() { nSeedLow = (int)lo, nSeedHigh = (int)hi };

    [Fact]
    public void NextSplits64BitStateIntoHalves()
    {
        var s = new Seed(1, 0x29a);
        ulong state = s.Next();
        // state = 1 * 0x6ac690c5 + 0x29a = 0x6ac6935f
        Assert.Equal(0x6ac690c5ul + 0x29a, state);
        Assert.Equal(0x6ac6935fu, s.Low);
        Assert.Equal(0u, s.High);
    }

    [Fact]
    public void PickPowerOfTwoMasksLowWord()
    {
        var s = new Seed(1, 0x29a);
        Assert.Equal(0x6ac6935fu & 0xf, s.Pick(16));
    }

    [Fact]
    public void PickNonPowerOfTwoUsesNewLowWordModulo()
    {
        var s = new Seed(1, 0x29a);
        Assert.Equal(0x6ac6935fu % 100, s.Pick(100));
    }

    [Fact]
    public void PickZeroAndNegativeModuloReturnZeroWithoutStepping()
    {
        var s = new Seed(123, 456);
        var before = s;
        Assert.Equal(0u, s.Pick(0));
        Assert.Equal(0u, s.Pick(0x80000000));
        Assert.Equal(before.Low, s.Low);
        Assert.Equal(before.High, s.High);
    }

    [Fact]
    public void LevelSeedDerivationIsTwoStageAndDeterministic()
    {
        uint start = Seed.ActStartSeed(12345);
        var s1 = new Seed(12345, 0x29a);
        s1.Next();
        Assert.Equal(s1.Low, start);
        var ls = Seed.LevelSeed(start, 8);
        Assert.Equal(unchecked(start + 8), ls.Low);
        Assert.Equal(0x29au, ls.High);
        Assert.NotEqual(Seed.LevelSeed(start, 1).Low, Seed.LevelSeed(start, 2).Low);
    }

    [Fact]
    public void RollRangeReturnsMinWhenMaxNotAboveMinAndIsDeterministic()
    {
        var s = new Seed(7, 11);
        Assert.Equal(5, s.RollRange(5, 5));
        Assert.Equal(5, s.RollRange(5, 3));
        var a = new Seed(7, 11);
        var b = new Seed(7, 11);
        Assert.Equal(a.RollRange(10, 100), b.RollRange(10, 100));
    }

    [Fact]
    public void RandomNumberSelectorMatchesPickOverSeedModuloGrid()
    {
        uint[] lows = { 0, 1, 2, 7, 12345, 0x29a, 0xdeadbeef, 0xffffffff, 0x80000000, 0x6ac690c5 };
        uint[] highs = { 0, 0x29a, 1, 666, 0xffffffff, 0x12345678 };
        uint[] mods = { 0, 1, 2, 3, 4, 5, 8, 16, 100, 1000, 1024, 0x10000, 7, 13, 0x80000000, 0xffffffff };
        foreach (uint lo in lows)
            foreach (uint hi in highs)
                foreach (uint m in mods)
                {
                    var a = S(lo, hi);
                    var b = new Seed(lo, hi);
                    uint ra = DrlgRng.RandomNumberSelector(&a, m);
                    uint rb = b.Pick(m);
                    Assert.Equal(rb, ra);
                    Assert.Equal(b.Low, (uint)a.nSeedLow);
                    Assert.Equal(b.High, (uint)a.nSeedHigh);
                }
    }

    [Fact]
    public void LongChainStaysInLockstep()
    {
        uint[] mods = { 4, 5, 16, 100, 1024, 7, 0x10000, 13 };
        uint seed = 1;
        for (int s = 0; s < 256; s++)
        {
            seed = unchecked(seed * 1103515245 + 12345); // host-side seed spread (not the D2 RNG)
            var a = S(seed, 0x29a);
            var b = new Seed(seed, 0x29a);
            for (int i = 0; i < 200; i++)
            {
                uint m = mods[i % mods.Length];
                Assert.Equal(b.Pick(m), DrlgRng.RandomNumberSelector(&a, m));
                Assert.Equal(b.Low, (uint)a.nSeedLow);
                Assert.Equal(b.High, (uint)a.nSeedHigh);
            }
        }
    }

    [Fact]
    public void RollRandomSeedMatchesCanonical()
    {
        uint[] lows = { 0, 1, 7, 12345, 0xdeadbeef, 0xffffffff };
        uint[] highs = { 0, 0x29a, 666, 0xffffffff };
        foreach (uint lo in lows)
            foreach (uint hi in highs)
            {
                var a = S(lo, hi);
                var b = new Seed(lo, hi);
                for (int i = 0; i < 64; i++) Assert.Equal(b.RollRandomSeed(), (uint)DrlgRng.RollRandomSeed(&a));
            }
    }

    [Fact]
    public void GetModuloFromSeedEqualsRandomNumberSelector()
    {
        uint[] mods = { 0, 1, 4, 5, 16, 100, 1024, 7, 0x80000000 };
        foreach (uint m in mods)
        {
            var a = S(12345, 0x29a);
            var b = S(12345, 0x29a);
            Assert.Equal(DrlgRng.RandomNumberSelector(&a, m), DrlgRng.GetModuloFromSeed(&b, m));
            Assert.Equal(a.nSeedLow, b.nSeedLow);
            Assert.Equal(a.nSeedHigh, b.nSeedHigh);
        }
    }

    [Fact]
    public void SeedNextAdvancesLikeCanonicalNext()
    {
        uint[] lows = { 0, 1, 7, 12345, 0xdeadbeef, 0xffffffff };
        uint[] highs = { 0, 0x29a, 666, 0xffffffff };
        foreach (uint lo in lows)
            foreach (uint hi in highs)
            {
                var a = DrlgRng.SeedNext(S(lo, hi));
                var b = new Seed(lo, hi);
                b.Next();
                Assert.Equal(b.Low, (uint)a.nSeedLow);
                Assert.Equal(b.High, (uint)a.nSeedHigh);
            }
    }
}

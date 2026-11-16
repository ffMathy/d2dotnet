// Port of libd2 packages/core/src/rng.zig and packages/drlg/src/drlg/rng.zig
// (MIT, (c) 2026 jaenster).
//
// D2 1.14d seed RNG (DRLG / item / monster rolls). The seed is a 64-bit LCG kept as two
// 32-bit halves {low, high}. Each step:
//     state64 = low * 0x6ac690c5 + high
//     low  = state64 & 0xFFFFFFFF
//     high = state64 >> 32
//
// Ported bit-exact from the reconstructed 1.14d sources:
//   D2_SEED_NEXT macro            (d2_platform.h)
//   RANDOM_RandomNumberSelector   (1.14d 0045c3e0)
//   RollRandomSeed                (1.14d 0045c370)
//   SEED_RollRange                (1.14d 004bc500)
//   SEED_GenerateRandomSeed       (1.14d 00650de0)

namespace D2Dotnet.Map.Drlg.Engine;

/// <summary>The canonical seed (d2-core rng.Seed).</summary>
public struct Seed
{
    private const ulong Mul = 0x6ac690c5;

    public uint Low;
    public uint High;

    public Seed(uint low, uint high)
    {
        Low = low;
        High = high;
    }

    /// <summary>Seed from a single value with the engine's InitSeed_Param_666 (high = 666).</summary>
    public static Seed FromValue(uint v) => new(v, 666);

    /// <summary>Advance the LCG one step, returning the full 64-bit state (D2_SEED_NEXT).</summary>
    public ulong Next()
    {
        ulong state = unchecked((ulong)Low * Mul + High);
        Low = (uint)state;
        High = (uint)(state >> 32);
        return state;
    }

    /// <summary>RollRandomSeed: step, return the new low word.</summary>
    public uint RollRandomSeed()
    {
        Next();
        return Low;
    }

    /// <summary>RANDOM_RandomNumberSelector(nModulo): uniform in [0, modulo).
    /// Power-of-two modulo masks the low word; otherwise the new LOW WORD mod modulo
    /// (engine 0045c40b-0d: `XOR EDX,EDX; DIV ESI` on EDX:EAX = 0:newLow — low-word,
    /// NOT the full 64-bit state). A modulo whose top bit is set ((int)nModulo &lt; 1)
    /// returns 0, matching the signed check.</summary>
    public uint Pick(uint modulo)
    {
        if ((int)modulo < 1) return 0;
        Next();
        if ((modulo & (modulo - 1)) != 0) return Low % modulo;
        return Low & (modulo - 1);
    }

    /// <summary>RollBetweenMinAndMax (1.14d 0x472280): min + RANDOM_RandomNumberSelector(max).</summary>
    public int RollBetween(int min, int max) => unchecked(min + (int)Pick((uint)max));

    /// <summary>SEED_RollRange(min, max): uniform in [min, max). Returns min if max &lt;= min.
    /// NOTE: uses the FULL 64-bit state for non-power-of-two (DRLG semantics).</summary>
    public int RollRange(int min, int max)
    {
        if (max <= min) return min;
        uint width = (uint)(max - min);
        ulong state = Next();
        if ((width & (width - 1)) != 0) return unchecked((int)(state % width) + min);
        return unchecked((int)(Low & (width - 1)) + min);
    }

    /// <summary>The act "start seed" — the game's init seed stepped once. From
    /// DRLG_AllocDrlgActMisc (1.14d 006424xx): nSeed = {init, 0x29a}; SEED_NEXT;
    /// dwStartSeed = nSeed.low.</summary>
    public static uint ActStartSeed(uint initSeed)
    {
        var s = new Seed(initSeed, 0x29a);
        s.Next();
        return s.Low;
    }

    /// <summary>A level's RNG seed, from DRLGACTMISC_AllocDrlgLevel (Drlg.cpp:815):
    /// sSeed = { dwStartSeed + levelId, 0x29a }.</summary>
    public static Seed LevelSeed(uint startSeed, int levelId) => new(unchecked(startSeed + (uint)levelId), 0x29a);

    /// <summary>SEED_GenerateRandomSeed: tick/time-based seed masked to 31 bits.</summary>
    public static uint GenerateRandomSeed(uint tickPlusTimePlusOffset)
    {
        uint v = tickPlusTimePlusOffset;
        for (int i = 0; i < 3; i++) v = unchecked(v * 0x19660d + 0x3c6ef35f);
        return v & 0x7fffffff;
    }
}

/// <summary>DRLG seed RNG — the recon-named facade the transformed closure calls
/// (drlg/rng.zig: Archive.cpp RollRandomSeed / UNIT_GetModuloFromSeed /
/// RANDOM_RandomNumberSelector), with a two-word conversion at the boundary.</summary>
internal static unsafe class DrlgRng
{
    private static Seed ToSeed(D2SeedStrc s) => new((uint)s.nSeedLow, (uint)s.nSeedHigh);
    private static D2SeedStrc FromSeed(Seed s) => new() { nSeedLow = (int)s.Low, nSeedHigh = (int)s.High };

    /// <summary>D2_SEED_NEXT(seed): state64 = low*0x6ac690c5 + high, reinterpreted as a seed.
    /// (d2_platform.h:380)</summary>
    public static D2SeedStrc SeedNext(D2SeedStrc seed)
    {
        var s = ToSeed(seed);
        s.Next();
        return FromSeed(s);
    }

    /// <summary>RollRandomSeed (Archive.cpp:528 / 0045c370):
    /// DVar1 = D2_SEED_NEXT(*pSeed); *pSeed = DVar1; return DVar1.nSeedLow;</summary>
    public static int RollRandomSeed(D2SeedStrc* pSeed)
    {
        var s = ToSeed(*pSeed);
        uint low = s.RollRandomSeed();
        *pSeed = FromSeed(s);
        return (int)low;
    }

    /// <summary>RANDOM_RandomNumberSelector (Archive.cpp:556 / 0045c3e0): uniform in [0,nModulo).
    /// The engine modulos the LOW WORD, not the full 64-bit state: at 0045c40b-0d it runs
    /// `XOR EDX,EDX; DIV ESI` on EDX:EAX = 0:newSeedLow (verified disasm). The prior full64
    /// form was a decompiler artifact; it broke Act-III shrine/file-tracker draws.</summary>
    public static uint RandomNumberSelector(D2SeedStrc* pSeed, uint nModulo)
    {
        var s = ToSeed(*pSeed);
        uint r = s.Pick(nModulo);
        *pSeed = FromSeed(s);
        return r;
    }

    /// <summary>UNIT_GetModuloFromSeed (Archive.cpp:536 / 0045c390): identical result to
    /// RANDOM_RandomNumberSelector; kept as a faithful separate entry point.</summary>
    public static uint GetModuloFromSeed(D2SeedStrc* pSeed, uint nModulo) => RandomNumberSelector(pSeed, nModulo);
}

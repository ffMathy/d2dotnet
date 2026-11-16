// Port of d2go pkg/utils/map_seed_hash.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez): same inputs and
// results. The reversal itself is written here from the hash formula (an odd multiplier is invertible modulo 2^32)
// instead of porting d2go's search loop, which d2go credits to MapAssist.

namespace D2Dotnet.D2Go.Memory;

/// <summary>Recovers the map seed from the game's seed hashes (Go <c>utils.GetMapSeed</c>).</summary>
public static class MapSeed
{
    /// <summary>The game's seed hash multiplier: <c>hash = seed * Multiplier + Increment (mod 2^32)</c>.</summary>
    internal const uint Multiplier = 0x6AC690C5;

    /// <summary>The game's seed hash increment.</summary>
    internal const uint Increment = 666;

    /// <summary>The hash is taken modulo 2^32 (Go masks with <c>&amp; 0xFFFFFFFF</c>).</summary>
    internal const ulong HashMask = 0xFFFFFFFF;

    /// <summary>The inverse of <see cref="Multiplier"/> modulo 2^32.</summary>
    internal static readonly uint MultiplierInverse = Invert(Multiplier);

    /// <summary>
    /// The map seed whose hash is <paramref name="endHashSeed"/>: the <c>seed</c> with
    /// <c>(seed * 0x6AC690C5 + 666) mod 2^32 == endHashSeed</c> (Go <c>GetMapSeed</c>). Returns false, and a
    /// <paramref name="seed"/> of 0, when <paramref name="initHashSeed"/> XOR the seed is 0, which includes an
    /// <paramref name="initHashSeed"/> equal to the seed.
    /// </summary>
    /// <remarks>
    /// The hashes are 32-bit values in the game. Go takes them as 64-bit <c>uint</c>s, and for a hash of 2^32 or
    /// more its search never ends; with <see cref="uint"/> parameters that input can not occur here.
    /// </remarks>
    public static bool TryGetMapSeed(uint initHashSeed, uint endHashSeed, out uint seed)
    {
        var reversed = ReverseMapSeedHash(endHashSeed);
        if ((initHashSeed ^ reversed) == 0)
        {
            seed = 0;
            return false;
        }

        seed = reversed;
        return true;
    }

    /// <summary>
    /// The seed whose hash is <paramref name="hash"/> (Go <c>reverseMapSeedHash</c>). The multiplier is odd, so every
    /// 32-bit hash has exactly one 32-bit seed; Go's search returns that same seed, as it is the smallest match.
    /// </summary>
    internal static uint ReverseMapSeedHash(uint hash) =>
        (uint)(unchecked(((ulong)hash - Increment) * MultiplierInverse) & HashMask);

    /// <summary>Inverse of an odd <paramref name="value"/> modulo 2^32 (Newton's iteration).</summary>
    private static uint Invert(uint value)
    {
        var inverse = value; // correct to 3 bits for any odd value
        for (var i = 0; i < 5; i++)
            inverse = unchecked(inverse * (2 - (value * inverse)));
        return inverse;
    }
}

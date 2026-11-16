// Port of d2go pkg/utils/map_seed_hash.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez), whose logic
// comes from MapAssist.

namespace D2Dotnet.D2Go.Memory;

/// <summary>Recovers the map seed from the game's seed hashes (Go <c>utils.GetMapSeed</c>).</summary>
public static class MapSeed
{
    private const ulong MapHashDivisor = 1 << 16;

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
        ulong gameSeedXor = 0;
        var found = TryReverseMapSeedHash(endHashSeed, out var reversed);
        if (found)
            gameSeedXor = initHashSeed ^ reversed;

        if (gameSeedXor == 0)
        {
            seed = 0;
            return false;
        }

        seed = (uint)reversed;
        return true;
    }

    /// <summary>
    /// The smallest seed whose hash is <paramref name="hash"/> (Go <c>reverseMapSeedHash</c>): tries every value until
    /// one has the hash's low 16 bits, then only values 2^16 apart, which keep them.
    /// </summary>
    internal static bool TryReverseMapSeedHash(ulong hash, out ulong seed)
    {
        var incrementalValue = 1UL;

        for (var startValue = 0UL; startValue < ulong.MaxValue; startValue = unchecked(startValue + incrementalValue))
        {
            var seedResult = unchecked((startValue * 0x6AC690C5) + 666) & 0xFFFFFFFF;

            if (seedResult == hash)
            {
                seed = startValue;
                return true;
            }

            if (incrementalValue == 1 && (seedResult % MapHashDivisor) == (hash % MapHashDivisor))
                incrementalValue = MapHashDivisor;
        }

        seed = 0;
        return false;
    }
}

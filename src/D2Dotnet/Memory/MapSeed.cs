// The map seed check of d2go pkg/utils/map_seed_hash.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).
// The reversal is written from the hash formula (an odd multiplier is invertible modulo 2^32) instead of the search
// loop d2go credits to MapAssist.

namespace D2Dotnet.Memory;

/// <summary>The game's map seed hash, and its reversal.</summary>
public static class MapSeed
{
    /// <summary>The hash multiplier: <c>hash = seed * Multiplier + Increment (mod 2^32)</c>.</summary>
    public const uint Multiplier = 0x6AC690C5;

    /// <summary>The hash increment.</summary>
    public const uint Increment = 666;

    /// <summary>The inverse of <see cref="Multiplier"/> modulo 2^32.</summary>
    internal static readonly uint MultiplierInverse = Invert(Multiplier);

    /// <summary>The hash of a seed: <c>(seed * 0x6AC690C5 + 666) mod 2^32</c>.</summary>
    public static uint Hash(uint seed) => unchecked((seed * Multiplier) + Increment);

    /// <summary>The seed whose <see cref="Hash"/> is <paramref name="hash"/>. Every hash has exactly one seed.</summary>
    public static uint Reverse(uint hash) => unchecked((hash - Increment) * MultiplierInverse);

    /// <summary>
    /// The map seed from the game's two seed hashes: the seed whose hash is <paramref name="endHash"/>. Returns false,
    /// as d2go and MapAssist do, when that seed equals <paramref name="initHash"/> (the hashes were not read from a game).
    /// </summary>
    public static bool TryGetMapSeed(uint initHash, uint endHash, out uint seed)
    {
        seed = Reverse(endHash);
        if ((initHash ^ seed) != 0)
            return true;

        seed = 0;
        return false;
    }

    // The inverse of an odd value modulo 2^32, by Newton's iteration (each step doubles the correct low bits).
    private static uint Invert(uint value)
    {
        var inverse = value; // correct to 3 bits for any odd value
        for (var i = 0; i < 5; i++)
            inverse = unchecked(inverse * (2 - (value * inverse)));
        return inverse;
    }
}

namespace D2Dotnet.Files.Casc;

/// <summary>
/// Bob Jenkins' lookup3 hash (<c>hashlittle</c> / <c>hashlittle2</c>), public domain.
/// CASC uses it for the guarded blocks of the local <c>.idx</c> files and for the
/// checksum in the 30-byte header in front of every record in a <c>data.###</c> archive.
/// </summary>
/// <remarks>
/// Ported from the byte-at-a-time branch of lookup3.c (as bundled with CascLib), which gives the
/// same result as the aligned branches on little-endian machines.
/// </remarks>
internal static class Jenkins96
{
    /// <summary><c>hashlittle(key, length, initval)</c>.</summary>
    public static uint HashLittle(ReadOnlySpan<byte> key, uint initval)
    {
        uint c = initval, b = 0;
        HashLittle2(key, ref c, ref b);
        return c;
    }

    /// <summary>
    /// <c>hashlittle2(key, length, &amp;pc, &amp;pb)</c>: <paramref name="pc"/> and <paramref name="pb"/>
    /// are the primary and secondary seeds on input, and the primary and secondary hashes on output.
    /// </summary>
    public static void HashLittle2(ReadOnlySpan<byte> key, ref uint pc, ref uint pb)
    {
        uint a, b, c;
        a = b = c = 0xdeadbeef + (uint)key.Length + pc;
        c += pb;

        var k = key;
        while (k.Length > 12)
        {
            a += k[0] + ((uint)k[1] << 8) + ((uint)k[2] << 16) + ((uint)k[3] << 24);
            b += k[4] + ((uint)k[5] << 8) + ((uint)k[6] << 16) + ((uint)k[7] << 24);
            c += k[8] + ((uint)k[9] << 8) + ((uint)k[10] << 16) + ((uint)k[11] << 24);
            Mix(ref a, ref b, ref c);
            k = k[12..];
        }

        switch (k.Length)
        {
            case 12: c += (uint)k[11] << 24; goto case 11;
            case 11: c += (uint)k[10] << 16; goto case 10;
            case 10: c += (uint)k[9] << 8; goto case 9;
            case 9: c += k[8]; goto case 8;
            case 8: b += (uint)k[7] << 24; goto case 7;
            case 7: b += (uint)k[6] << 16; goto case 6;
            case 6: b += (uint)k[5] << 8; goto case 5;
            case 5: b += k[4]; goto case 4;
            case 4: a += (uint)k[3] << 24; goto case 3;
            case 3: a += (uint)k[2] << 16; goto case 2;
            case 2: a += (uint)k[1] << 8; goto case 1;
            case 1: a += k[0]; break;
            case 0: pc = c; pb = b; return; // zero length strings require no mixing
        }

        Final(ref a, ref b, ref c);
        pc = c;
        pb = b;
    }

    private static uint Rot(uint x, int k) => (x << k) | (x >> (32 - k));

    private static void Mix(ref uint a, ref uint b, ref uint c)
    {
        a -= c; a ^= Rot(c, 4); c += b;
        b -= a; b ^= Rot(a, 6); a += c;
        c -= b; c ^= Rot(b, 8); b += a;
        a -= c; a ^= Rot(c, 16); c += b;
        b -= a; b ^= Rot(a, 19); a += c;
        c -= b; c ^= Rot(b, 4); b += a;
    }

    private static void Final(ref uint a, ref uint b, ref uint c)
    {
        c ^= b; c -= Rot(b, 14);
        a ^= c; a -= Rot(c, 11);
        b ^= a; b -= Rot(a, 25);
        c ^= b; c -= Rot(b, 16);
        a ^= c; a -= Rot(c, 4);
        b ^= a; b -= Rot(a, 14);
        c ^= b; c -= Rot(b, 24);
    }
}

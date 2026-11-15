namespace D2Dotnet.Map.Mpq;

/// <summary>The MPQ crypt table (upstream <c>encryption.ts</c>).</summary>
public static class MpqEncryption
{
    /// <summary>0x500 entry table used for hashing and decryption.</summary>
    public static IReadOnlyList<uint> Table => TableValues;

    internal static readonly uint[] TableValues = Generate();

    private static uint[] Generate()
    {
        var table = new uint[0x500];
        uint seed = 0x00100001;
        for (var i = 0; i < 256; i++)
        {
            var k = i;
            for (var j = 0; j < 5; j++)
            {
                seed = (seed * 125 + 3) % 0x2aaaab;
                var a = (seed & 0xffff) << 0x10;
                seed = (seed * 125 + 3) % 0x2aaaab;
                var b = seed & 0xffff;
                table[k] = a | b;
                k += 0x100;
            }
        }
        return table;
    }
}

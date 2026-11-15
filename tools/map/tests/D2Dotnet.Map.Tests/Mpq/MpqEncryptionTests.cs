using D2Dotnet.Map.Binary;
using D2Dotnet.Map.Mpq;

namespace D2Dotnet.Map.Tests.Mpq;

/// <summary>Port of upstream <c>mpq/src/__test__/mpq.encryption.test.ts</c>.</summary>
public class MpqEncryptionTests
{
    [Fact]
    public void ShouldMatchProvidedTable()
    {
        Assert.Equal(MpqEncryptionTableData.Expected.Length, MpqEncryption.Table.Count);
        for (var i = 0; i < MpqEncryptionTableData.Expected.Length; i++)
        {
            Assert.Equal(MpqEncryptionTableData.Expected[i], Hex.ToHex(MpqEncryption.Table[i]));
        }
    }
}

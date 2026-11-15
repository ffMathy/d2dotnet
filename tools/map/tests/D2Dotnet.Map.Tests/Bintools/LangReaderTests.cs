using D2Dotnet.Map.Bintools.Readers;

namespace D2Dotnet.Map.Tests.Bintools;

/// <summary>Port of upstream <c>bintools/src/readers/__test__/lang.reader.test.ts</c>.</summary>
public class LangReaderTests
{
    [Fact]
    public void ShouldReadV1LanguageTables()
    {
        var res = LangReader.Parse(Fixture.Bytes("patchstring.tbl"));

        Assert.Equal(1179, res.Count);
        Assert.Equal(1179, res.Indexes.Count);

        Assert.Equal(1181u, res.CountHash);
        Assert.Equal(1181, res.HashTable.Count);

        Assert.Equal("D2bnetHelp13c", res.HashTable[6].Key);
        Assert.Equal("/reply", res.HashTable[6].Value);
        Assert.Equal("/reply".Length + 1, res.HashTable[6].ValueLength);
    }
}

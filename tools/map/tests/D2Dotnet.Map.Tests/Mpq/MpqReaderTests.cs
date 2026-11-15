using System.Security.Cryptography;
using D2Dotnet.Map.Mpq;
using MpqArchive = D2Dotnet.Map.Mpq.Mpq;

namespace D2Dotnet.Map.Tests.Mpq;

/// <summary>Port of upstream <c>mpq/src/__test__/mpq.reader.test.ts</c>.</summary>
public class MpqReaderTests
{
    private const string Pd2 = "strings/pd2/patchstring.tbl";
    private const string Pd2Sha256 = "3867b6f6bc92c4c2895d9cec970e6e499c2d3a9e647cc43cae4449e695b1a2c8";

    private readonly MpqArchive _mpq = MpqArchive.Load("foo.mpq");

    [Fact]
    public void ShouldHashStrings()
    {
        Assert.Equal(0xef7b1bdbu, _mpq.Hash("hello world", MpqHashType.HashA));
        Assert.Equal(0xef7b1bdbu, _mpq.Hash("HELLO world", MpqHashType.HashA));

        Assert.Equal(0x4ba6f4b8u, _mpq.Hash("hello world", MpqHashType.HashB));
        Assert.Equal(0x59c90699u, _mpq.Hash("hello world", MpqHashType.Table));
        Assert.Equal(0xfeb41eddu, _mpq.Hash("hello world", MpqHashType.TableOffset));

        Assert.Equal(0xc3af3770u, _mpq.Hash("(hash table)", MpqHashType.Table));
        Assert.Equal(0xec83b3a3u, _mpq.Hash("(block table)", MpqHashType.Table));
        Assert.Equal(0xfd657910u, _mpq.Hash("(listfile)", MpqHashType.HashA));
        Assert.Equal(0x4e9b98a7u, _mpq.Hash("(listfile)", MpqHashType.HashB));

        Assert.Equal(0xaaf47eddu, _mpq.Hash("data\\local\\LNG\\ENG\\expansionstring.tbl", MpqHashType.HashA));
        Assert.Equal(0xaaf47eddu, _mpq.Hash("data/local/LNG/ENG/expansionstring.tbl", MpqHashType.HashA));
    }

    [Fact]
    public void ShouldGenerateDecryptionKeys()
    {
        Assert.Equal(0xdee9de0bu, _mpq.DecryptionKey("data\\global\\excel\\monstats.bin"));
        Assert.Equal(0xfd7285c8u, _mpq.DecryptionKey("data\\global\\excel\\Armor.bin"));
        Assert.Equal(0xfd7285c8u, _mpq.DecryptionKey("data\\global\\excel\\armor.bin"));
    }

    [Theory]
    [InlineData("test-implode.mpq", 59335u)]
    [InlineData("test-pkware.mpq", 59364u)]
    public async Task ShouldReadCompressedData(string file, uint archivedSize)
    {
        await using var mpq = MpqArchive.Load(Fixture.Path(file));
        var header = await mpq.Header;
        Assert.Equal("MPQ\x1A", header.Magic);
        Assert.Equal(32u, header.HeaderSize);
        Assert.Equal(archivedSize, header.ArchivedSize);

        Assert.True(await mpq.HasAsync(Pd2));
        var bytes = await mpq.ExtractAsync(Pd2);
        Assert.NotNull(bytes);
        Assert.Equal(61650, bytes.Length);
        Assert.Equal(Pd2Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task ShouldReadFromMemoryAndExtractRepeatedly()
    {
        await using var mpq = MpqArchive.Load(Fixture.Bytes("test-pkware.mpq"), "pkware");
        Assert.Equal("pkware", mpq.Name);
        for (var i = 0; i < 2; i++)
        {
            var bytes = await mpq.ExtractAsync(Pd2);
            Assert.Equal(Pd2Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes!)));
        }
    }

    [Fact]
    public async Task MissingFilesReturnNull()
    {
        await using var mpq = MpqArchive.Load(Fixture.Path("test-implode.mpq"));
        Assert.False(await mpq.HasAsync("data/global/excel/levels.bin"));
        Assert.Null(await mpq.ExtractAsync("data/global/excel/levels.bin"));
    }

    [Fact]
    public async Task InvalidArchivesAreRejected()
    {
        await using var mpq = MpqArchive.Load(new byte[64]);
        var err = await Assert.ThrowsAsync<InvalidDataException>(() => mpq.Header);
        Assert.Equal("Only MPQ.magic 0x1a is supported", err.Message);
    }

    [Theory]
    [MemberData(nameof(MpqDecryptionTests.Cases), MemberType = typeof(MpqDecryptionTests))]
    public void ShouldDecryptTable(string name, string input, string output, uint key)
    {
        Assert.NotEmpty(name);
        var inputBuf = Convert.FromBase64String(input);
        _mpq.Decrypt(inputBuf, key);
        Assert.Equal(output, Convert.ToBase64String(inputBuf));
    }
}

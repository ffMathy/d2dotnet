using System.Text;
using D2Dotnet.Map.GameData.Casc;

namespace D2Dotnet.Map.GameData.Tests;

/// <summary>Reference vectors from the driver5() self-test in Bob Jenkins' lookup3.c (public domain).</summary>
public class JenkinsTests
{
    private static readonly byte[] FourScore = Encoding.ASCII.GetBytes("Four score and seven years ago");

    [Theory]
    [InlineData(0u, 0u, 0xdeadbeefu, 0xdeadbeefu)]
    [InlineData(0u, 0xdeadbeefu, 0xbd5b7ddeu, 0xdeadbeefu)]
    [InlineData(0xdeadbeefu, 0xdeadbeefu, 0x9c093ccdu, 0xbd5b7ddeu)]
    public void HashLittle2_EmptyKey(uint c, uint b, uint expectedC, uint expectedB)
    {
        Jenkins96.HashLittle2([], ref c, ref b);
        Assert.Equal(expectedC, c);
        Assert.Equal(expectedB, b);
    }

    [Theory]
    [InlineData(0u, 0u, 0x17770551u, 0xce7226e6u)]
    [InlineData(0u, 1u, 0xe3607caeu, 0xbd371de4u)]
    [InlineData(1u, 0u, 0xcd628161u, 0x6cbea4b3u)]
    public void HashLittle2_FourScore(uint c, uint b, uint expectedC, uint expectedB)
    {
        Jenkins96.HashLittle2(FourScore, ref c, ref b);
        Assert.Equal(expectedC, c);
        Assert.Equal(expectedB, b);
    }

    [Fact]
    public void HashLittle_FourScore()
    {
        Assert.Equal(0x17770551u, Jenkins96.HashLittle(FourScore, 0));
        Assert.Equal(0xcd628161u, Jenkins96.HashLittle(FourScore, 1));
    }
}

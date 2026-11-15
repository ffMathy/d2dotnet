using D2Dotnet.Map.Data;
using D2Dotnet.Map.Server;
using D2Dotnet.Map.Server.Routes;
using Microsoft.Extensions.Logging.Abstractions;

namespace D2Dotnet.Map.Tests.Server;

public class RouteValidationTests
{
    private static RouteRequest Req(params (string Key, string Value)[] p) =>
        new("ID", NullLogger.Instance, p.ToDictionary(x => x.Key, x => (string?)x.Value), "/");

    [Theory]
    [InlineData("0x3607656c", "Normal", 0x3607656c, Difficulty.Normal)]
    [InlineData("906454380", "2", 906454380, Difficulty.Hell)]
    [InlineData("1", "nightmare", 1, Difficulty.Nightmare)]
    [InlineData("4294967294", "0", 4294967294, Difficulty.Normal)]
    public void ValidSeedAndDifficulty(string seed, string difficulty, double expectedSeed, Difficulty expectedDifficulty)
    {
        var (s, d) = MapRoute.ValidateParams(Req(("seed", seed), ("difficulty", difficulty)));
        Assert.Equal(expectedSeed, s);
        Assert.Equal(expectedDifficulty, d);
    }

    [Theory]
    [InlineData("0", "Normal", "Invalid seed")]
    [InlineData("-1", "Normal", "Invalid seed")]
    [InlineData("0xffffffff", "Normal", "Invalid seed")]
    [InlineData("abc", "Normal", "Invalid seed")]
    [InlineData("10", "Easy", "Invalid difficulty")]
    [InlineData("10", "3", "Invalid difficulty")]
    [InlineData("\u0663", "Normal", "Invalid seed")]
    [InlineData("10", "\u0661", "Invalid difficulty")]
    public void InvalidSeedOrDifficulty(string seed, string difficulty, string message)
    {
        var err = Assert.Throws<HttpError>(() => MapRoute.ValidateParams(Req(("seed", seed), ("difficulty", difficulty))));
        Assert.Equal(422, err.Status);
        Assert.Equal(message, err.Message);
    }

    [Fact]
    public void ActIsValidatedFirst()
    {
        var err = Assert.Throws<HttpError>(() => MapActRoute.ValidateParams(Req(("seed", "0"), ("difficulty", "x"), ("act", "ActX"))));
        Assert.Equal("Invalid act", err.Message);
        var ok = MapActRoute.ValidateParams(Req(("seed", "10"), ("difficulty", "Hell"), ("act", "ActIII")));
        Assert.Equal((10d, Difficulty.Hell, Act.ActIII), ok);
    }

    [Theory]
    [InlineData("ActI", "1", 1)]
    [InlineData("0", "39", 39)]
    [InlineData("ActII", "40", 40)]
    [InlineData("4", "abc", double.NaN)]
    public void LevelMustBelongToTheAct(string act, string level, double expected)
    {
        var (_, _, _, mapId) = MapActLevelRoute.ValidateParams(Req(("seed", "10"), ("difficulty", "0"), ("act", act), ("level", level)));
        Assert.Equal(expected, mapId);
    }

    [Theory]
    [InlineData("ActI", "40")]
    [InlineData("ActII", "1")]
    [InlineData("ActI", "abc")]
    public void LevelInAnotherActIsRejected(string act, string level)
    {
        var err = Assert.Throws<HttpError>(() => MapActLevelRoute.ValidateParams(Req(("seed", "10"), ("difficulty", "0"), ("act", act), ("level", level))));
        Assert.Equal("Invalid map id", err.Message);
    }

    [Fact]
    public void ImageRouteValidatesLevel()
    {
        Assert.Equal((10d, Difficulty.Hell, 23d), MapImageRoute.ValidateParams(Req(("seed", "10"), ("difficulty", "Hell"), ("level", "23"))));
        var err = Assert.Throws<HttpError>(() => MapImageRoute.ValidateParams(Req(("seed", "10"), ("difficulty", "Hell"), ("level", "x"))));
        Assert.Equal("Invalid level", err.Message);
        err = Assert.Throws<HttpError>(() => MapImageRoute.ValidateParams(Req(("seed", "0"), ("difficulty", "Hell"), ("level", "x"))));
        Assert.Equal("Invalid seed", err.Message);
    }

    [Fact]
    public void UlidsAreSortableCrockfordBase32()
    {
        var a = Ulid.NewUlid(DateTimeOffset.FromUnixTimeMilliseconds(1469918176385));
        Assert.Equal(26, a.Length);
        Assert.StartsWith("01ARYZ6S41", a);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{26}$", Ulid.NewUlid());
    }
}

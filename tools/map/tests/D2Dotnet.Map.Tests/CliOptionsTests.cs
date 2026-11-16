namespace D2Dotnet.Map.Tests;

public class CliOptionsTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars) =>
        key => vars.FirstOrDefault(v => v.Key == key).Value;

    [Fact]
    public void Defaults()
    {
        var o = CliOptions.Parse([], Env());
        Assert.Equal(8899, o.Port);
        Assert.False(o.ShowHelp);
        Assert.Empty(o.Configuration);
        Assert.Empty(o.Remaining);
    }

    [Fact]
    public void EnvironmentVariables()
    {
        var o = CliOptions.Parse([], Env(("PORT", "9000abc"), ("DIABLO2_CONCURRENCY", "2.5")));
        Assert.Equal(9000, o.Port);
        Assert.Equal("3", o.Configuration["Map:MaxConcurrency"]);

        var ignored = CliOptions.Parse([], Env(("DIABLO2_CONCURRENCY", "lots")));
        Assert.False(ignored.Configuration.ContainsKey("Map:MaxConcurrency"));
        // non-ASCII digits are NaN in JavaScript, so ignored too
        var arabic = CliOptions.Parse([], Env(("DIABLO2_CONCURRENCY", "٣")));
        Assert.False(arabic.Configuration.ContainsKey("Map:MaxConcurrency"));
        Assert.Equal("1", CliOptions.Parse([], Env(("DIABLO2_CONCURRENCY", "0"))).Configuration["Map:MaxConcurrency"]);
        Assert.Equal("1", CliOptions.Parse([], Env(("DIABLO2_CONCURRENCY", "Infinity"))).Configuration["Map:MaxConcurrency"]);
    }

    [Fact]
    public void ClusterSizeIsTheOlderNameOfConcurrency()
    {
        Assert.Equal("4", CliOptions.Parse([], Env(("DIABLO2_CLUSTER_SIZE", "4"))).Configuration["Map:MaxConcurrency"]);
        // the new name wins over the old one
        Assert.Equal("2", CliOptions.Parse([], Env(("DIABLO2_CLUSTER_SIZE", "4"), ("DIABLO2_CONCURRENCY", "2"))).Configuration["Map:MaxConcurrency"]);
        Assert.Equal("5", CliOptions.Parse(["--cluster-size", "5"], Env()).Configuration["Map:MaxConcurrency"]);
    }

    [Fact]
    public void SwitchesOverrideEnvironment()
    {
        var o = CliOptions.Parse(
            ["--port", "1234", "--concurrency", "4", "--game-path", "/games/d2r", "--language", "dede",
             "--Logging:LogLevel:Default=Warning", "-h"],
            Env(("PORT", "1"), ("DIABLO2_CONCURRENCY", "8")));
        Assert.Equal(1234, o.Port);
        Assert.True(o.ShowHelp);
        Assert.Equal("4", o.Configuration["Map:MaxConcurrency"]);
        Assert.Equal("/games/d2r", o.Configuration["Map:Diablo2Path"]);
        Assert.Equal("deDE", o.Configuration["Map:Language"]);
        Assert.Equal(["--Logging:LogLevel:Default=Warning"], o.Remaining);
    }

    [Fact]
    public void ClassicLanguageCodesMapToD2RFields() =>
        Assert.Equal("frFR", CliOptions.Parse(["--language", "FRA"], Env()).Configuration["Map:Language"]);

    [Fact]
    public void RemovedWineSwitchesArePassedThrough()
    {
        // the wine/process switches are gone; like any unknown argument they go to ASP.NET Core configuration
        var o = CliOptions.Parse(["--wine"], Env());
        Assert.Equal(["--wine"], o.Remaining);
    }

    [Theory]
    [InlineData("--port")]
    [InlineData("--port", "x")]
    [InlineData("--concurrency", "many")]
    [InlineData("--concurrency", "٣")]
    [InlineData("--cluster-size", "many")]
    [InlineData("--game-path")]
    [InlineData("--language")]
    [InlineData("--language", " ")]
    public void InvalidSwitches(params string[] args) =>
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(args, Env()));
}

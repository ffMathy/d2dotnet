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
        var o = CliOptions.Parse([], Env(("PORT", "9000abc"), ("DIABLO2_CLUSTER_SIZE", "2.5")));
        Assert.Equal(9000, o.Port);
        Assert.Equal("3", o.Configuration["Map:ProcessCount"]);

        var ignored = CliOptions.Parse([], Env(("DIABLO2_CLUSTER_SIZE", "lots")));
        Assert.False(ignored.Configuration.ContainsKey("Map:ProcessCount"));
        // non-ASCII digits are NaN in JavaScript, so ignored too
        var arabic = CliOptions.Parse([], Env(("DIABLO2_CLUSTER_SIZE", "\u0663")));
        Assert.False(arabic.Configuration.ContainsKey("Map:ProcessCount"));
        Assert.Equal("1", CliOptions.Parse([], Env(("DIABLO2_CLUSTER_SIZE", "0"))).Configuration["Map:ProcessCount"]);
    }

    [Fact]
    public void SwitchesOverrideEnvironment()
    {
        var o = CliOptions.Parse(
            ["--port", "1234", "--cluster-size", "4", "--game-path", "/games/d2", "--registry-path", "/r.reg", "--wine", "wine64",
             "--map-exe", "a.exe", "--map-exe", "b.exe", "--Logging:LogLevel:Default=Warning", "-h"],
            Env(("PORT", "1"), ("DIABLO2_CLUSTER_SIZE", "8")));
        Assert.Equal(1234, o.Port);
        Assert.True(o.ShowHelp);
        Assert.Equal("4", o.Configuration["Map:ProcessCount"]);
        Assert.Equal("/games/d2", o.Configuration["Map:Diablo2Path"]);
        Assert.Equal("/r.reg", o.Configuration["Map:RegistryPath"]);
        Assert.Equal("wine64", o.Configuration["Map:WineCommand"]);
        Assert.Equal("a.exe", o.Configuration["Map:MapCommand:0"]);
        Assert.Equal("b.exe", o.Configuration["Map:MapCommand:1"]);
        Assert.Equal(["--Logging:LogLevel:Default=Warning"], o.Remaining);
    }

    [Theory]
    [InlineData("--port")]
    [InlineData("--port", "x")]
    [InlineData("--cluster-size", "many")]
    [InlineData("--cluster-size", "\u0663")]
    public void InvalidSwitches(params string[] args) =>
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(args, Env()));
}

using System.Net;
using System.Text.Json;
using D2Dotnet.Map.Data;
using D2Dotnet.Map.Process;
using D2Dotnet.Map.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;

namespace D2Dotnet.Map.Tests.Server;

/// <summary>Runs the real server (routes, cluster, process pool) against the fake generator script.</summary>
public sealed class MapServerFactory : WebApplicationFactory<Program>
{
    public FakeGame Game { get; } = new();
    public FakeMpqDataSource MpqSource { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMpqDataSource>();
            services.AddSingleton<IMpqDataSource>(MpqSource);
            services.PostConfigure<MapProcessOptions>(o =>
            {
                var fake = Game.Options();
                o.WineCommand = fake.WineCommand;
                o.MapCommand = fake.MapCommand;
                o.Diablo2Path = fake.Diablo2Path;
                o.RegistryPath = fake.RegistryPath;
                o.ProcessTimeout = fake.ProcessTimeout;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) Game.Dispose();
    }
}

public sealed class MapServerTests(MapServerFactory factory) : IClassFixture<MapServerFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.Equal("application/json; charset=utf-8", res.Content.Headers.ContentType?.ToString());
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }

    private static string[] Keys(JsonElement obj) => obj.EnumerateObject().Select(p => p.Name).ToArray();

    [Fact]
    public async Task Health()
    {
        var res = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("{\"message\":\"ok\"}", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CorsAllowsAnyOrigin()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/health");
        req.Headers.Add("Origin", "https://example.com");
        var res = await _client.SendAsync(req);
        Assert.Equal("*", res.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task WholeWorld()
    {
        var res = await _client.GetAsync("/v1/map/0x3607656c/Normal.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await Json(res);

        Assert.Equal(["id", "seed", "difficulty", "levels"], Keys(json));
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{26}$", json.GetProperty("id").GetString());
        Assert.Equal(0x3607656c, json.GetProperty("seed").GetInt64());
        Assert.Equal(0, json.GetProperty("difficulty").GetInt32());

        var levels = json.GetProperty("levels").EnumerateArray().ToList();
        Assert.Equal([1, 2, 40], levels.Select(l => l.GetProperty("id").GetInt32()));

        var level = levels[0];
        Assert.Equal(["type", "id", "name", "offset", "size", "objects", "map"], Keys(level));
        Assert.Equal("map", level.GetProperty("type").GetString());
        Assert.Equal("Rogue Encampment", level.GetProperty("name").GetString());
        Assert.Equal("{\"x\":100,\"y\":200}", level.GetProperty("offset").GetRawText());
        Assert.Equal("{\"width\":40,\"height\":30}", level.GetProperty("size").GetRawText());
        Assert.Equal("[[1,5,1],[2,3,2],[1,5,1],[]]", level.GetProperty("map").GetRawText());

        var objects = level.GetProperty("objects").EnumerateArray().ToList();
        Assert.Equal("{\"id\":119,\"type\":\"object\",\"x\":20,\"y\":20,\"name\":\"Waypoint\",\"op\":23,\"class\":\"waypoint\"}", objects[0].GetRawText());
        Assert.Equal("{\"id\":2,\"type\":\"exit\",\"x\":35,\"y\":10,\"name\":\"Blood Moor\",\"isGoodExit\":true}", objects[1].GetRawText());
        Assert.Equal("{\"id\":0,\"type\":\"npc\",\"x\":3,\"y\":3,\"name\":\"Zombie\"}", objects[2].GetRawText());
        Assert.Equal("{\"id\":5,\"type\":\"npc\",\"x\":4,\"y\":4,\"name\":\"Coldcrow\",\"isSuperUnique\":true,\"superId\":2}", objects[3].GetRawText());
        Assert.Equal("{\"id\":999,\"type\":\"npc\",\"x\":5,\"y\":5}", objects[4].GetRawText());
        Assert.Equal("{\"id\":580,\"type\":\"object\",\"x\":6,\"y\":6,\"class\":\"chest-super\"}", objects[5].GetRawText());
    }

    [Theory]
    [InlineData("/v1/map/10/Hell/ActII.json", 2, 1, new[] { 40 })]
    [InlineData("/v1/map/10/1/0.json", 1, 0, new[] { 1, 2 })]
    [InlineData("/V1/MAP/11/NORMAL/ACTI.JSON", 0, 0, new[] { 1, 2 })]
    public async Task Act(string url, int difficulty, int act, int[] levelIds)
    {
        var json = await Json(await _client.GetAsync(url));
        Assert.Equal(["id", "seed", "difficulty", "levels", "act"], Keys(json));
        Assert.Equal(difficulty, json.GetProperty("difficulty").GetInt32());
        Assert.Equal(act, json.GetProperty("act").GetInt32());
        Assert.Equal(levelIds, json.GetProperty("levels").EnumerateArray().Select(l => l.GetProperty("id").GetInt32()));
    }

    [Theory]
    [InlineData("/v1/map/10/Normal/ActI/2.json", new[] { 2 })]
    [InlineData("/v1/map/10/Normal/ActI/3.json", new int[0])]
    [InlineData("/v1/map/10/Normal/ActV/abc.json", new int[0])]
    public async Task Level(string url, int[] levelIds)
    {
        var res = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await Json(res);
        Assert.Equal(["id", "seed", "difficulty", "levels", "act"], Keys(json));
        Assert.Equal(levelIds, json.GetProperty("levels").EnumerateArray().Select(l => l.GetProperty("id").GetInt32()));
    }

    [Theory]
    [InlineData("/v1/map/0/Normal.json", "Invalid seed")]
    [InlineData("/v1/map/4294967295/Normal.json", "Invalid seed")]
    [InlineData("/v1/map/nope/Normal.json", "Invalid seed")]
    [InlineData("/v1/map/10/Easy.json", "Invalid difficulty")]
    [InlineData("/v1/map/10/Normal/ActVI.json", "Invalid act")]
    [InlineData("/v1/map/0/Normal/ActI.json", "Invalid seed")]
    [InlineData("/v1/map/10/Normal/ActI/40.json", "Invalid map id")]
    [InlineData("/v1/map/10/Normal/x.png", "Invalid level")]
    [InlineData("/v1/map/10/Normal/3.png", "Invalid level")]
    [InlineData("/v1/map/10/Normal/1.5.png", "Invalid level")]
    public async Task InvalidRequests(string url, string message)
    {
        var res = await _client.GetAsync(url);
        Assert.Equal((HttpStatusCode)422, res.StatusCode);
        var json = await Json(res);
        Assert.Equal(["id", "message"], Keys(json));
        Assert.Equal(message, json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task FractionalSeedFailsLikeUpstream()
    {
        // isInSeedRange(1.5) passes, but the generator (scanf %u) echoes 1 so setting the seed fails
        var res = await _client.GetAsync("/v1/map/1.5/Normal/ActII.json");
        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        var json = await Json(res);
        Assert.Equal("Internal server error", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Image()
    {
        var res = await _client.GetAsync("/v1/map/0xff00ff/Hell/1.png");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("image/png", res.Content.Headers.ContentType?.ToString());
        using var bmp = SKBitmap.Decode(await res.Content.ReadAsByteArrayAsync());
        Assert.Equal(40, bmp.Width);
        Assert.Equal(62, bmp.Height);
        Assert.Equal(SKColors.Blue, bmp.GetPixel(20, 20));
    }

    [Fact]
    public async Task HeadRequestsAreRouted()
    {
        var res = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/health"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task IndexPage()
    {
        var res = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html; charset=utf-8", res.Content.Headers.ContentType?.ToString());
        Assert.Contains("<h1>Diablo 2 Maps</h1>", await res.Content.ReadAsStringAsync());

        // the browser viewer bundle is not part of the port
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/index.js")).StatusCode);
    }

    [Fact]
    public async Task UnknownRoutesAre404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/v1/map/10")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/v1/map/10/Normal/ActI/1/2.json")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneGenerator()
    {
        var urls = Enumerable.Range(100, 6).Select(seed => $"/v1/map/{seed}/Nightmare/ActI.json");
        var responses = await Task.WhenAll(urls.Select(u => _client.GetAsync(u)));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, factory.Game.Starts);
        Assert.Equal(1, factory.MpqSource.Loads);
    }
}

public sealed class MapServerFailureTests
{
    [Fact]
    public async Task MissingGameDataIsAnInternalError()
    {
        using var game = new FakeGame();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.PostConfigure<MapProcessOptions>(o =>
            {
                o.Diablo2Path = game.GamePath; // no MPQ files in here
                o.WineCommand = "/bin/sh";
            })));
        var client = factory.CreateClient();
        var res = await client.GetAsync("/v1/map/10/Normal.json");
        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Internal server error", json.GetProperty("message").GetString());
        Assert.Equal(26, json.GetProperty("id").GetString()!.Length);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }
}

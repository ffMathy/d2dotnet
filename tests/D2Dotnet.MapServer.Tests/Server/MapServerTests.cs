using System.Net;
using System.Text.Json;
using D2Dotnet.MapServer.Data;
using D2Dotnet.MapServer.Generation;
using D2Dotnet.MapServer.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;

namespace D2Dotnet.MapServer.Tests.Server;

/// <summary>Runs the real server (routes, map service, cache, model to JSON conversion) over the fake generator.</summary>
public sealed class MapServerFactory : WebApplicationFactory<Program>
{
    public FakeMapGenerator Generator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMapGenerator>();
            services.AddSingleton<IMapGenerator>(Generator);
        });
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

    [Theory]
    [InlineData("/health")]
    [InlineData("/v1/map/0/Normal.json")]
    [InlineData("/does-not-exist")]
    public async Task CorsHeaderIsSentWithoutOrigin(string url)
    {
        // cors() always sets the header, even without an Origin request header
        var res = await _client.GetAsync(url);
        Assert.Equal("*", res.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task BareOptionsIsAnsweredLikeCors()
    {
        var res = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/health"));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal("*", res.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("GET,HEAD,PUT,PATCH,POST,DELETE", res.Headers.GetValues("Access-Control-Allow-Methods").Single());
        Assert.False(res.Headers.Contains("Access-Control-Allow-Headers"));
        Assert.Equal("Access-Control-Request-Headers", res.Headers.GetValues("Vary").Single());
        Assert.Empty(await res.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task PreflightEchoesRequestedHeaders()
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "/v1/map/10/Normal.json");
        req.Headers.Add("Origin", "https://example.com");
        req.Headers.Add("Access-Control-Request-Method", "GET");
        req.Headers.Add("Access-Control-Request-Headers", "x-custom, content-type");
        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal("*", res.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("GET,HEAD,PUT,PATCH,POST,DELETE", res.Headers.GetValues("Access-Control-Allow-Methods").Single());
        Assert.Equal("x-custom, content-type", res.Headers.GetValues("Access-Control-Allow-Headers").Single());
        Assert.Equal("Access-Control-Request-Headers", res.Headers.GetValues("Vary").Single());
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
        // level 3 has no rooms and is skipped, as upstream skipped levels whose rooms failed to load
        Assert.Equal([1, 2, 40], levels.Select(l => l.GetProperty("id").GetInt32()));

        var level = levels[0];
        Assert.Equal(["type", "id", "name", "offset", "size", "objects", "map"], Keys(level));
        Assert.Equal("map", level.GetProperty("type").GetString());
        Assert.Equal("Rogue Encampment", level.GetProperty("name").GetString());
        Assert.Equal("{\"x\":100,\"y\":200}", level.GetProperty("offset").GetRawText());
        Assert.Equal("{\"width\":40,\"height\":30}", level.GetProperty("size").GetRawText());
        Assert.Equal("[[1,5,1],[2,3,2],[],[1,5,1]]", level.GetProperty("map").GetRawText());

        Assert.Equal(
        [
            "{\"id\":119,\"type\":\"object\",\"x\":20,\"y\":20,\"name\":\"Waypoint\",\"op\":23,\"class\":\"waypoint\"}",
            "{\"id\":0,\"type\":\"npc\",\"x\":3,\"y\":3,\"name\":\"Zombie\"}",
            "{\"id\":5,\"type\":\"npc\",\"x\":4,\"y\":4,\"name\":\"Coldcrow\",\"isSuperUnique\":true,\"superId\":2}",
            "{\"id\":999,\"type\":\"npc\",\"x\":5,\"y\":5}",
            "{\"id\":580,\"type\":\"object\",\"x\":6,\"y\":6,\"class\":\"chest-super\"}",
            "{\"id\":2,\"type\":\"object\",\"x\":30,\"y\":25,\"name\":\"Shrine\",\"op\":2,\"class\":\"shrine\"}",
            "{\"id\":2,\"type\":\"exit\",\"x\":35,\"y\":10,\"name\":\"Blood Moor\"}",
        ], level.GetProperty("objects").EnumerateArray().Select(o => o.GetRawText()));

        var bloodMoor = levels[1];
        Assert.Equal("{\"x\":140,\"y\":200}", bloodMoor.GetProperty("offset").GetRawText());
        Assert.Equal("{\"width\":10,\"height\":10}", bloodMoor.GetProperty("size").GetRawText());
        Assert.Equal("[" + string.Join(",", Enumerable.Repeat("[0]", 10)) + "]", bloodMoor.GetProperty("map").GetRawText());
        Assert.Equal("[{\"id\":8,\"type\":\"exit\",\"x\":5,\"y\":5,\"name\":\"Den of Evil\",\"isGoodExit\":true}]",
            bloodMoor.GetProperty("objects").GetRawText());

        // the true staff tomb is a good exit; level 66 is not in the level table, so the exit has no name
        Assert.Equal("[{\"id\":66,\"type\":\"exit\",\"x\":0,\"y\":0,\"isGoodExit\":true}]", levels[2].GetProperty("objects").GetRawText());
    }

    [Fact]
    public async Task JsonIsNotHtmlEscaped()
    {
        // express res.json (JSON.stringify) writes ', <, >, &, + and non-ASCII text as-is
        var res = await _client.GetAsync("/v1/map/12/Hell/ActII.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"name\":\"Tal Rasha's Tömb <&+>\"", body);
        Assert.DoesNotContain("\\u", body);
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
    // Arabic-Indic digit one (U+0661): NaN for JavaScript's Number()
    [InlineData("/v1/map/%D9%A1/0.json", "Invalid seed")]
    [InlineData("/v1/map/1/%D9%A1.json", "Invalid difficulty")]
    [InlineData("/v1/map/1/0/%D9%A1.json", "Invalid act")]
    [InlineData("/v1/map/1/0/%D9%A1.png", "Invalid level")]
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
        // isInSeedRange(1.5) passes, but upstream's generator (scanf %u) echoed 1 so setting the seed failed
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
    public async Task ConcurrentRequestsAreGeneratedOneAtATime()
    {
        // the default MaxConcurrency is 1, like upstream's single generator process
        var generator = factory.Generator;
        generator.Delay = TimeSpan.FromMilliseconds(20);
        try
        {
            var before = generator.Calls;
            var urls = Enumerable.Range(100, 6).Select(seed => $"/v1/map/{seed}/Nightmare/ActI.json");
            var responses = await Task.WhenAll(urls.Select(u => _client.GetAsync(u)));
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            Assert.Equal(before + 6, generator.Calls);
            Assert.Equal(1, generator.MaxRunning);
        }
        finally
        {
            generator.Delay = TimeSpan.Zero;
        }
    }

    [Fact]
    public async Task RequestsAreCached()
    {
        var generator = factory.Generator;
        var first = await (await _client.GetAsync("/v1/map/4242/Hell/ActII.json")).Content.ReadAsStringAsync();
        var calls = generator.Calls;
        var second = await (await _client.GetAsync("/v1/map/0x1092/2/1.json")).Content.ReadAsStringAsync();
        var level = await _client.GetAsync("/v1/map/4242/Hell/ActII/40.json");
        var image = await _client.GetAsync("/v1/map/4242/Hell/40.png");
        Assert.Equal(HttpStatusCode.OK, level.StatusCode);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal(calls, generator.Calls);
        // only the request id differs
        Assert.Equal(first[first.IndexOf(",\"seed\"", StringComparison.Ordinal)..], second[second.IndexOf(",\"seed\"", StringComparison.Ordinal)..]);
        Assert.Contains(generator.Requests, r => r == (4242u, Difficulty.Hell, 1));
    }

    [Fact]
    public async Task WholeWorldGeneratesEveryAct()
    {
        var generator = factory.Generator;
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/v1/map/777/Nightmare.json")).StatusCode);
        List<(uint Seed, Difficulty Difficulty, int Act)> requests;
        lock (generator.Requests) requests = generator.Requests.Where(r => r.Seed == 777).ToList();
        Assert.Equal([0, 1, 2, 3, 4], requests.Select(r => r.Act).Order());
        Assert.All(requests, r => Assert.Equal(Difficulty.Nightmare, r.Difficulty));
    }
}

public sealed class MapServerFailureTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingGameDataIsAnInternalError(bool folderExists)
    {
        var dir = Directory.CreateTempSubdirectory("d2map-test-");
        var path = folderExists ? dir.FullName : Path.Join(dir.FullName, "missing"); // no game files in here
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.PostConfigure<MapOptions>(o => o.Diablo2Path = path)));
        var client = factory.CreateClient();
        var res = await client.GetAsync("/v1/map/10/Normal.json");
        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Internal server error", json.GetProperty("message").GetString());
        Assert.Equal(26, json.GetProperty("id").GetString()!.Length);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        // the failure is kept, like upstream's cluster creation
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync("/v1/map/11/Normal/ActI.json")).StatusCode);
        dir.Delete(true);
    }

    [Fact]
    public async Task GeneratorErrorsAreInternalErrorsAndNotCached()
    {
        var generator = new FakeMapGenerator { Failure = new InvalidOperationException("boom") };
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMapGenerator>();
            services.AddSingleton<IMapGenerator>(generator);
        }));
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync("/v1/map/10/Normal/ActI.json")).StatusCode);
        generator.Failure = null;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/map/10/Normal/ActI.json")).StatusCode);
    }
}

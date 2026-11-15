using System.Diagnostics;
using D2Dotnet.Map.Bintools.Readers;
using D2Dotnet.Map.Data;
using Microsoft.Extensions.Logging;

namespace D2Dotnet.Map.Bintools;

/// <summary>
/// Loads game data out of the Diablo 2 MPQ archives (upstream <c>@diablo2/bintools</c> <c>Diablo2MpqLoader</c>).
/// </summary>
public sealed class Diablo2MpqLoader : IAsyncDisposable
{
    private static readonly string[] LangFiles = ["string.tbl", "expansionstring.tbl", "patchstring.tbl"];

    // Order matters for item imports
    private static readonly string[] ItemFiles = ["Weapons.bin", "Armor.bin", "Misc.bin"];

    private readonly ILogger? _log;

    private Diablo2MpqLoader(string basePath, ILogger? log)
    {
        _log = log;
        MpqPatch = Mpq.Mpq.Load(Path.Join(basePath, "patch_d2.mpq"));
        MpqData = Mpq.Mpq.Load(Path.Join(basePath, "d2data.mpq"));
        MpqExp = Mpq.Mpq.Load(Path.Join(basePath, "d2exp.mpq"));
    }

    public Mpq.Mpq MpqPatch { get; }
    public Mpq.Mpq MpqExp { get; }
    public Mpq.Mpq MpqData { get; }

    /// <summary>
    /// Open <c>patch_d2.mpq</c>, <c>d2data.mpq</c> and <c>d2exp.mpq</c> from <paramref name="basePath"/> and load
    /// the lang, level, object, monster and item tables into <paramref name="data"/>.
    /// </summary>
    /// <exception cref="IOException">An archive is missing or unreadable.</exception>
    public static async Task<Diablo2MpqData> LoadAsync(string basePath, ILogger? log = null, Diablo2MpqData? data = null)
    {
        await using var loader = await OpenAsync(basePath, log).ConfigureAwait(false);
        return await loader.LoadDataAsync(basePath, data ?? new Diablo2MpqData()).ConfigureAwait(false);
    }

    /// <summary>Open the three archives and read their headers.</summary>
    public static async Task<Diablo2MpqLoader> OpenAsync(string basePath, ILogger? log = null)
    {
        log?.LogInformation("Mpq:Load {Mpq}", basePath);
        var loader = new Diablo2MpqLoader(basePath, log);

        var startTime = Stopwatch.GetTimestamp();
        try
        {
            await Task.WhenAll(loader.MpqData.Header, loader.MpqExp.Header, loader.MpqPatch.Header).ConfigureAwait(false);
        }
        catch
        {
            await loader.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        log?.LogDebug("Mpq:Init {Duration}", Stopwatch.GetElapsedTime(startTime).TotalMilliseconds);
        return loader;
    }

    private async Task<Diablo2MpqData> LoadDataAsync(string basePath, Diablo2MpqData data)
    {
        data.BasePath = basePath;

        await InitLangAsync(data).ConfigureAwait(false);
        await InitLevelsAsync(data).ConfigureAwait(false);
        await InitObjectsAsync(data).ConfigureAwait(false);
        await InitMonstersAsync(data).ConfigureAwait(false);
        await InitItemsAsync(data).ConfigureAwait(false);

        return data;
    }

    /// <summary>Find the archive containing a path: patch first, then expansion, then classic data.</summary>
    public async Task<Mpq.Mpq?> GetMpqAsync(string path)
    {
        if (await MpqPatch.HasAsync(path).ConfigureAwait(false)) return MpqPatch;
        if (await MpqExp.HasAsync(path).ConfigureAwait(false)) return MpqExp;
        if (await MpqData.HasAsync(path).ConfigureAwait(false)) return MpqData;
        return null;
    }

    /// <summary>Find and load the raw bytes for a path.</summary>
    public async Task<byte[]?> GetAsync(string path)
    {
        var mpq = await GetMpqAsync(path).ConfigureAwait(false);
        if (mpq == null) return null;
        return await mpq.ExtractAsync(path).ConfigureAwait(false);
    }

    /// <summary>Extract and parse a file, returning null (and logging) on any failure.</summary>
    public async Task<(T Value, double Duration)?> ReadAndParseAsync<T>(string key, Func<byte[], T> parser)
    {
        try
        {
            var startTime = Stopwatch.GetTimestamp();
            var bytes = await GetAsync(key).ConfigureAwait(false);
            if (bytes == null) return null;
            var value = parser(bytes);
            return (value, Stopwatch.GetElapsedTime(startTime).TotalMilliseconds);
        }
        catch (Exception e)
        {
            // ignore (upstream logs the error to the console)
            _log?.LogDebug(e, "Mpq:Parse:Error {File}", key);
        }
        return null;
    }

    private async Task InitLevelsAsync(Diablo2MpqData data)
    {
        const string file = "data/global/excel/levels.bin";
        var res = await ReadAndParseAsync(file, LevelReader.Parse).ConfigureAwait(false);
        if (res == null)
        {
            _log?.LogWarning("Mpq:Load:Failed {File}", file);
            return;
        }
        foreach (var r in res.Value.Value)
            data.Levels[r.Id] = new Diablo2MpqLevel(r.Id, (Act)r.Act, r.Name, r.IsRaining, r.IsTeleportEnabled);

        _log?.LogDebug("Mpq:Load:Levels {File} {Records} {Duration}", "levels.bin", data.Levels.Count, res.Value.Duration);
    }

    private async Task InitObjectsAsync(Diablo2MpqData data)
    {
        const string file = "data/global/excel/objects.bin";
        var res = await ReadAndParseAsync(file, ObjectReader.Parse).ConfigureAwait(false);
        if (res == null)
        {
            _log?.LogWarning("Mpq:Load:Failed {File}", file);
            return;
        }
        var objects = res.Value.Value;
        for (var i = 0; i < objects.Count; i++) data.Objects[i] = new Diablo2MpqObject(objects[i].Name);
        _log?.LogDebug("Mpq:Load:Objects {File} {Records} {Duration}", "objects.bin", data.Objects.Count, res.Value.Duration);
    }

    /// <summary>Load in the lang files.</summary>
    private async Task InitLangAsync(Diablo2MpqData data)
    {
        foreach (var langFile in LangFiles)
        {
            var res = await ReadAndParseAsync($"data/local/LNG/ENG/{langFile}", LangReader.Parse).ConfigureAwait(false);
            if (res == null)
            {
                _log?.LogWarning("Mpq:Load:Failed {File}", langFile);
                continue;
            }
            var langItems = res.Value.Value.HashTable;
            var tbl = langFile switch
            {
                "string.tbl" => data.Lang.Classic,
                "patchstring.tbl" => data.Lang.Patch,
                "expansionstring.tbl" => data.Lang.Expansion,
                _ => throw new InvalidOperationException("Invalid lang file " + langFile),
            };
            foreach (var itm in langItems) tbl.Add(itm.Key, itm.Index, itm.Value);

            _log?.LogDebug("Mpq:Load:Language {File} {Records} {Duration}", langFile, langItems.Count, res.Value.Duration);
        }
    }

    /// <summary>Load in the monster files.</summary>
    private async Task InitMonstersAsync(Diablo2MpqData data)
    {
        var res = await ReadAndParseAsync("data/global/excel/monstats.bin", MonsterReader.Parse).ConfigureAwait(false);
        if (res == null)
        {
            _log?.LogWarning("Mpq:Load:Failed {File}", "MonStats.bin");
            return;
        }

        var monsters = res.Value.Value;
        foreach (var mon in monsters) data.Monsters.Add(mon.Id, new Diablo2MpqMonster(mon.Id, mon.BaseId, mon.NameLangId));
        _log?.LogDebug("Mpq:Load:Monster {File} {Records} {Duration}", "MonStats.bin", monsters.Count, res.Value.Duration);

        var res2 = await ReadAndParseAsync("data/global/excel/MonStats2.bin", MonsterReader2.Parse).ConfigureAwait(false);
        if (res2 == null)
        {
            _log?.LogWarning("Mpq:Load:Failed {File}", "MonStats2.bin");
            return;
        }

        var monsters2 = res2.Value.Value;
        foreach (var mon in monsters2) data.Monsters.AddState((int)mon.Id, mon.State);
        _log?.LogDebug("Mpq:Load:Monster {File} {Records} {Duration}", "MonStats2.bin", monsters2.Count, res2.Value.Duration);
    }

    private async Task InitItemsAsync(Diablo2MpqData data)
    {
        foreach (var itemFile in ItemFiles)
        {
            var res = await ReadAndParseAsync($"data/global/excel/{itemFile}", ItemReader.Parse).ConfigureAwait(false);
            if (res == null)
            {
                _log?.LogWarning("Mpq:Load:Failed {File}", itemFile);
                continue;
            }
            var items = res.Value.Value;
            foreach (var item in items) data.Items.Add(new Diablo2Item(item.Code, item.NameId));
            _log?.LogDebug("Mpq:Load:Items {File} {Records} {Duration}", itemFile, items.Count, res.Value.Duration);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await MpqPatch.DisposeAsync().ConfigureAwait(false);
        await MpqExp.DisposeAsync().ConfigureAwait(false);
        await MpqData.DisposeAsync().ConfigureAwait(false);
    }
}

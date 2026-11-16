using D2Dotnet.Maps.Formats;

namespace D2Dotnet.Tests.Maps;

/// <summary>The production raw .ds1/.dt1 parsers against libd2's baked blobs: each raw fixture of
/// libd2 is parsed, re-packed with the bake tools' packRecord logic, and must reproduce the baked
/// record of that file byte for byte.</summary>
public sealed class RawFormatTests
{
    public static IEnumerable<object[]> Ds1Fixtures() => Fixtures(".ds1");
    public static IEnumerable<object[]> Dt1Fixtures() => Fixtures(".dt1");

    /// <summary>The fixture names; when the fixtures are missing a single placeholder row, so the
    /// test runs and fails with the fixtures' message instead of vanishing from discovery.</summary>
    private static IEnumerable<object[]> Fixtures(string ext)
    {
        IReadOnlyList<string> files;
        try
        {
            files = LibD2Fixtures.RawMapFixtures(ext);
        }
        catch (Exception)
        {
            files = new[] { "libd2-fixtures-missing" + ext };
        }
        return files.Select(f => new object[] { Path.GetFileName(f) });
    }

    private static string FixturePath(string name, string ext) =>
        LibD2Fixtures.RawMapFixtures(ext).Single(f => Path.GetFileName(f) == name);

    /// <summary>The blob keys a fixture file name can stand for: libd2 names its fixtures after
    /// the tiles path with '/' as '_' (Act1_Town_TownN1.ds1 = act1/town/townn1.ds1); a bare name
    /// (TownETrans.ds1) matches any key with that file name.</summary>
    private static List<string> CandidateKeys(IEnumerable<string> keys, string fileName)
    {
        string lower = fileName.ToLowerInvariant();
        var exact = keys.Where(k => k.Replace('/', '_') == lower).ToList();
        if (exact.Count > 0) return exact;
        return keys.Where(k => k.EndsWith("/" + lower, StringComparison.Ordinal)).ToList();
    }

    /// <summary>Raw fixtures of libd2 that are a different revision of the game file than the one
    /// its DS1 blob was baked from. Their bytes, parsed by libd2's algorithm (ds1.zig), give
    /// other cells/objects than the baked record: TownETrans.ds1 (v13) holds 0 objects where the
    /// baked act1/town/townetrans.ds1 holds 2; LutW and townWest differ in their cell layers
    /// (and townWest in its objects). The three pass the structural checks in
    /// <see cref="FormatUnitTests"/>.</summary>
    private static readonly HashSet<string> OtherRevision = new(StringComparer.Ordinal)
    {
        "Act2_Town_LutW.ds1",
        "Expansion_Town_townWest.ds1",
        "TownETrans.ds1",
    };

    [Theory]
    [MemberData(nameof(Ds1Fixtures))]
    public void RawDs1ParseMatchesBakedRecord(string name)
    {
        var raw = Ds1File.Parse(File.ReadAllBytes(FixturePath(name, ".ds1")));
        var packed = BakedBlob.PackDs1(raw);
        var recs = LibD2Fixtures.Source.Ds1Records;
        var keys = CandidateKeys(recs.Keys, name);
        Assert.True(keys.Count > 0, $"no baked DS1 record for {name}");
        var match = keys.FirstOrDefault(k => recs[k].AsSpan().SequenceEqual(packed));
        if (OtherRevision.Contains(name))
        {
            // Another revision of the file than the one the blob was baked from: the header and
            // layer layout agree, the cell contents / objects do not (libd2's own parser yields
            // the same as ours on these bytes; see OtherRevision).
            Assert.True(match == null, $"{name} now matches its baked record; drop it from OtherRevision");
            var other = BakedBlob.UnpackDs1(recs[keys.Single()]);
            Assert.Equal((raw.Width, raw.Height, raw.Act, raw.ActId), (other.Width, other.Height, other.Act, other.ActId));
            Assert.Equal(raw.WallLayers.Length, other.WallLayers.Length);
            Assert.Equal(raw.FloorLayers.Length, other.FloorLayers.Length);
            Assert.Equal(raw.SubstTags == null, other.SubstTags == null);
            return;
        }
        Assert.True(match != null, $"{name}: raw parse re-packs to {packed.Length} bytes, matching none of the baked records " +
            $"[{string.Join(", ", keys.Select(k => $"{k}:{recs[k].Length} ({Ds1Delta(raw, BakedBlob.UnpackDs1(recs[k]))})"))}]");

        // The unpacked baked record and the raw parse agree field for field too.
        var baked = BakedBlob.UnpackDs1(recs[match!]);
        Assert.Equal(raw.Width, baked.Width);
        Assert.Equal(raw.Height, baked.Height);
        Assert.Equal(raw.Act, baked.Act);
        Assert.Equal(raw.ActId, baked.ActId);
        Assert.Equal(raw.Objects, baked.Objects);
        Assert.Equal(raw.SubstGroups, baked.SubstGroups);
    }

    /// <summary>Which parts of two parsed DS1s differ (failure diagnostics).</summary>
    private static string Ds1Delta(Ds1File a, Ds1File b)
    {
        var d = new List<string>();
        if ((a.Width, a.Height, a.Act, a.ActId) != (b.Width, b.Height, b.Act, b.ActId))
            d.Add($"header {a.Width}x{a.Height} act {a.Act}/{a.ActId} vs {b.Width}x{b.Height} act {b.Act}/{b.ActId}");
        if (a.WallLayers.Length != b.WallLayers.Length) d.Add($"wall layers {a.WallLayers.Length} vs {b.WallLayers.Length}");
        for (int i = 0; i < Math.Min(a.WallLayers.Length, b.WallLayers.Length); i++)
        {
            if (!a.WallLayers[i].Wall.SequenceEqual(b.WallLayers[i].Wall)) d.Add($"wall[{i}]");
            if (!a.WallLayers[i].Orient.SequenceEqual(b.WallLayers[i].Orient)) d.Add($"orient[{i}]");
        }
        if (a.FloorLayers.Length != b.FloorLayers.Length) d.Add($"floor layers {a.FloorLayers.Length} vs {b.FloorLayers.Length}");
        for (int i = 0; i < Math.Min(a.FloorLayers.Length, b.FloorLayers.Length); i++)
            if (!a.FloorLayers[i].SequenceEqual(b.FloorLayers[i])) d.Add($"floor[{i}]");
        if (!a.Shadow.SequenceEqual(b.Shadow)) d.Add("shadow");
        if ((a.SubstTags == null) != (b.SubstTags == null) || (a.SubstTags != null && !a.SubstTags.SequenceEqual(b.SubstTags!))) d.Add("tags");
        if (!a.SubstGroups.SequenceEqual(b.SubstGroups)) d.Add($"groups {a.SubstGroups.Length} vs {b.SubstGroups.Length}");
        if (!a.Objects.SequenceEqual(b.Objects))
        {
            int k = 0;
            while (k < a.Objects.Length && k < b.Objects.Length && a.Objects[k] == b.Objects[k]) k++;
            d.Add($"objects {a.Objects.Length} vs {b.Objects.Length}, first diff #{k}: " +
                  $"{(k < a.Objects.Length ? a.Objects[k].ToString() : "-")} vs {(k < b.Objects.Length ? b.Objects[k].ToString() : "-")}");
        }
        return d.Count == 0 ? "equal" : string.Join("; ", d);
    }

    [Theory]
    [MemberData(nameof(Dt1Fixtures))]
    public void RawDt1ParseMatchesBakedRecord(string name)
    {
        var raw = Dt1File.Parse(File.ReadAllBytes(FixturePath(name, ".dt1")));
        var packed = BakedBlob.PackDt1(raw);
        var recs = LibD2Fixtures.Source.Dt1Records;
        var keys = CandidateKeys(recs.Keys, name);
        Assert.True(keys.Count > 0, $"no baked DT1 record for {name}");
        Assert.True(keys.Any(k => recs[k].AsSpan().SequenceEqual(packed)),
            $"{name}: raw parse re-packs to {packed.Length} bytes, matching none of the baked records [{string.Join(", ", keys.Select(k => $"{k}:{recs[k].Length}"))}]");
    }

    [Fact]
    public void Dt1LookupIsReverseRecordOrder()
    {
        var d = new Dt1File
        {
            Tiles = new[]
            {
                new Dt1Tile { Orientation = 0, Main = 1, Sub = 2, Rarity = 1 },
                new Dt1Tile { Orientation = 3, Main = 1, Sub = 2, Rarity = 1 },
                new Dt1Tile { Orientation = 0, Main = 1, Sub = 2, Rarity = 5 },
            },
        };
        Assert.Equal(new[] { 2, 0 }, d.Lookup(0, 1, 2));
        Assert.Equal(new[] { 1 }, d.Lookup(3, 1, 2));
        Assert.Empty(d.Lookup(9, 9, 9));
    }
}

using System.Text;
using D2Dotnet.Map.GameData.Casc;
using D2Dotnet.Map.GameData.Tests.Writer;

namespace D2Dotnet.Map.GameData.Tests;

public class CascFileProviderTests : IDisposable
{
    private const string Levels = "Name\tId\tLevelName\nAct 1 - Town\t1\tRogue Encampment\n";
    private const string Strings = """[{"id":1,"Key":"Rogue Encampment","enUS":"Rogue Encampment"}]""";

    private static readonly byte[] Ds1 = RandomBytes(5000, seed: 1);
    private static readonly byte[] Dt1 = RandomBytes(70_000, seed: 2);
    private static readonly byte[] SpanA = Encoding.ASCII.GetBytes("first span|");
    private static readonly byte[] SpanB = Encoding.ASCII.GetBytes("second span");

    private readonly TempDirectory _dir = new();

    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        // Half random, half repetitive, so zlib frames are neither trivial nor incompressible.
        for (var i = 0; i < length; i += 2)
            bytes[i] = (byte)(i % 64);
        return bytes;
    }

    public void Dispose() => _dir.Dispose();

    private static CascStorageWriter StandardWriter() => new CascStorageWriter()
        .Add("data/global/excel/levels.txt", Levels)
        .Add("data/global/excel/LvlPrest.txt", "Name\tDef\n", BlteLayout.HeaderlessN)
        .Add("data/global/tiles/act1/town/townn1.ds1", Ds1, BlteLayout.Chunked(Ds1.Length, 1024, 'Z', 'N'))
        .Add("data/global/tiles/act1/town/floor.dt1", Dt1, BlteLayout.Chunked(Dt1.Length, 16 * 1024, 'Z'))
        .Add("data/local/lng/strings/levels.json", Strings, BlteLayout.Single('N'))
        .Add("data/global/excel/empty.txt", "", BlteLayout.HeaderlessN)
        .AddMultiSpan("data/global/big.bin", [SpanA, SpanB])
        .AddNotInstalled("data/local/lng/strings/dede-only.json", "{}")
        .Add("data/local/lng/strings/levels.json", "[\"from another mount\"]", mount: "enUS")
        .Add("data/local/lng/strings/only-in-enus.json", "[]", mount: "enUS")
        .Add("D2R.exe.manifest", "<root file>", mount: null);

    private WrittenStorage WriteStandard(Action<CascStorageWriter>? configure = null)
    {
        var writer = StandardWriter();
        configure?.Invoke(writer);
        return writer.Write(_dir.Path);
    }

    private CascFileProvider OpenStandard(Action<CascStorageWriter>? configure = null)
    {
        WriteStandard(configure);
        return CascFileProvider.Open(_dir.Path);
    }

    [Fact]
    public void WriterProducesTheD2RLayout()
    {
        var storage = WriteStandard();
        Assert.True(File.Exists(Path.Combine(_dir.Path, ".build.info")));
        Assert.True(File.Exists(storage.BuildConfigPath));
        Assert.True(Directory.GetFiles(storage.DataDirectory, "data.*").Length > 1, "files should span several archives");
        Assert.All(storage.IndexFiles, f => Assert.Matches("^[0-9a-f]{10}\\.idx$", Path.GetFileName(f)));
        var config = File.ReadAllText(storage.BuildConfigPath);
        Assert.Contains("vfs-root = ", config);
        Assert.Contains("vfs-1 = ", config);
        Assert.Contains("encoding = ", config);
    }

    [Fact]
    public void ReadsFilesOfEveryEncoding()
    {
        using var casc = OpenStandard();
        Assert.Equal(Levels, Encoding.UTF8.GetString(casc.ReadFile("data/global/excel/levels.txt")!));
        Assert.Equal("Name\tDef\n", Encoding.UTF8.GetString(casc.ReadFile("data/global/excel/lvlprest.txt")!));
        Assert.Equal(Ds1, casc.ReadFile("data/global/tiles/act1/town/townn1.ds1"));
        Assert.Equal(Dt1, casc.ReadFile("data/global/tiles/act1/town/floor.dt1"));
        Assert.Equal(Strings, Encoding.UTF8.GetString(casc.ReadFile("data/local/lng/strings/levels.json")!));
        Assert.Empty(casc.ReadFile("data/global/excel/empty.txt")!);
    }

    [Fact]
    public void MultiSpanFilesAreConcatenated()
    {
        using var casc = OpenStandard();
        Assert.Equal([.. SpanA, .. SpanB], casc.ReadFile("data/global/big.bin")!);
    }

    [Theory]
    [InlineData("data/global/excel/levels.txt")]
    [InlineData("DATA/Global/Excel/Levels.TXT")]
    [InlineData(@"data\global\excel\levels.txt")]
    [InlineData(@"\Data\Global\Excel\LEVELS.txt")]
    [InlineData("/data/global/excel/levels.txt")]
    public void PathsAreCaseAndSeparatorInsensitive(string path)
    {
        using var casc = OpenStandard();
        Assert.True(casc.Exists(path));
        Assert.Equal(Levels, Encoding.UTF8.GetString(casc.ReadFile(path)!));
    }

    [Fact]
    public void ManifestCaseIsNormalized()
    {
        using var casc = OpenStandard();
        // Stored as "LvlPrest.txt" in the manifest.
        Assert.True(casc.Exists("data/global/excel/lvlprest.txt"));
        Assert.Contains("data/global/excel/lvlprest.txt", casc.EnumerateFiles("data/global/excel"));
    }

    [Theory]
    [InlineData("data/global/excel/missing.txt")]
    [InlineData("data/global/excel")]
    [InlineData("data:data/global/excel/levels.txt")]
    [InlineData("")]
    public void MissingFiles(string path)
    {
        using var casc = OpenStandard();
        Assert.False(casc.Exists(path));
        Assert.Null(casc.ReadFile(path));
    }

    [Fact]
    public void FilesNotInstalledLocallyAreHidden()
    {
        using var casc = OpenStandard();
        Assert.False(casc.Exists("data/local/lng/strings/dede-only.json"));
        Assert.Null(casc.ReadFile("data/local/lng/strings/dede-only.json"));
        Assert.DoesNotContain("data/local/lng/strings/dede-only.json", casc.EnumerateFiles(""));
    }

    [Fact]
    public void EnumeratesRecursivelyAndSorted()
    {
        using var casc = OpenStandard();
        Assert.Equal(
            ["data/global/excel/empty.txt", "data/global/excel/levels.txt", "data/global/excel/lvlprest.txt"],
            casc.EnumerateFiles("data/global/excel"));
        Assert.Equal(
            ["data/global/tiles/act1/town/floor.dt1", "data/global/tiles/act1/town/townn1.ds1"],
            casc.EnumerateFiles(@"Data\Global\Tiles\"));
        Assert.Equal(
            ["data/local/lng/strings/levels.json", "data/local/lng/strings/only-in-enus.json"],
            casc.EnumerateFiles("data/local"));
        Assert.Empty(casc.EnumerateFiles("data/global/excel/levels.txt"));
        Assert.Empty(casc.EnumerateFiles("data/glob")); // a prefix of a folder name is not a folder

        var all = casc.EnumerateFiles("").ToList();
        Assert.Equal(all.Order(StringComparer.Ordinal), all);
        Assert.Equal(all, casc.EnumerateFiles("/"));
        Assert.Equal(casc.Count, all.Count);
        Assert.Contains("d2r.exe.manifest", all);
        Assert.All(all, p => Assert.Equal(GamePath.Normalize(p), p));
    }

    [Fact]
    public void DataMountWinsOverOtherMounts()
    {
        using var casc = OpenStandard();
        Assert.Equal(Strings, Encoding.UTF8.GetString(casc.ReadFile("data/local/lng/strings/levels.json")!));
        Assert.Equal("[]", Encoding.UTF8.GetString(casc.ReadFile("data/local/lng/strings/only-in-enus.json")!));
    }

    [Fact]
    public void ExposesCascLibStyleNames()
    {
        using var casc = OpenStandard();
        Assert.Contains("data:data/global/excel/levels.txt", casc.VfsNames);
        Assert.Contains("data:data/global/excel/lvlprest.txt", casc.VfsNames);
        Assert.Contains("enus:data/local/lng/strings/levels.json", casc.VfsNames);
        Assert.Contains("d2r.exe.manifest", casc.VfsNames);
        Assert.DoesNotContain("data", casc.VfsNames); // the mount point itself is not a file

        Assert.Equal(Levels, Encoding.UTF8.GetString(casc.ReadVfsFile(@"data:data\global\excel\Levels.txt")!));
        Assert.Equal("[\"from another mount\"]", Encoding.UTF8.GetString(casc.ReadVfsFile("enUS:data/local/lng/strings/levels.json")!));
        Assert.Null(casc.ReadVfsFile("data:data/nothing"));
    }

    [Theory]
    [InlineData("data:data/global/excel/levels.txt", "data/global/excel/levels.txt")]
    [InlineData(@"data:data\global\excel\Levels.TXT", "data/global/excel/levels.txt")]
    [InlineData("enUS:data/local/lng/strings/levels.json", "data/local/lng/strings/levels.json")]
    [InlineData("a:b:c/d.txt", "c/d.txt")]
    [InlineData("readme.txt", "readme.txt")]
    public void MapsVfsNamesToGamePaths(string vfsName, string expected) =>
        Assert.Equal(expected, CascFileProvider.ToGamePath(vfsName));

    [Fact]
    public void ReportsBuildInfo()
    {
        using var casc = OpenStandard();
        Assert.Equal("osi", casc.Product);
        Assert.Equal("1.6.80273", casc.Version);
        Assert.Contains("osi", casc.ToString());
    }

    [Fact]
    public void ManifestsCanBeResolvedThroughEncoding()
    {
        // vfs-root and vfs-1 name only their CKey: the reader must look the EKey up in ENCODING.
        using var casc = OpenStandard(w => w.ManifestEKeysInBuildConfig = false);
        Assert.Equal(Levels, Encoding.UTF8.GetString(casc.ReadFile("data/global/excel/levels.txt")!));
        Assert.Equal(Dt1, casc.ReadFile("data/global/tiles/act1/town/floor.dt1"));
    }

    [Fact]
    public void ProductSelection()
    {
        WriteStandard();
        using (var casc = CascFileProvider.Open(_dir.Path, "OSI"))
            Assert.Equal("osi", casc.Product);
        // The "other" row is inactive.
        Assert.Throws<CascFormatException>(() => CascFileProvider.Open(_dir.Path, "other"));
    }

    [Fact]
    public void CaseInsensitiveFolderNames()
    {
        WriteStandard();
        Directory.Move(Path.Combine(_dir.Path, "Data"), Path.Combine(_dir.Path, "tmp"));
        Directory.Move(Path.Combine(_dir.Path, "tmp"), Path.Combine(_dir.Path, "DATA"));
        using var casc = CascFileProvider.Open(_dir.Path);
        Assert.True(casc.Exists("data/global/excel/levels.txt"));
    }

    [Fact]
    public void ConcurrentReads()
    {
        using var casc = OpenStandard();
        Parallel.For(0, 64, i =>
        {
            Assert.Equal(Dt1, casc.ReadFile("data/global/tiles/act1/town/floor.dt1"));
            Assert.Equal(Ds1, casc.ReadFile("data/global/tiles/act1/town/townn1.ds1"));
        });
    }

    [Fact]
    public void ReadAfterDispose_Throws()
    {
        var casc = OpenStandard();
        casc.Dispose();
        Assert.Throws<ObjectDisposedException>(() => casc.ReadFile("data/global/excel/levels.txt"));
        casc.Dispose();
    }

    [Fact]
    public void ImplementsTheInterface()
    {
        using var casc = OpenStandard();
        IGameFileProvider provider = casc;
        Assert.True(provider.Exists("data/global/excel/levels.txt"));
    }

    // ---- corrupt and unsupported storages ----

    private static void FlipByte(string file, long offset)
    {
        using var s = new FileStream(file, FileMode.Open, FileAccess.ReadWrite);
        s.Position = offset;
        var b = s.ReadByte();
        s.Position = offset;
        s.WriteByte((byte)(b ^ 0x5A));
    }

    [Fact]
    public void CorruptFrame_ThrowsOnRead()
    {
        var storage = WriteStandard();
        var record = storage.Records["data:data/global/tiles/act1/town/floor.dt1"];
        FlipByte(record.ArchivePath, record.FirstFrameOffset + 20);

        using var casc = CascFileProvider.Open(_dir.Path);
        var e = Assert.Throws<CascFormatException>(() => casc.ReadFile("data/global/tiles/act1/town/floor.dt1"));
        Assert.Contains("floor.dt1", e.Message);
        Assert.Contains("MD5", e.Message);
        // Other files are unaffected.
        Assert.Equal(Levels, Encoding.UTF8.GetString(casc.ReadFile("data/global/excel/levels.txt")!));
    }

    [Fact]
    public void CorruptZlibWithoutFrameTable_ThrowsOnRead()
    {
        var storage = WriteStandard();
        var record = storage.Records["data:data/global/excel/levels.txt"];
        FlipByte(record.ArchivePath, record.FirstFrameOffset + 3);

        using var casc = CascFileProvider.Open(_dir.Path);
        var e = Assert.Throws<CascFormatException>(() => casc.ReadFile("data/global/excel/levels.txt"));
        Assert.Contains("zlib", e.Message);
    }

    [Fact]
    public void MissingBlteSignature_ThrowsOnRead()
    {
        var storage = WriteStandard();
        var record = storage.Records["data:data/global/excel/levels.txt"];
        FlipByte(record.ArchivePath, record.RecordOffset + CascStorage.RecordHeaderSize);

        using var casc = CascFileProvider.Open(_dir.Path);
        var e = Assert.Throws<CascFormatException>(() => casc.ReadFile("data/global/excel/levels.txt"));
        Assert.Contains("BLTE", e.Message);
    }

    [Fact]
    public void TruncatedArchive_ThrowsOnRead()
    {
        var storage = WriteStandard();
        var record = storage.Records["data:data/global/tiles/act1/town/floor.dt1"];
        using (var s = new FileStream(record.ArchivePath, FileMode.Open, FileAccess.ReadWrite))
            s.SetLength(record.RecordOffset + 100);

        using var casc = CascFileProvider.Open(_dir.Path);
        var e = Assert.Throws<CascFormatException>(() => casc.ReadFile("data/global/tiles/act1/town/floor.dt1"));
        Assert.Contains("truncated", e.Message);
    }

    [Fact]
    public void EncryptedFile_ThrowsWithTheKeyName()
    {
        WriteStandard(w => w.Add("data/global/secret.bin", "classified", BlteLayout.Single('E')));
        using var casc = CascFileProvider.Open(_dir.Path);
        Assert.True(casc.Exists("data/global/secret.bin"));
        var e = Assert.Throws<CascEncryptedException>(() => casc.ReadFile("data/global/secret.bin"));
        Assert.Equal(0xFEEDFACE00C0FFEEUL, e.KeyName);
    }

    [Fact]
    public void CorruptIndex_FailsToOpen()
    {
        var storage = WriteStandard();
        FlipByte(storage.IndexFiles[0], 0x2C);
        Assert.Throws<CascFormatException>(() => CascFileProvider.Open(_dir.Path));
    }

    [Fact]
    public void RootManifestNotMatchingItsContentKey_FailsToOpen()
    {
        var storage = WriteStandard();
        var config = File.ReadAllText(storage.BuildConfigPath);
        var rootCKey = Convert.ToHexStringLower(storage.RootCKey);
        File.WriteAllText(storage.BuildConfigPath, config.Replace($"vfs-root = {rootCKey}", $"vfs-root = {new string('0', 32)}"));

        var e = Assert.Throws<CascFormatException>(() => CascFileProvider.Open(_dir.Path));
        Assert.Contains("content key", e.Message);
    }

    [Fact]
    public void MissingBuildConfig_FailsToOpen()
    {
        var storage = WriteStandard();
        File.Delete(storage.BuildConfigPath);
        Assert.Throws<FileNotFoundException>(() => CascFileProvider.Open(_dir.Path));
    }

    [Fact]
    public void MissingBuildInfo_FailsToOpen()
    {
        Assert.Throws<FileNotFoundException>(() => CascFileProvider.Open(_dir.Path));
    }

    [Fact]
    public void MissingArchive_ThrowsOnRead()
    {
        var storage = WriteStandard();
        var record = storage.Records["data:data/global/tiles/act1/town/floor.dt1"];
        Assert.NotEqual(storage.Records["vfs-root"].ArchivePath, record.ArchivePath);
        Assert.NotEqual(storage.Records["vfs-1"].ArchivePath, record.ArchivePath);
        File.Delete(record.ArchivePath);

        using var casc = CascFileProvider.Open(_dir.Path);
        Assert.Throws<FileNotFoundException>(() => casc.ReadFile("data/global/tiles/act1/town/floor.dt1"));
    }
}

using System.Text;
using System.Text.Json;
using D2Dotnet.Map.Generator.Cli;
using D2Dotnet.Map.Generator.Tests.Fakes;

namespace D2Dotnet.Map.Generator.Tests;

/// <summary>
/// Differential test against the original program: Golden/*.out are the stdout bytes (and
/// *.code the exit codes) of the upstream C sources, compiled unmodified (apart from the two
/// magic-jump asm bodies) against the fake game in CReference/fake_d2.cpp, which builds the
/// same world as <see cref="TestWorld"/>. Regenerate with CReference/generate_golden.py.
/// </summary>
public class GoldenOutputTests
{
    private sealed record Scenario(string Name, string[] Files, string[] Args, string Stdin, bool NoRegistry);

    private static string Dir(params string[] parts) => System.IO.Path.Combine([AppContext.BaseDirectory, .. parts]);

    private static List<Scenario> Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Dir("CReference", "scenarios.json")));
        return doc.RootElement.EnumerateArray().Select(e => new Scenario(
            e.GetProperty("name").GetString()!,
            e.GetProperty("files").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            e.GetProperty("args").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            e.GetProperty("stdin").GetString()!,
            e.TryGetProperty("noRegistry", out var r) && r.GetBoolean())).ToList();
    }

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var s in Load()) data.Add(s.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void MatchesUpstreamCOutput(string name)
    {
        var sc = Load().Single(s => s.Name == name);
        var h = new Harness(sc.Files);
        _ = new TestWorld(h.Game);
        h.Game.RegistryKeyExists = !sc.NoRegistry;

        int code;
        try
        {
            code = new MapGeneratorProgram(h.Context).Main(sc.Args, new MemoryStream(Encoding.Latin1.GetBytes(sc.Stdin)));
        }
        catch (FakeExit e)
        {
            code = e.Code;
        }

        // The reference build replaces offset.c (DefineOffsets) with the fake, so its
        // "Dll:Init" trace lines (map/offset.c:46, only printed with -v) do not exist there.
        var actual = string.Concat(h.Out.Split('\n').Select((l, i) => (l, i)).Where(x => !x.l.Contains("\"source\":\"map/offset.c:46\"", StringComparison.Ordinal))
            .Select(x => x.l + "\n"));
        actual = actual[..^1]; // Split/Concat adds one trailing newline

        var expected = Encoding.Latin1.GetString(File.ReadAllBytes(Dir("Golden", name + ".out")));
        Assert.Equal(expected, actual);
        Assert.Equal(int.Parse(File.ReadAllText(Dir("Golden", name + ".code")).Trim(), System.Globalization.CultureInfo.InvariantCulture), code);
    }
}

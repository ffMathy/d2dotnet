using System.Xml.Linq;

namespace D2Dotnet.Tests;

/// <summary>The library's package carries the licences of the code it ports (MIT requires the notice with the code).</summary>
public class PackageTests
{
    private static string RepoRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "D2Dotnet.slnx")))
                    return dir.FullName;
            }

            Assert.Fail($"D2Dotnet.slnx not found above {AppContext.BaseDirectory}");
            return "";
        }
    }

    private static string LibraryDirectory => Path.Combine(RepoRoot, "src", "D2Dotnet");

    private static string Relative(string path) => Path.GetRelativePath(LibraryDirectory, path).Replace('\\', '/');

    [Fact]
    public void EveryThirdPartyLicence_AndTheNotices_ArePacked()
    {
        var project = XDocument.Load(Path.Combine(LibraryDirectory, "D2Dotnet.csproj"));
        var packed = project.Descendants("None")
            .Where(e => (string?)e.Attribute("Pack") == "true" && (string?)e.Attribute("PackagePath") == "")
            .Select(e => ((string?)e.Attribute("Include") ?? "").Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        var licences = Directory.EnumerateFiles(LibraryDirectory, "LICENSE-*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Files/Casc/LICENSE-CascLib", "Maps/LICENSE-libd2", "Memory/LICENSE-d2go"], licences);
        foreach (var licence in licences)
            Assert.Contains(licence, packed);
        Assert.Contains("../../THIRD-PARTY-NOTICES.md", packed);
    }

    [Theory]
    [InlineData("Files/Casc/LICENSE-CascLib", "Copyright (c) 2014 Ladislav Zezula")]
    [InlineData("Maps/LICENSE-libd2", "Copyright (c) 2026 jaenster")]
    [InlineData("Memory/LICENSE-d2go", "Copyright (c) 2023 Héctor Giménez")]
    public void TheNotices_NameEachLicence(string licence, string copyright)
    {
        var notices = File.ReadAllText(Path.Combine(RepoRoot, "THIRD-PARTY-NOTICES.md"));
        Assert.Contains("src/D2Dotnet/" + licence, notices, StringComparison.Ordinal);
        Assert.Contains(copyright, notices, StringComparison.Ordinal);
        Assert.Contains(copyright, File.ReadAllText(Path.Combine(LibraryDirectory, licence)), StringComparison.Ordinal);
    }
}

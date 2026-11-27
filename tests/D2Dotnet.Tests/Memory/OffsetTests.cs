using D2Dotnet.Memory;

namespace D2Dotnet.Tests.Memory;

/// <summary>The pattern scan and offset decoding of offset.go.</summary>
public sealed class OffsetTests
{
    // Each pattern and its operand, built byte by byte independently of GameImage.
    private static byte[] HandBuiltImage(byte[]? gameDataOperand = null)
    {
        var image = new byte[0x400];
        void Put(int at, byte[] bytes) => bytes.CopyTo(image, at);
        Put(0x10, [0x44, 0x88, 0x25, .. gameDataOperand ?? [0x78, 0x56, 0x34, 0x12], 0x66, 0x44, 0x89, 0x25, 0xAA, 0xBB, 0xCC, 0xDD]);
        Put(0x40, [0x48, 0x03, 0xC7, 0x49, 0x8B, 0x8C, 0xC6, 0x10, 0x32, 0x54, 0x01]);
        Put(0x60, [0x40, 0x84, 0xed, 0x0f, 0x94, 0x05, 0x00, 0x01, 0x00, 0x00]);
        Put(0x80, [0xc6, 0x84, 0xc2, 0x01, 0x20, 0x00, 0x00, 0x99, 0x48, 0x8b, 0x74]);
        Put(0xA0, [0x48, 0x8B, 0x05, 0x00, 0x10, 0x00, 0x00, 0x48, 0x8B, 0xD9, 0xF3, 0x0F, 0x10, 0x50, 0x7F]);
        Put(0xC0, [0x00, 0x30, 0x00, 0x02, 0x45, 0x33, 0xD2, 0x4D, 0x8B]); // roster pattern at 0xC3
        Put(0xE0, [0x48, 0x89, 0x05, 0x00, 0x00, 0x05, 0x00, 0x48, 0x85, 0xDB, 0x74, 0x1E]);
        return image;
    }

    [Fact]
    public void GameReader_ResolvesEveryOffsetFromThePatterns()
    {
        var reader = new GameImage().Reader();
        Assert.Equal(GameImage.ExpectedOffsets, reader.Offsets);
    }

    [Fact]
    public void Find_FollowsTheFormulasOfOffsetGo()
    {
        var o = GameOffsets.Find(HandBuiltImage());

        Assert.Equal((nuint)(0x10 - 0x121 + 0x12345678), o.GameData);
        Assert.Equal((nuint)0x01543210, o.UnitTable);
        Assert.Equal((nuint)(0x60 + 10 + 0x100), o.UI);
        Assert.Equal((nuint)(0x2001 - 1), o.Hover);
        Assert.Equal((nuint)(0xA0 + 7 + 0x1000), o.Expansion);
        Assert.Equal((nuint)(0xC3 + 1 + 0x02003000), o.Roster);
        Assert.Equal((nuint)(0xE0 + 0x050000 + 7), o.PanelManager);
    }

    [Fact]
    public void Find_SignExtendsRipRelativeDisplacements()
    {
        // d2go read the displacement as unsigned: -0x10 became +0xFFFFFFF0.
        var image = HandBuiltImage(gameDataOperand: [0xF0, 0xFF, 0xFF, 0xFF]);
        var o = GameOffsets.Find(image);
        Assert.Equal(unchecked((nuint)(0x10 - 0x121 - 0x10)), o.GameData);
    }

    [Fact]
    public void Find_ReportsTheMissingRequiredPatterns()
    {
        // d2go decoded meaningless offsets from address 0 instead.
        var image = HandBuiltImage();
        image.AsSpan(0x80, 0x20).Clear(); // Hover
        image.AsSpan(0xE0, 0x10).Clear(); // PanelManagerContainer
        var e = Assert.Throws<InvalidDataException>(() => GameOffsets.Find(image));
        Assert.Contains("Hover", e.Message, StringComparison.Ordinal);
        Assert.Contains("PanelManagerContainer", e.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("UnitTable", e.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => GameOffsets.Find(new byte[0x100]));
    }

    [Fact]
    public void Find_LeavesTheMissingOptionalOffsetsNull()
    {
        var o = GameOffsets.Find(HandBuiltImage());
        Assert.Empty(o.Missing);

        var image = HandBuiltImage();
        image.AsSpan(0x10, 0x10).Clear(); // GameData
        image.AsSpan(0xA0, 0x10).Clear(); // Expansion
        image.AsSpan(0xC0, 0x10).Clear(); // Roster
        o = GameOffsets.Find(image);
        Assert.Null(o.GameData);
        Assert.Null(o.Expansion);
        Assert.Null(o.Roster);
        Assert.Equal(["GameData", "Expansion", "Roster"], o.Missing);
        Assert.Equal((nuint)0x01543210, o.UnitTable);
    }

    [Fact]
    public void Find_ARosterPatternWithoutRoomForItsDisplacement_IsMissing()
    {
        var image = HandBuiltImage();
        image.AsSpan(0xC0, 0x10).Clear();
        image[0] = 0x02; image[1] = 0x45; image[2] = 0x33; image[3] = 0xD2; image[4] = 0x4D; image[5] = 0x8B;
        Assert.Null(GameOffsets.Find(image).Roster);
    }

    [Fact]
    public void GameReader_ScansAPartlyReadableModule()
    {
        // The running game keeps some pages of its image unreadable: they read as zeros, and the patterns in the
        // readable pages are still found.
        var image = new GameImage();
        var expected = image.Reader().Offsets;
        image.Memory.ModuleSize = 0x10_0000; // past the mapped code
        Assert.Equal(expected, image.Reader().Offsets);
    }

    [Fact]
    public void GameReader_ValidatesItsArguments_AndDoesNotOwnTheProcess()
    {
        var image = new GameImage();
        var process = new GameProcess(image.Memory);
        Assert.Throws<ArgumentNullException>(() => new GameReader(null!, TestDatabase.Database));
        Assert.Throws<ArgumentNullException>(() => new GameReader(process, null!));

        var reader = new GameReader(process, TestDatabase.Database);
        Assert.Same(process, reader.Process);
        Assert.Same(TestDatabase.Database, reader.Database);
        Assert.False(image.Memory.IsDisposed);
    }

    [Fact]
    public void ToText_AndParse_RoundTrip()
    {
        var full = new GameOffsets(0x11, 0x1EBE5F0, 0x1ECE2EC, 0x1E12220, 0x22, 0x33, 0x1E28FE0);
        var partial = full with { GameData = null, Expansion = null, Roster = null };
        Assert.Equal(full, GameOffsets.Parse(full.ToText()));
        Assert.Equal(partial, GameOffsets.Parse(partial.ToText()));
        Assert.Equal(
            "GameData=\nUnitTable=0x1EBE5F0\nUI=0x1ECE2EC\nHover=0x1E12220\nExpansion=\nRoster=\nPanelManager=0x1E28FE0\n",
            partial.ToText());
        // Windows line ends, blank lines, spaces and left-out optional lines are fine.
        Assert.Equal(partial, GameOffsets.Parse("\r\n UnitTable = 0x1ebe5f0\r\nUI=0x1ECE2EC\r\n\r\nHover=0x1E12220\r\nPanelManager=0x1E28FE0"));
    }

    [Theory]
    [InlineData("UnitTable=0x1\nUI=0x2\nHover=0x3")]                             // PanelManager missing
    [InlineData("UnitTable=0x1\nUI=0x2\nHover=0x3\nPanelManager=")]              // required, empty
    [InlineData("UnitTable=0x1\nUI=0x2\nHover=0x3\nPanelManager=12")]            // no 0x
    [InlineData("UnitTable=0x1\nUI=0x2\nHover=0x3\nPanelManager=0x4\nUI=0x5")]   // twice
    [InlineData("UnitTable=0x1\nUI=0x2\nHover=0x3\nPanelManager=0x4\nFps=0x5")]  // unknown name
    [InlineData("UnitTable 0x1")]                                                 // no =
    public void Parse_RejectsMalformedText(string text) => Assert.Throws<FormatException>(() => GameOffsets.Parse(text));

    [Fact]
    public void GameReader_TakesKnownOffsetsWithoutScanning()
    {
        // A module whose code is unreadable: a scan would throw, the given offsets are used as they are.
        var memory = new FakeProcessMemory(unchecked((nuint)0x7FF6_2000_0000), 0x1000);
        var offsets = new GameOffsets(null, 0x100, 0x200, 0x300, null, null, 0x400);
        var reader = new GameReader(new GameProcess(memory), TestDatabase.Database, offsets);
        Assert.Equal(offsets, reader.Offsets);
        Assert.Throws<ArgumentNullException>(() => new GameReader(null!, TestDatabase.Database, offsets));
        Assert.Throws<ArgumentNullException>(() => new GameReader(new GameProcess(memory), null!, offsets));
    }
}

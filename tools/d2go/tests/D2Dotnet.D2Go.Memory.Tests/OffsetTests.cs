namespace D2Dotnet.D2Go.Memory.Tests;

/// <summary>offset.go: pattern scan and offset decoding.</summary>
public sealed class OffsetTests
{
    [Fact]
    public void GameReader_ResolvesEveryOffsetFromThePatterns()
    {
        using var reader = new GameImage().Reader();
        Assert.Equal(GameImage.ExpectedOffset, reader.Offset);
    }

    [Fact]
    public void Calculate_FollowsTheGoFormulas_HandBuiltImage()
    {
        // Built byte by byte, independently of GameImage: each pattern and its operand, and the result worked out by
        // hand from offset.go.
        const ulong b = 0x1_4000_0000;
        var m = new FakeProcessMemory(unchecked((nuint)b), 0x400);
        m.Map(b, 0x400);
        m.Write(b + 0x10, [0x44, 0x88, 0x25, 0x78, 0x56, 0x34, 0x12, 0x66, 0x44, 0x89, 0x25, 0xAA, 0xBB, 0xCC, 0xDD]);
        m.Write(b + 0x40, [0x48, 0x03, 0xC7, 0x49, 0x8B, 0x8C, 0xC6, 0x10, 0x32, 0x54, 0x01]);
        m.Write(b + 0x60, [0x40, 0x84, 0xed, 0x0f, 0x94, 0x05, 0x00, 0x01, 0x00, 0x00]);
        m.Write(b + 0x80, [0xc6, 0x84, 0xc2, 0x01, 0x20, 0x00, 0x00, 0x99, 0x48, 0x8b, 0x74]);
        m.Write(b + 0xA0, [0x48, 0x8B, 0x05, 0x00, 0x10, 0x00, 0x00, 0x48, 0x8B, 0xD9, 0xF3, 0x0F, 0x10, 0x50, 0x7F]);
        m.Write(b + 0xC0, [0x00, 0x30, 0x00, 0x02, 0x45, 0x33, 0xD2, 0x4D, 0x8B]); // roster pattern at 0xC3
        m.Write(b + 0xE0, [0x48, 0x89, 0x05, 0x00, 0x00, 0x05, 0x00, 0x48, 0x85, 0xDB, 0x74, 0x1E]);
        var p = new GameProcess(m);

        var o = Offsets.Calculate(p);

        Assert.Equal((nuint)(0x10 - 0x121 + 0x12345678), o.GameData);
        Assert.Equal((nuint)0x01543210, o.UnitTable);
        Assert.Equal((nuint)(0x60 + 10 + 0x100), o.UI);
        Assert.Equal((nuint)(0x2001 - 1), o.Hover);
        Assert.Equal((nuint)(0xA0 + 7 + 0x1000), o.Expansion);
        Assert.Equal((nuint)(0xC3 + 1 + 0x02003000), o.RosterOffset);
        Assert.Equal((nuint)(0xE0 + 0x050000 + 7), o.PanelManagerContainerOffset);
    }

    [Fact]
    public void Calculate_WithoutPatterns_WrapsLikeGoUintptr()
    {
        const ulong b = 0x1_4000_0000;
        var m = new FakeProcessMemory(unchecked((nuint)b), 0x100);
        m.Map(b, 0x100);
        var o = Offsets.Calculate(new GameProcess(m));

        // Every pattern is "not found" (address 0) and every operand read from near address 0 is 0.
        unchecked
        {
            Assert.Equal((nuint)(0 - b - 0x121), o.GameData);
            Assert.Equal((nuint)0, o.UnitTable);
            Assert.Equal((nuint)(0 - b + 10), o.UI);
            Assert.Equal(nuint.MaxValue, o.Hover);
            Assert.Equal((nuint)(0 - b + 7), o.Expansion);
            Assert.Equal((nuint)(0 - b + 1), o.RosterOffset);
            Assert.Equal((nuint)(0 - b), o.PanelManagerContainerOffset);
        }
    }

    [Fact]
    public void Calculate_WhenTheModuleCannotBeRead_PanicsLikeGo()
    {
        var image = new GameImage();
        image.Memory.ModuleBaseSize = 0x10_0000; // past the mapped code
        Assert.Throws<IndexOutOfRangeException>(() => image.Reader());
    }

    [Fact]
    public void GetData_ScansAgainWhenTheUnitTableOffsetIsZero()
    {
        var image = new GameImage();
        image.Memory.WriteU32(GameImage.Base + GameImage.UnitTableAt + 7, 0);
        using var reader = image.Reader();
        Assert.Equal((nuint)0, reader.Offset.UnitTable);

        image.Memory.WriteU32(GameImage.Base + GameImage.UnitTableAt + 7, (uint)GameImage.UnitTableOffset);
        reader.GetData();
        Assert.Equal(GameImage.ExpectedOffset, reader.Offset);
    }

    [Fact]
    public void UpdateWidgets_ScansAgainWhenThePanelOffsetIsZero()
    {
        // With the module at address 0, a missing panel pattern gives offset 0 - 0 = 0.
        var m = new FakeProcessMemory(0, 0x100);
        m.Map(0, 0x100);
        using var reader = new GameReader(new GameProcess(m), TestTables.Tables);
        Assert.Equal((nuint)0, reader.Offset.PanelManagerContainerOffset);
        Assert.Empty(reader.UpdateWidgets());

        m.Write(0x20, [0x48, 0x89, 0x05, 0x00, 0x10, 0x00, 0x00, 0x48, 0x85, 0xDB, 0x74, 0x1E]);
        reader.UpdateWidgets();
        Assert.Equal((nuint)(0x20 + 0x1000 + 7), reader.Offset.PanelManagerContainerOffset);
    }

    [Fact]
    public void GameReader_SharesTheProcessAndValidatesArguments()
    {
        var image = new GameImage();
        var process = new GameProcess(image.Memory);
        Assert.Throws<ArgumentNullException>(() => new GameReader(null!, TestTables.Tables));
        Assert.Throws<ArgumentNullException>(() => new GameReader(process, null!));

        var reader = new GameReader(process, TestTables.Tables);
        Assert.Same(process.Memory, reader.Memory);
        Assert.Same(TestTables.Tables, reader.Tables);
        Assert.Equal(process.GetPid(), reader.GetPid());
        reader.Dispose();
        Assert.True(image.Memory.IsDisposed);
    }
}

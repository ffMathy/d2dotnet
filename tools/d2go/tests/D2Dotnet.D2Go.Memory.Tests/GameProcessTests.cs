namespace D2Dotnet.D2Go.Memory.Tests;

/// <summary>process.go: reads, strings, pattern scanning and widgets.</summary>
public sealed class GameProcessTests
{
    private const ulong Base = 0x7FF6_2000_0000;

    private static (FakeProcessMemory Memory, GameProcess Process) NewProcess(uint moduleSize = 0x100)
    {
        var memory = new FakeProcessMemory(unchecked((nuint)Base), moduleSize, processId: 77);
        memory.Map(Base, moduleSize);
        return (memory, new GameProcess(memory));
    }

    [Fact]
    public void ModuleInfoAndPid_ComeFromTheMemory()
    {
        var (_, p) = NewProcess(0x1234);
        Assert.Equal(77u, p.GetPid());
        Assert.Equal(unchecked((nuint)Base), p.ModuleBaseAddress);
        Assert.Equal(0x1234u, p.ModuleBaseSize);
    }

    [Fact]
    public void Constructor_RejectsNull() => Assert.Throws<ArgumentNullException>(() => new GameProcess(null!));

    [Fact]
    public void Dispose_ClosesTheMemory()
    {
        var (memory, p) = NewProcess();
        p.Dispose();
        Assert.True(memory.IsDisposed);
    }

    [Fact]
    public void ReadUInt_IsLittleEndianForEveryWidth()
    {
        var (memory, p) = NewProcess();
        memory.Write(Base + 0x10, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x88]);
        var a = unchecked((nuint)(Base + 0x10));
        Assert.Equal(0x01UL, p.ReadUInt(a, IntType.Uint8));
        Assert.Equal(0x0201UL, p.ReadUInt(a, IntType.Uint16));
        Assert.Equal(0x04030201UL, p.ReadUInt(a, IntType.Uint32));
        Assert.Equal(0x8807060504030201UL, p.ReadUInt(a, IntType.Uint64));
        // Any other size reads that many bytes and gives 0 (Go bytesToUint's default).
        Assert.Equal(0UL, p.ReadUInt(a, (IntType)3));
    }

    [Fact]
    public void ReadBytesFromMemory_IgnoresErrorsAndReturnsZeroesForUnmappedBytes()
    {
        var (memory, p) = NewProcess(0x1000);
        memory.Write(Base + 0xFFE, [0xAA, 0xBB]);
        // The last two module bytes are mapped, the two after them are not.
        Assert.Equal([0xAA, 0xBB, 0x00, 0x00], p.ReadBytesFromMemory(unchecked((nuint)(Base + 0xFFE)), 4));
        Assert.Equal([0, 0, 0], p.ReadBytesFromMemory(0x10, 3));
    }

    [Fact]
    public void ReadBytesFromMemory_PanicsLikeGoForZeroAndHugeSizes()
    {
        var (_, p) = NewProcess();
        Assert.Throws<IndexOutOfRangeException>(() => p.ReadBytesFromMemory(unchecked((nuint)Base), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.ReadBytesFromMemory(unchecked((nuint)Base), ulong.MaxValue));
    }

    [Fact]
    public void ReadUIntFromBuffer_ReadsAtOffsetAndPanicsOutOfRange()
    {
        byte[] buffer = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        Assert.Equal(0x0302UL, GameProcess.ReadUIntFromBuffer(buffer, 2, IntType.Uint16));
        Assert.Equal(0x0908070605040302UL, GameProcess.ReadUIntFromBuffer(buffer, 2, IntType.Uint64));
        Assert.Equal(9UL, GameProcess.ReadUIntFromBuffer(buffer, 9, IntType.Uint8));
        Assert.Equal(0UL, GameProcess.ReadUIntFromBuffer(buffer, 0, (IntType)5));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameProcess.ReadUIntFromBuffer(buffer, 3, IntType.Uint64));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameProcess.ReadUIntFromBuffer(buffer, ulong.MaxValue, IntType.Uint16));
    }

    [Fact]
    public void ReadStringFromMemory_WithSizeZero_ReadsUpToTheFirstNul()
    {
        var (memory, p) = NewProcess();
        memory.Write(Base + 0x20, "Héctor\0rest\0"u8);
        Assert.Equal("Héctor", p.ReadStringFromMemory(unchecked((nuint)(Base + 0x20)), 0));
        // Starting on a NUL gives "".
        Assert.Equal("", p.ReadStringFromMemory(unchecked((nuint)(Base + 0x27)), 0));
        // Unreadable memory reads as NUL.
        Assert.Equal("", p.ReadStringFromMemory(0x40, 0));
        // A string running into unmapped memory stops there.
        memory.Write(Base + 0xFFD, "abc"u8);
        Assert.Equal("abc", p.ReadStringFromMemory(unchecked((nuint)(Base + 0xFFD)), 0));
    }

    [Fact]
    public void ReadStringFromMemory_WithSize_TrimsLeadingAndTrailingNulsOnly()
    {
        var (memory, p) = NewProcess();
        memory.Write(Base + 0x30, "\0\0Bob\0x\0\0\0"u8);
        Assert.Equal("Bob\0x", p.ReadStringFromMemory(unchecked((nuint)(Base + 0x30)), 11));
        Assert.Equal("Bo", p.ReadStringFromMemory(unchecked((nuint)(Base + 0x30)), 4));
        Assert.Equal("", p.ReadStringFromMemory(unchecked((nuint)(Base + 0x30)), 2));
    }

    [Fact]
    public void FindPattern_MatchesWithWildcards()
    {
        var (memory, p) = NewProcess(0x40);
        memory.Write(Base + 0x10, [0x48, 0x00, 0x11, 0x22, 0x33]);
        memory.Write(Base + 0x20, [0x48, 0x8B, 0x99, 0x22, 0x33]);
        var image = p.ReadBytesFromMemory(unchecked((nuint)Base), 0x40);

        Assert.Equal(unchecked((nuint)(Base + 0x20)), p.FindPattern(image, [0x48, 0x8B, 0x00, 0x22], "xx?x"));
        Assert.Equal(unchecked((nuint)(Base + 0x10)), p.FindPattern(image, [0x48, 0x00, 0x00, 0x22], "x??x"));
        Assert.Equal(0x10, p.FindPatternOffset(image, [0x48], "x"));
        Assert.Equal((nuint)0, p.FindPattern(image, [0x48, 0x8B, 0x77], "xxx"));
    }

    [Fact]
    public void FindPattern_LikeGo_ReportsAMatchAtOffsetZeroAsNotFound()
    {
        var (memory, p) = NewProcess(0x40);
        memory.Write(Base, [0xC6, 0x84]);
        memory.Write(Base + 0x30, [0xC6, 0x84]);
        var image = p.ReadBytesFromMemory(unchecked((nuint)Base), 0x40);

        // The match at 0 returns 0, which means "not found": the later match is never seen.
        Assert.Equal((nuint)0, p.FindPattern(image, [0xC6, 0x84], "xx"));
    }

    [Fact]
    public void FindPattern_LikeGo_DoesNotTryTheLastPosition()
    {
        var (memory, p) = NewProcess(0x40);
        memory.Write(Base + 0x3D, [0xAB, 0xCD, 0xEF]);
        var image = p.ReadBytesFromMemory(unchecked((nuint)Base), 0x40);

        // Positions 0 .. size - len - 1 are tried; 0x3D is size - len.
        Assert.Equal((nuint)0, p.FindPattern(image, [0xAB, 0xCD, 0xEF], "xxx"));
        Assert.Equal(unchecked((nuint)(Base + 0x3D)), p.FindPattern(image, [0xAB, 0xCD], "xx"));
    }

    [Fact]
    public void FindPattern_PanicsLikeGoOnAShortImageOrMask()
    {
        var (_, p) = NewProcess(0x40);
        Assert.Throws<IndexOutOfRangeException>(() => p.FindPattern(new byte[0x10], [0xAB], "x"));
        Assert.Throws<IndexOutOfRangeException>(() => p.FindPattern(new byte[0x40], [0x00, 0x00], "x"));
    }

    [Fact]
    public void FindPatternByOperand_AddsTheUnsignedOperandAndTheInstructionLength()
    {
        var (memory, p) = NewProcess(0x40);
        memory.Write(Base + 0x08, [0x48, 0x89, 0x05, 0x10, 0x20, 0x00, 0x00, 0x48]);
        memory.Write(Base + 0x18, [0x48, 0x89, 0x05, 0xF0, 0xFF, 0xFF, 0xFF, 0x49]);
        var image = p.ReadBytesFromMemory(unchecked((nuint)Base), 0x40);

        Assert.Equal(unchecked((nuint)(Base + 0x08 + 0x2010 + 7)), p.FindPatternByOperand(image, [0x48, 0x89, 0x05, 0, 0, 0, 0, 0x48], "xxx????x"));
        // A negative displacement is not sign-extended, as in Go.
        Assert.Equal(unchecked((nuint)(Base + 0x18 + 0xFFFFFFF0 + 7)), p.FindPatternByOperand(image, [0x48, 0x89, 0x05, 0, 0, 0, 0, 0x49], "xxx????x"));
        Assert.Equal((nuint)0, p.FindPatternByOperand(image, [0x48, 0x89, 0x06], "xxx"));
    }

    [Fact]
    public void ReadPointer_ReadsEightBytes()
    {
        var (memory, p) = NewProcess();
        memory.WriteU64(Base + 0x40, 0x1122334455667788);
        Assert.Equal(unchecked((nuint)0x1122334455667788), p.ReadPointer(unchecked((nuint)(Base + 0x40)), 8));
        Assert.Equal(unchecked((nuint)0x1122334455667788), p.ReadPointer(unchecked((nuint)(Base + 0x40)), 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.ReadPointer(unchecked((nuint)(Base + 0x40)), 4));
    }

    [Fact]
    public void GetProcessMemory_ReadsTheModuleOrReturnsNull()
    {
        var (memory, p) = NewProcess(0x20);
        memory.Write(Base + 1, [9]);
        var image = p.GetProcessMemory();
        Assert.NotNull(image);
        Assert.Equal(0x20, image.Length);
        Assert.Equal(9, image[1]);

        memory.ModuleBaseSize = 0x2000; // runs past the mapped bytes
        Assert.Null(p.GetProcessMemory());

        memory.ModuleBaseSize = 0;
        Assert.Throws<IndexOutOfRangeException>(() => p.GetProcessMemory());
    }

    [Fact]
    public void ReadWidgetContainer_ReadsTheShortForm()
    {
        var image = new GameImage();
        var widget = image.Widget("LobbyBackgroundPanel", active: true, visible: false);
        var p = new GameProcess(image.Memory);

        Assert.True(p.TryReadWidgetContainer((nuint)widget, false, out var w));
        Assert.Equal(4, w.Count);
        Assert.Equal("LobbyBackgroundPanel", w["WidgetNameString"]);
        Assert.Equal(0UL, w["WidgetNameLength"]);
        Assert.Equal(true, w["WidgetActive"]);
        Assert.Equal(false, w["WidgetVisible"]);
    }

    [Fact]
    public void ReadWidgetContainer_ReadsTheFullFormFromTheNameStruct()
    {
        var image = new GameImage();
        var widget = image.Widget("PanelX", active: false, visible: true, nameLength: 6, childList: 0xABC0, childCount: 3);
        var nameStruct = image.Memory.ReadU64(widget + 0x08);
        image.Memory.WriteU64(nameStruct + 0x68, 0x1111);
        image.Memory.WriteU32(nameStruct + 0x78, 4);
        image.Memory.WriteU64(nameStruct + 0x80, 0x2222);
        image.Memory.WriteU32(nameStruct + 0x90, 5);
        var p = new GameProcess(image.Memory);

        Assert.True(p.TryReadWidgetContainer((nuint)widget, true, out var w));
        Assert.Equal(10, w.Count);
        Assert.Equal("PanelX", w["WidgetNameString"]);
        Assert.Equal(6UL, w["WidgetNameLength"]);
        Assert.Equal(false, w["WidgetActive"]);
        Assert.Equal(true, w["WidgetVisible"]);
        Assert.Equal((nuint)0xABC0, w["ChildWidgetsListPointer"]);
        Assert.Equal(3UL, w["ChildWidgetSize"]);
        Assert.Equal((nuint)0x1111, w["WidgetListPointer"]);
        Assert.Equal(4UL, w["WidgetListSize"]);
        Assert.Equal((nuint)0x2222, w["WidgetList2Pointer"]);
        Assert.Equal(5UL, w["WidgetList2Size"]);
    }

    [Fact]
    public void ReadWidgetContainer_FailsWithoutAName()
    {
        var image = new GameImage();
        var widget = image.Widget("", active: true, visible: true);
        var p = new GameProcess(image.Memory);

        Assert.False(p.TryReadWidgetContainer((nuint)widget, true, out var w));
        Assert.Empty(w);
        Assert.False(p.TryReadWidgetContainer(0x10, false, out _));
    }

    [Fact]
    public void ReadWidgetList_KeysByNameLastWins_AndFailsOnAnUnnamedWidget()
    {
        var image = new GameImage();
        var a = image.Widget("A", true, true);
        var b = image.Widget("B", false, false);
        var a2 = image.Widget("A", false, true);
        var unnamed = image.Widget("", true, true);
        var list = image.Memory.Alloc(0x40);
        foreach (var (w, i) in new[] { a, b, a2, unnamed }.Select((w, i) => (w, i)))
            image.Memory.WriteU64(list + (ulong)(i * 8), w);
        var p = new GameProcess(image.Memory);

        Assert.True(p.TryReadWidgetList((nuint)list, 3, out var widgets));
        Assert.Equal(["A", "B"], widgets.Keys.Order());
        Assert.Equal(false, widgets["A"]["WidgetActive"]);

        Assert.True(p.TryReadWidgetList((nuint)list, 0, out var none));
        Assert.Empty(none);
        Assert.True(p.TryReadWidgetList((nuint)list, -1, out _));

        Assert.False(p.TryReadWidgetList((nuint)list, 4, out var failed));
        Assert.Empty(failed);
    }
}

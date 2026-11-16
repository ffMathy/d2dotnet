using D2Dotnet.Memory;

namespace D2Dotnet.Tests.Memory;

/// <summary>Typed reads, strings and the pattern scan.</summary>
public sealed class GameProcessTests
{
    private const ulong Base = 0x7FF6_2000_0000;

    private static (FakeProcessMemory Memory, GameProcess Process) NewProcess(uint moduleSize = 0x100)
    {
        var memory = new FakeProcessMemory(unchecked((nuint)Base), moduleSize, processId: 77);
        memory.Map(Base, moduleSize);
        return (memory, new GameProcess(memory));
    }

    private static nuint At(ulong offset) => unchecked((nuint)(Base + offset));

    [Fact]
    public void ModuleAndProcessId_ComeFromTheMemory()
    {
        var (_, p) = NewProcess(0x1234);
        Assert.Equal(77u, p.ProcessId);
        Assert.Equal(At(0), p.ModuleBaseAddress);
        Assert.Equal(0x1234u, p.ModuleSize);
        Assert.Throws<ArgumentNullException>(() => new GameProcess(null!));
    }

    [Fact]
    public void Dispose_ClosesTheMemoryItOwns()
    {
        var (memory, p) = NewProcess();
        p.Dispose();
        Assert.True(memory.IsDisposed);

        var shared = new FakeProcessMemory(0, 0);
        new GameProcess(shared, ownsMemory: false).Dispose();
        Assert.False(shared.IsDisposed);
    }

    [Fact]
    public void Reads_AreLittleEndian()
    {
        var (memory, p) = NewProcess();
        memory.Write(Base + 0x10, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x88]);
        Assert.Equal(0x01, p.ReadByte(At(0x10)));
        Assert.Equal(0x0201, p.ReadUInt16(At(0x10)));
        Assert.Equal(0x04030201u, p.ReadUInt32(At(0x10)));
        Assert.Equal(0x8807060504030201UL, p.ReadUInt64(At(0x10)));
        Assert.Equal(unchecked((nuint)0x8807060504030201UL), p.ReadPointer(At(0x10)));
    }

    [Fact]
    public void UnreadableBytes_ReadAsZero()
    {
        var (memory, p) = NewProcess(0x1000);
        memory.Write(Base + 0xFFE, [0xAA, 0xBB]);
        // The last two module bytes are mapped, the two after them are not.
        Assert.Equal([0xAA, 0xBB, 0x00, 0x00], p.ReadBytes(At(0xFFE), 4));
        Assert.Equal([0, 0, 0], p.ReadBytes(0x10, 3));
        Assert.False(p.TryRead(At(0xFFE), new byte[4]));
        Assert.Equal(0u, p.ReadUInt32(0x10));
    }

    [Fact]
    public void ReadingNothing_IsNotAnError()
    {
        // d2go panics on an empty read (it takes the address of the first byte).
        var (_, p) = NewProcess();
        Assert.Empty(p.ReadBytes(At(0), 0));
        Assert.True(p.TryRead(0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.ReadBytes(At(0), -1));
    }

    [Fact]
    public void ReadString_WithoutLength_ReadsUpToTheFirstNul()
    {
        var (memory, p) = NewProcess();
        memory.Write(Base + 0x20, "Héctor\0rest\0"u8);
        Assert.Equal("Héctor", p.ReadString(At(0x20)));
        Assert.Equal("", p.ReadString(At(0x27)));
        // Unreadable memory ends the string.
        Assert.Equal("", p.ReadString(0x40));
        memory.Write(Base + 0xFFD, "abc"u8);
        Assert.Equal("abc", p.ReadString(At(0xFFD)));
    }

    [Fact]
    public void ReadString_WithoutLength_IsCapped()
    {
        var (memory, p) = NewProcess(0x4000);
        memory.Write(Base, Enumerable.Repeat((byte)'a', 0x3000).ToArray());
        Assert.Equal(GameProcess.MaxStringLength, p.ReadString(At(0)).Length);
    }

    [Fact]
    public void ReadString_WithLength_IsAFieldEndingAtItsFirstNul()
    {
        // d2go trimmed NULs from both ends and kept those inside ("\0\0Bob\0x" read as "Bob\0x").
        var (memory, p) = NewProcess();
        memory.Write(Base + 0x30, "Bob\0x\0\0\0"u8);
        Assert.Equal("Bob", p.ReadString(At(0x30), 8));
        Assert.Equal("Bo", p.ReadString(At(0x30), 2));
        memory.Write(Base + 0x40, "\0\0Bob"u8);
        Assert.Equal("", p.ReadString(At(0x40), 5));
        memory.Write(Base + 0x50, "0123456789"u8);
        Assert.Equal("01234", p.ReadString(At(0x50), 5));
    }

    [Fact]
    public void ReadModule_ReadsTheImageOrThrows()
    {
        var (memory, p) = NewProcess(0x20);
        memory.Write(Base + 1, [9]);
        var image = p.ReadModule();
        Assert.Equal(0x20, image.Length);
        Assert.Equal(9, image[1]);

        memory.ModuleSize = 0x2000; // runs past the mapped bytes
        Assert.Throws<InvalidOperationException>(() => p.ReadModule());
        memory.ModuleSize = 0;
        Assert.Throws<InvalidOperationException>(() => p.ReadModule());
    }

    [Fact]
    public void FindPattern_MatchesWithWildcards()
    {
        byte[] image = new byte[0x40];
        image[0x10] = 0x48; image[0x11] = 0x00; image[0x12] = 0x11; image[0x13] = 0x22;
        image[0x20] = 0x48; image[0x21] = 0x8B; image[0x22] = 0x99; image[0x23] = 0x22;

        Assert.Equal(0x20, GameProcess.FindPattern(image, [0x48, 0x8B, 0x00, 0x22], "xx?x"));
        Assert.Equal(0x10, GameProcess.FindPattern(image, [0x48, 0x00, 0x00, 0x22], "x??x"));
        Assert.Equal(-1, GameProcess.FindPattern(image, [0x48, 0x8B, 0x77], "xxx"));
        Assert.Throws<ArgumentException>(() => GameProcess.FindPattern(image, [0x00, 0x00], "x"));
    }

    [Fact]
    public void FindPattern_FindsAMatchAtTheFirstAndAtTheLastPosition()
    {
        // d2go returned 0 for "not found", so it missed a match at offset 0, and never tried the last position.
        byte[] image = new byte[0x40];
        image[0] = 0xC6; image[1] = 0x84;
        image[0x3D] = 0xAB; image[0x3E] = 0xCD; image[0x3F] = 0xEF;

        Assert.Equal(0, GameProcess.FindPattern(image, [0xC6, 0x84], "xx"));
        Assert.Equal(0x3D, GameProcess.FindPattern(image, [0xAB, 0xCD, 0xEF], "xxx"));
        Assert.Equal(-1, GameProcess.FindPattern(image.AsSpan(0, 2), [0xC6, 0x84, 0x00], "xxx"));
    }
}

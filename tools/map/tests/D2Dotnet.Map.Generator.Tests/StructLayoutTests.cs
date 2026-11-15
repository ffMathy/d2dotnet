using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using D2Dotnet.Map.Generator.Native;

namespace D2Dotnet.Map.Generator.Tests;

/// <summary>Struct layouts must equal what i686-w64-mingw32-g++ produces for d2_structs.h.</summary>
public class StructLayoutTests
{
    private static Type StructType(string name) =>
        typeof(Level).Assembly.GetType("D2Dotnet.Map.Generator.Native." + name)
        ?? throw new InvalidOperationException($"no C# struct for {name}");

    public static TheoryData<string, int> Sizes()
    {
        var data = new TheoryData<string, int>();
        foreach (var (s, size) in CStructLayout.Sizes) data.Add(s, size);
        return data;
    }

    public static TheoryData<string, string, int> Offsets()
    {
        var data = new TheoryData<string, string, int>();
        foreach (var (s, f, o) in CStructLayout.Offsets) data.Add(s, f, o);
        return data;
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void SizeMatchesC(string name, int expected)
    {
        var t = StructType(name);
        var unsafeSize = (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(t).Invoke(null, null)!;
        Assert.Equal(expected, unsafeSize);
        Assert.Equal(expected, Marshal.SizeOf(t));
    }

    [Theory]
    [MemberData(nameof(Offsets))]
    public void FieldOffsetMatchesC(string name, string field, int expected)
    {
        var t = StructType(name);
        Assert.Equal(expected, (int)Marshal.OffsetOf(t, field));
    }

    [Fact]
    public void EveryCStructIsPortedAndNoExtraFieldsExist()
    {
        var cStructs = CStructLayout.Sizes.Select(s => s.Struct).ToHashSet();
        var ported = typeof(Level).Assembly.GetTypes()
            .Where(t => t.Namespace == "D2Dotnet.Map.Generator.Native" && t.IsValueType && !t.IsEnum && t.IsPublic
                        && t.StructLayoutAttribute?.Value == LayoutKind.Explicit)
            .Select(t => t.Name).ToHashSet();
        Assert.Equal(cStructs.OrderBy(x => x), ported.OrderBy(x => x));

        // Every public instance field corresponds to a C field (MonsterData.Flags is the anonymous bit-field byte).
        var cFields = CStructLayout.Offsets.Select(o => o.Struct + "." + o.Field).ToHashSet();
        foreach (var name in cStructs)
        {
            foreach (var f in StructType(name).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (name == "MonsterData" && f.Name == "Flags") continue;
                Assert.Contains(name + "." + f.Name, cFields);
            }
        }
    }

    [Fact]
    public void MonsterDataBitFieldsAreLsbFirst()
    {
        var m = new MonsterData { Flags = 0b10110 };
        Assert.False(m.fUnk);
        Assert.True(m.fNormal);
        Assert.True(m.fChamp);
        Assert.False(m.fBoss);
        Assert.True(m.fMinion);
    }

    [Fact]
    public unsafe void FixedBuffersHaveCLengths()
    {
        var level = new Level();
        Assert.Equal(0x1AC - 0x2C, 96 * sizeof(uint));
        level._3[95] = 7;
        Assert.Equal(7u, level._3[95]);
        Assert.Equal(40 * 2, Marshal.OffsetOf<LevelTxt>(nameof(LevelTxt.wEntranceText)) - Marshal.OffsetOf<LevelTxt>(nameof(LevelTxt.wName)));
    }
}

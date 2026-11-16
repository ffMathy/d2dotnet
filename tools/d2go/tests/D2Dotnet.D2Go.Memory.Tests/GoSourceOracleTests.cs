using System.Globalization;
using System.Text.RegularExpressions;
using D2Dotnet.D2Go.Data.Npc;
using D2Dotnet.D2Go.Data.Quest;
using D2Dotnet.D2Go.Data.Skill;
using D2Dotnet.D2Go.Data.Stat;
using D2Dotnet.D2Go.Data.State;
using D2Dotnet.D2Go.Data.Tests;

namespace D2Dotnet.D2Go.Memory.Tests;

// Inside the namespace, so these win over the library's types of the same name in the enclosing D2Dotnet namespace.
using QuestId = D2Dotnet.D2Go.Data.Quest.QuestId;
using SkillId = D2Dotnet.D2Go.Data.Skill.SkillId;
using StatId = D2Dotnet.D2Go.Data.Stat.StatId;
using StateId = D2Dotnet.D2Go.Data.State.StateId;

/// <summary>
/// Checks the port against d2go's own Go source (pkg/memory, pkg/utils) at the pinned commit, downloaded at build time
/// into .cache/d2go/: the patterns, the key binding offsets, the ignored NPCs, the stat rescaling, the quest byte
/// ranges, and every hex constant of each Go file.
/// </summary>
public sealed partial class GoSourceOracleTests
{
    private static string Go(string file) => D2GoFixtures.GoFile("pkg/" + file);

    [Fact]
    public void Patterns_MatchOffsetGo()
    {
        var calls = PatternCall().Matches(Go("memory/offset.go")).ToArray();
        Assert.Equal(Offsets.All.Count, calls.Length);
        for (var i = 0; i < calls.Length; i++)
        {
            var expected = Offsets.All[i];
            Assert.Equal(calls[i].Groups[1].Value == "FindPatternByOperand", expected.ByOperand);
            Assert.Equal(GoSource.Unquote(calls[i].Groups[2].Value).Select(c => (byte)c), expected.Bytes);
            Assert.Equal(GoSource.Unquote(calls[i].Groups[3].Value), expected.Mask);
        }
    }

    [Fact]
    public void Addresses_MatchAddressesGo()
    {
        var consts = GoSource.Constants(Go("memory/addresses.go"), "addresses.go").ToDictionary(c => c.Name, c => (long)c.Value);
        Assert.Equal((long)Offsets.TzOffline, consts["tzOffline"]);
        Assert.Equal((long)Offsets.TzOnline, consts["tzOnline"]);
    }

    [Fact]
    public void KeyBindings_ReadEveryKeyFromTheGoOffsets()
    {
        var source = Go("memory/keybindings.go");

        // Named bindings: Field: data.KeyBinding{ Key1: [2]byte{blob[a], blob[a']}, Key2: [2]byte{blob[b], blob[b']} }.
        var named = NamedBinding().Matches(source).ToDictionary(
            m => m.Groups[1].Value,
            m => new[] { Hex(m.Groups[2].Value), Hex(m.Groups[3].Value), Hex(m.Groups[4].Value), Hex(m.Groups[5].Value) });
        Assert.Equal(
            typeof(KeyBindings).GetProperties().Where(p => p.PropertyType == typeof(KeyBinding)).Select(p => p.Name).Order(),
            named.Keys.Order());

        // The three loops: skills 0-6, skills 7-15 and the belt, each `for i := 0; i < n; i++` with blob[base+(i*stride)].
        var loops = LoopBinding().Matches(source).Select(m => (
            Count: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            Offsets: LoopOffset().Matches(m.Groups[2].Value).Select(o => (Base: Hex(o.Groups[1].Value), Stride: Hex(o.Groups[2].Value))).ToArray())).ToArray();
        Assert.Equal(3, loops.Length);
        Assert.Equal([7, 9, 4], loops.Select(l => l.Count));
        Assert.All(loops, l => Assert.Equal(4, l.Offsets.Length));
        var skillStride = Hex(SkillStride().Match(source).Groups[1].Value);

        // Fill the blob so that two reads tell every byte's offset apart, and the skill blob with distinct ids.
        var low = Read(i => (byte)i);
        var high = Read(i => (byte)(i >> 8));

        foreach (var (field, offsets) in named)
        {
            var property = typeof(KeyBindings).GetProperty(field);
            Assert.NotNull(property);
            AssertKey(offsets, (KeyBinding)property.GetValue(low.Bindings)!, (KeyBinding)property.GetValue(high.Bindings)!);
        }

        for (var i = 0; i < 7; i++)
        {
            AssertKey(loops[0].Offsets.Select(o => o.Base + (i * o.Stride)).ToArray(), low.Bindings.Skills[i].KeyBinding, high.Bindings.Skills[i].KeyBinding);
            Assert.Equal(low.SkillAt(i * skillStride), low.Bindings.Skills[i].SkillId);
        }

        for (var i = 0; i < 9; i++)
        {
            AssertKey(loops[1].Offsets.Select(o => o.Base + (i * o.Stride)).ToArray(), low.Bindings.Skills[i + 7].KeyBinding, high.Bindings.Skills[i + 7].KeyBinding);
            Assert.Equal(low.SkillAt((i + 7) * skillStride), low.Bindings.Skills[i + 7].SkillId);
        }

        for (var i = 0; i < 4; i++)
            AssertKey(loops[2].Offsets.Select(o => o.Base + (i * o.Stride)).ToArray(), low.Bindings.UseBelt[i], high.Bindings.UseBelt[i]);

        static void AssertKey(int[] offsets, KeyBinding low, KeyBinding high)
        {
            int Offset(KeyBytes l, KeyBytes h, int index) => l[index] | (h[index] << 8);
            Assert.Equal(offsets, new[] { Offset(low.Key1, high.Key1, 0), Offset(low.Key1, high.Key1, 1), Offset(low.Key2, high.Key2, 0), Offset(low.Key2, high.Key2, 1) });
        }

        static (KeyBindings Bindings, Func<int, SkillId> SkillAt) Read(Func<int, byte> fill)
        {
            var image = new GameImage();
            var blob = new byte[0x500];
            for (var i = 0; i < blob.Length; i++)
                blob[i] = fill(i);
            image.Memory.Write(GameImage.Base + 0x1DFA914, blob);
            var skills = new byte[0x500];
            for (var i = 0; i < skills.Length; i += 4)
                BitConverter.TryWriteBytes(skills.AsSpan(i), (uint)(i * 3 + 1));
            image.Memory.Write(GameImage.Base + 0x21E29B0, skills);
            using var reader = image.Reader();
            return (reader.GetKeyBindings(), offset => (SkillId)(short)(offset * 3 + 1));
        }
    }

    [Fact]
    public void ShouldBeIgnored_MatchesMonstersGo()
    {
        var body = GoSource.FunctionBody(Go("memory/monsters.go"), "func (gd *GameReader) shouldBeIgnored(");
        var ids = GoSource.CaseOperands(body).Select(o =>
        {
            Assert.StartsWith("npc.", o);
            return Enum.Parse<NpcId>(o["npc.".Length..]);
        }).ToHashSet();
        Assert.Equal(64, ids.Count);

        for (var id = -2; id <= 1100; id++)
            Assert.Equal(ids.Contains((NpcId)id), GameReader.ShouldBeIgnored(unchecked((ulong)id)));
    }

    // The value expressions of getStatsList's switch, as Go text, and what they compute.
    private static readonly Dictionary<string, Func<ulong, long>> StatExpressions = new()
    {
        ["int(statValue >> 8)"] = v => (long)(v >> 8),
        ["int(statValue / 25)"] = v => (long)(v / 25),
        ["int(float64(statValue) / .8)"] = v => (long)(v / .8),
        ["int(float64(statValue) / 1.28)"] = v => (long)(v / 1.28),
        ["int(statValue / 2)"] = v => (long)(v / 2),
        ["int(math.Max(float64(statValue/8), 1))"] = v => Math.Max((long)(v / 8), 1),
        ["int(math.Max(float64(statValue/2048), 1))"] = v => Math.Max((long)(v / 2048), 1),
        ["int(math.Max(float64(2/statValue), 1))"] = v => Math.Max((long)(2 / v), 1),
        ["int(statValue) * 10"] = v => (long)v * 10,
        ["int(statValue) * -1"] = v => -(long)v,
        ["int(math.Max(float64(statValue), 15))"] = v => Math.Max((long)v, 15),
    };

    [Fact]
    public void StatRescaling_MatchesGameReaderGo()
    {
        var body = GoSource.FunctionBody(Go("memory/game_reader.go"), "func (gd *GameReader) getStatsList(");
        var groups = StatCase().Matches(body).Select(m => (
            Ids: m.Groups[1].Value.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).Select(t =>
            {
                Assert.StartsWith("stat.", t);
                return Enum.Parse<StatId>(t["stat.".Length..]);
            }).ToArray(),
            Expression: m.Groups[2].Value.Trim())).ToArray();
        Assert.Equal(StatExpressions.Keys.Order(), groups.Select(g => g.Expression).Order());

        var expressionOf = groups.SelectMany(g => g.Ids.Select(id => (id, g.Expression))).ToDictionary(p => p.id, p => p.Expression);
        ulong[] values = [1, 2, 3, 7, 8, 25, 255, 256, 2047, 2048, 4097, 123456, 0x7FFF_FFFF, 0x8000_0000, 0xFFFF_FFFF];
        for (var id = -2; id <= 400; id++)
        {
            foreach (var v in values)
            {
                var expected = expressionOf.TryGetValue((StatId)id, out var e) ? StatExpressions[e](v) : (long)v;
                Assert.Equal(expected, GameReader.StatValue((StatId)id, v));
            }
        }
    }

    [Fact]
    public void States_MatchCalculateStatesGo()
    {
        // calculateStates tests the 32 bits in order: `if <mask>&stateFlag != 0 { ... state.State(<n>+offset) }`.
        var body = GoSource.FunctionBody(Go("memory/player.go"), "func calculateStates(");
        var checks = StateCheck().Matches(body).Select(m => (Mask: Convert.ToUInt64(m.Groups[1].Value, 16), N: int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))).ToArray();
        Assert.Equal(32, checks.Length);

        foreach (var (mask, n) in checks)
        {
            foreach (var offset in new[] { ulong.MaxValue, 31UL, 63UL })
                Assert.Equal([(StateId)(uint)unchecked((ulong)n + offset)], GameReader.CalculateStates(mask, offset));
        }

        Assert.Equal(checks.Select(c => (StateId)(c.N + 31)), GameReader.CalculateStates(checks.Aggregate(0UL, (a, c) => a | c.Mask), 31));
    }

    [Fact]
    public void QuestBytes_MatchQuestsGo()
    {
        var ranges = QuestRange().Matches(Go("memory/quests.go")).Select(m => (
            Quest: Enum.Parse<QuestId>(m.Groups[1].Value),
            Start: int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            End: int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))).ToArray();
        Assert.Equal(27, ranges.Length);

        foreach (var (quest, start, end) in ranges)
        {
            Assert.Equal(start + 2, end);
            var bytes = new byte[82];
            bytes[start] = 0x10;    // high byte: status 12
            bytes[start + 1] = 0x01; // low byte: status 0
            var quests = GameReader.GetQuests(bytes);
            Assert.Equal(27, quests.Count);
            foreach (var (q, states) in quests)
                Assert.Equal(q == quest ? [QuestStatus.UpdateQuestLogCompleted, QuestStatus.RewardGranted] : (QuestStatus[])[], states);
        }
    }

    public static TheoryData<string, string[]> HexFiles => new()
    {
        { "memory/addresses.go", ["Offset.cs"] },
        { "memory/offset.go", ["Offset.cs"] },
        { "memory/process.go", ["GameProcess.cs", "WindowsProcessMemory.cs"] },
        { "memory/game_reader.go", ["GameReader.cs"] },
        { "memory/player.go", ["GameReader.Player.cs"] },
        { "memory/monsters.go", ["GameReader.Monsters.cs"] },
        { "memory/item.go", ["GameReader.Items.cs"] },
        { "memory/object.go", ["GameReader.Objects.cs"] },
        { "memory/keybindings.go", ["GameReader.KeyBindings.cs"] },
        { "memory/roster.go", ["GameReader.Roster.cs"] },
        { "memory/quests.go", ["GameReader.Quests.cs"] },
        { "memory/terrorzones.go", ["GameReader.TerrorZones.cs"] },
        { "memory/player_unit.go", ["RawPlayerUnit.cs"] },
        { "utils/map_seed_hash.go", ["Utils/MapSeed.cs"] },
        { "utils/utils.go", ["Utils/Geometry.cs"] },
    };

    /// <summary>
    /// Every hex literal of the Go file (offsets, addresses, flags, masks) is also a hex literal of its C# port. The
    /// single-bit masks of calculateStates are a loop in C# and are checked by <see cref="States_MatchCalculateStatesGo"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(HexFiles))]
    public void HexConstants_AppearInThePort(string goFile, string[] csFiles)
    {
        // Commented-out Go code is not ported.
        var go = GoLineComment().Replace(Go(goFile), "");
        if (goFile == "memory/player.go")
            go = go.Replace(GoSource.FunctionBody(go, "func calculateStates("), "", StringComparison.Ordinal);

        var cs = string.Concat(csFiles.Select(f => File.ReadAllText(Path.Combine(SourceDirectory, f))));
        var csValues = HexLiteral().Matches(cs).Select(m => Convert.ToUInt64(m.Groups[1].Value.Replace("_", "", StringComparison.Ordinal), 16)).ToHashSet();
        var missing = HexLiteral().Matches(go).Select(m => m.Value).Where(v => !csValues.Contains(Convert.ToUInt64(v[2..], 16))).Distinct().ToArray();
        Assert.True(missing.Length == 0, $"{goFile}: not in {string.Join(", ", csFiles)}: {string.Join(", ", missing)}");
    }

    private static string SourceDirectory
    {
        get
        {
            // <repo>/.cache/d2go/d2go-<sha> -> <repo>/tools/d2go/src/D2Dotnet.D2Go.Memory
            var repo = Path.GetFullPath(Path.Combine(D2GoFixtures.Root, "..", "..", ".."));
            var dir = Path.Combine(repo, "tools", "d2go", "src", "D2Dotnet.D2Go.Memory");
            Assert.True(Directory.Exists(dir), $"C# sources not found at {dir}");
            return dir;
        }
    }

    private static int Hex(string literal) => Convert.ToInt32(literal, 16);

    [GeneratedRegex(@"process\.(FindPattern|FindPatternByOperand)\(memory, (""[^""]*""), (""[^""]*"")\)")]
    private static partial Regex PatternCall();

    [GeneratedRegex(@"(\w+): data\.KeyBinding\{\s*Key1: \[2\]byte\{blob\[(0x[0-9a-f]+)\], blob\[(0x[0-9a-f]+)\]\},\s*Key2: \[2\]byte\{blob\[(0x[0-9a-f]+)\], blob\[(0x[0-9a-f]+)\]\},\s*\}")]
    private static partial Regex NamedBinding();

    [GeneratedRegex(@"for i := 0; i < (\d+); i\+\+ \{(.*?)\n\t\}", RegexOptions.Singleline)]
    private static partial Regex LoopBinding();

    [GeneratedRegex(@"blob\[(0x[0-9a-f]+)\+\(i\*(0x[0-9a-f]+)\)\]")]
    private static partial Regex LoopOffset();

    [GeneratedRegex(@"blobSkills\[i\*(0x[0-9a-f]+) : i\*0x[0-9a-f]+\+4\]")]
    private static partial Regex SkillStride();

    [GeneratedRegex(@"case ([^:]+):\s*\n\s*value = ([^\n]+)\n")]
    private static partial Regex StatCase();

    [GeneratedRegex(@"if (0x[0-9a-fA-F]+)&stateFlag != 0 \{\s*states = append\(states, state\.State\((\d+)\+offset\)\)")]
    private static partial Regex StateCheck();

    [GeneratedRegex(@"quest\.(\w+):\s+gd\.getQuestStatus\(\[2\]byte\(questBytes\[(\d+):(\d+)\]\)\)")]
    private static partial Regex QuestRange();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex GoLineComment();

    [GeneratedRegex(@"\b0[xX]([0-9a-fA-F_]+)\b")]
    private static partial Regex HexLiteral();
}

using D2Dotnet.Maps;

namespace D2Dotnet.Tests.Maps;

/// <summary>Ports of the pinned libd2 commit's own preset tests (packages/drlg/src/lib.zig, added
/// by 4eaa28c / f6f6f79 / 00004e6), which check the room preset lists against the 1.14d game
/// server's own data for seed 4294967295 (normal). These cover the preset behaviour the LibD2
/// NuGet oracle predates.</summary>
public sealed class EnginePresetTests
{
    private const uint Seed = 4294967295;

    private readonly record struct Obj(int Level, int Class, int X, int Y);

    // The outdoor waypoints of Acts I and II come from the waypoint LvlSub file placed into a room
    // by InitGridCells, not from a LvlPrest map, so they only exist once the room is materialized
    // (CopyPresetUnitsFromSubTxt). Positions are level-local subtiles.
    private static readonly Obj[] OutdoorWaypoints =
    {
        new(3, 119, 114, 349), // Cold Plains
        new(4, 119, 179, 139), // Stony Field
        new(5, 119, 99, 299), // Dark Wood
        new(6, 119, 299, 139), // Black Marsh
        new(42, 156, 59, 339), // Dry Hills
        new(43, 156, 339, 219), // Far Oasis
        new(44, 156, 259, 99), // Lost City
    };

    // Objects the 1.14d game server has in these rooms for seed 4294967295: the Act II shrines and
    // wells of the shrine LvlSub files, and the units Act I maze and preset tiles mark
    // (CreatesPresets), including those a room takes over its seam with a neighbour.
    private static readonly Obj[] EngineObjects =
    {
        new(42, 166, 339, 189), // Dry Hills: shrine
        new(42, 322, 214, 184), // Dry Hills: well
        new(42, 172, 109, 94),
        new(42, 135, 59, 94),
        new(42, 86, 269, 54),
        new(26, 27, 140, 78), // Monastery Gate, over a room seam
        new(26, 24, 141, 2),
        new(28, 15, 100, 105), // Barracks, over a room seam
        new(28, 15, 100, 175),
        new(28, 16, 75, 200),
        new(29, 13, 235, 160), // Jail 1
        new(29, 14, 205, 150),
    };

    // How many objects the 1.14d game server's own room preset lists hold per level (levels
    // 1..136, normal) for seed 4294967295, inside the level rect, counted once per (class, pos).
    private static readonly ushort[] EngineObjectCounts =
    {
        25, 14, 15, 29, 34, 38, 26, 14, 28, 58, 25, 33, 11, 13, 8, 12, 11, 38, 59, 0,
        24, 10, 16, 52, 45, 14, 5, 53, 39, 44, 46, 6, 32, 46, 71, 77, 94, 83, 8, 35,
        13, 15, 13, 43, 2, 17, 60, 50, 69, 13, 75, 46, 44, 46, 20, 25, 49, 59, 15, 32,
        1, 11, 8, 7, 43, 81, 53, 50, 40, 23, 30, 20, 44, 132, 63, 71, 68, 59, 188, 228,
        167, 19, 67, 38, 41, 31, 32, 32, 36, 51, 41, 162, 16, 15, 23, 27, 37, 17, 31, 63,
        81, 61, 45, 43, 45, 98, 185, 125, 40, 134, 128, 70, 70, 143, 80, 69, 49, 114, 59, 6,
        16, 235, 242, 116, 73, 68, 73, 72, 90, 90, 75, 41, 18, 10, 73, 83,
    };

    // Objects that only come out right when one specific engine step is followed, and objects
    // the port once listed that the engine's rooms do not have.
    private static readonly Obj[] EngineObjectsMore =
    {
        new(75, 382, 221, 111), // Kurast Docks: a MonPreset turned object
        new(83, 404, 136, 91), // Travincal: the same
        new(109, 461, 76, 126), // Harrogath: Act V, after the divider row
        new(120, 474, 58, 77), // Arreat Summit statues
        new(120, 475, 37, 77),
        new(120, 476, 48, 67),
    };

    private static readonly Obj[] EngineAbsent =
    {
        new(6, 37, 93, 360), // a map unit inside none of its rooms
        new(34, 29, 374, 79),
        new(57, 261, 198, 198), // seed-filtered: another room loads the map
        new(74, 196, 688, 453),
        new(106, 400, 137, 160),
    };

    private static bool HasObject(MapLevel l, Obj o) =>
        l.Presets.Any(p => p.Kind == PresetKind.Object && p.ClassId == o.Class && p.Position == new Point(o.X, o.Y));

    [Fact]
    public void PresetsMatchTheEngine_OutdoorWaypointsActIIShrinesTilePresets()
    {
        foreach (int actNo in new[] { 0, 1 })
        {
            var act = LibD2Fixtures.Generator.Generate(Seed, Difficulty.Normal, (ActId)actNo);
            foreach (var want in OutdoorWaypoints)
            {
                if ((want.Level < 40) != (actNo == 0)) continue;
                var l = act[(AreaId)want.Level];
                var wps = l.Presets.Where(p => p.Kind == PresetKind.Object && p.ClassId == want.Class).ToList();
                Assert.Single(wps);
                Assert.Equal((want.X, want.Y), (wps[0].Position.X, wps[0].Position.Y));
                Assert.Contains(l.Waypoints, w => w.Position == new Point(want.X, want.Y));
            }
            foreach (var want in EngineObjects)
            {
                if ((want.Level < 40) != (actNo == 0)) continue;
                Assert.True(HasObject(act[(AreaId)want.Level], want), $"level {want.Level}: object {want.Class} at {want.X},{want.Y} missing");
            }
            // Act II shrines and wells come from the Act II shrine files: none of the Act I
            // shrine/well classes may show up there.
            if (actNo == 1)
            {
                foreach (var l in act.Levels.Where(l => l.Id is >= AreaId.DryHills and <= AreaId.LostCity))
                    foreach (var p in l.Presets.Where(p => p.Kind == PresetKind.Object))
                        Assert.DoesNotContain(p.ClassId, new[] { 2, 81, 83, 84, 130 });
            }
        }
    }

    [Fact]
    public void EveryLevelsObjectsMatchTheEnginesRoomLists()
    {
        var bad = new List<string>();
        for (int actNo = 0; actNo < 5; actNo++)
        {
            var act = LibD2Fixtures.Generator.Generate(Seed, Difficulty.Normal, (ActId)actNo);
            foreach (var l in act.Levels)
            {
                int id = (int)l.Id;
                if (id < 1 || id > 136) continue;
                int n = l.Presets.Count(p => p.Kind == PresetKind.Object);
                if (n != EngineObjectCounts[id - 1]) bad.Add($"level {id}: {n} objects, the engine has {EngineObjectCounts[id - 1]}");
                foreach (var want in EngineObjectsMore.Where(o => o.Level == id))
                    if (!HasObject(l, want)) bad.Add($"level {id}: object {want.Class} at {want.X},{want.Y} missing");
                foreach (var want in EngineAbsent.Where(o => o.Level == id))
                    if (HasObject(l, want)) bad.Add($"level {id}: object {want.Class} at {want.X},{want.Y} should be absent");
            }
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }
}

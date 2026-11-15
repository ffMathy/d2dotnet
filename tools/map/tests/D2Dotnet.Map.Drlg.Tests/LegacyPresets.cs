using D2Dotnet.Map.Drlg.Engine;

namespace D2Dotnet.Map.Drlg.Tests;

/// <summary>The preset-unit list as libd2 built it before the room-preset rework (89db01d, the
/// code the LibD2 0.1.1 NuGet oracle ships), computed from the port's own engine state at the
/// point that code read it: after a level is generated and before its rooms are built for
/// collision.
/// </summary>
/// <remarks>
/// <para>The pinned libd2 commit (00004e6) changed what a level's presets are in three commits
/// after the newest published NuGet (4eaa28c, f6f6f79, 00004e6): presets now come from the rooms'
/// own lists after they are built (outdoor LvlSub waypoints/shrines/wells, tile-marked presets,
/// gated maps loaded by the first room), MonPreset entries that are objects in the game become
/// objects, and the Act I shrine files are no longer rolled for Act II. The port follows the
/// pinned commit; to still compare the engine state the presets derive from against the native
/// oracle, the oracle test feeds this reconstruction of the old collector from that state.</para>
/// <para>Sources (89db01d lib.zig collectLevelPresets), per preset DrlgMap of the level (deduped
/// by pointer): the map's engine-populated preset chain; for gated maps whose chain is empty, a
/// replay of the DS1 objects through CopyPresetUnit's world transform; then the seeded outdoor
/// shrines/wells (without the later Act-I-files-only gate). Monster codes are mapped back to the
/// old MonStats id space (txt rows, "Expansion" divider counted), and the MonPresets the newer
/// code turns into objects are reported as the monsters they were then.</para>
/// </remarks>
internal static unsafe class LegacyPresets
{
    private const int SUB = 5;

    private static D2DrlgMapStrc* RoomPMap(D2RoomExStrc* p)
    {
        if (p->pRoomExData == null) return null;
        var pmap = ((D2DrlgPresetRoomStrc*)p->pRoomExData)->pMap;
        if (pmap == null || pmap->pTxtLevelPrest == null || pmap->nSizeX <= 0 || pmap->nSizeY <= 0) return null;
        return pmap;
    }

    /// <summary>The pre-0.7 MonStats id space: a MonStats preset's DBM code was its txt row (the
    /// "Expansion" divider row counted), where it is now the game row. SuperUnique and MonPlace
    /// codes were already game-row based.</summary>
    private static int[] MonStatsGameToTxtRow()
    {
        var t = LibD2Fixtures.Source.GetTable("MonStats")!;
        var map = new List<int>();
        for (int i = 0; i < t.RowCount; i++)
        {
            var key = t.Str(i, "Id");
            if (key.Length == 0 || key == "Expansion") continue;
            map.Add(i);
        }
        return map.ToArray();
    }

    private static readonly Lazy<int[]> _gameToTxt = new(MonStatsGameToTxtRow);

    private static int OldDbm(int code)
    {
        var m = _gameToTxt.Value;
        return code >= 0 && code < m.Length ? m[code] : code;
    }

    /// <summary>The MonStats class a converted object came from, when the map's DS1 has the
    /// monster at that (DS1-local subtile) position.</summary>
    private static int? ConvertedFrom(Formats.Ds1File d, int objectClass, int x, int y)
    {
        foreach (var o in d.Objects)
        {
            if (o.Kind != 1 || o.X != x || o.Y != y) continue;
            int mon = GenContext.Data.Presets.MonsterClassId(d.ActId, o.Id);
            int obj = d.ActId switch
            {
                2 => mon == 0x129 ? 0x17e : mon == 0x16e ? 0x194 : -1,
                4 => mon == 0x202 ? 0x1cd : mon >= 0x219 && mon <= 0x21b ? 0x3f5 - mon : -1,
                _ => -1,
            };
            if (obj == objectClass) return mon;
        }
        return null;
    }

    public static List<(int Type, int ClassId, int X, int Y)> Collect(nint level)
    {
        var pLevel = (D2DrlgLevelStrc*)level;
        var outList = new List<(int, int, int, int)>();
        var seen = new HashSet<ulong>();
        var seenMap = new HashSet<nint>();
        int ox = pLevel->sCoordinatesAndSize.WorldPosition.x * SUB;
        int oy = pLevel->sCoordinatesAndSize.WorldPosition.y * SUB;
        int bw = pLevel->sCoordinatesAndSize.WorldSize.x * SUB;
        int bh = pLevel->sCoordinatesAndSize.WorldSize.y * SUB;

        void Add(int et, int cls, int lx, int ly)
        {
            if (lx < 0 || ly < 0 || lx >= bw || ly >= bh) return;
            ulong k = ((ulong)(long)et << 56) ^ ((ulong)(long)cls << 40) ^ ((ulong)(lx & 0xfffff) << 20) ^ (ulong)(ly & 0xfffff);
            if (seen.Add(k)) outList.Add((et, cls, lx, ly));
        }

        var presets = GenContext.Data.Presets;
        for (var p = pLevel->pRoomExFirst; p != null; p = p->pRoomExNext)
        {
            var pmap = RoomPMap(p);
            if (pmap == null || !seenMap.Add((nint)pmap)) continue;
            if (pmap->pPresetUnit != null)
            {
                // Engine-populated chain (world coords, copy-filter already applied).
                var rel = Preset.PresetDs1Path(pmap);
                var d = rel == null ? null : GenContext.Data.GetDs1(rel);
                int wox = pmap->nRealOffsetX * SUB;
                int woy = pmap->nRealOffsetY * SUB;
                for (var u = pmap->pPresetUnit; u != null; u = u->pPresetUnitNext)
                {
                    int et = u->eType;
                    if (et != 1 && et != 2) continue;
                    int code = et == 1 && u->nDbmCode >= 0 ? OldDbm(u->nDbmCode) : u->nClassId;
                    // A MonPreset the newer code turns into an object (ParsePresetsOfDrlgFile)
                    // was a monster then: find its DS1 monster at the unit's position.
                    if (et == 2 && d != null && ConvertedFrom(d, u->nClassId, u->nPosX - wox, u->nPosY - woy) is int monClass)
                    {
                        et = 1;
                        code = OldDbm(monClass);
                    }
                    Add(et, code, u->nPosX - ox, u->nPosY - oy);
                }
            }
            else
            {
                // Gated wilderness map: replay the DS1 objects through the engine's world
                // transform (CopyPresetUnit: pos + nRealOffset*SUB).
                var rel = Preset.PresetDs1Path(pmap);
                if (rel == null) continue;
                var d = GenContext.Data.GetDs1(rel);
                if (d == null) continue;
                int wox = pmap->nRealOffsetX * SUB;
                int woy = pmap->nRealOffsetY * SUB;
                foreach (var o in d.Objects)
                {
                    int et, classId;
                    switch (o.Kind)
                    {
                        case 1:
                            et = 1;
                            classId = OldDbm(presets.DbmMonsterCode(d.ActId, o.Id));
                            break;
                        case 2:
                            et = 2;
                            classId = presets.ObjectClassId(d.ActId, o.Id);
                            break;
                        default:
                            continue;
                    }
                    if (classId < 0) continue; // recon Preset.cpp:350 — classId < 0 dropped
                    Add(et, classId, o.X + wox - ox, o.Y + woy - oy);
                }
            }
        }

        // Seeded outdoor shrines/wells (SpawnAct12Shrines) — listed as presets.
        foreach (var s in DrlgGenerator.OutdoorShrines(pLevel, act1ShrineFilesOnly: false)) Add(2, s.ClassId, s.X, s.Y);
        return outList;
    }
}

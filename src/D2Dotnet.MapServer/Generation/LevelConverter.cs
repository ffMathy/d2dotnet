using D2Dotnet.Data;
using D2Dotnet.Maps;
using D2Dotnet.MapServer.Data;

namespace D2Dotnet.MapServer.Generation;

/// <summary>
/// Turns a generated level (<see cref="MapLevel"/>) into the JSON level the upstream generator printed
/// (upstream <c>map/d2_client.c</c> <c>d2_dump_map</c>, <c>dump_objects</c> and <c>dump_map_collision</c>), with
/// names filled in as upstream's server did afterwards (<c>Diablo2MapProcess.fixMap</c>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>offset</c> and <c>size</c> are the level's world tile origin and size times 5 (subtiles).</item>
/// <item><c>objects</c>: the preset npcs and objects, then the seeded outdoor shrines, then one exit per warp; all in
/// level-local subtiles. Npcs and objects upstream filters out (<see cref="UselessPresets"/>) are left out.</item>
/// <item><c>map</c>: the collision map run-length encoded per row (see <see cref="EncodeCollision"/>).</item>
/// </list>
/// </remarks>
public static class LevelConverter
{
    private const int SubTiles = 5;

    /// <summary>Objects with an id at or above this are not looked up in objects.txt by the generator (upstream).</summary>
    public const int ObjectTxtLimit = 580;

    /// <summary>Convert one generated level.</summary>
    public static Diablo2Level Convert(MapLevel level, GeneratedAct act, GameDatabase data)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(act);
        ArgumentNullException.ThrowIfNull(data);

        var width = level.Bounds.Width * SubTiles;
        var height = level.Bounds.Height * SubTiles;
        return new Diablo2Level
        {
            Type = "map",
            Id = (int)level.Id,
            Name = data.LevelName((int)level.Id) ?? RawLevelName(data, (int)level.Id),
            Offset = new LevelOffset(level.Bounds.X * SubTiles, level.Bounds.Y * SubTiles),
            Size = new LevelSize(width, height),
            Objects = Objects(level, act, data),
            Map = EncodeCollision(level.Collision, width),
        };
    }

    /// <summary>
    /// The name of a level that has no typed levels.txt record (a row the typed table leaves out): its raw
    /// <c>LevelName</c> cell, trimmed, as the generator reads it; empty when levels.txt has no such id at all.
    /// </summary>
    private static string RawLevelName(GameDatabase data, int levelId)
    {
        var levels = data.GetTable("levels");
        return levels?.FindRow("Id", levelId) is int row ? JsNumber.TrimJs(levels.GetString(row, "LevelName")) : "";
    }

    /// <summary>The <c>objects</c> array of a level.</summary>
    public static List<Diablo2LevelObject> Objects(MapLevel level, GeneratedAct act, GameDatabase data)
    {
        var objects = new List<Diablo2LevelObject>();
        var seen = new HashSet<(int Id, int X, int Y)>();

        foreach (var preset in level.Presets)
        {
            var (x, y) = (preset.Position.X, preset.Position.Y);
            if (preset.Kind == PresetKind.Npc)
            {
                if (UselessPresets.UselessNpcs.Contains(preset.ClassId)) continue;
                objects.Add(Npc(preset.ClassId, x, y, data));
            }
            else if (preset.Kind == PresetKind.Object)
            {
                if (UselessPresets.UselessObjects.Contains(preset.ClassId)) continue;
                seen.Add((preset.ClassId, x, y));
                objects.Add(Object(preset.ClassId, x, y, data));
            }
        }

        // The game builds the seeded Act I shrines and wells into the room's preset list; the DRLG port reports them apart
        foreach (var shrine in level.Shrines)
        {
            var classId = (int)shrine.Object;
            if (UselessPresets.UselessObjects.Contains(classId)) continue;
            if (!seen.Add((classId, shrine.Position.X, shrine.Position.Y))) continue;
            objects.Add(Object(classId, shrine.Position.X, shrine.Position.Y, data));
        }

        // Upstream only knew warp tiles (UNIT_TYPE_TILE presets); open borders between outdoor levels were never exits
        foreach (var exit in level.Exits)
        {
            if (exit.Kind != ExitKind.Warp) continue;
            var destination = (int)exit.Destination;
            objects.Add(new Diablo2LevelObject
            {
                Id = destination,
                Type = Diablo2LevelObjectType.Exit,
                X = exit.Position.X,
                Y = exit.Position.Y,
                Name = data.LevelName(destination),
                IsGoodExit = IsGoodExit((int)level.Id, destination, (int?)act.StaffTomb ?? 0) ? true : null,
            });
        }
        return objects;
    }

    private static Diablo2LevelObject Npc(int classId, int x, int y, GameDatabase data)
    {
        var npc = data.Npc(classId);
        return new Diablo2LevelObject
        {
            Id = classId,
            Type = Diablo2LevelObjectType.Npc,
            X = x,
            Y = y,
            Name = npc.Name,
            IsSuperUnique = npc.SuperId != null ? true : null,
            SuperId = npc.SuperId,
        };
    }

    private static Diablo2LevelObject Object(int classId, int x, int y, GameDatabase data)
    {
        var info = data.Object(classId);
        var operateFn = classId < ObjectTxtLimit && info is { Selectable: true } ? info.OperateFn : -1;
        return new Diablo2LevelObject
        {
            Id = classId,
            Type = Diablo2LevelObjectType.Object,
            X = x,
            Y = y,
            Name = info?.Name,
            Op = operateFn > -1 ? operateFn : null,
            Class = ObjectClass(classId, operateFn),
        };
    }

    /// <summary>Upstream <c>get_object_class</c>: the object class from objects.txt <c>OperateFn</c> (-1 when not selectable).</summary>
    public static string? ObjectClass(int classId, int operateFn)
    {
        switch (operateFn)
        {
            case 1: return Diablo2ItemClasses.Casket;
            case 2: return Diablo2ItemClasses.Shrine;
            case 3: return Diablo2ItemClasses.Urn;
            case 5: return Diablo2ItemClasses.Barrel;
            case 7: return Diablo2ItemClasses.BarrelExploding;
            case 14: return "bolder";
            case 19: return Diablo2ItemClasses.RackArmor;
            case 20: return Diablo2ItemClasses.RackWeapon;
            case 22: return Diablo2ItemClasses.Well;
            case 23: return Diablo2ItemClasses.Waypoint;
            case 68: return Diablo2ItemClasses.UrnEvil;
            case 30: return Diablo2ItemClasses.ChestExploding;
            case 40:
            case 41:
            case 59:
            case 58:
            case 4:
                return Diablo2ItemClasses.Chest;
            case 8:
            case 18:
            case 29:
                return Diablo2ItemClasses.Door;
            // Diablo seals
            case 54:
            case 52:
            case 55:
            case 56:
            // Tristram stones
            case 9:
            // Compelling orb
            case 53:
            // Horadric orifice
            case 25:
            // Sewer lever
            case 45:
            // Hell forge
            case 49:
            // Tome
            case 28:
            // Sun altar
            case 24:
                return Diablo2ItemClasses.Quest;
        }

        if (classId == 580 || classId == 581) return Diablo2ItemClasses.ChestSuper;
        return null;
    }

    /// <summary>Upstream <c>is_good_exit</c>: exits that lead towards quest areas.</summary>
    /// <param name="levelId">The level the exit is in.</param>
    /// <param name="exitId">The level the exit leads to.</param>
    /// <param name="staffTombLevelId">Act II's true Tal Rasha's Tomb (<see cref="GeneratedAct.StaffTomb"/>), 0 elsewhere.</param>
    public static bool IsGoodExit(int levelId, int exitId, int staffTombLevelId)
    {
        // Act 1: Blood Moor -> Den of Evil, Tamoe Highland -> Pit, Black Marsh -> Forgotten Tower
        if (levelId == 2 && exitId == 8) return true;
        if (levelId == 7 && exitId == 12) return true;
        if (levelId == 6 && exitId == 20) return true;

        // Act 2: the correct tomb
        if (staffTombLevelId != 0 && exitId == staffTombLevelId) return true;
        // Staff components: Far Oasis -> Maggot Lair, Valley of Snakes -> Claw Viper Temple, Rocky Waste -> Stony Tomb
        if (levelId == 43 && exitId == 62) return true;
        if (levelId == 45 && exitId == 58) return true;
        if (levelId == 41 && exitId == 55) return true;
        // Lost City -> Ancient Tunnels
        if (levelId == 44 && exitId == 65) return true;

        // Act 3 parts: Spider Forest -> Spider Cavern, Flayer Jungle -> Flayer Dungeon; Kurast Bazaar -> Ruined Temple
        if (levelId == 76 && exitId == 85) return true;
        if (levelId == 78 && exitId == 88) return true;
        if (levelId == 80 && exitId == 94) return true;

        // Act 5: Crystalline Passage -> Frozen River
        if (levelId == 113 && exitId == 114) return true;

        return false;
    }

    /// <summary>
    /// Upstream <c>dump_map_collision</c> over upstream's collision grid (<c>map.c</c>): one row per y from 0 to the
    /// last row holding a walkable subtile (at least one row); each row lists the lengths of alternating runs,
    /// starting with a (possibly empty) blocked run, over <paramref name="width"/> subtiles. The trailing run of a row
    /// is never written, so an all blocked row is <c>[]</c>.
    /// </summary>
    /// <remarks>
    /// A subtile is walkable when its raw collision value is even (upstream: <c>value % 2 == 0</c>, i.e.
    /// <c>COLLIDE_BLOCK_PLAYER</c> clear). The value 1024 (0x400 alone) counts as blocked, as upstream rewrote it to 1,
    /// and uncovered subtiles (<see cref="CollisionMap.Uncovered"/>, upstream's -1) are blocked.
    /// </remarks>
    public static List<List<int>> EncodeCollision(CollisionMap? grid, int width)
    {
        var maxY = 0;
        if (grid != null)
        {
            for (var y = grid.Height - 1; y > 0 && maxY == 0; y--)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    if (IsWalkable(grid, x, y))
                    {
                        maxY = y;
                        break;
                    }
                }
            }
        }

        var rows = new List<List<int>>(maxY + 1);
        for (var y = 0; y <= maxY; y++)
        {
            var row = new List<int>();
            var lastWalkable = false;
            var count = 0;
            for (var x = 0; x < width; x++)
            {
                var walkable = IsWalkable(grid, x, y);
                if (walkable == lastWalkable)
                {
                    count++;
                    continue;
                }
                row.Add(count);
                count = 1;
                lastWalkable = walkable;
            }
            rows.Add(row);
        }
        return rows;
    }

    private static bool IsWalkable(CollisionMap? grid, int x, int y)
    {
        if (grid == null || (uint)x >= (uint)grid.Width || (uint)y >= (uint)grid.Height) return false;
        int value = grid[x, y];
        if (value == CollisionMap.Uncovered || value == 1024) return false;
        return value % 2 == 0;
    }
}

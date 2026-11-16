using D2Dotnet.Data;

namespace D2Dotnet.MapServer.Generation;

/// <summary>Name and object data of a level object: objects.txt <c>Name</c> (translated), and its operate function.</summary>
/// <param name="Name">The display name.</param>
/// <param name="OperateFn">objects.txt <c>OperateFn</c>.</param>
/// <param name="Selectable">objects.txt <c>Selectable0</c> is set (upstream only reports <c>op</c> for those).</param>
public sealed record GameObjectInfo(string Name, int OperateFn, bool Selectable);

/// <summary>What the server knows about an npc class id.</summary>
/// <param name="Name">The display name, or null.</param>
/// <param name="SuperId">The SuperUniques.txt index when the npc is a super unique.</param>
public readonly record struct NpcInfo(string? Name, int? SuperId);

/// <summary>
/// The names the server labels levels, npcs, super uniques and objects with (upstream <c>Diablo2MpqData</c>, which read
/// <c>levels.bin</c>, <c>monstats.bin</c>, <c>objects.bin</c> and the <c>.tbl</c> string tables out of the classic
/// MPQs): the library's typed tables and <see cref="Localization"/>.
/// </summary>
/// <remarks>
/// Each name is the string table text of the table's string key (Levels <c>LevelName</c>, MonStats <c>NameStr</c>,
/// SuperUniques <c>Name</c>, Objects <c>Name</c>), or the key itself when no string table has it; trimmed.
/// Npc ids follow <see cref="Maps.PresetUnit.ClassId"/>: a MonStats game row (the <c>Expansion</c> divider row not
/// counted), the MonStats game row count + a SuperUniques row for a super unique, and anything above that for a
/// MonPlace entry (which has no name).
/// </remarks>
public static class GameNames
{
    /// <summary>Display name of a level, or null for an unknown level.</summary>
    public static string? LevelName(this GameDatabase data, int levelId)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.Localization.GetLevelName((AreaId)levelId);
    }

    /// <summary>Objects.txt data of an object class, or null when the table has no such row or the row has no name.</summary>
    public static GameObjectInfo? Object(this GameDatabase data, int objectId)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Objects.Find((ObjectId)objectId) is not { } info) return null;
        var name = data.Localization.GetObjectName(info.Id);
        return name == null ? null : new GameObjectInfo(name, info.OperateFn, info.Selectable);
    }

    /// <summary>Name and super unique id of an npc class id (upstream <c>FixMap</c> for npcs).</summary>
    public static NpcInfo Npc(this GameDatabase data, int classId)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (classId < 0) return default;
        var monsters = data.Monsters.Count;
        if (classId < monsters) return new NpcInfo(data.Localization.GetMonsterName((MonsterId)classId), null);
        var superId = classId - monsters;
        if (superId < data.SuperUniques.Count) return new NpcInfo(data.Localization.GetSuperUniqueName(superId), superId);
        return default; // a MonPlace entry
    }
}

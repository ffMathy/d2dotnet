using D2Dotnet.Map.Generator.Native;

namespace D2Dotnet.Map.Generator.Game;

/// <summary>
/// Port of upstream map/d2_client_version.h: dispatches D2Common calls to the Path of Diablo or
/// the Project Diablo 2 / 1.13c exports, and caches the last loaded act per act id.
/// </summary>
public sealed class D2Common
{
    private readonly ID2Functions _fn;
    private readonly D2Addresses _ptr;

    /* Cache the recently generated acts if they change unload it */
    private readonly uint[] _acts = [0, 0, 0, 0, 0];
    private readonly int[] _actSeeds = [0, 0, 0, 0, 0];
    private readonly int[] _actDiff = [-1, -1, -1, -1, -1];

    public D2Common(ID2Functions functions, D2Addresses addresses)
    {
        _fn = functions;
        _ptr = addresses;
    }

    private const uint TRUE = 1;
    private const uint FALSE = 0;

    /// <summary><c>d2common_load_act_run</c>.</summary>
    public uint LoadActRun(D2Version gameVersion, int actId, int seed, int difficulty)
    {
        if (gameVersion == D2Version.VersionPathOfDiablo)
            return _fn.D2COMMON_Pod_LoadAct((uint)actId, (uint)seed, TRUE, FALSE, (uint)difficulty, 0, 1, _ptr[D2Ptr.D2CLIENT_Pod_LoadAct_1], _ptr[D2Ptr.D2CLIENT_Pod_LoadAct_2]);
        if (gameVersion == D2Version.VersionProjectDiablo2)
            return _fn.D2COMMON_Pd2_LoadAct((uint)actId, (uint)seed, TRUE, FALSE, (uint)difficulty, 0, 1, _ptr[D2Ptr.D2CLIENT_Pd2_LoadAct_1], _ptr[D2Ptr.D2CLIENT_Pd2_LoadAct_2]);
        if (gameVersion == D2Version.VersionDiablo2)
            return _fn.D2COMMON_Pd2_LoadAct((uint)actId, (uint)seed, TRUE, FALSE, (uint)difficulty, 0, 1, _ptr[D2Ptr.D2CLIENT_Pd2_LoadAct_1], _ptr[D2Ptr.D2CLIENT_Pd2_LoadAct_2]);
        return 0;
    }

    /// <summary><c>d2common_unload_act</c>.</summary>
    public void UnloadAct(D2Version gameVersion, uint pAct)
    {
        if (gameVersion == D2Version.VersionPathOfDiablo) { _fn.D2COMMON_Pod_UnloadAct(pAct); return; }
        if (gameVersion == D2Version.VersionProjectDiablo2) { _fn.D2COMMON_Pd2_UnloadAct(pAct); return; }
        if (gameVersion == D2Version.VersionDiablo2) _fn.D2COMMON_Pd2_UnloadAct(pAct);
    }

    /// <summary>
    /// <c>d2common_load_act</c>: returns the cached act when seed and difficulty match, otherwise
    /// unloads the previous act (if any) and loads a new one. Like the C code, the act id is not
    /// range checked (it always comes from <c>get_act</c>).
    /// </summary>
    public uint LoadAct(D2Version gameVersion, int actId, int seed, int difficulty)
    {
        /* Cache hit */
        if (_actSeeds[actId] == seed && _actDiff[actId] == difficulty) return _acts[actId];
        /* Seed or difficulty has changed unload old act */
        if (_acts[actId] != 0) UnloadAct(gameVersion, _acts[actId]);

        var pAct = LoadActRun(gameVersion, actId, seed, difficulty);
        _actSeeds[actId] = seed;
        _actDiff[actId] = difficulty;
        _acts[actId] = pAct;

        return pAct;
    }

    /// <summary><c>d2common_get_level_text</c>.</summary>
    public uint GetLevelText(D2Version gameVersion, int levelCode)
    {
        if (gameVersion == D2Version.VersionPathOfDiablo) return _fn.D2COMMON_Pod_GetLevelText((uint)levelCode);
        if (gameVersion == D2Version.VersionProjectDiablo2) return _fn.D2COMMON_Pd2_GetLevelText((uint)levelCode);
        if (gameVersion == D2Version.VersionDiablo2) return _fn.D2COMMON_Pd2_GetLevelText((uint)levelCode);
        return 0;
    }

    /// <summary><c>d2common_init_level</c>.</summary>
    public void InitLevel(D2Version gameVersion, uint pLevel)
    {
        if (gameVersion == D2Version.VersionPathOfDiablo) { _fn.D2COMMON_Pod_InitLevel(pLevel); return; }
        if (gameVersion == D2Version.VersionProjectDiablo2) { _fn.D2COMMON_Pd2_InitLevel(pLevel); return; }
        if (gameVersion == D2Version.VersionDiablo2) _fn.D2COMMON_Pd2_InitLevel(pLevel);
    }

    /// <summary><c>d2common_add_room_data</c>.</summary>
    public void AddRoomData(D2Version gameVersion, IGameMemory mem, uint pAct, uint pLevel, uint pRoom2)
    {
        if (gameVersion == D2Version.VersionPathOfDiablo) { _fn.D2COMMON_Pod_AddRoomData(pAct, LevelNo(mem, pLevel), PosX(mem, pRoom2), PosY(mem, pRoom2), 0); return; }
        if (gameVersion == D2Version.VersionProjectDiablo2) { _fn.D2COMMON_Pd2_AddRoomData(pAct, LevelNo(mem, pLevel), PosX(mem, pRoom2), PosY(mem, pRoom2), 0); return; }
        if (gameVersion == D2Version.VersionDiablo2) _fn.D2COMMON_Pd2_AddRoomData(pAct, LevelNo(mem, pLevel), PosX(mem, pRoom2), PosY(mem, pRoom2), 0);
    }

    /// <summary>
    /// <c>d2common_remove_room_data</c>. NOTE: upstream calls the *AddRoomData* exports here
    /// (the RemoveRoomData pointers are resolved but never called); this port keeps that
    /// behaviour so the game sees exactly the same calls.
    /// </summary>
    public void RemoveRoomData(D2Version gameVersion, IGameMemory mem, uint pAct, uint pLevel, uint pRoom2)
    {
        if (gameVersion == D2Version.VersionPathOfDiablo) { _fn.D2COMMON_Pod_AddRoomData(pAct, LevelNo(mem, pLevel), PosX(mem, pRoom2), PosY(mem, pRoom2), 0); return; }
        if (gameVersion == D2Version.VersionProjectDiablo2) { _fn.D2COMMON_Pd2_AddRoomData(pAct, LevelNo(mem, pLevel), PosX(mem, pRoom2), PosY(mem, pRoom2), 0); return; }
        if (gameVersion == D2Version.VersionDiablo2) _fn.D2COMMON_Pd2_AddRoomData(pAct, LevelNo(mem, pLevel), PosX(mem, pRoom2), PosY(mem, pRoom2), 0);
    }

    /// <summary><c>d2common_get_level</c>.</summary>
    public uint GetLevel(D2Version gameVersion, uint misc, uint levelCode)
    {
        if (gameVersion == D2Version.VersionPathOfDiablo) return _fn.D2COMMON_Pod_GetLevel(misc, levelCode);
        if (gameVersion == D2Version.VersionProjectDiablo2) return _fn.D2COMMON_Pd2_GetLevel(misc, levelCode);
        if (gameVersion == D2Version.VersionDiablo2) return _fn.D2COMMON_Pd2_GetLevel(misc, levelCode);
        return 0;
    }

    /// <summary><c>d2common_get_object_txt</c>.</summary>
    public uint GetObjectTxt(D2Version gameVersion, uint dwTxtFileNo)
    {
        if (gameVersion == D2Version.VersionPathOfDiablo) return _fn.D2COMMON_Pod_GetObjectTxt(dwTxtFileNo);
        if (gameVersion == D2Version.VersionProjectDiablo2) return _fn.D2COMMON_Pd2_GetObjectTxt(dwTxtFileNo);
        if (gameVersion == D2Version.VersionDiablo2) return _fn.D2COMMON_Pd2_GetObjectTxt(dwTxtFileNo);
        return 0;
    }

    // Field reads happen at call time, in argument order, like the C code.
    private static int LevelNo(IGameMemory mem, uint pLevel) => (int)mem.Read<Level>(pLevel).dwLevelNo;
    private static int PosX(IGameMemory mem, uint pRoom2) => (int)mem.Read<Room2>(pRoom2).dwPosX;
    private static int PosY(IGameMemory mem, uint pRoom2) => (int)mem.Read<Room2>(pRoom2).dwPosY;
}

using D2Dotnet.Map.Generator.Data;
using D2Dotnet.Map.Generator.Native;
using D2Dotnet.Map.Generator.Output;

namespace D2Dotnet.Map.Generator.Game;

/// <summary>
/// Port of upstream map/d2_client.c: initialises the D2 client inside this process and dumps
/// levels (objects + collision map) as JSON. Every log call keeps the upstream
/// <c>__FILE__:__LINE__</c>.
/// </summary>
public sealed class D2Client
{
    public const int UNIT_TYPE_PLAYER = 0;
    public const int UNIT_TYPE_NPC = 1;
    public const int UNIT_TYPE_OBJECT = 2;
    public const int UNIT_TYPE_MISSILE = 3;
    public const int UNIT_TYPE_ITEM = 4;
    public const int UNIT_TYPE_TILE = 5;

    public const string DIABLO_2 = "Diablo II";
    public const string DIABLO_2_VERSION = "v1.xy";

    /// <summary><c>sizeof(d2client_struct)</c>.</summary>
    public const int D2ClientStructSize = 0x211;

    /// <summary>Registry key whose <c>InstallPath</c> value is set to the game folder.</summary>
    public const string RegistryKey = "SOFTWARE\\Blizzard Entertainment\\Diablo II";

    private const string F = "map/d2_client.c";

    private readonly GeneratorContext _ctx;
    private readonly GameVersions _versions;
    private readonly MapGrid _map = new();

    private D2Common? _common;
    private uint _d2Client;
    private uint _szD2;
    private uint _szDiablo2;
    private uint _szDiablo2Version;
    private uint _szEng;

    public D2Client(GeneratorContext ctx)
    {
        _ctx = ctx;
        _versions = new GameVersions(ctx.Files, ctx.Log);
    }

    /// <summary><c>gameVersion</c>.</summary>
    public D2Version GameVersion { get; private set; } = D2Version.VersionUnknown;

    /// <summary><c>D2_DIR</c>.</summary>
    public string D2Dir { get; private set; } = "";

    /// <summary>Resolved d2_ptrs.h globals (after <see cref="GameInit"/>).</summary>
    public D2Addresses? Addresses { get; private set; }

    /// <summary>The collision grid (exposed for tests).</summary>
    public MapGrid Map => _map;

    /// <summary>Address of the static <c>D2Client</c> struct.</summary>
    public uint D2ClientStruct => _d2Client;

    private Logger Log => _ctx.Log;
    private JsonWriter Json => _ctx.Json;
    private IGameMemory Mem => _ctx.Memory;
    private D2Common Common => _common ?? throw new InvalidOperationException("d2_game_init has not run");

    private static LogSource At(int line) => new(F, line);

    private void AllocateGlobals()
    {
        if (_d2Client != 0) return;
        var p = _ctx.Platform;
        _d2Client = p.AllocateStatic(D2ClientStructSize);
        _szD2 = p.AllocateAnsiString("D2");
        _szDiablo2 = p.AllocateAnsiString(DIABLO_2);
        _szDiablo2Version = p.AllocateAnsiString(DIABLO_2_VERSION);
        _szEng = p.AllocateAnsiString("ENG");
    }

    private void SetClientInit()
    {
        // D2Client.dwInit = 1; D2Client.fpInit = (DWORD)D2ClientInterface;
        Mem.Write<uint>(_d2Client + 0x00, 1);
        Mem.Write(_d2Client + 0x20D, _ctx.Platform.D2ClientInterfaceCallback(_d2Client));
    }

    /// <summary><c>d2_game_init_pod</c>.</summary>
    private void GameInitPod(ID2Functions fn, D2Addresses a)
    {
        Mem.Write<uint>(a[D2Ptr.STORM_Pod_MPQHashTable], 0); // *p_STORM_Pod_MPQHashTable = (DWORD)NULL;
        SetClientInit();

        Log.Trace(At(69), "Init:Dll", LogField.S("dll", "Fog.dll"));
        fn.FOG_10021(_szD2);
        fn.FOG_10019(_szDiablo2, _ctx.Platform.ExceptionHandlerCallback(), _szDiablo2Version, 1);
        fn.FOG_10101(1, 0);
        fn.FOG_10089(1);
        if (fn.FOG_10218() == 0)
        {
            Log.Error(At(75), "Init:Dll:Failed", LogField.S("dll", "Fog.dll"));
            _ctx.Platform.ExitProcess(1);
        }
        Log.Debug(At(78), "Init:Dll:Done", LogField.S("dll", "Fog.dll"));

        Log.Trace(At(80), "Init:Dll", LogField.S("dll", "D2Win.dll"));
        if (fn.D2WIN_10174() == 0 || fn.D2WIN_10072(0, 0, 0, _d2Client) == 0)
        {
            Log.Error(At(82), "Init:Dll:Failed", LogField.S("dll", "D2Win.dll"));
            _ctx.Platform.ExitProcess(1);
        }
        Log.Debug(At(85), "Init:Dll:Done", LogField.S("dll", "D2Win.dll"));

        Log.Trace(At(87), "Init:Dll", LogField.S("dll", "D2Lang.dll"));
        fn.D2LANG_10009(0, _szEng, 0);
        Log.Debug(At(89), "Init:Dll:Done", LogField.S("dll", "D2Lang.dll"));

        Log.Trace(At(91), "Init:Dll", LogField.S("dll", "D2Client.dll"));
        fn.D2COMMON_Pod_InitDataTables(0, 0, 0);
        fn.D2CLIENT_Pod_InitGameMisc();
        Log.Debug(At(94), "Init:Dll:Done", LogField.S("dll", "D2Client.dll"));
    }

    /// <summary><c>d2_game_init_pd2</c> (also used for plain 1.13c Diablo 2).</summary>
    private void GameInitPd2(ID2Functions fn, D2Addresses a)
    {
        Mem.Write<uint>(a[D2Ptr.STORM_Pd2_MPQHashTable], 0); // *p_STORM_Pd2_MPQHashTable = (DWORD)NULL;
        SetClientInit();

        Log.Trace(At(117), "Init:Dll", LogField.S("dll", "Fog.dll"));
        fn.FOG_10021(_szD2);
        fn.FOG_10019(_szDiablo2, _ctx.Platform.ExceptionHandlerCallback(), _szDiablo2Version, 1);
        fn.FOG_10101(1, 0);
        fn.FOG_10089(1);

        if (fn.FOG_10218() == 0)
        {
            Log.Error(At(124), "Init:Dll:Failed", LogField.S("dll", "Fog.dll"));
            _ctx.Platform.ExitProcess(1);
        }
        Log.Debug(At(127), "Init:Dll:Done", LogField.S("dll", "Fog.dll"));

        Log.Trace(At(129), "Init:Dll", LogField.S("dll", "D2Win.dll"));
        if (fn.D2WIN_10086() == 0 || fn.D2WIN_10005(0, 0, 0, _d2Client) == 0)
        {
            Log.Error(At(131), "InitFailed", LogField.S("dll", "D2Win.dll"));
            _ctx.Platform.ExitProcess(1);
        }
        Log.Debug(At(134), "Init:Dll:Done", LogField.S("dll", "D2Win.dll"));

        Log.Trace(At(136), "Init:Dll", LogField.S("dll", "D2Lang.dll"));
        fn.D2LANG_10008(0, _szEng, 0);
        Log.Debug(At(138), "Init:Dll:Done", LogField.S("dll", "D2Lang.dll"));

        Log.Trace(At(140), "Init:Dll", LogField.S("dll", "D2Client.dll"));
        fn.D2COMMON_Pd2_InitDataTables(0, 0, 0);
        fn.D2CLIENT_Pd2_InitGameMisc();
        Log.Debug(At(143), "Init:Dll:Done", LogField.S("dll", "D2Client.dll"));
    }

    /// <summary><c>d2_game_init</c>.</summary>
    public void GameInit(string folderName)
    {
        var platform = _ctx.Platform;
        Log.Debug(At(147), "Init:Dll", LogField.S("path", folderName));

        GameVersion = _versions.GameVersion(folderName);
        if (GameVersion == D2Version.VersionUnknown)
        {
            Log.Error(At(151), "Init:Failed:UnknownGameVersion", LogField.S("path", folderName));
            platform.ExitProcess(1);
        }

        var gamePath = GameVersions.GameVersionPath(GameVersion);
        if (gamePath == null)
        {
            // Unreachable: every known version has a path. (The C call would pass NULL to lk_s.)
            Log.Error(At(157), "Init:Failed:UnknownGamePath", LogField.S("path", folderName), LogField.S("version", ""));
            platform.ExitProcess(1);
        }

        AllocateGlobals();

        if (platform.RegOpenCurrentUserKey(RegistryKey, out var hKey))
        {
            Log.Trace(At(167), "Registry:Opened");
        }
        else
        {
            Log.Error(At(169), "Registry:Failed:Open");
            platform.ExitProcess(1);
        }

        platform.RegSetStringValue(hKey, "InstallPath", folderName);
        Log.Info(At(176), "Registry:InstallPath", LogField.S("value", folderName));
        platform.RegCloseKey(hKey);

        D2Dir = folderName + "/" + gamePath; // sprintf_s(D2_DIR, sizeof(D2_DIR), "%s/%s", ...)
        Log.Info(At(180), "Init:Game", LogField.S("version", gamePath), LogField.S("path", D2Dir));
        Mem.Fill(_d2Client, 0, D2ClientStructSize); // memset(&D2Client, 0, sizeof(d2client_struct))
        platform.SetCurrentDirectory(D2Dir);

        var addresses = new DllOffsets(_ctx.Modules, Log, _ctx.Exit).DefineOffsets();
        Addresses = addresses;
        Log.Debug(At(185), "Init:Offsets:Defined");

        var fn = platform.BindFunctions(addresses);
        _common = new D2Common(fn, addresses);

        if (GameVersion == D2Version.VersionPathOfDiablo)
        {
            GameInitPod(fn, addresses);
        }
        else if (GameVersion == D2Version.VersionProjectDiablo2 || GameVersion == D2Version.VersionDiablo2)
        {
            GameInitPd2(fn, addresses);
        }
        else
        {
            Log.Error(At(192), "Init:Failed:GameInit", LogField.S("path", D2Dir));
            platform.ExitProcess(1);
        }

        platform.SetCurrentDirectory(folderName);
    }

    /// <summary><c>d2_get_level</c>: walk ActMisc's level list, otherwise ask D2Common to create it.</summary>
    public uint GetLevel(uint misc, uint levelCode)
    {
        var levelData = Common.GetLevelText(GameVersion, (int)levelCode);
        if (levelData == 0) return 0;

        for (var pLevel = Mem.Read<ActMisc>(misc).pLevelFirst; pLevel != 0; pLevel = Mem.Read<Level>(pLevel).pNextLevel)
        {
            if (Mem.Read<Level>(pLevel).dwLevelNo == levelCode) return pLevel;
        }

        return Common.GetLevel(GameVersion, misc, levelCode);
    }

    /// <summary><c>add_collision_data</c>: copy a room's collision words into the grid (1024 becomes 1).</summary>
    public void AddCollisionData(uint pCol, int originX, int originY)
    {
        if (pCol == 0) return;
        var col = Mem.Read<CollMap>(pCol);

        var x = unchecked((int)col.dwPosGameX - originX);
        var y = unchecked((int)col.dwPosGameY - originY);
        var cx = (int)col.dwSizeGameX;
        var cy = (int)col.dwSizeGameY;

        var nLimitX = x + cx;
        var nLimitY = y + cy;

        var p = col.pMapStart;
        for (var j = y; j < nLimitY; j++)
        {
            for (var i = x; i < nLimitX; i++)
            {
                int pVal = Mem.Read<ushort>(p);
                if (pVal == 1024) pVal = 1;
                _map.Set(i, j, pVal);
                p += 2;
            }
        }
    }

    /// <summary><c>get_object_type</c>.</summary>
    public static string? GetObjectType(int code)
    {
        if (GameObjects.IsUseless(code)) return null;
        return "object";
    }

    /// <summary><c>get_object_class</c> (the name argument is unused upstream too).</summary>
    public static string? GetObjectClass(int code, string? name, int operateFn)
    {
        _ = name;
        switch (operateFn)
        {
            case 1: return "casket";
            case 2: return "shrine";
            case 3: return "urn";

            case 5: return "barrel";
            case 7: return "barrel-exploding";
            case 14: return "bolder";
            case 19: return "rack-armor";
            case 20: return "rack-weapon";
            case 22: return "well";
            case 23: return "waypoint";
            case 68: return "urn-evil";
            case 30: return "chest-exploding";
            case 40:
            case 41:
            case 59:
            case 58:
            case 4:
                return "chest";
            case 8:
            case 18:
            case 29:
                return "door";
            /* Diablo Seals */
            case 54:
            case 52:
            case 55:
            case 56:
            /* Trist stones */
            case 9:
            /* complelling orb */
            case 53:
            /* Horiadric orifice */
            case 25:
            /* Sewer Lever */
            case 45:
            // /* Hell forge */
            case 49:
            /* Tome */
            case 28:
            /* Sun altar */
            case 24:
                return "quest";
        }

        if (code == 580 || code == 581) return "chest-super";
        return null;
    }

    /// <summary><c>is_good_exit</c>: exits that lead towards quest areas.</summary>
    public bool IsGoodExit(uint pAct, uint pLevel, int exitId)
    {
        var levelNo = Mem.Read<Level>(pLevel).dwLevelNo;
        bool Is(AreaLevel a) => levelNo == (uint)a;

        // Act 1
        // BloodMoor -> Den of evil
        if (Is(AreaLevel.BloodMoor) && exitId == (int)AreaLevel.DenOfEvil) return true;

        // Tamoe Highlands -> Pit
        if (Is(AreaLevel.TamoeHighland) && exitId == (int)AreaLevel.PitLevel1) return true;
        // Black Forest -> ForgottenTower
        if (Is(AreaLevel.BlackMarsh) && exitId == (int)AreaLevel.ForgottenTower) return true;

        // Act 2
        // Correct tomb
        if ((uint)exitId == Mem.Read<ActMisc>(Mem.Read<Act>(pAct).pMisc).dwStaffTombLevel) return true;
        // Staff Components
        if (Is(AreaLevel.FarOasis) && exitId == (int)AreaLevel.MaggotLairLevel1) return true;
        if (Is(AreaLevel.ValleyOfSnakes) && exitId == (int)AreaLevel.ClawViperTempleLevel1) return true;
        if (Is(AreaLevel.RockyWaste) && exitId == (int)AreaLevel.StonyTombLevel1) return true;

        // Ancient tunnels
        if (Is(AreaLevel.LostCity) && exitId == (int)AreaLevel.AncientTunnels) return true;

        // Act 3
        // Parts
        if (Is(AreaLevel.SpiderForest) && exitId == (int)AreaLevel.SpiderCavern) return true;
        if (Is(AreaLevel.FlayerJungle) && exitId == (int)AreaLevel.FlayerDungeonLevel1) return true;

        // Kurast -> RuinedTemple
        if (Is(AreaLevel.KurastBazaar) && exitId == (int)AreaLevel.RuinedTemple) return true;

        // Act 5
        // Crystaline passage -> Frozen River
        if (Is(AreaLevel.CrystallinePassage) && exitId == (int)AreaLevel.FrozenRiver) return true;

        return false;
    }

    /// <summary><c>dump_objects</c>: preset NPCs, objects and exits of one Room2.</summary>
    public int DumpObjects(uint pAct, uint pLevel, uint pRoom2)
    {
        var level = Mem.Read<Level>(pLevel);
        var offsetX = unchecked((int)(level.dwPosX * 5));
        var offsetY = unchecked((int)(level.dwPosY * 5));

        var room2 = Mem.Read<Room2>(pRoom2);
        var roomOffsetX = unchecked((int)(room2.dwPosX * 5) - offsetX);
        var roomOffsetY = unchecked((int)(room2.dwPosY * 5) - offsetY);

        for (var pPresetUnit = Mem.Read<Room2>(pRoom2).pPreset; pPresetUnit != 0; pPresetUnit = Mem.Read<PresetUnit>(pPresetUnit).pPresetNext)
        {
            string? objectType = null;
            string? objectName = null;
            string? objectClass = null;
            var isGoodExit = false;
            var operateFn = -1;

            var objectId = -1;

            var preset = Mem.Read<PresetUnit>(pPresetUnit);
            var coordX = unchecked(roomOffsetX + (int)preset.dwPosX);
            var coordY = unchecked(roomOffsetY + (int)preset.dwPosY);

            if (preset.dwType == UNIT_TYPE_NPC)
            {
                if (Npcs.IsUseless((int)preset.dwTxtFileNo)) continue;
                objectType = "npc";
                objectId = (int)preset.dwTxtFileNo;
            }
            else if (preset.dwType == UNIT_TYPE_OBJECT)
            {
                objectType = GetObjectType((int)preset.dwTxtFileNo);
                if (objectType == null) continue;
                objectId = (int)preset.dwTxtFileNo;
                if (preset.dwTxtFileNo < 580)
                {
                    var txt = Common.GetObjectTxt(GameVersion, preset.dwTxtFileNo);
                    var objectTxt = Mem.Read<ObjectTxt>(txt);
                    objectName = Mem.ReadCString(txt); // txt->szName is at offset 0
                    if (objectTxt.nSelectable0 != 0) operateFn = objectTxt.nOperateFn;
                }
                objectClass = GetObjectClass((int)preset.dwTxtFileNo, objectName, operateFn);
            }
            else if (preset.dwType == UNIT_TYPE_TILE)
            {
                for (var pRoomTile = Mem.Read<Room2>(pRoom2).pRoomTiles; pRoomTile != 0; pRoomTile = Mem.Read<RoomTile>(pRoomTile).pNext)
                {
                    var tile = Mem.Read<RoomTile>(pRoomTile);
                    if (Mem.Read<uint>(tile.nNum) == preset.dwTxtFileNo)
                    {
                        objectId = (int)Mem.Read<Level>(Mem.Read<Room2>(tile.pRoom2).pLevel).dwLevelNo;
                        if (IsGoodExit(pAct, pLevel, objectId)) isGoodExit = true;
                        objectType = "exit";
                    }
                }
            }

            if (objectType != null)
            {
                Json.ObjectStart();
                Json.KeyValue("id", objectId);
                Json.KeyValue("type", objectType);
                Json.KeyValue("x", coordX);
                Json.KeyValue("y", coordY);
                if (objectName != null) Json.KeyValue("name", objectName);
                if (operateFn > -1) Json.KeyValue("op", operateFn);
                if (isGoodExit) Json.KeyValue("isGoodExit", true);
                if (objectClass != null) Json.KeyValue("class", objectClass);
                Json.ObjectEnd();
            }
        }
        return 0;
    }

    /// <summary>
    /// <c>dump_map_collision</c>: one run-length row per y in [0, maxY]; each row lists the lengths
    /// of alternating runs starting with a (possibly empty) blocked run. The trailing run of each
    /// row is never written (upstream behaviour).
    /// </summary>
    public void DumpMapCollision(int width, int height)
    {
        _ = height;
        var maxY = _map.MaxY;
        _ = _map.MaxX;
        for (var y = 0; y <= maxY; y++)
        {
            Json.ArrayStart();
            var last = 'X';
            var count = 0;
            var outputCount = 0;
            for (var x = 0; x < width; x++)
            {
                var mapVal = _map.Value(x, y) % 2 != 0 ? 'X' : ' ';
                if (mapVal == last)
                {
                    count++;
                    continue;
                }

                if (outputCount == 0 && last == ' ') _ctx.Stderr.Write("-1, ");

                Json.Value(count);

                outputCount++;
                count = 1;
                last = mapVal;
            }

            Json.ArrayEnd();
        }
    }

    /// <summary><c>get_act</c>: the act a level belongs to.</summary>
    public static int GetAct(int levelCode)
    {
        if (levelCode < 40) return 0;
        if (levelCode < 75) return 1;
        if (levelCode < 103) return 2;
        if (levelCode < 109) return 3;
        if (levelCode < 200) return 4;
        return -1;
    }

    /// <summary><c>d2_dump_map</c>: 0 when a map was written, 1 when the level was skipped.</summary>
    public int DumpMap(int seed, int difficulty, int levelCode)
    {
        var levelData = Common.GetLevelText(GameVersion, levelCode);
        if (levelData == 0) return 1;

        if (GameVersion == D2Version.VersionPathOfDiablo)
        {
            switch (levelCode)
            {
                // Why are these levels broken?
                case 20:
                case 59:
                case 63:
                case 99:
                    return 1;
            }
        }
        else if (GameVersion == D2Version.VersionProjectDiablo2)
        {
            switch (levelCode)
            {
                case 150:
                    return 1;
            }
        }

        var actId = GetAct(levelCode);
        var pAct = Common.LoadAct(GameVersion, actId, seed, difficulty);
        if (pAct == 0) return 1;

        var pLevel = GetLevel(Mem.Read<Act>(pAct).pMisc, (uint)levelCode); // Loading Town Level
        if (pLevel == 0) return 1;

        var levelName = Mem.ReadCString(levelData + 0xF5); // levelData->szName (offset 0xF5)
        // Upstream re-checks !pLevel here and would log "Map:SkippingLevel:FailedLoading"
        // (d2_client.c:454); that branch is dead code because of the return above.

        if (Mem.Read<Level>(pLevel).pRoom2First == 0) Common.InitLevel(GameVersion, pLevel);
        if (Mem.Read<Level>(pLevel).pRoom2First == 0)
        {
            Log.Warn(At(460), "Map:SkippingLevel:FailedRoomLoading", LogField.I("mapId", levelCode), LogField.S("mapName", levelName));
            return 1;
        }

        var level = Mem.Read<Level>(pLevel);
        var originX = unchecked((int)(level.dwPosX * 5));
        var originY = unchecked((int)(level.dwPosY * 5));

        var mapWidth = unchecked((int)(level.dwSizeX * 5));
        var mapHeight = unchecked((int)(level.dwSizeY * 5));

        Log.Trace(At(470), "MapInit", LogField.I("actId", actId), LogField.I("mapId", levelCode), LogField.S("mapName", levelName),
            LogField.I("originY", originY), LogField.I("originX", originX), LogField.I("width", mapWidth), LogField.I("height", mapHeight));
        _map.Reset();

        // Start JSON DUMP
        Json.Start();
        Json.KeyValue("type", "map");
        Json.KeyValue("id", levelCode);
        Json.KeyValue("name", levelName);

        Json.ObjectStart("offset");
        Json.KeyValue("x", originX);
        Json.KeyValue("y", originY);
        Json.ObjectEnd();

        Json.ObjectStart("size");
        Json.KeyValue("width", mapWidth);
        Json.KeyValue("height", mapHeight);
        Json.ObjectEnd();

        Json.ArrayStart("objects");

        for (var pRoom2 = Mem.Read<Level>(pLevel).pRoom2First; pRoom2 != 0; pRoom2 = Mem.Read<Room2>(pRoom2).pRoom2Next)
        {
            var bAdded = Mem.Read<Room2>(pRoom2).pRoom1 == 0;

            if (bAdded) Common.AddRoomData(GameVersion, Mem, pAct, pLevel, pRoom2);
            DumpObjects(pAct, pLevel, pRoom2);

            var pRoom1 = Mem.Read<Room2>(pRoom2).pRoom1;
            if (pRoom1 != 0) AddCollisionData(Mem.Read<Room1>(pRoom1).Coll, originX, originY);
            if (bAdded) Common.RemoveRoomData(GameVersion, Mem, pAct, pLevel, pRoom2);
        }

        Json.ArrayEnd();
        Json.ArrayStart("map");
        DumpMapCollision(mapWidth, mapHeight);
        Json.ArrayEnd();
        Json.End();
        return 0;
    }
}

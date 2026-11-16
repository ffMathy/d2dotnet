// Port of libd2 packages/drlg/src/drlg/tables.zig and packages/drlg/src/tables.zig
// (MIT, (c) 2026 jaenster).
//
// DRLG txt-row structs — mechanical transform of the reconstructed 1.14d table headers, with a
// loader that fills them from the .txt files BY COLUMN NAME.
//
// STRUCT LAYOUT is faithful to the recon (field order + types). C-array fields
// (char File[6][60]) stay fixed-size buffers; runtime pointer/grid members (filled later by the
// engine, not from .txt) are kept for faithfulness but left zero/null by the loader.
//
// Sources:
//   D2LvlPrestTxt   ghidra-reconstruct/output/D2Common/DataTbls/LvlTbls.h:45
//   D2LvlTypesTxt   .../LvlTbls.h:73
//   D2LvlWarpTxt    .../LvlTbls.h:81
//   D2LvlMazeTxt    .../D2Common/Drlg/Drlg.h  (struct D2LvlMazeTxt)
//   D2LvlSubTxt     .../D2Common/Drlg/Drlg.h  (struct D2LvlSubTxt)
//   D2LevelDefsTxt  .../_unnamespaced.h       (struct D2LevelDefsTxt)
//
// FINDING: DRLG geometry (SizeX/DrlgType/LevelType/Depend/Offsets/Warp/Vis) is read from
// D2LevelDefsTxt (TXT_LevelDefs_GetLine), NOT the monster/visual D2LevelsTxt — both are parsed
// from the single Levels.txt.

using System.Runtime.InteropServices;
using System.Text;

using D2Dotnet.Data;

namespace D2Dotnet.Maps.Engine;

#pragma warning disable CS0649

/// <summary>LvlPrest.txt row — LvlTbls.h:45. `File[6][60]` = up to 6 DS1 variant names.</summary>
internal unsafe struct D2LvlPrestTxt
{
    public int Def;
    public int LevelId;
    public int Populate;
    public int Logicals;
    public int Outdoors;
    public int Animate;
    public int KillEdge;
    public int FillBlanks;
    public int Expansion;
    public byte field_0x24;
    public int SizeX;
    public int SizeY;
    public int AutoMap;
    public int Scan;
    public int Pops;
    public int PopPad;
    public int Files;
    public fixed byte File[6 * 60];
    public int Dt1Mask;
}

/// <summary>LvlMaze.txt row — Drlg.h. `Rooms[3]` = Normal/Nightmare/Hell section counts.</summary>
internal unsafe struct D2LvlMazeTxt
{
    public int Level;
    public fixed int Rooms[3];
    public int SizeX;
    public int SizeY;
    public int Merge;
}

/// <summary>LvlSub.txt row — Drlg.h. Prob/Trials/Max are 5-wide. The grid/pDrlgFile members are
/// populated at runtime by TXT_AllocTxt_lvlsub, not from the .txt.</summary>
internal unsafe struct D2LvlSubTxt
{
    public int Type;
    public fixed byte File[60];
    public int CheckAll;
    public int BordType;
    public int Dt1Mask;
    public int GridSize;
    public D2DrlgFileStrc* pDrlgFile;
    public GridArray4 pTileTypeGrid;
    public GridArray4 pWallGrid;
    public D2DrlgGridStrc pFloorGrid;
    public D2DrlgGridStrc pShadowGrid;
    public fixed int Prob[5];
    public fixed int Trials[5];
    public fixed int Max[5];
    public int Expansion;
}

/// <summary>LvlWarp.txt row — LvlTbls.h:81.</summary>
internal struct D2LvlWarpTxt
{
    public int Id;
    public int SelectX;
    public int SelectY;
    public int SelectDX;
    public int SelectDY;
    public int ExitWalkX;
    public int ExitWalkY;
    public int OffsetX;
    public int OffsetY;
    public int LitVersion;
    public int Tiles;
    public int Direction;
}

/// <summary>Levels.txt DRLG geometry row — D2LevelDefsTxt (_unnamespaced.h). This is what the
/// closure reads for layout (SizeX[diff]/SizeY[diff], DrlgType, LevelType, …).</summary>
internal unsafe struct D2LevelDefsTxt
{
    public int QuestFlag;
    public int QuestFlagEx;
    public int Layer;
    public fixed int SizeX[3];
    public fixed int SizeY[3];
    public int OffsetX;
    public int OffsetY;
    public int Depend;
    public int DrlgType;
    public int LevelType;
    public int SubType;
    public int SubTheme;
    public int SubWaypoint;
    public int SubShrine;
    public fixed int Vis[8];
    public fixed int Warp[8];
    public int Intensity;
    public int Red;
    public int Green;
    public int Blue;
    public int Portal;
    public int Position;
    public int SaveMonsters;
    public int LOSDraw;
}

#pragma warning restore CS0649

/// <summary>tables.zig Level: one Levels.txt row in the act-table view.</summary>
internal readonly record struct LevelRow(int Row, long Id, string Name, string LevelName, long Act, int DrlgType,
    long LvlType, long SizeX, long SizeY, long OffsetX, long OffsetY, long Depend);

/// <summary>The parsed game tables the generator reads, plus the built row structs. Immutable
/// once built and shared by every generation (each generation copies the row arrays into its
/// own native pool because the closure mutates LvlSub rows during generation).</summary>
internal sealed class GameTables
{
    public ExcelTable Levels { get; }
    public ExcelTable LvlPrest { get; }
    public ExcelTable LvlTypes { get; }
    public ExcelTable LvlMaze { get; }
    public ExcelTable LvlSub { get; }
    public ExcelTable LvlWarp { get; }

    internal readonly D2LvlPrestTxt[] PrestRows;
    internal readonly D2LvlMazeTxt[] MazeRows;
    internal readonly D2LvlSubTxt[] SubRows;
    internal readonly D2LvlWarpTxt[] WarpRows;
    internal readonly D2LevelDefsTxt[] LevelDefRows;

    public GameTables(ExcelTable levels, ExcelTable lvlPrest, ExcelTable lvlTypes, ExcelTable lvlMaze, ExcelTable lvlSub, ExcelTable lvlWarp)
    {
        Levels = levels;
        LvlPrest = lvlPrest;
        LvlTypes = lvlTypes;
        LvlMaze = lvlMaze;
        LvlSub = lvlSub;
        LvlWarp = lvlWarp;
        PrestRows = BuildPrest(lvlPrest);
        MazeRows = BuildMaze(lvlMaze);
        SubRows = BuildSub(lvlSub);
        WarpRows = BuildWarp(lvlWarp);
        LevelDefRows = BuildLevelDefs(levels);
    }

    private static int I32(ExcelTable t, int row, string name) => t.GetInt32(row, name);

    private static unsafe void CopyStr(byte* dst, int cap, string s)
    {
        new Span<byte>(dst, cap).Clear();
        int n = Math.Min(cap - 1, s.Length);
        for (int i = 0; i < n; i++) dst[i] = (byte)s[i];
    }

    private static unsafe D2LvlPrestTxt[] BuildPrest(ExcelTable t)
    {
        var o = new D2LvlPrestTxt[t.RowCount];
        for (int i = 0; i < o.Length; i++)
        {
            ref var r = ref o[i];
            r.Def = I32(t, i, "Def");
            r.LevelId = I32(t, i, "LevelId");
            r.Populate = I32(t, i, "Populate");
            r.Logicals = I32(t, i, "Logicals");
            r.Outdoors = I32(t, i, "Outdoors");
            r.Animate = I32(t, i, "Animate");
            r.KillEdge = I32(t, i, "KillEdge");
            r.FillBlanks = I32(t, i, "FillBlanks");
            r.Expansion = I32(t, i, "Expansion");
            r.SizeX = I32(t, i, "SizeX");
            r.SizeY = I32(t, i, "SizeY");
            r.AutoMap = I32(t, i, "AutoMap");
            r.Scan = I32(t, i, "Scan");
            r.Pops = I32(t, i, "Pops");
            r.PopPad = I32(t, i, "PopPad");
            r.Files = I32(t, i, "Files");
            r.Dt1Mask = I32(t, i, "Dt1Mask");
            fixed (byte* f = r.File)
                for (int k = 0; k < 6; k++) CopyStr(f + k * 60, 60, t.GetString(i, "File" + (k + 1)));
        }
        return o;
    }

    private static unsafe D2LvlMazeTxt[] BuildMaze(ExcelTable t)
    {
        var o = new D2LvlMazeTxt[t.RowCount];
        for (int i = 0; i < o.Length; i++)
        {
            ref var r = ref o[i];
            r.Level = I32(t, i, "Level");
            r.Rooms[0] = I32(t, i, "Rooms");
            r.Rooms[1] = I32(t, i, "Rooms(N)");
            r.Rooms[2] = I32(t, i, "Rooms(H)");
            r.SizeX = I32(t, i, "SizeX");
            r.SizeY = I32(t, i, "SizeY");
            r.Merge = I32(t, i, "Merge");
        }
        return o;
    }

    private static unsafe D2LvlSubTxt[] BuildSub(ExcelTable t)
    {
        var o = new D2LvlSubTxt[t.RowCount];
        for (int i = 0; i < o.Length; i++)
        {
            ref var r = ref o[i];
            r.Type = I32(t, i, "Type");
            fixed (byte* f = r.File) CopyStr(f, 60, t.GetString(i, "File"));
            r.CheckAll = I32(t, i, "CheckAll");
            r.BordType = I32(t, i, "BordType");
            r.Dt1Mask = I32(t, i, "Dt1Mask");
            r.GridSize = I32(t, i, "GridSize");
            r.Expansion = I32(t, i, "Expansion");
            for (int k = 0; k < 5; k++)
            {
                r.Prob[k] = I32(t, i, "Prob" + k);
                r.Trials[k] = I32(t, i, "Trials" + k);
                r.Max[k] = I32(t, i, "Max" + k);
            }
        }
        return o;
    }

    private static D2LvlWarpTxt[] BuildWarp(ExcelTable t)
    {
        var o = new D2LvlWarpTxt[t.RowCount];
        for (int i = 0; i < o.Length; i++)
        {
            var dtxt = t.GetString(i, "Direction");
            o[i] = new D2LvlWarpTxt
            {
                Id = I32(t, i, "Id"),
                SelectX = I32(t, i, "SelectX"),
                SelectY = I32(t, i, "SelectY"),
                SelectDX = I32(t, i, "SelectDX"),
                SelectDY = I32(t, i, "SelectDY"),
                ExitWalkX = I32(t, i, "ExitWalkX"),
                ExitWalkY = I32(t, i, "ExitWalkY"),
                OffsetX = I32(t, i, "OffsetX"),
                OffsetY = I32(t, i, "OffsetY"),
                LitVersion = I32(t, i, "LitVersion"),
                Tiles = I32(t, i, "Tiles"),
                // Direction is a char column ('b'/'l'/'r'); the engine keeps the raw byte at
                // +0x2C (TXT_LvlWarp_Setup compares it to 0x62/0x6c/0x72).
                Direction = dtxt.Length > 0 ? (byte)dtxt[0] : 0,
            };
        }
        return o;
    }

    private static unsafe D2LevelDefsTxt[] BuildLevelDefs(ExcelTable t)
    {
        var o = new D2LevelDefsTxt[t.RowCount];
        for (int i = 0; i < o.Length; i++)
        {
            ref var r = ref o[i];
            r.QuestFlag = I32(t, i, "QuestFlag");
            r.QuestFlagEx = I32(t, i, "QuestFlagEx");
            r.Layer = I32(t, i, "Layer");
            // SizeX/SizeY: [normal, nightmare, hell] — N/H columns are parenthesised.
            r.SizeX[0] = I32(t, i, "SizeX");
            r.SizeX[1] = I32(t, i, "SizeX(N)");
            r.SizeX[2] = I32(t, i, "SizeX(H)");
            r.SizeY[0] = I32(t, i, "SizeY");
            r.SizeY[1] = I32(t, i, "SizeY(N)");
            r.SizeY[2] = I32(t, i, "SizeY(H)");
            r.OffsetX = I32(t, i, "OffsetX");
            r.OffsetY = I32(t, i, "OffsetY");
            r.Depend = I32(t, i, "Depend");
            r.DrlgType = I32(t, i, "DrlgType");
            r.LevelType = I32(t, i, "LevelType");
            r.SubType = I32(t, i, "SubType");
            r.SubTheme = I32(t, i, "SubTheme");
            r.SubWaypoint = I32(t, i, "SubWaypoint");
            r.SubShrine = I32(t, i, "SubShrine");
            r.LOSDraw = I32(t, i, "LOSDraw");
            r.Portal = I32(t, i, "Portal");
            r.Position = I32(t, i, "Position");
            r.SaveMonsters = I32(t, i, "SaveMonsters");
            r.Intensity = I32(t, i, "Intensity");
            r.Red = I32(t, i, "Red");
            r.Green = I32(t, i, "Green");
            r.Blue = I32(t, i, "Blue");
            for (int k = 0; k < 8; k++)
            {
                r.Vis[k] = I32(t, i, "Vis" + k);
                r.Warp[k] = I32(t, i, "Warp" + k);
            }
        }
        return o;
    }

    // ---- tables.zig (the act-graph view of Levels/LvlTypes) ----------------------------

    public LevelRow? Level(long id)
    {
        var row = Levels.FindRow("Id", id);
        return row is int r ? LevelFromRow(r) : null;
    }

    public int LevelCount => Levels.RowCount;

    public LevelRow? LevelAtRow(int row)
    {
        if (row >= Levels.RowCount) return null;
        if (Levels.GetInt64(row, "Id") == 0) return null;
        return LevelFromRow(row);
    }

    private LevelRow LevelFromRow(int row)
    {
        var t = Levels;
        return new LevelRow(row, t.GetInt64(row, "Id"), t.GetString(row, "Name"), t.GetString(row, "LevelName"), t.GetInt64(row, "Act"),
            (byte)t.GetInt64(row, "DrlgType"), t.GetInt64(row, "LevelType"), t.GetInt64(row, "SizeX"), t.GetInt64(row, "SizeY"),
            t.GetInt64(row, "OffsetX"), t.GetInt64(row, "OffsetY"), t.GetInt64(row, "Depend"));
    }

    /// <summary>A LevelType's DT1 file list (LvlTypes "File 1".."File 32", skipping "" and "0"),
    /// with each entry's File COLUMN index (N-1).</summary>
    public int TypeFilesCols(long lvlType, string[] outFiles, byte[] cols)
    {
        var row = LvlTypes.FindRow("Id", lvlType);
        if (row is not int r) return 0;
        int n = 0;
        for (int i = 1; i <= 32; i++)
        {
            var f = LvlTypes.GetString(r, "File " + i);
            if (f.Length > 0 && f != "0")
            {
                outFiles[n] = f;
                cols[n] = (byte)(i - 1);
                n += 1;
            }
        }
        return n;
    }
}

/// <summary>The per-generation native copy of the row arrays, and the recon-style free-function
/// table accessors (TXT_*_GetLine) over the generation running on this thread. They return
/// mutable pointers INTO contiguous row arrays so callers can do pointer arithmetic (`row + 1`)
/// and mutate runtime fields (e.g. TileSub sets pDrlgFile), matching the recon. Every array has
/// one zeroed sentinel row past its end, which the closure's `row + 1` walks stop on.</summary>
internal static unsafe class DrlgTables
{
    [ThreadStatic] private static GameTables? _src;
    [ThreadStatic] private static D2LvlPrestTxt* _prest;
    [ThreadStatic] private static D2LvlMazeTxt* _maze;
    [ThreadStatic] private static D2LvlSubTxt* _sub;
    [ThreadStatic] private static D2LvlWarpTxt* _warp;
    [ThreadStatic] private static D2LevelDefsTxt* _defs;
    [ThreadStatic] private static int _warpCount;

    private static T* Copy<T>(T[] src) where T : unmanaged
    {
        var p = Pool.Alloc<T>(src.Length + 1);
        fixed (T* s = src) Buffer.MemoryCopy(s, p, (long)sizeof(T) * src.Length, (long)sizeof(T) * src.Length);
        return p;
    }

    /// <summary>Bind the tables for the generation about to run on this thread (copies the row
    /// arrays into the current generation pool).</summary>
    public static void Bind(GameTables t)
    {
        _src = t;
        _prest = Copy(t.PrestRows);
        _maze = Copy(t.MazeRows);
        _sub = Copy(t.SubRows);
        _warp = Copy(t.WarpRows);
        _defs = Copy(t.LevelDefRows);
        _warpCount = t.WarpRows.Length;
    }

    public static void Unbind()
    {
        _src = null;
        _prest = null;
        _maze = null;
        _sub = null;
        _warp = null;
        _defs = null;
        _warpCount = 0;
    }

    private static GameTables Src => _src ?? throw new InvalidOperationException("No DRLG tables bound on this thread.");

    /// <summary>TXT_LvlPrest_GetLine(def)</summary>
    public static D2LvlPrestTxt* LvlPrestGetLine(int def) => Src.LvlPrest.FindRow("Def", def) is int r ? _prest + r : null;

    /// <summary>TXT_LvlPrest_FindLineByLevelId(level)</summary>
    public static D2LvlPrestTxt* LvlPrestFindLineByLevelId(int level) => Src.LvlPrest.FindRow("LevelId", level) is int r ? _prest + r : null;

    /// <summary>TXT_LvlMaze_FindLineByLevelId(level)</summary>
    public static D2LvlMazeTxt* LvlMazeFindLineByLevelId(int level) => Src.LvlMaze.FindRow("Level", level) is int r ? _maze + r : null;

    /// <summary>TXT_LvlSub_GetLineFromSubType(type)</summary>
    public static D2LvlSubTxt* LvlSubGetLineFromSubType(int subType) => Src.LvlSub.FindRow("Type", subType) is int r ? _sub + r : null;

    /// <summary>LvlWarp row by Id.</summary>
    public static D2LvlWarpTxt* LvlWarpGetLine(int id) => Src.LvlWarp.FindRow("Id", id) is int r ? _warp + r : null;

    /// <summary>TXT_LvlWarp_Setup (0x0061f310): linear scan in table order; a row matches when Id
    /// equals AND (wanted=='b' 0x62 || row.Direction=='b' || row.Direction==wanted). The engine
    /// returns the matched row pointer and HALTS (line 0x33a) on a full miss. Returns a 1-based
    /// row index (0 = miss) so callers can stash it in the warp node's i32 slot.</summary>
    public static int LvlWarpSetupIndex(int nWarpId, byte nDirection)
    {
        for (int i = 0; i < _warpCount; i++)
        {
            var row = _warp + i;
            if (row->Id != nWarpId) continue;
            byte d = (byte)row->Direction;
            if (nDirection == 0x62 || d == 0x62 || d == nDirection) return i + 1;
        }
        return 0;
    }

    /// <summary>LvlWarp row from a LvlWarpSetupIndex 1-based handle.</summary>
    public static D2LvlWarpTxt* LvlWarpRowAt(int idx1)
    {
        if (idx1 <= 0 || idx1 > _warpCount) return null;
        return _warp + (idx1 - 1);
    }

    /// <summary>TXT_LevelDefs_GetLine(level)</summary>
    public static D2LevelDefsTxt* LevelDefsGetLine(int eLevel) => Src.Levels.FindRow("Id", eLevel) is int r ? _defs + r : null;

    /// <summary>A NUL-terminated fixed char buffer as a string.</summary>
    public static string FixedString(byte* p, int cap)
    {
        int n = 0;
        while (n < cap && p[n] != 0) n++;
        return Encoding.Latin1.GetString(p, n);
    }
}

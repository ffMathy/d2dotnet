// Port of libd2 packages/drlg/src/drlg/presettables.zig (MIT, (c) 2026 jaenster).
//
// Faithful transform of the preset-unit classId resolution the engine performs at DS1-parse
// time — D2Common::Drlg::Preset::ParsePresetsOfDrlgFile (1.14d 0x665950) — feeding
// DRLGPRESET_AddPresetUnitToDrlgMap (Preset.cs).
//
// AddPresetUnit steps the level seed once per QUALIFYING preset unit, and whether a unit
// qualifies keys on its TRUE game classId. Resolution rules (cited):
//  - Monsters (DS1 unit type 1): the DS1 id indexes the act's MonPreset block
//    (MONSTERTBLS_GetMonPresetRecord 0x659b40 -> sgtDataTable.pTxtMonPresetPerAct[act],
//    block = MonPreset.txt rows with Act == act_id+1, in file order). Each "Place" string was
//    resolved at compile by DATATBLS_LinkerMonsterPreset (0x6597e0):
//      SuperUniques.txt -> nType=2, MonStats.txt -> nType=1, MonPlace.txt -> nType=0,
//      else nType=0/wPlace=0. classId (disasm 0x665c66..0x665c94):
//        nType==1            -> wPlace (a MonStats classId)
//        nType==0 OR nType==2 -> wPlace + nTxtMonStatsSize (object-as-monster, idx=wPlace)
//      DS1 id >= block count -> classId = raw id (recon ~line 235 false branch).
//  - Objects (DS1 unit type 2): id &lt; 0x96 -> gpsPresetObjectTable[act*0x96 + id]
//    (DRLGPRESET_GetObjectIdFromActTable 0x6658e0; static .data @0x748ad8, a 5*150 int32 array);
//    id >= 0x96 -> id - 0x96.
//  - Items (type 4) / any other: never reach a seed-stepping branch -> ignored.
//
// nTxtMonStatsSize forms object-as-monster classIds (+size) AND splits them back in
// AddPresetUnit (idx = classId - size); the constant cancels, so any value larger than every
// real monster classId is self-consistent. We use the MonStats game row count.

using D2Dotnet.Map.Drlg.Data;

namespace D2Dotnet.Map.Drlg.Engine;

/// <summary>The resolver tables (immutable once built).</summary>
internal sealed class PresetTables
{
    /// <summary>sgptDataTable->nTxtMonStatsSize — the MonStats game rows (the divider excluded).</summary>
    public int MonSize { get; }

    private readonly int[][] _mon = new int[5][];
    private readonly int[][] _dbm = new int[5][];
    private readonly int[] _obj;

    /// <summary>Sequential game index per name, SKIPPING the "Expansion" divider row (and blanks)
    /// so the returned index matches the engine's own row numbering. First occurrence wins.</summary>
    private static (Dictionary<string, int> Map, int Count) GameIndexMap(TxtTable t, string colName)
    {
        var m = new Dictionary<string, int>(StringComparer.Ordinal);
        int idx = 0;
        for (int i = 0; i < t.RowCount; i++)
        {
            var key = t.Str(i, colName);
            if (key.Length == 0 || key == "Expansion") continue; // divider row
            m.TryAdd(key, idx);
            idx += 1;
        }
        return (m, idx);
    }

    private static Dictionary<string, int> NameIndexMap(TxtTable t, string colName)
    {
        var m = new Dictionary<string, int>(StringComparer.Ordinal);
        // First occurrence wins (engine link tables index by file order).
        for (int i = 0; i < t.RowCount; i++) m.TryAdd(t.Str(i, colName), i);
        return m;
    }

    public PresetTables(TxtTable monStats, TxtTable superUniques, TxtTable monPlace, TxtTable monPreset, int[] presetObjectTable)
    {
        if (presetObjectTable.Length < 750) throw new ArgumentException("the preset object table must hold 5*150 entries", nameof(presetObjectTable));

        // The compiled tables leave out the "Expansion" divider rows of MonStats.txt and
        // SuperUniques.txt, so the engine's ids (and nTxtMonStatsSize) are game-row indices: an
        // expansion monster is one below its txt row.
        var monGame = GameIndexMap(monStats, "Id");
        var suGame = GameIndexMap(superUniques, "Superunique");
        MonSize = monGame.Count;
        var mp = NameIndexMap(monPlace, "code");

        // DBM/oracle ID space: MonStats game rows (Expansion divider excluded), then
        // SuperUniques game rows, then MonPlace.
        int dbmSuBase = monGame.Count;
        int dbmMpBase = monGame.Count + suGame.Count;

        var blocks = new List<int>[5];
        var dbmblk = new List<int>[5];
        for (int i = 0; i < 5; i++)
        {
            blocks[i] = new List<int>();
            dbmblk[i] = new List<int>();
        }
        for (int r = 0; r < monPreset.RowCount; r++)
        {
            long act = monPreset.Int(r, "Act"); // 1..5
            if (act < 1 || act > 5) continue;
            var place = monPreset.Str(r, "Place");
            // DATATBLS_LinkerMonsterPreset link order: SuperUniques, MonStats, MonPlace.
            int nType = 0, wPlace = 0;
            if (suGame.Map.TryGetValue(place, out var su)) { nType = 2; wPlace = su; }
            else if (monGame.Map.TryGetValue(place, out var ms)) { nType = 1; wPlace = ms; }
            else if (mp.TryGetValue(place, out var pl)) { nType = 0; wPlace = pl; }
            int classId = nType == 1 ? wPlace : wPlace + MonSize;
            blocks[act - 1].Add(classId);

            // DBM code: MonStats passes through (n_type==1 -> its classId). SuperUniques and
            // MonPlace map into the concatenated space using the same first-match order.
            int dbmCode = nType switch
            {
                2 => dbmSuBase + (suGame.Map.TryGetValue(place, out var a) ? a : 0),
                1 => wPlace,
                _ => dbmMpBase + (mp.TryGetValue(place, out var b) ? b : 0),
            };
            dbmblk[act - 1].Add(dbmCode);
        }
        for (int i = 0; i < 5; i++)
        {
            _mon[i] = blocks[i].ToArray();
            _dbm[i] = dbmblk[i].ToArray();
        }
        _obj = presetObjectTable[..750];
    }

    /// <summary>Resolved monster classId for a DS1 monster unit (type 1).</summary>
    public int MonsterClassId(int actId, int id)
    {
        if (actId < 0 || actId > 4) return id;
        var blk = _mon[actId];
        if (id < 0 || id >= blk.Length) return id; // recon: block-miss keeps the raw id
        return blk[id];
    }

    /// <summary>DBM/oracle "unresolved preset code" for a DS1 monster unit (type 1): the same
    /// resolution as MonsterClassId, but in the concatenated MonStats|SuperUniques|MonPlace ID
    /// space. Block-miss keeps the raw id.</summary>
    public int DbmMonsterCode(int actId, int id)
    {
        if (actId < 0 || actId > 4) return id;
        var blk = _dbm[actId];
        if (id < 0 || id >= blk.Length) return id;
        return blk[id];
    }

    /// <summary>Resolved object classId for a DS1 object unit (type 2).</summary>
    public int ObjectClassId(int actId, int id)
    {
        if (id < 0x96)
        {
            int ai = actId < 0 ? 0 : (actId & 0xff);
            // usize arithmetic in libd2: a negative id wraps to a huge index unless the act base
            // absorbs it.
            long slot = (long)ai * 0x96 + id;
            if (slot < 0 || slot >= _obj.Length) return -1;
            return _obj[slot];
        }
        return id - 0x96;
    }
}

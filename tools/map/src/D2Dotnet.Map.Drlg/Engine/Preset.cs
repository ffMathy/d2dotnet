// Port of libd2 packages/drlg/src/drlg/preset.zig (MIT, (c) 2026 jaenster).
//
// Mechanical transform of recon/closure/Preset.cpp (D2Common::Drlg::Preset). Faithful by
// construction; field access by NAME, RNG -> DrlgRng, allocs -> Pool, tables -> DrlgTables, grid
// -> DrlgGrid, warp/room ops -> DrlgRoom.
//
// Scope: the seed-consuming pipeline the maze's InitAllRoomsEx tail calls into
// (DRLGPRESET_AllocDrlgMap + BuildArea + DRLGPRESET_BuildPresetArea) plus the seed-dependent
// DRLGPRESET_AddPresetUnitToDrlgMap state machine, the DS1 keystone (ParsePresetsOfDrlgFile)
// and the room-build preset steps (CreateMonsterPresets / AddAndSetPresetUnits).

using D2Dotnet.Map.Drlg.Formats;

namespace D2Dotnet.Map.Drlg.Engine;

internal static unsafe class Preset
{
    // eD2UnitType (UNIT_MONSTER=1, UNIT_OBJECT=2).
    private const int UNIT_MONSTER = 1;
    private const int UNIT_OBJECT = 2;
    private const int UNIT_PLAYER = 0;

    /// <summary>sgptDataTable->nTxtMonStatsSize (globals_drlg.cpp). The MonStats.txt row count;
    /// the AddPresetUnit classId switch splits monster vs object-as-monster on it.</summary>
    private static int g_nTxtMonStatsSize => GenContext.Data.Presets.MonSize;

    /// <summary>DataTbls::MonsterTbls::MONSTERTBLS_ReturnZero() — recon returns 0.</summary>
    private static int ReturnZero() => 0;

    /// <summary>LvlPrest row access (D2DrlgMapStrc.pTxtLevelPrest).</summary>
    public static D2LvlPrestTxt* Prest(D2DrlgMapStrc* pDrlgMap) => pDrlgMap->pTxtLevelPrest;

    /// <summary>Preset.cpp:1216 DRLGPRESET_AllocDrlgMap. 0x58 in the 32-bit engine; sizeof in the
    /// 64-bit transform (8-byte pointers + embedded grid).</summary>
    public static D2DrlgMapStrc* AllocDrlgMap(D2DrlgLevelStrc* pLevel, int nLvlPrestId, D2DrlgCoordStrc* pDrlgCoord, D2SeedStrc* pSeed)
    {
        var pDrlgMap = (D2DrlgMapStrc*)Pool.AllocServerMemory(pLevel->pDrlg->pMemoryPool, (nuint)sizeof(D2DrlgMapStrc));
        var pLvlPrest = DrlgTables.LvlPrestGetLine(nLvlPrestId);
        pDrlgMap->pTxtLevelPrest = pLvlPrest;
        pDrlgMap->nNumber = nLvlPrestId;
        uint nFiles = (uint)pLvlPrest->Files;
        uint nFileSelector;
        if ((int)nFiles < 1)
        {
            nFileSelector = 0;
        }
        else if ((nFiles & unchecked(nFiles - 1)) == 0) // power of two
        {
            var newSeed = DrlgRng.SeedNext(*pSeed);
            *pSeed = newSeed;
            nFileSelector = unchecked(nFiles - 1) & (uint)newSeed.nSeedLow;
        }
        else
        {
            var newSeed = DrlgRng.SeedNext(*pSeed);
            *pSeed = newSeed;
            nFileSelector = (uint)newSeed.nSeedLow % nFiles;
        }

        pDrlgMap->nRandomMapFileSelector = (int)nFileSelector;
        pDrlgMap->nRealOffsetX = pDrlgCoord->nPosX;
        pDrlgMap->nRealOffsetY = pDrlgCoord->nPosY;
        var pLvlPrestTxt = Prest(pDrlgMap);
        if (pLvlPrestTxt->SizeX == 0 || pLvlPrestTxt->SizeY == 0)
        {
            pDrlgMap->nSizeX = pDrlgCoord->nWidth;
            pDrlgMap->nSizeY = pDrlgCoord->nHeight;
        }
        else
        {
            pDrlgMap->nSizeX = pLvlPrestTxt->SizeX;
            pDrlgMap->nSizeY = pLvlPrestTxt->SizeY;
        }

        pDrlgMap->bInited = 1;
        pDrlgMap->pNext = pLevel->pDrlgMapFirst;
        pLevel->pDrlgMapFirst = pDrlgMap;
        return pDrlgMap;
    }

    private static D2DrlgPresetRoomStrc* PresetRoom(D2RoomExStrc* pRoomEx) => (D2DrlgPresetRoomStrc*)pRoomEx->pRoomExData;

    /// <summary>Preset.cpp:791 DRLGROOMEX_AllocRoomExTypePreset</summary>
    public static D2RoomExStrc* AllocRoomExTypePreset(D2DrlgLevelStrc* pLevel, D2DrlgMapStrc* pDrlgMap, D2DrlgCoordStrc* pCoord, int nDT1Mask, int dwRoomExFlags, int nPickedFile, D2DrlgMapStrc* pParentDrlgMap)
    {
        var pRoomEx = DrlgRoom.AllocRoomEx(pLevel, 2);
        pRoomEx->eRoomExFlags.OrRaw(dwRoomExFlags);
        pRoomEx->nDT1Mask = nDT1Mask;
        pRoomEx->sCoords.WorldPosition.x = pCoord->nPosX;
        pRoomEx->sCoords.WorldPosition.y = pCoord->nPosY;
        pRoomEx->sCoords.WorldSize.x = pCoord->nWidth;
        pRoomEx->sCoords.WorldSize.y = pCoord->nHeight;
        var pPresetData = PresetRoom(pRoomEx);
        pPresetData->pMap = pDrlgMap; // [2]
        pPresetData->nPickedFile = nPickedFile; // [3]
        pPresetData->nLevelPrest = Prest(pDrlgMap)->Def; // [0]
        pPresetData->pMazeGrid = pParentDrlgMap; // [0x3b] = pParentDrlgMap
        if (pParentDrlgMap != null) pParentDrlgMap->pDrlgFile = (D2DrlgFileStrc*)((nuint)pParentDrlgMap->pDrlgFile | 1);
        if (Prest(pDrlgMap)->Populate == 0) pRoomEx->eRoomExFlags.NoSpawn = true;
        DrlgRoom.AddRoomExToLevel(pLevel, pRoomEx);
        return pRoomEx;
    }

    /// <summary>Preset.cpp:529 DRLGPRESET_CopyPresetObjects. Path object: { int nCount;
    /// D2PresetPathEntry* pEntries } — copied as a raw 8-byte head (faithful to libd2).</summary>
    private static void* CopyPresetObjects(void* pMemory, void* pSrcPresetUnits)
    {
        void* pNew = Pool.AllocServerMemory(pMemory, 8);
        Buffer.MemoryCopy(pSrcPresetUnits, pNew, 8, 8);
        return pNew;
    }

    /// <summary>Preset.cpp:1529 CopyPresetUnit. 0x20 in the 32-bit engine; sizeof in the 64-bit
    /// transform (8-byte pointers).</summary>
    private static D2PresetUnitStrc* CopyPresetUnit(void* pMemory, D2PresetUnitStrc* pPresetUnit, int nX, int nY)
    {
        var pNewPreset = (D2PresetUnitStrc*)Pool.AllocServerMemory(pMemory, (nuint)sizeof(D2PresetUnitStrc));
        pNewPreset->nMode = 0;
        pNewPreset->nClassId = 0;
        pNewPreset->nPosX = 0;
        pNewPreset->pPresetUnitNext = null;
        pNewPreset->pPath = null;
        pNewPreset->eType = UNIT_PLAYER;
        pNewPreset->nPosY = 0;
        pNewPreset->nFlags = 0;
        pNewPreset->eType = pPresetUnit->eType;
        pNewPreset->nClassId = pPresetUnit->nClassId;
        pNewPreset->nMode = pPresetUnit->nMode;
        pNewPreset->nPosX = pPresetUnit->nPosX + nX;
        pNewPreset->nPosY = pPresetUnit->nPosY + nY;
        pNewPreset->nFlags = pPresetUnit->nFlags;
        pNewPreset->nDbmCode = pPresetUnit->nDbmCode;
        if (pPresetUnit->pPath == null) return pNewPreset;
        pNewPreset->pPath = CopyPresetObjects(pMemory, pPresetUnit->pPath);
        return pNewPreset;
    }

    /// <summary>Preset.cpp:1584 DRLGPRESET_AddPresetUnitToDrlgMap. Seed-dependent state machine.
    /// Per the 64-bit finding the engine tests the 32-bit LOW word, not Ghidra's widened
    /// (uint64_t)seed; %3/&amp;1/&amp;3 below all use newSeed.nSeedLow.</summary>
    public static void AddPresetUnitToDrlgMap(void* pMemory, D2DrlgMapStrc* pDrlgMap, D2SeedStrc* pSeed)
    {
        D2PresetUnitStrc* pPresetUnit = pDrlgMap->pDrlgFile != null ? pDrlgMap->pDrlgFile->pPresetUnit : null;
        int nWorldX = pDrlgMap->nRealOffsetX;
        int nWorldY = pDrlgMap->nRealOffsetY;
        Drlg.CoordsRoomToWorld(&nWorldX, &nWorldY);
        while (true)
        {
            if (pPresetUnit == null) return;

            bool bCopy = true; // fall to switchD_0066771a_caseD_ce (CopyPresetUnit)
            if (pPresetUnit->eType == UNIT_MONSTER)
            {
                int nTableSize = g_nTxtMonStatsSize;
                int nMonClassId = pPresetUnit->nClassId;
                if (nMonClassId < nTableSize)
                {
                    if (nMonClassId < 0) nMonClassId = -1;
                    switch (nMonClassId)
                    {
                        case 0xcc:
                        case 0xcd:
                        case 0x173:
                        case 0x174:
                            {
                                var newSeed = DrlgRng.SeedNext(*pSeed);
                                *pSeed = newSeed;
                                if ((uint)newSeed.nSeedLow % 3 != 0) bCopy = false; // -> LAB_006676c0
                                break;
                            }
                    }
                }
                else
                {
                    if (ReturnZero() <= pPresetUnit->nClassId - nTableSize)
                    {
                        int idx = pPresetUnit->nClassId - g_nTxtMonStatsSize - ReturnZero();
                        if (idx == 0x21)
                        {
                            // LAB_00667687 (&3 path)
                            var newSeed = DrlgRng.SeedNext(*pSeed);
                            *pSeed = newSeed;
                            if (((uint)newSeed.nSeedLow & 3) == 0) bCopy = false;
                        }
                        else if (idx == 0x22)
                        {
                            var newSeed = DrlgRng.SeedNext(*pSeed);
                            *pSeed = newSeed;
                            if (((uint)newSeed.nSeedLow & 1) != 0) bCopy = false; // bInclude2 even -> skip
                        }
                        else if (idx == 0x23)
                        {
                            var newSeed = DrlgRng.SeedNext(*pSeed);
                            *pSeed = newSeed;
                            if (((uint)newSeed.nSeedLow & 3) != 0) bCopy = false;
                        }
                    }
                }
            }
            else if (pPresetUnit->eType == UNIT_OBJECT)
            {
                int classId = pPresetUnit->nClassId;
                if (classId == 0xc4 || classId == 0x105)
                {
                    var newSeed = DrlgRng.SeedNext(*pSeed);
                    *pSeed = newSeed;
                    if (((uint)newSeed.nSeedLow & 1) != 0) bCopy = false; // odd -> skip copy
                }
                else if (classId == 0x245)
                {
                    // LAB_00667687 (&3 path)
                    var newSeed = DrlgRng.SeedNext(*pSeed);
                    *pSeed = newSeed;
                    if (((uint)newSeed.nSeedLow & 3) == 0) bCopy = false;
                }
            }

            if (bCopy)
            {
                var pCopied = CopyPresetUnit(pMemory, pPresetUnit, nWorldX, nWorldY);
                pCopied->pPresetUnitNext = pDrlgMap->pPresetUnit;
                pDrlgMap->pPresetUnit = pCopied;
            }
            pPresetUnit = pPresetUnit->pPresetUnitNext; // LAB_006676c0
        }
    }

    /// <summary>Preset.cpp:1159 DRLGPRESET_InitDrlgMapPopLocations</summary>
    private static void InitDrlgMapPopLocations(int nPopCount, D2DrlgMapStrc* pDrlgMap)
    {
        pDrlgMap->nPops = nPopCount;
        if (nPopCount <= 0) return;
        var pos = pDrlgMap->pPosLocation;
        var idxArr = pDrlgMap->pPopsIndex;
        for (int i = 0; i < nPopCount; i++)
        {
            var p = &pos[i];
            int nX0 = p->nPosX;
            int nX1 = p->nWidth;
            int nMinX = nX0;
            if (nX1 <= nX0) nMinX = nX1;
            int nY0 = p->nPosY;
            int nY1 = p->nHeight;
            int nMinY = nY0;
            if (nY1 <= nY0) nMinY = nY1;
            if (nX0 <= nX1) nX0 = nX1;
            if (nY0 <= nY1) nY0 = nY1;
            p->nPosX = pDrlgMap->nRealOffsetX + nMinX;
            p->nPosY = pDrlgMap->nRealOffsetY + nMinY;
            p->nWidth = nX0 - nMinX + 1;
            p->nHeight = nY0 - nMinY + 1;
            int v = idxArr[i];
            idxArr[i] = (v + ((int)((uint)v >> 0x1f) & 3)) / 4 - 1;
        }
    }

    /// <summary>Preset.cpp:457 AllocDrlgFile (1.14d 0x665f40). Allocs a zeroed D2DrlgFileStrc then
    /// parses the DS1 into it. The global gpDrlgFileCache is dropped (DS1 parsing consumes no
    /// seed). szFile is the asset-relative DS1 path; an empty/"0" path leaves the file zeroed.</summary>
    public static void AllocDrlgFile(D2DrlgFileStrc** ppDrlgFile, void* pMemory, string szFile)
    {
        var pDrlgFile = (D2DrlgFileStrc*)Pool.AllocServerMemory(pMemory, (nuint)sizeof(D2DrlgFileStrc));
        *ppDrlgFile = pDrlgFile;
        ParsePresetsOfDrlgFile(pDrlgFile, pMemory, szFile);
    }

    /// <summary>Preset.cpp:86 ParsePresetsOfDrlgFile (1.14d 0x665950). THE outdoor DS1-parse
    /// keystone. Reads the DS1 through the data source (identical version gates, recon lines
    /// 110-453) and copies each layer into pool memory so it outlives the temporary parse. A
    /// missing DS1 must never change the seed stream: it leaves the file zeroed.</summary>
    private static void ParsePresetsOfDrlgFile(D2DrlgFileStrc* pDrlgFile, void* pMemory, string rel)
    {
        if (rel.Length == 0 || rel == "0") return; // no DS1 -> leave zeroed
        var d = GenContext.Data.GetDs1(rel);
        if (d == null) return;
        FillDrlgFileFromDs1(pDrlgFile, pMemory, d);
    }

    /// <summary>Shared parse of a DS1 by asset-relative path (null when the data holds no such
    /// file).</summary>
    public static Ds1File? CachedDs1(string rel) => GenContext.Data.GetDs1(rel);

    /// <summary>Populate a (zeroed) D2DrlgFileStrc from a parsed DS1 (recon Preset.cpp:111-391).
    /// Ds1File stores width/height as the ACTUAL tile counts (header+1); the recon struct keeps the
    /// header values (tiles-1). nWidth+1 (InitializeDrlgFile) then recovers the layer stride.</summary>
    private static void FillDrlgFileFromDs1(D2DrlgFileStrc* pDrlgFile, void* pMemory, Ds1File d)
    {
        pDrlgFile->nWidth = (int)d.Width - 1;
        pDrlgFile->nHeight = (int)d.Height - 1;
        pDrlgFile->nAct = d.Act;
        int cells = (int)(d.Width * d.Height); // (nWidth+1)*(nHeight+1) (recon 142)

        // Wall layers: recon Preset.cpp:168-177 assigns block1->pWallLayer[i],
        // block2->pTileTypeLayer[i]; Ds1File reads {wall, orient} in that order.
        pDrlgFile->nWallLayers = d.WallLayers.Length;
        for (int i = 0; i < d.WallLayers.Length; i++)
        {
            if (i >= 4) break;
            pDrlgFile->pWallLayer[i] = (nint)CopyCells(pMemory, d.WallLayers[i].Wall, cells);
            pDrlgFile->pTileTypeLayer[i] = (nint)CopyCells(pMemory, d.WallLayers[i].Orient, cells);
        }
        pDrlgFile->nFloorLayers = d.FloorLayers.Length;
        for (int i = 0; i < d.FloorLayers.Length; i++)
        {
            if (i >= 2) break;
            pDrlgFile->pFloorLayer[i] = (nint)CopyCells(pMemory, d.FloorLayers[i], cells);
        }
        pDrlgFile->pShadowLayer = CopyCells(pMemory, d.Shadow, cells); // recon 212
        if (d.SubstTags != null) pDrlgFile->pSubstGroupTags = CopyCells(pMemory, d.SubstTags, cells); // recon 214-217

        // Preset units (recon 218-359).
        BuildPresetUnits(pMemory, pDrlgFile, d.ActId, d.Objects);

        // Substitution groups (recon 362-391). nSubstGroups is the keystone field.
        if (d.SubstGroups.Length > 0)
        {
            pDrlgFile->nSubstGroups = d.SubstGroups.Length;
            var grp = (D2DrlgSubstGroupStrc*)Pool.AllocServerMemory(pMemory, (nuint)(d.SubstGroups.Length * sizeof(D2DrlgSubstGroupStrc)));
            for (int i = 0; i < d.SubstGroups.Length; i++)
            {
                var g = d.SubstGroups[i];
                // recon reads x,y,w,h into tBox then the 5th int into nVariantCount (offset
                // 0x14); nProbability (0x10) is left unwritten and never read.
                grp[i].tBox = new D2DrlgCoordStrc { nPosX = g.X, nPosY = g.Y, nWidth = g.W, nHeight = g.H };
                grp[i].nProbability = 0;
                grp[i].nVariantCount = g.Unknown;
            }
            pDrlgFile->pSubstGroups = grp;
        }
    }

    /// <summary>Copy `cells` packed DS1 cell values (raw int32) into a fresh pool allocation.</summary>
    private static void* CopyCells(void* pMemory, uint[] src, int cells)
    {
        var dst = (int*)Pool.AllocServerMemory(pMemory, (nuint)(cells * 4));
        int n = Math.Min(src.Length, cells);
        for (int k = 0; k < n; k++) dst[k] = (int)src[k];
        return dst;
    }

    /// <summary>Build pDrlgFile->pPresetUnit from the DS1 object list (recon Preset.cpp:218-359
    /// switch). Monsters (kind=1) / objects (kind=2) get engine-resolved classIds via
    /// PresetTables; classId&lt;0 drops the unit (recon line 350). Items/warps never reach a
    /// seed-stepping branch so are skipped. Prepended in file order (recon line 352).</summary>
    private static void BuildPresetUnits(void* pMemory, D2DrlgFileStrc* pDrlgFile, int actId, Ds1Object[] objects)
    {
        var pt = GenContext.Data.Presets;
        foreach (var o in objects)
        {
            int eType;
            int nClassId;
            int nMode = 0;
            int nDbmCode = -1;
            switch (o.Kind)
            {
                case 1:
                    eType = UNIT_MONSTER;
                    nClassId = pt.MonsterClassId(actId, o.Id);
                    nDbmCode = pt.DbmMonsterCode(actId, o.Id);
                    nMode = 1;
                    // ParsePresetsOfDrlgFile (0x665950): a few MonPreset entries are objects in
                    // the game. Act III: MonStats 0x129 -> object 0x17e, 0x16e -> 0x194. Act V:
                    // 0x202 -> 0x1cd, 0x219..0x21b -> 0x3f5 - id. (The engine gates this on DS1
                    // version > 4; this assumes the version every shipped DS1 has.)
                    if (nClassId < g_nTxtMonStatsSize)
                    {
                        int nObject = -1;
                        if (actId == 2)
                        {
                            if (nClassId == 0x129) nObject = 0x17e;
                            else if (nClassId == 0x16e) nObject = 0x194;
                        }
                        else if (actId == 4 && nClassId >= 0)
                        {
                            if (nClassId == 0x202) nObject = 0x1cd;
                            else if (nClassId >= 0x219 && nClassId <= 0x21b) nObject = 0x3f5 - nClassId;
                        }
                        if (nObject >= 0)
                        {
                            eType = UNIT_OBJECT;
                            nClassId = nObject;
                            nDbmCode = -1;
                            nMode = 0;
                        }
                    }
                    break;
                case 2:
                    eType = UNIT_OBJECT;
                    nClassId = pt.ObjectClassId(actId, o.Id);
                    break;
                default:
                    continue; // items (4) / warps never step the seed
            }
            if (nClassId < 0) continue; // recon line 350: classId < 0 -> unit dropped

            var u = (D2PresetUnitStrc*)Pool.AllocServerMemory(pMemory, (nuint)sizeof(D2PresetUnitStrc));
            u->pPath = null;
            u->nFlags = o.Flags;
            u->eType = eType;
            u->nClassId = nClassId;
            u->nDbmCode = nDbmCode;
            u->nMode = nMode;
            u->nPosX = o.X;
            u->nPosY = o.Y;
            u->pPresetUnitNext = pDrlgFile->pPresetUnit;
            pDrlgFile->pPresetUnit = u;
        }
    }

    /// <summary>Preset.cpp:1731 DRLGPRESET_BuildPresetArea</summary>
    public static void BuildPresetArea(D2DrlgMapStrc* pDrlgMap, D2DrlgLevelStrc* pLevel, D2DrlgGridStrc* pDrlgGrid, int nFlagsIn, bool bSingleRoom)
    {
        int nFlags = nFlagsIn;
        var pDrlg = pLevel->pDrlg;
        void* pMemory = pDrlg->pMemoryPool;
        int* pVisCfg = DrlgRoom.GetVisArrayFromLevelId(pDrlg, pLevel->eD2LevelId);
        uint nFlagBit = 0x10;
        for (int nVisIndex = 0; nVisIndex < 8; nVisIndex++, nFlagBit = unchecked(nFlagBit * 2))
        {
            if (pVisCfg[nVisIndex] != LevelIds.None)
            {
                int nWarpDestLevel = DrlgRoom.GetWarpDestinationFromArray(pLevel, (byte)nVisIndex);
                if (nWarpDestLevel == -1) nFlags |= (int)nFlagBit;
            }
        }

        DrlgGrid.AlterAllGridFlags(pDrlgGrid, nFlags, 0);
        var pLvlPrest = Prest(pDrlgMap);
        int nScanMode = pLvlPrest->Scan;
        int nPopsCfg = pLvlPrest->Pops;
        if (nScanMode == 0 && nPopsCfg == 0) return; // recon Preset.cpp:1778 — the Act2 sewer gate

        // Heavy DS1 path (Scan/Pops presets). AllocDrlgFile -> ParsePresetsOfDrlgFile loads the
        // DS1 and builds pDrlgFile->pPresetUnit with engine-resolved classIds (recon
        // Preset.cpp:86). AddPresetUnit then consumes the level seed once per qualifying preset
        // UNIT. The DS1 wall-layer grid scan (recon 1828-1945) consumes NO seed; the warp-tile
        // portion is ported below, the pops/spawns portion stays dormant.
        AllocDrlgFile(&pDrlgMap->pDrlgFile, pDrlg->pDS1MemPool, PresetDs1Path(pDrlgMap) ?? "");
        AddPresetUnitToDrlgMap(pMemory, pDrlgMap, &pLevel->sSeed);

        // DS1 wall-layer WARP-TILE scan (recon Preset.cpp:1826-1856). For every wall-layer cell
        // whose orientation block == 0xa/0xb (a warp/exit tile), the wall block's main index
        // (nTileType = wallCell>>0x14 & 0x3f) is the vis/warp slot: OR bit 1<<(slot+4) into the
        // room's grid cell. Consumes NO seed.
        var pf = pDrlgMap->pDrlgFile;
        if (nScanMode != 0 && pf != null && pf->nWidth == pDrlgMap->nSizeX && pf->nHeight == pDrlgMap->nSizeY)
        {
            int W = pf->nWidth + 1;
            int H = pf->nHeight + 1;
            int nwl = pf->nWallLayers;
            for (int li = 0; li < nwl && li < 4; li++)
            {
                var orient = (int*)pf->pTileTypeLayer[li];
                var wall = (int*)pf->pWallLayer[li];
                if (orient == null || wall == null) continue;
                for (int yy = 0; yy < H; yy++)
                {
                    for (int xx = 0; xx < W; xx++)
                    {
                        int o = orient[yy * W + xx];
                        if (o != 0xa && o != 0xb) continue;
                        uint wc = (uint)wall[yy * W + xx];
                        int nTileType = (int)((wc >> 0x14) & 0x3f);
                        uint nPopSubIndex = (wc >> 8) & 0xff;
                        if (nTileType >= 8) continue;
                        if (!(nPopSubIndex == 0 || nPopSubIndex == 4 || (wc & 0x80000000) != 0)) continue;
                        int gx = bSingleRoom ? 0 : xx / 8;
                        int gy = bSingleRoom ? 0 : yy / 8;
                        DrlgGrid.AlterGridFlag(pDrlgGrid, gx, gy, 1 << (nTileType + 4), 0);
                    }
                }
            }
        }

        const int nPopCount = 0;
        if (nPopsCfg != 0)
        {
            int n = nPopsCfg;
            pDrlgMap->pPopsIndex = (int*)Pool.AllocServerMemory(pMemory, (nuint)(n * 4));
            pDrlgMap->pPopsSubIndex = (int*)Pool.AllocServerMemory(pMemory, (nuint)(n * 4));
            pDrlgMap->pPopsOrientation = (int*)Pool.AllocServerMemory(pMemory, (nuint)(n * 4));
            pDrlgMap->pPosLocation = (D2DrlgCoordStrc*)Pool.AllocServerMemory(pMemory, (nuint)(n * 16));
        }

        // The pops/spawn-point population (recon 1858-1945) stays dormant; it consumes no seed.
        if (nPopsCfg != 0) InitDrlgMapPopLocations(nPopCount, pDrlgMap);
    }

    /// <summary>LvlPrest File[idx] as a string.</summary>
    private static string FileCol(D2LvlPrestTxt* row, int idx) => DrlgTables.FixedString(row->File + idx * 60, 60);

    /// <summary>The DS1 to load for this preset map. Faithful selection is File[selector]
    /// (AllocDrlgFile, BuildPresetArea recon 1783); an empty "0" slot is skipped to the first real
    /// File — the engine's File[0]="0" placeholder convention for non-random (Files=0) presets
    /// like Lut Gholein (File1="0", File2="LutW.ds1").</summary>
    public static string? PresetDs1Path(D2DrlgMapStrc* pDrlgMap)
    {
        var p = Prest(pDrlgMap);
        int sel = pDrlgMap->nRandomMapFileSelector;
        if ((uint)sel < 6)
        {
            var f = FileCol(p, sel);
            if (f.Length > 0 && f != "0") return f;
        }
        for (int i = 0; i < 6; i++)
        {
            var f = FileCol(p, i);
            if (f.Length > 0 && f != "0") return f;
        }
        return null;
    }

    /// <summary>Preset.cpp:1964 BuildArea</summary>
    public static D2RoomExStrc* BuildArea(D2DrlgLevelStrc* pLevel, D2DrlgMapStrc* pDrlgMap, int nFlagsIn, int bSingleRoom)
    {
        int nFlags = nFlagsIn;
        int* gridCellBuf = stackalloc int[1024];
        int* gridLinkBuf = stackalloc int[256];
        D2DrlgGridStrc tmpGrid = default;

        if (Prest(pDrlgMap)->Outdoors != 0) nFlags |= 0x80000;

        int sx = pDrlgMap->nSizeX;
        int sy = pDrlgMap->nSizeY;
        DrlgGrid.FillGrid(&tmpGrid, (sx + ((int)((uint)sx >> 0x1f) & 7)) / 8 + 1, (sy + ((int)((uint)sy >> 0x1f) & 7)) / 8 + 1, gridCellBuf, gridLinkBuf);
        BuildPresetArea(pDrlgMap, pLevel, &tmpGrid, nFlags, bSingleRoom != 0);
        int dt1Mask = Prest(pDrlgMap)->Dt1Mask;
        D2RoomExStrc* pLastRoomEx = null;

        if (bSingleRoom == 0)
        {
            int y = pDrlgMap->nRealOffsetY;
            int xEnd = pDrlgMap->nSizeX + pDrlgMap->nRealOffsetX;
            int yEnd = pDrlgMap->nSizeY + y;
            int nGridRow = 0;
            for (; y < yEnd; nGridRow++, y += 8)
            {
                int x = pDrlgMap->nRealOffsetX;
                int nCol = 0;
                for (; x < xEnd; nCol++, x += 8)
                {
                    int xRemaining = xEnd - x;
                    int nTileWidth = xRemaining;
                    if (nTileWidth > 7) nTileWidth = 8;
                    int nTileHeight = yEnd - y;
                    if (nTileHeight > 7) nTileHeight = 8;
                    var coord = new D2DrlgCoordStrc { nPosX = x, nPosY = y, nWidth = nTileWidth, nHeight = nTileHeight };
                    int nCellFlags = DrlgGrid.GetGridFlags(&tmpGrid, nCol, nGridRow);
                    int nMapGridFlags = 0;
                    if (pDrlgMap->pVertices != null) nMapGridFlags = DrlgGrid.GetGridFlags(&pDrlgMap->pMapGrid, nCol, nGridRow);
                    if (nTileWidth != 0 && nTileHeight != 0)
                        pLastRoomEx = AllocRoomExTypePreset(pLevel, pDrlgMap, &coord, dt1Mask, nCellFlags, 0, (D2DrlgMapStrc*)(nuint)(uint)nMapGridFlags);
                }
            }
            DrlgGrid.ResetGrid(&tmpGrid);
            return pLastRoomEx;
        }

        int nGridFlags = DrlgGrid.GetGridFlags(&tmpGrid, 0, 0);
        var pRoomEx = DrlgRoom.AllocRoomEx(pLevel, 2);
        pRoomEx->eRoomExFlags.OrRaw(nGridFlags);
        pRoomEx->nDT1Mask = dt1Mask;
        pRoomEx->sCoords.WorldPosition.x = pDrlgMap->nRealOffsetX;
        pRoomEx->sCoords.WorldPosition.y = pDrlgMap->nRealOffsetY;
        pRoomEx->sCoords.WorldSize.x = pDrlgMap->nSizeX;
        pRoomEx->sCoords.WorldSize.y = pDrlgMap->nSizeY;
        var pRoomExData = PresetRoom(pRoomEx);
        pRoomExData->pMap = pDrlgMap; // [2]
        pRoomExData->nLevelPrest = Prest(pDrlgMap)->Def; // [0]
        pRoomExData->nPickedFile = 1; // [3]
        pRoomExData->pMazeGrid = null; // [0x3b]
        if (Prest(pDrlgMap)->Populate == 0) pRoomEx->eRoomExFlags.NoSpawn = true;
        DrlgRoom.AddRoomExToLevel(pLevel, pRoomEx);
        DrlgGrid.ResetGrid(&tmpGrid);
        return pRoomEx;
    }

    /// <summary>DRLGPRESET_CreateMonsterPresets (1.14d 0x667890), run by InitRoomEx before the
    /// room's grids are built. A map whose generation did not load its DS1 (BuildPresetArea's
    /// Scan==0 &amp;&amp; Pops==0 gate) loads it now and copies its units through the seed-dependent
    /// filter of AddPresetUnitToDrlgMap, rolled on THIS room's seed (so whichever room of the map
    /// is built first decides). A map BuildPresetArea marked bInited then gets its generated
    /// presets once: the Blood Moor camp monster (LvlPrest 4..7 on level 2 with file 3). Marks the
    /// room's presets added (eRoomExFlags 0x2000000).</summary>
    public static void CreateMonsterPresets(D2RoomExStrc* pRoomEx, D2DrlgMapStrc* pDrlgMap)
    {
        try
        {
            if (pDrlgMap->pDrlgFile == null)
            {
                var rel = PresetDs1Path(pDrlgMap);
                if (rel != null)
                {
                    AllocDrlgFile(&pDrlgMap->pDrlgFile, null, rel);
                    AddPresetUnitToDrlgMap(null, pDrlgMap, &pRoomEx->sSeed);
                }
            }
            if (pDrlgMap->bInited != 1) return;
            pDrlgMap->bInited = 0;
            int nLvlPrestId = pDrlgMap->nNumber;
            if (nLvlPrestId < 8)
            {
                if (3 < nLvlPrestId)
                {
                    if (pRoomEx->pLevel->eD2LevelId == LevelIds.BloodMoor && pDrlgMap->nRandomMapFileSelector == 3) CreateMonsterPreset(pDrlgMap);
                    return;
                }
                if (nLvlPrestId != 1 && nLvlPrestId != 3) return;
            }
            else if (nLvlPrestId < 0x1a || (0x1c < nLvlPrestId && nLvlPrestId != 300))
            {
                return;
            }
            // CreateObjectPresetsForMap (0x6663c0) for a mapReveal room: the river and bridge
            // object rows. Not reached by the levels the oracle covers, so not ported (libd2).
        }
        finally
        {
            pRoomEx->eRoomExFlags.PresetsAdded = true;
        }
    }

    /// <summary>CreateMonsterPreset (1.14d 0x6664a0): one monster (MonStats 0x10a, or -1 past the
    /// table) at the map's centre, on the map's preset list.</summary>
    private static void CreateMonsterPreset(D2DrlgMapStrc* pDrlgMap)
    {
        int nX = pDrlgMap->nSizeX / 2 + pDrlgMap->nRealOffsetX;
        int nY = pDrlgMap->nSizeY / 2 + pDrlgMap->nRealOffsetY;
        Drlg.CoordsRoomToWorld(&nX, &nY);
        int nClassId = 0x10a < g_nTxtMonStatsSize ? 0x10a : -1;
        var pPreset = DrlgRoom.AllocPresetUnit(null, null, UNIT_MONSTER, nClassId, 1, nX, nY);
        pPreset->pPresetUnitNext = pDrlgMap->pPresetUnit;
        pDrlgMap->pPresetUnit = pPreset;
    }

    /// <summary>DRLGPRESET_AddAndSetPresetUnits (1.14d 0x666710), the end of InitGridsFromDS1File:
    /// every unit of the map's list that lies inside this room (world subtiles) moves to the
    /// room's list, room-relative. The rest stays with the map for its other rooms.</summary>
    public static void AddAndSetPresetUnits(D2RoomExStrc* pRoomEx, D2DrlgMapStrc* pDrlgMap)
    {
        var roomCoords = pRoomEx->sCoords;
        Drlg.CoordsRoomToWorld(&roomCoords.WorldPosition.x, &roomCoords.WorldPosition.y);
        Drlg.CoordsRoomToWorld(&roomCoords.WorldSize.x, &roomCoords.WorldSize.y);
        int nOffsetX = pRoomEx->sCoords.WorldPosition.x * 5;
        int nOffsetY = pRoomEx->sCoords.WorldPosition.y * 5;
        D2PresetUnitStrc* pPrev = null;
        var pNext = pDrlgMap->pPresetUnit;
        while (pNext != null)
        {
            var pCurrent = pNext;
            if (DrlgRoom.AreXYInsideCoordinates(&roomCoords, pCurrent->nPosX, pCurrent->nPosY) == 0)
            {
                pNext = pCurrent->pPresetUnitNext;
                pPrev = pCurrent;
                continue;
            }
            pCurrent->nPosX -= nOffsetX;
            pCurrent->nPosY -= nOffsetY;
            if (pPrev != null) pPrev->pPresetUnitNext = pCurrent->pPresetUnitNext;
            else pDrlgMap->pPresetUnit = pCurrent->pPresetUnitNext;
            pCurrent->pPresetUnitNext = pRoomEx->pPresetUnit;
            pRoomEx->pPresetUnit = pCurrent;
            pNext = pPrev != null ? pPrev->pPresetUnitNext : pDrlgMap->pPresetUnit;
        }
    }

    /// <summary>The rooms DefineRoomsNear (1.14d 0x66bc20) puts in a room's near list: every room
    /// of the level within 5 tiles on both axes (the room itself included), in
    /// ReorderNearRoomList's bubble order (a room wholly left of or above its predecessor moves
    /// ahead). The rooms LinkNearRoomsByVis adds from other levels are left out.</summary>
    private static int NearRoomsInOrder(D2RoomExStrc* pRoomEx, D2RoomExStrc** buf, int cap)
    {
        int n = 0;
        for (var o = pRoomEx->pLevel->pRoomExFirst; o != null; o = o->pRoomExNext)
        {
            var a = pRoomEx->sCoords;
            var b = o->sCoords;
            int gx = a.WorldPosition.x < b.WorldPosition.x ? b.WorldPosition.x - a.WorldSize.x - a.WorldPosition.x : a.WorldPosition.x - b.WorldSize.x - b.WorldPosition.x;
            int gy = a.WorldPosition.y < b.WorldPosition.y ? b.WorldPosition.y - a.WorldSize.y - a.WorldPosition.y : a.WorldPosition.y - b.WorldSize.y - b.WorldPosition.y;
            if (gx < 6 && gy < 6 && n < cap)
            {
                buf[n] = o;
                n += 1;
            }
        }
        if (n > 1)
        {
            for (int pass = n - 1; pass > 0; pass--)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    var nx = buf[i + 1]->sCoords;
                    var cur = buf[i]->sCoords;
                    if (nx.WorldSize.x + nx.WorldPosition.x <= cur.WorldPosition.x || nx.WorldSize.y + nx.WorldPosition.y <= cur.WorldPosition.y)
                    {
                        var t = buf[i];
                        buf[i] = buf[i + 1];
                        buf[i + 1] = t;
                    }
                }
            }
        }
        return n;
    }

    /// <summary>The order in which building every room of a level (AddRoomData on each room, in
    /// the level's room order, as a map dump does) first reaches each room's presets, and what it
    /// does there. AddRoomDataActual (0x61b490) -> InitNearRooms (0x61b390) walks the near list
    /// twice deep: for each near room N, every room in N's near list gets activation handler 3,
    /// DRLGROOMEX_InitTileDataAndPresets (0x61b320), which runs CreateMonsterPresets once per
    /// room. The first room of a map reached so loads a gated map with its own seed. Handlers 2
    /// and 1 add no presets.</summary>
    public static void TouchPresetsInBuildOrder(D2DrlgLevelStrc* pLevel)
    {
        var near1 = stackalloc D2RoomExStrc*[64];
        var near2 = stackalloc D2RoomExStrc*[64];
        for (var room = pLevel->pRoomExFirst; room != null; room = room->pRoomExNext)
        {
            int n1 = NearRoomsInOrder(room, near1, 64);
            for (int i = 0; i < n1; i++)
            {
                int n2 = NearRoomsInOrder(near1[i], near2, 64);
                for (int j = 0; j < n2; j++)
                {
                    var m = near2[j];
                    if (m->nPresetType != 2 || m->eRoomExFlags.PresetsAdded) continue;
                    var rd = (D2DrlgPresetRoomStrc*)m->pRoomExData;
                    if (rd == null) continue;
                    var pMap = rd->pMap;
                    if (pMap == null) continue;
                    CreateMonsterPresets(m, pMap);
                }
            }
        }
    }
}

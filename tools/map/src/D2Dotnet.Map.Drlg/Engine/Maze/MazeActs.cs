// Port of libd2 packages/drlg/src/drlg/maze/{Act1,Act2,Act2Sewer,Act3,Act4,Act5}.zig
// (MIT, (c) 2026 jaenster). ReplaceRoom-table special-preset generators per act; the tables are
// in MazeTables.cs (generated, verbatim).

using static D2Dotnet.Map.Drlg.Engine.Maze.MazeTables;

namespace D2Dotnet.Map.Drlg.Engine.Maze;

internal static unsafe class MazeHelpers
{
    public static void Rr(D2DrlgLevelStrc* pLevel, D2DrlgReplaceRoomStrc[] tbl, int* nRoll) =>
        MazeGen.ReplaceRoom(tbl[*nRoll], pLevel, nRoll);

    /// <summary>The recurring "alloc room, size from maze txt, place in dir, connect+pick or free"
    /// block.</summary>
    public static D2RoomExStrc* GrowRoom(int dir, D2RoomExStrc* pSrc)
    {
        var pNew = DrlgRoom.AllocRoomEx(pSrc->pLevel, 2);
        var t = MazeGen.MazeTxt(pNew->pLevel);
        pNew->sCoords.WorldSize.x = t->SizeX;
        pNew->sCoords.WorldSize.y = t->SizeY;
        if (!MazeDeps.ReplaceRoomWithNewRoom(dir, pNew, pSrc))
        {
            DrlgRoom.FreeDrlgRoomEx(pNew);
            return null;
        }
        DrlgRoom.AllocNodesForBothRoomEx(pSrc, pNew, dir);
        MazeGen.PickRoomPresets(pNew);
        DrlgRoom.AddRoomExToLevel(pSrc->pLevel, pNew);
        MazeGen.PickRoomPreset(pSrc);
        MazeGen.PickRoomPreset(pNew);
        return pNew;
    }

    public static void Translate(D2DrlgLevelStrc* pLevel, int dx, int dy)
    {
        for (var p = pLevel->pRoomExFirst; p != null; p = p->pRoomExNext)
        {
            p->sCoords.WorldPosition.x += dx;
            p->sCoords.WorldPosition.y += dy;
        }
        int nMinX = 0, nMinY = 0, nMaxX = 0, nMaxY = 0;
        MazeDeps.GetMinAndMaxCoordinatesFromLevel(pLevel, &nMinX, &nMinY, &nMaxX, &nMaxY);
        pLevel->sCoordinatesAndSize.WorldPosition.x = nMinX;
        pLevel->sCoordinatesAndSize.WorldPosition.y = nMinY;
        pLevel->sCoordinatesAndSize.WorldSize.x = nMaxX - nMinX;
        pLevel->sCoordinatesAndSize.WorldSize.y = nMaxY - nMinY;
    }

    public static int Roll4(D2DrlgLevelStrc* pLevel)
    {
        var s = DrlgRng.SeedNext(pLevel->sSeed);
        pLevel->sSeed = s;
        return s.nSeedLow & 3;
    }
}

/// <summary>maze/Act1.zig (recon Maze/Act1.cpp + Act1/{Jail,Catacombs,Barracks}.cpp).</summary>
internal static unsafe class MazeAct1
{
    /// <summary>Caves (Act1.cpp:24, 1.14d 00672550)</summary>
    public static void Caves(D2DrlgLevelStrc* pLevel)
    {
        int nRoll = MazeHelpers.Roll4(pLevel);
        MazeHelpers.Rr(pLevel, Act1_gaDrlgCavesReplacements_A, &nRoll);
        var tbl = pLevel->eD2LevelId == LevelIds.DenofEvil ? Act1_gaDrlgCavesReplacements_B : Act1_gaDrlgCavesReplacements_C;
        MazeHelpers.Rr(pLevel, tbl, &nRoll);
        if (pLevel->eD2LevelId == LevelIds.CaveLvl1) MazeHelpers.Rr(pLevel, Act1_gaDrlgCavesReplacements_D, &nRoll);
        if (pLevel->eD2LevelId != LevelIds.UndergroundPassageLvl1) return;
        MazeHelpers.Rr(pLevel, Act1_gaDrlgCavesReplacements_E, &nRoll);
    }

    /// <summary>Crypt (Act1.cpp:181, 1.14d 00672610)</summary>
    public static void Crypt(D2DrlgLevelStrc* pLevel)
    {
        int nRoll = MazeHelpers.Roll4(pLevel);
        MazeHelpers.Rr(pLevel, Act1_gaDrlgCryptReplacements_A, &nRoll);
        int lid = pLevel->eD2LevelId;
        if (lid == LevelIds.Crypt) MazeHelpers.Rr(pLevel, Act1_gaDrlgCryptReplacements_B, &nRoll);
        if (lid == LevelIds.Mausoleum || lid == LevelIds.MatronsDen) MazeHelpers.Rr(pLevel, Act1_gaDrlgCryptReplacements_C, &nRoll);
        if (!(0x14 < lid && lid < 0x19)) return;
        MazeHelpers.Rr(pLevel, Act1_gaDrlgCryptReplacements_D, &nRoll);
    }

    /// <summary>Jail::RollRooms (Act1/Jail.cpp:24, 1.14d 006726d0)</summary>
    public static void JailRollRooms(D2DrlgLevelStrc* pLevel)
    {
        int nRoll = MazeHelpers.Roll4(pLevel);
        MazeHelpers.Rr(pLevel, Act1_gaDrlgCatacombsRollRooms_E, &nRoll);
        int lid = pLevel->eD2LevelId;
        if (lid == LevelIds.JailLvl1) MazeHelpers.Rr(pLevel, Act1_gaDrlgCatacombsRollRooms_C, &nRoll);
        if (lid == LevelIds.JailLvl2) MazeHelpers.Rr(pLevel, Act1_gaDrlgCatacombsRollRooms_D, &nRoll);
        if (lid == LevelIds.JailLvl3)
        {
            MazeHelpers.Rr(pLevel, Act1_gaDrlgCatacombsRollRooms_A, &nRoll);
            return;
        }
        MazeHelpers.Rr(pLevel, Act1_gaDrlgCatacombsRollRooms_B, &nRoll);
    }

    private static void GrowCatacombs(int dir, D2RoomExStrc* pStartRoom) => MazeHelpers.GrowRoom(dir, pStartRoom);

    private static void Lock(D2RoomExStrc* p, int def)
    {
        MazeGen.SetDef(p, def);
        MazeGen.SetFlags(p, MazeGen.GetFlags(p) | 2);
        MazeGen.SetVariant(p, -1);
    }

    /// <summary>DRLGMAZE_ExpandCatacombsLevel (Catacombs.cpp:26). `bIsCatacombsLevel4` is a lost
    /// phantom; recovered as eD2LevelId==0x22 (Catacombs 1) — the recon writes the fixed preset
    /// 0x122 in this branch, and the deep golden for level 0x22 carries def 0x122 (while 0x23/0x24
    /// carry the seed-branch presets 0x120/0x121).</summary>
    public static void ExpandCatacombsLevel(D2DrlgLevelStrc* pLevel)
    {
        var pStartRoom = pLevel->pRoomExFirst;
        if (pLevel->eD2LevelId == LevelIds.CatacombsLvl1)
        {
            GrowCatacombs(1, pStartRoom);
            GrowCatacombs(2, pStartRoom);
            GrowCatacombs(3, pStartRoom);
            GrowCatacombs(0, pStartRoom);
            Lock(pStartRoom, 0x122);
            return;
        }
        var sNewSeed = DrlgRng.SeedNext(pLevel->sSeed);
        pLevel->sSeed = sNewSeed;
        if ((sNewSeed.nSeedLow & 1) == 0)
        {
            GrowCatacombs(1, pStartRoom);
            GrowCatacombs(3, pStartRoom);
            Lock(pStartRoom, 0x121);
            return;
        }
        GrowCatacombs(0, pStartRoom);
        GrowCatacombs(2, pStartRoom);
        Lock(pStartRoom, 0x120);
    }

    public static void ReplaceCatacombsRooms(D2DrlgLevelStrc* pLevel)
    {
        int nVariant = MazeHelpers.Roll4(pLevel);
        MazeHelpers.Rr(pLevel, Act1_gaDrlgCatacombsReplacements_A, &nVariant);
        if (pLevel->eD2LevelId != LevelIds.CatacombsLvl2) return;
        MazeHelpers.Rr(pLevel, Act1_gaDrlgCatacombsReplacements_B, &nVariant);
    }

    /// <summary>DRLGMAZE_GenerateBarracksLayout (Act1/Barracks.cpp:27, 1.14d 00673120).
    /// gaDrlgBarracksReplacements_A/_B are [4] (one entry per exit direction). The connector
    /// room's preset/variant/bPickSource args (0xa7, exitVariant, 1) were Ghidra phantom-register
    /// drops on DRLGMAZE_AllocRoomExAndPickPreset; recovered from the call site @0x673120. The
    /// indirect PTR_ARRAY_006f0468 dispatch (FindRightMost/BottomMost/LeftMost) is
    /// __fastcall(ECX=pDrlgLevelStrc) — disasm @0x673145 shows ECX=EDI, so it searches the
    /// BARRACKS level itself; the Jail level (id 0x1b) only supplies the alignment coords.</summary>
    public static void GenerateBarracksLayout(D2DrlgLevelStrc* pDrlgLevelStrc)
    {
        var pJailLevel = Drlg.GetLevelAndAlloc(pDrlgLevelStrc->pDrlg, LevelIds.OuterCloister);
        // Level 0x1b (Courtyard 1) is a PRESET level, so its pDrlgLevelData is a
        // D2DrlgLevelDataPresetArea, not a maze row. The engine reads the jail-exit variant from
        // byte offset 4 of that struct, which on 32-bit is nPickedFile. Read the NAMED field so
        // the 64-bit build uses the right offset.
        int nJailExitVariant = ((D2DrlgLevelDataPresetArea*)pJailLevel->pDrlgLevelData)->nPickedFile;
        D2RoomExStrc* pEdgeRoom;
        switch (nJailExitVariant)
        {
            case 0: pEdgeRoom = MazeGen.FindRightMostRoom(pDrlgLevelStrc); break;
            case 1: pEdgeRoom = MazeGen.FindBottomMostRoom(pDrlgLevelStrc); break;
            case 2: pEdgeRoom = MazeGen.FindLeftMostRoom(pDrlgLevelStrc); break;
            default: return;
        }
        if (pEdgeRoom == null) return;
        int nOffsetX = pJailLevel->sCoordinatesAndSize.WorldPosition.x;
        int nOffsetY = pJailLevel->sCoordinatesAndSize.WorldPosition.y;
        var pLvlMazeTxt = MazeGen.MazeTxt(pDrlgLevelStrc);
        int jailW = pJailLevel->sCoordinatesAndSize.WorldSize.x;
        int jailH = pJailLevel->sCoordinatesAndSize.WorldSize.y;
        switch (nJailExitVariant)
        {
            case 0:
                {
                    var pConnector = MazeGen.AllocRoomExAndPickPreset(MazeDeps.DIRECTION_SOUTHWEST, pEdgeRoom, 0xa7, 0, 1);
                    DrlgRoom.AllocDrlgOrth(pConnector, pJailLevel, MazeDeps.DIRECTION_SOUTHWEST, 0);
                    nOffsetX -= pConnector->sCoords.WorldPosition.x + pLvlMazeTxt->SizeX;
                    nOffsetY += jailH / 2 - pConnector->sCoords.WorldPosition.y;
                    break;
                }
            case 1:
                {
                    var pConnector = MazeGen.AllocRoomExAndPickPreset(MazeDeps.DIRECTION_NORTHWEST, pEdgeRoom, 0xa7, 1, 1);
                    DrlgRoom.AllocDrlgOrth(pConnector, pJailLevel, MazeDeps.DIRECTION_NORTHWEST, 0);
                    nOffsetX += -6 + (jailW / 2 - pConnector->sCoords.WorldPosition.x);
                    nOffsetY -= pConnector->sCoords.WorldPosition.y + pLvlMazeTxt->SizeY;
                    break;
                }
            default:
                {
                    var pConnector = MazeGen.AllocRoomExAndPickPreset(MazeDeps.DIRECTION_SOUTHEAST, pEdgeRoom, 0xa7, 2, 1);
                    DrlgRoom.AllocDrlgOrth(pConnector, pJailLevel, MazeDeps.DIRECTION_SOUTHEAST, 0);
                    nOffsetX += jailW - pConnector->sCoords.WorldPosition.x;
                    nOffsetY += 1 + (jailH / 2 - pConnector->sCoords.WorldPosition.y);
                    break;
                }
        }
        // Seed-roll picks which replacement set runs first; the SECOND ReplaceRoom indexes the
        // table with the post-advance roll (faithful to the recon ordering).
        int nRoll = nJailExitVariant;
        var DVar1 = DrlgRng.SeedNext(pDrlgLevelStrc->sSeed);
        pDrlgLevelStrc->sSeed = DVar1;
        D2DrlgReplaceRoomStrc pReplaceData;
        if ((DVar1.nSeedLow & 1) == 0)
        {
            MazeGen.ReplaceRoom(Act1_gaDrlgBarracksReplacements_B[nRoll], pDrlgLevelStrc, &nRoll);
            pReplaceData = Act1_gaDrlgBarracksReplacements_A[nRoll];
        }
        else
        {
            MazeGen.ReplaceRoom(Act1_gaDrlgBarracksReplacements_A[nRoll], pDrlgLevelStrc, &nRoll);
            pReplaceData = Act1_gaDrlgBarracksReplacements_B[nRoll];
        }
        MazeGen.ReplaceRoom(pReplaceData, pDrlgLevelStrc, &nRoll);
        MazeHelpers.Translate(pDrlgLevelStrc, nOffsetX, nOffsetY);
    }
}

/// <summary>maze/Act2.zig (recon Maze/Act2.cpp + Act2/Tombs.cpp).</summary>
internal static unsafe class MazeAct2
{
    /// <summary>Lair (Act2.cpp:24, 1.14d 00672dc0)</summary>
    public static void Lair(D2DrlgLevelStrc* pLevel)
    {
        int nRoll = MazeHelpers.Roll4(pLevel);
        D2DrlgReplaceRoomStrc pReplaceEntry;
        if (pLevel->eD2LevelId == LevelIds.MaggotLairLvl3)
        {
            MazeGen.ReplaceRoom(Act2_D2DrlgReplaceRoomStrc_006f00e8[0], pLevel, &nRoll);
            nRoll = 3;
            // The staff is always located in the "upper" version of the normal stairs.
            pReplaceEntry = Act2_D2DrlgReplaceRoomStrc_006f00d8[0];
        }
        else
        {
            pReplaceEntry = Act2_gaDrlgLairReplacements_A[nRoll];
        }
        MazeGen.ReplaceRoom(pReplaceEntry, pLevel, &nRoll);
        MazeHelpers.Rr(pLevel, Act2_gaDrlgLairReplacements_B, &nRoll);
    }

    /// <summary>Tombs::ReplaceAreas (Act2/Tombs.cpp:27, 1.14d 00672be0)</summary>
    public static void TombsReplaceAreas(D2DrlgLevelStrc* pLevel)
    {
        // Find the first room whose def >= 0x1ad (the centre orientation marker).
        var pRoomEx = pLevel->pRoomExFirst;
        int nCentreDef = 0;
        while (pRoomEx != null)
        {
            nCentreDef = MazeGen.GetDef(pRoomEx);
            if (nCentreDef >= 0x1ad) break;
            pRoomEx = pRoomEx->pRoomExNext;
        }
        int nRoll;
        switch (nCentreDef)
        {
            case 0x1bc: nRoll = 3; break;
            case 0x1bd: nRoll = 1; break;
            case 0x1be: nRoll = 0; break;
            case 0x1bf: nRoll = 2; break;
            default: return; // recon halts; treat as unsupported
        }
        int lid = pLevel->eD2LevelId;
        var pDrlg = pLevel->pDrlg;
        if (0x36 < lid && lid < 0x3b) MazeHelpers.Rr(pLevel, Act2_gaDrlgReplaceAreas_A, &nRoll);
        if (lid == LevelIds.HallsoftheDeadLvl2) MazeHelpers.Rr(pLevel, Act2_gaDrlgReplaceAreas_B, &nRoll);
        if (lid == LevelIds.StonyTombLvl2 || lid == LevelIds.ClawViperTempleLvl2) MazeHelpers.Rr(pLevel, Act2_gaDrlgAreaReplacementsB, &nRoll);
        if (0x41 < lid && lid < 0x49 && lid != pDrlg->nStaffTombLevel) MazeHelpers.Rr(pLevel, Act2_gaDrlgAreaReplacementsB, &nRoll);
        if (lid == LevelIds.StonyTombLvl2) MazeHelpers.Rr(pLevel, Act2_gaDrlgReplaceAreas_C, &nRoll);
        if (lid == LevelIds.HallsoftheDeadLvl3) MazeHelpers.Rr(pLevel, Act2_gaDrlgReplaceAreas_D, &nRoll);
        if (lid == LevelIds.StonyTombLvl2 || lid == LevelIds.ClawViperTempleLvl2) MazeHelpers.Rr(pLevel, Act2_gaDrlgReplaceAreas_E, &nRoll);
        if (lid == pDrlg->nStaffTombLevel) MazeHelpers.Rr(pLevel, Act2_gaDrlgReplaceAreas_F, &nRoll);
        if (lid != pDrlg->nBossTombLevel) return;
        MazeHelpers.Rr(pLevel, Act2_gaDrlgReplaceAreas_G, &nRoll);
    }
}

/// <summary>maze/Act2Sewer.zig (recon Maze/Act2/Sewer.cpp). The special-preset replacement
/// (ScanReplaceSpecial) for the Act 2 Sewers; produces the out-of-range Defs.</summary>
internal static unsafe class MazeAct2Sewer
{
    /// <summary>DRLGMAZE_GenerateSewerLevel (Sewer.cpp:27, 1.14d 00672810).</summary>
    public static void GenerateSewerLevel(D2DrlgLevelStrc* pDrlgLevelStrc)
    {
        var sNewSeed = DrlgRng.SeedNext(pDrlgLevelStrc->sSeed);
        pDrlgLevelStrc->sSeed = sNewSeed;
        int nDirection = (sNewSeed.nSeedLow & 1) * 2 | MazeDeps.DIRECTION_NORTHEAST;
        sNewSeed = DrlgRng.SeedNext(pDrlgLevelStrc->sSeed);
        pDrlgLevelStrc->sSeed = sNewSeed;
        int nVariant = sNewSeed.nSeedLow & 3;

        switch (pDrlgLevelStrc->eD2LevelId)
        {
            case LevelIds.A2SewersLvl1:
                {
                    var pNewRoom = MazeGen.FindTopMostRoom(pDrlgLevelStrc);
                    var pTempRoom = MazeHelpers.GrowRoom(1, pNewRoom);
                    pNewRoom = MazeHelpers.GrowRoom(1, pTempRoom);
                    // 1.14d 0x672984: PUSH 0x1 / PUSH 0x0 / PUSH 0x14d, XOR EAX,EAX -> eDir 0,
                    // preset 0x14d, variant 0, bPickSource 1. The variant is NOT -1: it pins the
                    // room's DS1 to File[0], so SelectRandomPresetFile returns before the roll.
                    MazeGen.AllocRoomExAndPickPreset(MazeDeps.DIRECTION_SOUTHEAST, pNewRoom, 0x14d, 0, 1);

                    pNewRoom = MazeGen.FindRightMostRoom(pDrlgLevelStrc);
                    pTempRoom = MazeHelpers.GrowRoom(2, pNewRoom);
                    pNewRoom = MazeHelpers.GrowRoom(2, pTempRoom);
                    // 1.14d 0x672a89: PUSH 0x1 / PUSH 0x0 / PUSH 0x150 -> preset 0x150, variant 0.
                    pNewRoom = MazeGen.AllocRoomExAndPickPreset(nDirection, pNewRoom, 0x150, 0, 1);

                    // Final appended room in nDirection off pNewRoom (no PickRoomPresets).
                    var pRoomEx = DrlgRoom.AllocRoomEx(pNewRoom->pLevel, 2);
                    var t = MazeGen.MazeTxt(pRoomEx->pLevel);
                    pRoomEx->sCoords.WorldSize.x = t->SizeX;
                    pRoomEx->sCoords.WorldSize.y = t->SizeY;
                    if (!MazeDeps.ReplaceRoomWithNewRoom(nDirection, pRoomEx, pNewRoom))
                    {
                        DrlgRoom.FreeDrlgRoomEx(pRoomEx);
                    }
                    else
                    {
                        DrlgRoom.AllocNodesForBothRoomEx(pNewRoom, pRoomEx, nDirection);
                        DrlgRoom.AddRoomExToLevel(pNewRoom->pLevel, pRoomEx);
                        MazeGen.PickRoomPreset(pRoomEx);
                    }
                    MazeHelpers.Rr(pDrlgLevelStrc, Act2Sewer_gaDrlgSewerReplacements_C, &nVariant);
                    break;
                }
            case LevelIds.A2SewersLvl2:
                MazeHelpers.Rr(pDrlgLevelStrc, Act2Sewer_gaDrlgSewerAreaReplacements, &nVariant);
                MazeHelpers.Rr(pDrlgLevelStrc, Act2Sewer_gaDrlgSewerReplacements_D, &nVariant);
                MazeHelpers.Rr(pDrlgLevelStrc, Act2Sewer_gaDrlgSewerReplacements_C, &nVariant);
                break;
            case LevelIds.A2SewersLvl3:
                MazeHelpers.Rr(pDrlgLevelStrc, Act2Sewer_gaDrlgSewerAreaReplacements, &nVariant);
                MazeHelpers.Rr(pDrlgLevelStrc, Act2Sewer_gaDrlgSewerReplacements_A, &nVariant);
                break;
            case LevelIds.AncientTunnels:
                MazeHelpers.Rr(pDrlgLevelStrc, Act2Sewer_gaDrlgSewerAreaReplacements, &nVariant);
                MazeHelpers.Rr(pDrlgLevelStrc, Act2Sewer_gaDrlgSewerReplacements_B, &nVariant);
                break;
        }
    }
}

/// <summary>maze/Act3.zig (recon Maze/Act3.cpp + Act3/{Durance,Sewers}.cpp).</summary>
internal static unsafe class MazeAct3
{
    /// <summary>DungeonRoll (Act3.cpp:23, 1.14d 00672ea0)</summary>
    public static void DungeonRoll(D2DrlgLevelStrc* pLevel)
    {
        int nRoll = MazeHelpers.Roll4(pLevel);
        MazeHelpers.Rr(pLevel, Act3_DRLG_ACT3_DUNGEON_PRESETS, &nRoll);
        MazeHelpers.Rr(pLevel, Act3_DRLG_ACT3_DUNGEON_PRESETS2, &nRoll);
    }

    /// <summary>Durance::ReplaceRooms (Act3/Durance.cpp:26, 1.14d 00672f60)</summary>
    public static void DuranceReplaceRooms(D2DrlgLevelStrc* pLevel)
    {
        int nRoll = MazeHelpers.Roll4(pLevel);
        MazeHelpers.Rr(pLevel, Act3_DRLG_DURA_ENTRY, &nRoll);
        if (pLevel->eD2LevelId == LevelIds.DuranceofHateLvl2)
        {
            MazeHelpers.Rr(pLevel, Act3_DRLG_DURA_WAYPOINT, &nRoll);
            MazeHelpers.Rr(pLevel, Act3_DRLG_DURA_EXIT, &nRoll);
        }
        if (pLevel->eD2LevelId != LevelIds.DuranceofHateLvl1) return;
        MazeHelpers.Rr(pLevel, Act3_DRLG_DURA_EXIT, &nRoll);
    }

    /// <summary>Sewers::ReplaceSewerRooms (Act3/Sewers.cpp:22, 1.14d 00672f00)</summary>
    public static void ReplaceSewerRooms(D2DrlgLevelStrc* pLevel)
    {
        int nVariant = MazeHelpers.Roll4(pLevel);
        MazeHelpers.Rr(pLevel, Act3_DRLG_ACT3_SEWERS_REPLACE_ONE, &nVariant);
        MazeHelpers.Rr(pLevel, Act3_DRLG_ACT3_SEWERS_REPLACE_TWO, &nVariant);
    }
}

/// <summary>maze/Act4.zig (recon Maze/Act4/RiverOfFlames.cpp).</summary>
internal static unsafe class MazeAct4
{
    private static D2RoomExStrc* LockedRoom(D2RoomExStrc* pFrom)
    {
        var pRoomEx = DrlgRoom.AllocRoomEx(pFrom->pLevel, 2);
        var t = MazeGen.MazeTxt(pRoomEx->pLevel);
        pRoomEx->sCoords.WorldSize.x = t->SizeX;
        pRoomEx->sCoords.WorldSize.y = t->SizeY;
        if (!MazeDeps.ReplaceRoomWithNewRoom(1, pRoomEx, pFrom))
        {
            DrlgRoom.FreeDrlgRoomEx(pRoomEx);
            return null;
        }
        DrlgRoom.AllocNodesForBothRoomEx(pFrom, pRoomEx, 1);
        DrlgRoom.AddRoomExToLevel(pFrom->pLevel, pRoomEx);
        MazeGen.SetFlags(pRoomEx, MazeGen.GetFlags(pRoomEx) | 2);
        MazeGen.SetDef(pRoomEx, 0x358);
        MazeGen.SetVariant(pRoomEx, -1);
        return pRoomEx;
    }

    /// <summary>DRLGMAZE_GenerateRiverOfFlames (Act4/RiverOfFlames.cpp:27, 1.14d 00673320). Two
    /// connector rooms grown off the bottom/top-most maze rooms (preset 0x354 then 0x357, variant
    /// -1, bPickSource=1 — Ghidra phantom-register drops recovered from the call site @0x673320),
    /// then two manual 0x358 rooms, an orth into the Chaos Sanctuary (id 0x6c), a seed-rolled
    /// replacement, ExpandRoomsWithPresets(0x344, pLastRoom), and a translate aligning the river
    /// to the Chaos Sanctuary.</summary>
    public static void GenerateRiverOfFlames(D2DrlgLevelStrc* pDrlgLevelStrc)
    {
        var pDrlg = pDrlgLevelStrc->pDrlg;
        if (pDrlg->nActNo == 4) return;
        var pChaosLevel = Drlg.GetLevelAndAlloc(pDrlgLevelStrc->pDrlg, LevelIds.ChaosSanctuary);

        var pNewRoom = MazeGen.FindBottomMostRoom(pDrlgLevelStrc);
        if (pNewRoom == null) return;
        MazeGen.AllocRoomExAndPickPreset(MazeDeps.DIRECTION_NORTHWEST, pNewRoom, 0x354, -1, 1);
        pNewRoom = MazeGen.FindTopMostRoom(pDrlgLevelStrc);
        if (pNewRoom == null) return;
        pNewRoom = MazeGen.AllocRoomExAndPickPreset(MazeDeps.DIRECTION_NORTHEAST, pNewRoom, 0x357, -1, 1);
        if (pNewRoom == null) return;

        var pRoomEx = LockedRoom(pNewRoom);
        if (pRoomEx == null) return;
        pNewRoom = LockedRoom(pRoomEx);
        if (pNewRoom == null) return;

        DrlgRoom.AllocDrlgOrth(pNewRoom, pChaosLevel, MazeDeps.DIRECTION_NORTHEAST, 0);
        int nHeight = pChaosLevel->sCoordinatesAndSize.WorldSize.y;
        int nRoomPosY = pNewRoom->sCoords.WorldPosition.y;
        int nRoomSizeX = pNewRoom->sCoords.WorldSize.x;
        int nChaosLevelY = pChaosLevel->sCoordinatesAndSize.WorldPosition.y;
        int nRoomPosX = pNewRoom->sCoords.WorldPosition.x;
        int nChaosLevelX = pChaosLevel->sCoordinatesAndSize.WorldPosition.x;

        var DVar1 = DrlgRng.SeedNext(pDrlgLevelStrc->sSeed);
        pDrlgLevelStrc->sSeed = DVar1;
        MazeGen.ReplaceRoom(Act4_gaDrlgRiverOfFlamesReplacements[DVar1.nSeedLow & 1], pDrlgLevelStrc, null);
        MazeGen.ExpandRoomsWithPresets(pDrlgLevelStrc, 0x344, pNewRoom);

        MazeHelpers.Translate(pDrlgLevelStrc, (nRoomSizeX * 2 - nRoomPosX) + nChaosLevelX, (nHeight - nRoomPosY) + nChaosLevelY);
    }
}

/// <summary>maze/Act5.zig (recon Maze/Act5/{Temple,IceCaves,Worldstone}.cpp).</summary>
internal static unsafe class MazeAct5
{
    /// <summary>Temple::ReplaceRooms (Act5/Temple.cpp:29, 1.14d 00673000). Three-entry table A
    /// (direction 2 / SOUTHWEST excluded); roll is low-word % 3. Ghidra decompiled B as [3], but the
    /// engine indexes it with nRoll in {1,2,3}, so it has a 4th element read from .rdata @0x6f0358
    /// (1.14d): {src=0x414, dst=0x41b, file=-1, dir=2 SOUTHWEST}.</summary>
    public static void TempleReplaceRooms(D2DrlgLevelStrc* pLevel)
    {
        var DVar1 = DrlgRng.SeedNext(pLevel->sSeed);
        pLevel->sSeed = DVar1;
        // nRollMod3 = low % 3 (low-word divide, 1.14d 0x673016).
        int nRollMod3 = (int)((uint)DVar1.nSeedLow % 3);
        int nRoll = nRollMod3;
        if (pLevel->eD2LevelId != LevelIds.HallsofVaught)
            MazeGen.ReplaceRoom(Act5_gaDrlgRoomReplacementsA[nRollMod3], pLevel, &nRoll);
        if (pLevel->eD2LevelId != LevelIds.HallsofPain) return;
        // recon: error-halt if no replacement advanced nRoll. Then B+nRoll.
        if (nRoll == nRollMod3) return;
        MazeHelpers.Rr(pLevel, Act5_gaDrlgRoomReplacementsB, &nRoll);
    }

    /// <summary>IceCaves::ReplaceRooms (Act5/IceCaves.cpp:25, 1.14d 00673530)</summary>
    public static void IceCavesReplaceRooms(D2DrlgLevelStrc* pLevel)
    {
        int nRoll = MazeHelpers.Roll4(pLevel);
        MazeHelpers.Rr(pLevel, Act5_DRLG_ICECAVES_REPLACE_ONE, &nRoll);
        MazeHelpers.Rr(pLevel, Act5_DRLG_ICECAVES_REPLACE_TWO, &nRoll);
        MazeHelpers.Rr(pLevel, Act5_DRLG_ICECAVES_REPLACE_THREE, &nRoll);
        int lid = pLevel->eD2LevelId;
        if (lid != LevelIds.CrystalizedPassage)
        {
            if (lid == LevelIds.GlacialTrail) MazeHelpers.Rr(pLevel, Act5_DRLG_ICECAVES_REPLACE_FOUR, &nRoll);
            else if (lid != LevelIds.AncientsWay) return;
        }
        MazeHelpers.Rr(pLevel, Act5_DRLG_ICECAVES_REPLACE_FIVE, &nRoll);
    }

    /// <summary>Worldstone::PutRedNextLevelAreasInPlace (Act5/Worldstone.cpp:25, 006730b0)</summary>
    public static void WorldstonePutRed(D2DrlgLevelStrc* pLevel)
    {
        int nRoll = MazeHelpers.Roll4(pLevel);
        MazeHelpers.Rr(pLevel, Act5_DRLG_WORLDSTONE_REPLACEMENT, &nRoll);
        if (pLevel->eD2LevelId != LevelIds.WorldstoneLvl2) return;
        MazeHelpers.Rr(pLevel, Act5_DRLG_WORLDSTONE_WAYPOINT_ROOMS, &nRoll);
    }
}

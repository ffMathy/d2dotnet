// Port of libd2 packages/drlg/src/drlg/outdoors/ActInit.zig (MIT, (c) 2026 jaenster).
//
// Per-act OUTDOOR placement entry points (the InitAct{1..5}OutdoorLevel tree dispatched from
// DRLGOUTDOOR_GenerateLevel, Outdoors.cpp:1402). Faithful transform of
// recon/closure/{Outdoors,OutWild,OutDesr,OutPlace,OutSiege}.cpp.

namespace D2Dotnet.Maps.Engine.Outdoors;

internal static unsafe class ActInit
{
    private static D2DrlgLevelDataWildernessLevel* Wild(D2DrlgLevelStrc* pLevel) => (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;

    // BuildChaosSanctuary   Outdoors.cpp:1481. Stamps the 5x5 grid of 3x3-cell presets that
    // tiles the whole 15x15 Chaos Sanctuary (level 0x6c). Each SpawnEx with nFileIndex=-1 rolls
    // one file index off pLevel->sSeed via the per-preset round-robin tracker.
    private static readonly int[] aChaosSanctuaryPresets =
    {
        0x344, 0x344, 0x344, 0x344, 0x344,
        0x344, 0x344, 0x35d, 0x344, 0x344,
        0x344, 0x35a, 0x35e, 0x35b, 0x344,
        0x344, 0x344, 0x35c, 0x344, 0x344,
        0x344, 0x344, 0x359, 0x344, 0x344,
    };

    public static void BuildChaosSanctuary(D2DrlgLevelStrc* pLevel)
    {
        for (int i = 0; i < 0x19; i++)
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, i % 5 * 3, i / 5 * 3, aChaosSanctuaryPresets[i], -1, 0);
    }

    /// <summary>DRLGOUTPLACE_PlaceAct4SpecialPresets   Outdoors.cpp:1467</summary>
    public static void PlaceAct4SpecialPresets(D2DrlgLevelStrc* pLevel)
    {
        uint flags = Wild(pLevel)->dwFlags;
        if ((flags & 0x400000) != 0) OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, 1, 0x31e, -1, 0);
        if ((flags & 0x800000) == 0) return;
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, 4, 0x31e, -1, 0);
    }

    /// <summary>InitAct1OutdoorLevel   OutWild.cpp:87 (1.14d 006807f0)</summary>
    public static void InitAct1OutdoorLevel(D2DrlgLevelStrc* pLevel)
    {
        Drlg.ApplyAct1WildRoadFlags(pLevel);
        int eLevelId = pLevel->eD2LevelId;
        if (eLevelId != (int)AreaId.BloodMoor && eLevelId != (int)AreaId.ColdPlains && eLevelId != (int)AreaId.BurialGrounds)
            Border.MarkBorderJunctions(pLevel);
        Border.SetOutGridLinkFlags(pLevel);
        Border.PlaceAct1245OutdoorBorders(pLevel);

        // OutWild.cpp:95 — levels 2-7: interleaved secondary borders and OutRoom steps
        if (eLevelId > 1 && eLevelId < 8)
        {
            Outdoors.AddAct124SecondaryBorder(pLevel, 0, 4);
            OutRoom.FillBorderCornersAndPresets(pLevel);
            Outdoors.AddAct124SecondaryBorder(pLevel, 1, 4);
            Outdoors.AddAct124SecondaryBorder(pLevel, 2, 4);
            OutRoom.SpawnTownTransitionsAndCaves(pLevel);
            Outdoors.AddAct124SecondaryBorder(pLevel, 3, 4);
            OutRoom.LinkOutdoorRoomExits(pLevel);
        }

        // OutWild.cpp:104 — Stony Field (0x27): four secondary borders back-to-back
        if (eLevelId == (int)AreaId.MooMooFarm)
        {
            Outdoors.AddAct124SecondaryBorder(pLevel, 0, 4);
            Outdoors.AddAct124SecondaryBorder(pLevel, 1, 4);
            Outdoors.AddAct124SecondaryBorder(pLevel, 2, 4);
            Outdoors.AddAct124SecondaryBorder(pLevel, 3, 4);
        }

        // OutWild.cpp:110 — waypoint for levels 3-6
        if (eLevelId > 2 && eLevelId < 7) Outdoors.SpawnAct12Waypoint(pLevel);
        // OutWild.cpp:113 — shrines for levels 2-7
        if (eLevelId > 1 && eLevelId < 8) Outdoors.SpawnAct12Shrines(pLevel, 5);

        // OutWild.cpp:117 — preset spawns for ALL Act-1 outdoor levels
        OutRoom.SpawnAct1LevelPresets(pLevel);
    }

    /// <summary>InitAct2OutdoorLevel   OutDesr.cpp:265 (the whole interleaved Act-2 desert tail).</summary>
    public static void InitAct2OutdoorLevel(D2DrlgLevelStrc* pLevel) => OutDesr.InitAct2OutdoorLevel(pLevel);

    // DRLGOUTDOOR_SpawnAct4OutdoorPresets   Outdoors/Act4.cpp:22 (1.14d 0067e6a0). .rdata tables
    // (indexed by eLevelId, normalised to 0-based via LEA [EAX*4+0xfffffe60]):
    //   gnAct4OutdoorsPresetOffsetByType  @ 0x6f22b0: 0x68→0x32C  0x69→0x331  0x6a→0x337
    //   gnAct5SceneryPresetOffsetByOrientation @ 0x6f22a4: 0x68→0x33C  0x69→0x340  0x6a→0x340
    private static readonly int[] gnAct4PresetBaseA = { 0x32C, 0x331, 0x337 };
    private static readonly int[] gnAct4PresetBaseB = { 0x33C, 0x340, 0x340 };

    private static void SpawnAct4OutdoorPresets(D2DrlgLevelStrc* pLevel)
    {
        int eLevelId = pLevel->eD2LevelId;
        if (eLevelId == (int)AreaId.CityOfTheDamned) OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x32b, -1, 0, 0xf);
        int nPresetBaseA = gnAct4PresetBaseA[eLevelId - 0x68];
        int nPresetBaseB = gnAct4PresetBaseB[eLevelId - 0x68];
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseA, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseA + 1, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseA + 1, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseA + 2, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseA + 2, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseA + 3, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseA + 3, -1, 0, 0xf);
        if (eLevelId == (int)AreaId.PlainsOfDespair) OutPlace.SpawnOutdoorLevelPreset(pLevel, 0x336, -1, 0, 0xf);
        nPresetBaseA += 4;
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseA, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseA, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseA, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseA, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseB, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseB + 1, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseB + 1, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseB + 2, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseB + 2, -1, 0, 0xf);
        nPresetBaseB += 3;
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseB, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseB, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseB, -1, 0, 0xf);
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nPresetBaseB, -1, 0, 0xf);
    }

    /// <summary>InitAct4OutdoorLevel   Outdoors.cpp:1520 (1.14d 0067e890)</summary>
    public static void InitAct4OutdoorLevel(D2DrlgLevelStrc* pLevel)
    {
        int eLevelId = pLevel->eD2LevelId;
        Border.SetOutGridLinkFlags(pLevel);
        if (eLevelId != (int)AreaId.ChaosSanctuary) Border.PlaceAct1245OutdoorBorders(pLevel);
        if (eLevelId <= 0x67) return;
        if (eLevelId < 0x6b)
        {
            // DRLGPLACE_Adjacent4DirMirrored (Drlg.cpp:3247) is called only for level 104 in
            // ACT4_D2DrlgLevelData1 (Drlg.cpp:4385). It draws one seed and sets
            // gdwDrlgLevelPlacementFlags = 0x800000 when sNextSeed&1=1, else 0x400000.
            // Drlg.cpp:4772 then OR's that flag into level 104's pDrlgLevelData->dwFlags.
            // Levels 105/106 use fpLDE2 (no placement-flag write) so their dwFlags = 0.
            // Direction 3 places the level at:
            //   sNextSeed&1=1: y = prevY + prevH - levelH + 8  → flag 0x800000
            //   sNextSeed&1=0: y = prevY - 8                   → flag 0x400000
            // Deriving the flag from the actual WorldPositions is faithful because the
            // positions are the sole observable output of that seed draw.
            if (eLevelId == (int)AreaId.OuterSteppes)
            {
                var myPos = pLevel->sCoordinatesAndSize;
                for (var ps = pLevel->pDrlg->pLevel; ps != null; ps = ps->pLevelNext)
                {
                    if (ps->eD2LevelId == (int)AreaId.PandemoniumFortress)
                    {
                        var prevPos = ps->sCoordinatesAndSize;
                        uint flag = myPos.WorldPosition.y == prevPos.WorldPosition.y + prevPos.WorldSize.y - myPos.WorldSize.y + 8 ? 0x800000u : 0x400000u;
                        Wild(pLevel)->dwFlags |= flag;
                        break;
                    }
                }
            }
            PlaceAct4SpecialPresets(pLevel);
            Outdoors.AddAct124SecondaryBorder(pLevel, 1, 799);
            Outdoors.AddAct124SecondaryBorder(pLevel, 2, 799);
            Outdoors.AddAct124SecondaryBorder(pLevel, 3, 799);
            SpawnAct4OutdoorPresets(pLevel);
        }
        else if (eLevelId == (int)AreaId.ChaosSanctuary)
        {
            BuildChaosSanctuary(pLevel);
        }
    }
}

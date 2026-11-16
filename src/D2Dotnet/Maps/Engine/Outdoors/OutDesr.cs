// Port of libd2 packages/drlg/src/drlg/outdoors/OutDesr.zig (MIT, (c) 2026 jaenster).
//
// Act-2 OUTDOOR placement tail (recon/closure/OutDesr.cpp, InitAct2OutdoorLevel @
// OutDesr.cpp:265, 1.14d 0067f980). Faithful transform of the per-level Act-2 desert tail:
// cliffs, exits, fills, ruins, tomb entries, secondary borders, waypoints, shrines. The
// interleave ORDER per level matches the recon switch exactly so the level seed stays in
// lock-step with the engine.

namespace D2Dotnet.Maps.Engine.Outdoors;

internal static unsafe class OutDesr
{
    private static D2DrlgLevelDataWildernessLevel* Wild(D2DrlgLevelStrc* pLevel) => (D2DrlgLevelDataWildernessLevel*)pLevel->pDrlgLevelData;

    /// <summary>PlacePresetVariants   OutDesr.cpp:34 (1.14d 0067f470). Rolls ONE variant index
    /// off pLevel->sSeed, then spawns each variant once round-robin starting at the rolled index.
    /// bIterateFiles spawns every File row of each variant; otherwise spawns the variant with
    /// file -1 (file-tracker draw).</summary>
    private static void PlacePresetVariants(D2DrlgLevelStrc* pLevel, ReadOnlySpan<int> pLevelPrestIds, bool bIterateFiles)
    {
        int nVariants = pLevelPrestIds.Length;
        int nRandVariantIdx = 0;
        if (nVariants >= 1) nRandVariantIdx = (int)DrlgRng.RandomNumberSelector(&pLevel->sSeed, (uint)nVariants);
        if (nVariants <= 0) return;

        int nIterCountdown = nVariants;
        while (true)
        {
            int idx = nRandVariantIdx;
            if (!bIterateFiles)
            {
                OutPlace.SpawnOutdoorLevelPreset(pLevel, pLevelPrestIds[idx], -1, 0, 0xf);
            }
            else
            {
                var pLvlPrest = DrlgTables.LvlPrestGetLine(pLevelPrestIds[idx]);
                int nFileCount = pLvlPrest->Files;
                for (int nFileIdx = 0; nFileIdx < nFileCount; nFileIdx++)
                    OutPlace.SpawnOutdoorLevelPreset(pLevel, pLevelPrestIds[idx], nFileIdx, 0, 0xf);
            }
            nRandVariantIdx = (nRandVariantIdx + 1) % nVariants;
            nIterCountdown -= 1;
            if (nIterCountdown == 0) break;
        }
    }

    /// <summary>DRLGOUTPLACE_PlaceAct2CanyonExitPresets   OutDesr.cpp:85 (1.14d 0067f560). Walks
    /// the level's inter-level orth list for the link to the Lut Gholein town (level 0x28); the
    /// recon's pRoomEx[1].apTiles[0x1f] deref is the linked level id read through the orth union.
    /// Places the canyon exit on the side facing town. No seed.</summary>
    private static void PlaceAct2CanyonExitPresets(D2DrlgLevelStrc* pLevel)
    {
        var pData = Wild(pLevel);
        var pOrth = pData->pOrthData;
        while (pOrth != null)
        {
            var pLinked = (D2DrlgLevelStrc*)pOrth->pRoomEx;
            if (pLinked != null && pLinked->eD2LevelId == (int)AreaId.LutGholein) break;
            pOrth = pOrth->pNext;
        }
        if (pOrth == null) return;
        if (pOrth->neDrlgDirection == 3) // DIRECTION_NORTHWEST
        {
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 0, pData->nGridCoordsHeight - 1, 0x16b, -1, 0);
            return;
        }
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, pData->nGridCoordsWidth - 1, 0, 0x16a, -1, 0);
    }

    /// <summary>PlaceCliffs   OutDesr.cpp:109 (1.14d 0067f5c0). Rolls one seed, picks one of 8
    /// cliff record-groups by (seedLow &amp; 7), then spawns the group's 5 preset records.</summary>
    private static void PlaceCliffs(D2DrlgLevelStrc* pLevel)
    {
        int nSeedLow = DrlgRng.RollRandomSeed(&pLevel->sSeed);
        int group = (int)((uint)nSeedLow & 7);
        for (int r = 0; r < 5; r++)
        {
            int b = group * 20 + r * 4;
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, gaCliffTable[b + 2], gaCliffTable[b + 3], gaCliffTable[b + 0], gaCliffTable[b + 1], 0);
        }
    }

    /// <summary>AddExits   OutDesr.cpp:126 (1.14d 0067f670)</summary>
    private static void AddExits(D2DrlgLevelStrc* pLevel)
    {
        int nExitPresetId;
        switch (pLevel->eD2LevelId)
        {
            case (int)AreaId.RockyWaste:
            case (int)AreaId.DryHills: nExitPresetId = 0x184; break;
            case (int)AreaId.FarOasis: nExitPresetId = 0x186; break;
            case (int)AreaId.LostCity: nExitPresetId = 0x19c; break;
            case (int)AreaId.ValleyOfSnakes: nExitPresetId = 0x185; break;
            default: return;
        }
        OutPlace.SpawnOutdoorLevelPreset(pLevel, nExitPresetId, -1, 0, 0xf);
    }

    // Place fills/ruins   OutDesr.cpp:164..261
    private static void PlaceFillsInRockyWaste(D2DrlgLevelStrc* pLevel)
    {
        ReadOnlySpan<int> ids = [395, 0x19b, 0x191, 0x192, 399, 0x18e, 0x193];
        PlacePresetVariants(pLevel, ids, false);
    }

    private static void PlaceFillsInDryHills(D2DrlgLevelStrc* pLevel)
    {
        ReadOnlySpan<int> ids = [404, 0x195, 0x196, 0x197, 0x18b, 0x19b, 400, 0x18e];
        PlacePresetVariants(pLevel, ids[4..8], false);
        PlacePresetVariants(pLevel, ids[0..4], true);
    }

    private static void PlaceFillsInFarOasis(D2DrlgLevelStrc* pLevel)
    {
        ReadOnlySpan<int> ids = [411, 399, 398, 403];
        ReadOnlySpan<int> bonus = [395];
        PlacePresetVariants(pLevel, ids, false);
        PlacePresetVariants(pLevel, bonus, true);
        PlacePresetVariants(pLevel, bonus, true);
    }

    private static void PlaceRuinsInLostCity(D2DrlgLevelStrc* pLevel)
    {
        ReadOnlySpan<int> ids = [413, 0x198, 0x199, 0x19a];
        PlacePresetVariants(pLevel, ids, false);
    }

    private static void PlaceFillsInLostCity(D2DrlgLevelStrc* pLevel)
    {
        ReadOnlySpan<int> ids = [395, 400, 0x18e, 0x194, 0x195, 0x19b];
        PlacePresetVariants(pLevel, ids[0..5], false);
        PlacePresetVariants(pLevel, ids[5..6], true);
        PlacePresetVariants(pLevel, ids[5..6], true);
    }

    private static void PlaceFillsInCanyon(D2DrlgLevelStrc* pLevel)
    {
        ReadOnlySpan<int> ids = [401, 0x192, 0x196, 0x197, 0x193, 0x188, 0x189];
        PlacePresetVariants(pLevel, ids[0..5], false);
        PlacePresetVariants(pLevel, ids[5..7], true);
    }

    /// <summary>PlaceTombEntriesInCanyon   OutDesr.cpp:235 (1.14d 0067f8d0). Fixed 9-record
    /// placement (no seed) from gaTombCanyonPresetOffsets .rdata @0x006f2610. Then a fixed 0x18a
    /// preset.</summary>
    private static void PlaceTombEntriesInCanyon(D2DrlgLevelStrc* pLevel)
    {
        for (int r = 0; r < 9; r++)
        {
            int b = r * 4;
            OutPlace.SpawnOutdoorLevelPresetEx(pLevel, gaTombTable[b + 2], gaTombTable[b + 3], gaTombTable[b + 0], gaTombTable[b + 1], 0);
        }
        OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 4, 4, 0x18a, -1, 0);
    }

    /// <summary>InitAct2OutdoorLevel   OutDesr.cpp:265 (1.14d 0067f980)</summary>
    public static void InitAct2OutdoorLevel(D2DrlgLevelStrc* pLevel)
    {
        Border.SetOutGridLinkFlags(pLevel);
        Border.PlaceAct1245OutdoorBorders(pLevel);
        switch (pLevel->eD2LevelId)
        {
            case (int)AreaId.RockyWaste:
                PlaceAct2CanyonExitPresets(pLevel);
                Outdoors.AddAct124SecondaryBorderSubId213(pLevel);
                AddExits(pLevel);
                Outdoors.SpawnAct12Shrines(pLevel, 5);
                PlaceFillsInRockyWaste(pLevel);
                break;
            case (int)AreaId.DryHills:
                PlaceCliffs(pLevel);
                Outdoors.AddAct124SecondaryBorderSubId213(pLevel);
                AddExits(pLevel);
                Outdoors.SpawnAct12Waypoint(pLevel);
                Outdoors.SpawnAct12Shrines(pLevel, 5);
                PlaceFillsInDryHills(pLevel);
                break;
            case (int)AreaId.FarOasis:
                {
                    PlaceCliffs(pLevel);
                    Outdoors.AddAct124SecondaryBorderSubId213(pLevel);
                    AddExits(pLevel);
                    ReadOnlySpan<int> ids = [396, 0x18d];
                    PlacePresetVariants(pLevel, ids, false);
                    Outdoors.SpawnAct12Waypoint(pLevel);
                    Outdoors.SpawnAct12Shrines(pLevel, 5);
                    PlaceFillsInFarOasis(pLevel);
                    break;
                }
            case (int)AreaId.LostCity:
                PlaceCliffs(pLevel);
                Outdoors.AddAct124SecondaryBorderSubId213(pLevel);
                AddExits(pLevel);
                PlaceRuinsInLostCity(pLevel);
                Outdoors.SpawnAct12Waypoint(pLevel);
                Outdoors.SpawnAct12Shrines(pLevel, 5);
                PlaceFillsInLostCity(pLevel);
                break;
            case (int)AreaId.ValleyOfSnakes:
                AddExits(pLevel);
                break;
            case (int)AreaId.CanyonOfTheMagi:
                PlaceTombEntriesInCanyon(pLevel);
                Outdoors.AddAct124SecondaryBorderSubId213(pLevel);
                Outdoors.SpawnAct12Shrines(pLevel, 5);
                PlaceFillsInCanyon(pLevel);
                break;
            case (int)AreaId.ForgottenSands:
                OutPlace.SpawnOutdoorLevelPresetEx(pLevel, 4, 4, 0x18a, -1, 0);
                Outdoors.AddAct124SecondaryBorderSubId213(pLevel);
                PlaceFillsInCanyon(pLevel);
                Outdoors.SpawnAct12Shrines(pLevel, 5);
                break;
        }
    }

    // gaCliffTable — gaCliffWallGridOffsets .rdata base @0x006f2390, 8 groups x 5 records x
    // {presetId, fileIdx, posX, posY} (160 int32, Ghidra 1.14d).
    private static readonly int[] gaCliffTable =
    {
        376, 1, 0, 4, 378, -1, 2, 4, 377, -1, 4, 4, 377, -1, 6, 4, 376, 2, 8, 4,
        376, 1, 0, 4, 377, -1, 2, 4, 378, -1, 4, 4, 377, -1, 6, 4, 376, 2, 8, 4,
        376, 1, 0, 4, 377, -1, 2, 4, 377, -1, 4, 4, 378, -1, 6, 4, 376, 2, 8, 4,
        376, 2, 8, 4, 377, -1, 6, 4, 382, -1, 4, 4, 381, -1, 4, 6, 379, 2, 4, 8,
        376, 2, 8, 4, 378, -1, 6, 4, 382, -1, 4, 4, 380, -1, 4, 6, 379, 2, 4, 8,
        379, 1, 4, 0, 381, -1, 4, 2, 380, -1, 4, 4, 380, -1, 4, 6, 379, 2, 4, 8,
        379, 1, 4, 0, 380, -1, 4, 2, 381, -1, 4, 4, 380, -1, 4, 6, 379, 2, 4, 8,
        379, 1, 4, 0, 380, -1, 4, 2, 380, -1, 4, 4, 381, -1, 4, 6, 379, 2, 4, 8,
    };

    // gaTombTable — gaTombCanyonPresetOffsets .rdata base @0x006f2610, 9 records x {presetId,
    // fileIdx, posX, posY} (36 int32, Ghidra 1.14d).
    private static readonly int[] gaTombTable =
    {
        384, 0, 8, 0, 383, 2, 6, 0, 383, 1, 4, 0, 383, 0, 2, 0, 387, 0, 0, 0,
        385, 0, 0, 2, 385, 1, 0, 4, 385, 2, 0, 6, 386, 0, 0, 8,
    };
}

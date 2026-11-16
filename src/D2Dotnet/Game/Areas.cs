using System.Collections.Frozen;

namespace D2Dotnet.Game;

/// <summary>A waypoint's place in the waypoint menu, and the areas next to it.</summary>
/// <param name="Tab">The act tab, 1 to 5.</param>
/// <param name="Row">The row in the tab, 1 to 9.</param>
/// <param name="LinkedFrom">The areas the waypoint area is reached from; empty for towns.</param>
public sealed record WaypointLocation(int Tab, int Row, IReadOnlyList<AreaId> LinkedFrom);

/// <summary>Facts about areas that the game's tables do not hold: towns, acts by id, terror zones and the waypoint menu.</summary>
public static class Areas
{
    /// <summary>The areas that can be terrorized.</summary>
    public static IReadOnlySet<AreaId> TerrorZoneAreas { get; } = new[]
    {
        AreaId.BloodMoor,
        AreaId.DenOfEvil,
        AreaId.ColdPlains,
        AreaId.CaveLevel1,
        AreaId.PitLevel1,
        AreaId.BurialGrounds,
        AreaId.Crypt,
        AreaId.Mausoleum,
        AreaId.StonyField,
        AreaId.DarkWood,
        AreaId.UndergroundPassageLevel1,
        AreaId.UndergroundPassageLevel2,
        AreaId.BlackMarsh,
        AreaId.HoleLevel1,
        AreaId.HoleLevel2,
        AreaId.ForgottenTower,
        AreaId.Barracks,
        AreaId.JailLevel1,
        AreaId.JailLevel2,
        AreaId.JailLevel3,
        AreaId.InnerCloister,
        AreaId.Cathedral,
        AreaId.CatacombsLevel1,
        AreaId.CatacombsLevel2,
        AreaId.CatacombsLevel3,
        AreaId.CatacombsLevel4,
        AreaId.Tristram,
        AreaId.MooMooFarm,
        AreaId.RockyWaste,
        AreaId.SewersLevel1Act2,
        AreaId.SewersLevel2Act2,
        AreaId.SewersLevel3Act2,
        AreaId.DryHills,
        AreaId.HallsOfTheDeadLevel1,
        AreaId.HallsOfTheDeadLevel2,
        AreaId.HallsOfTheDeadLevel3,
        AreaId.FarOasis,
        AreaId.LostCity,
        AreaId.ValleyOfSnakes,
        AreaId.ClawViperTempleLevel1,
        AreaId.ClawViperTempleLevel2,
        AreaId.AncientTunnels,
        AreaId.TalRashasTomb1,
        AreaId.TalRashasTomb2,
        AreaId.TalRashasTomb3,
        AreaId.TalRashasTomb4,
        AreaId.TalRashasTomb5,
        AreaId.TalRashasTomb6,
        AreaId.TalRashasTomb7,
        AreaId.ArcaneSanctuary,
        AreaId.SpiderForest,
        AreaId.SpiderCavern,
        AreaId.Travincal,
        AreaId.GreatMarsh,
        AreaId.FlayerJungle,
        AreaId.FlayerDungeonLevel1,
        AreaId.FlayerDungeonLevel2,
        AreaId.FlayerDungeonLevel3,
        AreaId.KurastBazaar,
        AreaId.RuinedTemple,
        AreaId.DisusedFane,
        AreaId.DuranceOfHateLevel1,
        AreaId.DuranceOfHateLevel2,
        AreaId.DuranceOfHateLevel3,
        AreaId.OuterSteppes,
        AreaId.PlainsOfDespair,
        AreaId.CityOfTheDamned,
        AreaId.RiverOfFlame,
        AreaId.ChaosSanctuary,
        AreaId.BloodyFoothills,
        AreaId.FrigidHighlands,
        AreaId.Abaddon,
        AreaId.GlacialTrail,
        AreaId.DrifterCavern,
        AreaId.CrystallinePassage,
        AreaId.FrozenRiver,
        AreaId.ArreatPlateau,
        AreaId.PitOfAcheron,
        AreaId.AncientsWay,
        AreaId.IcyCellar,
        AreaId.NihlathaksTemple,
        AreaId.WorldstoneKeepLevel1,
        AreaId.WorldstoneKeepLevel2,
        AreaId.WorldstoneKeepLevel3,
        AreaId.ThroneOfDestruction,
    }.ToFrozenSet();

    /// <summary>The waypoints, by waypoint area: their place in the waypoint menu and the areas linked to them.</summary>
    public static IReadOnlyDictionary<AreaId, WaypointLocation> Waypoints { get; } = new Dictionary<AreaId, WaypointLocation>
    {
        // Act 1
        [AreaId.RogueEncampment] = new(1, 1, []),
        [AreaId.ColdPlains] = new(1, 2, [AreaId.RogueEncampment, AreaId.BloodMoor]),
        [AreaId.StonyField] = new(1, 3, [AreaId.ColdPlains]),
        [AreaId.DarkWood] = new(1, 4, [AreaId.StonyField, AreaId.UndergroundPassageLevel1]),
        [AreaId.BlackMarsh] = new(1, 5, [AreaId.DarkWood]),
        [AreaId.OuterCloister] = new(1, 6, [AreaId.BlackMarsh, AreaId.TamoeHighland, AreaId.MonasteryGate]),
        [AreaId.JailLevel1] = new(1, 7, [AreaId.OuterCloister, AreaId.Barracks]),
        [AreaId.InnerCloister] = new(1, 8, [AreaId.JailLevel1, AreaId.JailLevel2, AreaId.JailLevel3]),
        [AreaId.CatacombsLevel2] = new(1, 9, [AreaId.InnerCloister, AreaId.Cathedral, AreaId.CatacombsLevel1]),
        // Act 2
        [AreaId.LutGholein] = new(2, 1, []),
        [AreaId.SewersLevel2Act2] = new(2, 2, [AreaId.LutGholein, AreaId.SewersLevel1Act2]),
        [AreaId.DryHills] = new(2, 3, [AreaId.LutGholein, AreaId.RockyWaste]),
        [AreaId.HallsOfTheDeadLevel2] = new(2, 4, [AreaId.DryHills, AreaId.HallsOfTheDeadLevel1]),
        [AreaId.FarOasis] = new(2, 5, [AreaId.DryHills]),
        [AreaId.LostCity] = new(2, 6, [AreaId.FarOasis]),
        [AreaId.PalaceCellarLevel1] = new(2, 7, [AreaId.LutGholein, AreaId.HaremLevel1, AreaId.HaremLevel2]),
        [AreaId.ArcaneSanctuary] = new(2, 8, [AreaId.PalaceCellarLevel1, AreaId.PalaceCellarLevel2, AreaId.PalaceCellarLevel3]),
        [AreaId.CanyonOfTheMagi] = new(2, 9, [AreaId.ArcaneSanctuary]),
        // Act 3
        [AreaId.KurastDocks] = new(3, 1, []),
        [AreaId.SpiderForest] = new(3, 2, [AreaId.KurastDocks]),
        [AreaId.GreatMarsh] = new(3, 3, [AreaId.SpiderForest]),
        [AreaId.FlayerJungle] = new(3, 4, [AreaId.GreatMarsh]),
        [AreaId.LowerKurast] = new(3, 5, [AreaId.FlayerJungle]),
        [AreaId.KurastBazaar] = new(3, 6, [AreaId.LowerKurast]),
        [AreaId.UpperKurast] = new(3, 7, [AreaId.KurastBazaar]),
        [AreaId.Travincal] = new(3, 8, [AreaId.UpperKurast, AreaId.KurastCauseway]),
        [AreaId.DuranceOfHateLevel2] = new(3, 9, [AreaId.Travincal, AreaId.DuranceOfHateLevel1]),
        // Act 4
        [AreaId.PandemoniumFortress] = new(4, 1, []),
        [AreaId.CityOfTheDamned] = new(4, 2, [AreaId.PandemoniumFortress, AreaId.OuterSteppes, AreaId.PlainsOfDespair]),
        [AreaId.RiverOfFlame] = new(4, 3, [AreaId.CityOfTheDamned]),
        // Act 5
        [AreaId.Harrogath] = new(5, 1, []),
        [AreaId.FrigidHighlands] = new(5, 2, [AreaId.Harrogath, AreaId.BloodyFoothills]),
        [AreaId.ArreatPlateau] = new(5, 3, [AreaId.FrigidHighlands]),
        [AreaId.CrystallinePassage] = new(5, 4, [AreaId.ArreatPlateau]),
        [AreaId.GlacialTrail] = new(5, 5, [AreaId.CrystallinePassage]),
        [AreaId.HallsOfPain] = new(5, 6, [AreaId.Harrogath, AreaId.NihlathaksTemple, AreaId.HallsOfAnguish]),
        [AreaId.FrozenTundra] = new(5, 7, [AreaId.GlacialTrail]),
        [AreaId.AncientsWay] = new(5, 8, [AreaId.FrozenTundra]),
        [AreaId.WorldstoneKeepLevel2] = new(5, 9, [AreaId.AncientsWay, AreaId.ArreatSummit, AreaId.WorldstoneKeepLevel1]),
    }.ToFrozenDictionary();

    /// <summary>True for the five towns.</summary>
    public static bool IsTown(this AreaId area) =>
        area is AreaId.RogueEncampment or AreaId.LutGholein or AreaId.KurastDocks or AreaId.PandemoniumFortress or AreaId.Harrogath;

    /// <summary>True when the area can be a terror zone (see <see cref="TerrorZoneAreas"/>).</summary>
    public static bool CanBeTerrorized(this AreaId area) => TerrorZoneAreas.Contains(area);

    /// <summary>
    /// The act of an area by its id range (ids below 40 are act 1, from 109 on act 5). The game's own value is
    /// <see cref="Data.LevelInfo.Act"/>.
    /// </summary>
    public static ActId GetAct(this AreaId area) => (int)area switch
    {
        < 40 => ActId.Act1,
        < 75 => ActId.Act2,
        < 103 => ActId.Act3,
        < 109 => ActId.Act4,
        _ => ActId.Act5,
    };
}

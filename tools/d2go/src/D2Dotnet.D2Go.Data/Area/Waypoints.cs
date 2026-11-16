// Port of d2go pkg/data/area/waypoint.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Collections.Frozen;

namespace D2Dotnet.D2Go.Data.Area;

/// <summary>A waypoint's place in the waypoint menu and the areas it is reached from (Go <c>area.WPAddress</c>).</summary>
/// <param name="Tab">The act tab, 1 to 5.</param>
/// <param name="Row">The row in the tab, 1 to 9.</param>
/// <param name="LinkedFrom">Areas linked to the waypoint area; empty for towns (a nil slice in Go).</param>
public sealed record WPAddress(int Tab, int Row, IReadOnlyList<AreaId> LinkedFrom);

/// <summary>The waypoints (Go <c>area.WPAddresses</c>).</summary>
public static class Waypoints
{
    /// <summary>The addresses of the waypoints in the game UI and the linked areas between them, by waypoint area.</summary>
    public static IReadOnlyDictionary<AreaId, WPAddress> WPAddresses { get; } = new Dictionary<AreaId, WPAddress>
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
        [AreaId.ThePandemoniumFortress] = new(4, 1, []),
        [AreaId.CityOfTheDamned] = new(4, 2, [AreaId.ThePandemoniumFortress, AreaId.OuterSteppes, AreaId.PlainsOfDespair]),
        [AreaId.RiverOfFlame] = new(4, 3, [AreaId.CityOfTheDamned]),
        // Act 5
        [AreaId.Harrogath] = new(5, 1, []),
        [AreaId.FrigidHighlands] = new(5, 2, [AreaId.Harrogath, AreaId.BloodyFoothills]),
        [AreaId.ArreatPlateau] = new(5, 3, [AreaId.FrigidHighlands]),
        [AreaId.CrystallinePassage] = new(5, 4, [AreaId.ArreatPlateau]),
        [AreaId.GlacialTrail] = new(5, 5, [AreaId.CrystallinePassage]),
        [AreaId.HallsOfPain] = new(5, 6, [AreaId.Harrogath, AreaId.NihlathaksTemple, AreaId.HallsOfAnguish]),
        [AreaId.FrozenTundra] = new(5, 7, [AreaId.GlacialTrail]),
        [AreaId.TheAncientsWay] = new(5, 8, [AreaId.FrozenTundra]),
        [AreaId.TheWorldStoneKeepLevel2] = new(5, 9, [AreaId.TheAncientsWay, AreaId.ArreatSummit, AreaId.TheWorldStoneKeepLevel1]),
    }.ToFrozenDictionary();
}

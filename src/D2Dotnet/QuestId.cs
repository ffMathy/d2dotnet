// Values and names from d2go pkg/data/quest/quest.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).
// The oracle tests check every value against the pinned d2go source.

namespace D2Dotnet;

/// <summary>A quest, in quest log order (six per act, Act IV has three).</summary>
/// <remarks>
/// Values and names come from d2go's <c>quest.Quest</c>.
/// </remarks>
public enum QuestId : short
{
    Act1DenOfEvil = 0,
    Act1SistersBurialGrounds = 1,
    Act1ToolsOfTheTrade = 2,
    Act1TheSearchForCain = 3,
    Act1TheForgottenTower = 4,
    Act1SistersToTheSlaughter = 5,
    Act2RadamentsLair = 6,
    Act2TheHoradricStaff = 7,
    Act2TaintedSun = 8,
    Act2ArcaneSanctuary = 9,
    Act2TheSummoner = 10,
    Act2TheSevenTombs = 11,
    Act3LamEsensTome = 12,
    Act3KhalimsWill = 13,
    Act3BladeOfTheOldReligion = 14,
    Act3TheGoldenBird = 15,
    Act3TheBlackenedTemple = 16,
    Act3TheGuardian = 17,
    Act4TheFallenAngel = 18,
    Act4HellForge = 19,
    Act4TerrorsEnd = 20,
    Act5SiegeOnHarrogath = 21,
    Act5RescueOnMountArreat = 22,
    Act5PrisonOfIce = 23,
    Act5BetrayalOfHarrogath = 24,
    Act5RiteOfPassage = 25,
    Act5EveOfDestruction = 26,
}

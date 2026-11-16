// Port of d2go pkg/memory/quests.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using D2Dotnet.D2Go.Data.Quest;

namespace D2Dotnet.D2Go.Memory;

public sealed partial class GameReader
{
    // Go getQuests: each quest's two status bytes in the quest buffer (after its first 3 bytes).
    private static readonly (QuestId Quest, int Start)[] QuestBytes =
    [
        // Act 1
        (QuestId.Act1DenOfEvil, 0),
        (QuestId.Act1SistersBurialGrounds, 2),
        (QuestId.Act1ToolsOfTheTrade, 4),
        (QuestId.Act1TheSearchForCain, 6),
        (QuestId.Act1TheForgottenTower, 8),
        (QuestId.Act1SistersToTheSlaughter, 10),
        // Act 2
        (QuestId.Act2RadamentsLair, 16),
        (QuestId.Act2TheHoradricStaff, 18),
        (QuestId.Act2TaintedSun, 20),
        (QuestId.Act2ArcaneSanctuary, 22),
        (QuestId.Act2TheSummoner, 24),
        (QuestId.Act2TheSevenTombs, 26),
        // Act 3
        (QuestId.Act3LamEsensTome, 32),
        (QuestId.Act3KhalimsWill, 34),
        (QuestId.Act3BladeOfTheOldReligion, 36),
        (QuestId.Act3TheGoldenBird, 38),
        (QuestId.Act3TheBlackenedTemple, 40),
        (QuestId.Act3TheGuardian, 42),
        // Act 4
        (QuestId.Act4TheFallenAngel, 48),
        (QuestId.Act4TerrorsEnd, 50),
        (QuestId.Act4HellForge, 52),
        // Act 5
        (QuestId.Act5SiegeOnHarrogath, 68),
        (QuestId.Act5RescueOnMountArreat, 70),
        (QuestId.Act5PrisonOfIce, 72),
        (QuestId.Act5BetrayalOfHarrogath, 74),
        (QuestId.Act5RiteOfPassage, 76),
        (QuestId.Act5EveOfDestruction, 78),
    ];

    /// <summary>The status flags of every quest from the quest buffer (Go <c>getQuests</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="questBytes"/> is shorter than 80 bytes (Go panics).</exception>
    internal static Quests GetQuests(ReadOnlySpan<byte> questBytes)
    {
        var quests = new Dictionary<QuestId, QuestStates>();
        foreach (var (quest, start) in QuestBytes)
            quests[quest] = GetQuestStatus(questBytes.Slice(start, 2));

        return new Quests(quests);
    }

    /// <summary>
    /// The set flags of a quest's two bytes, read big-endian (first byte high), highest bit first: bit <c>n</c> is
    /// <see cref="QuestStatus"/> <c>n</c> (Go <c>getQuestStatus</c>).
    /// </summary>
    internal static QuestStates GetQuestStatus(ReadOnlySpan<byte> questBytes)
    {
        var combinedValue = (ushort)((questBytes[0] << 8) | questBytes[1]);

        var activeStates = new List<QuestStatus>();
        for (var i = 0; i < 16; i++)
        {
            if ((combinedValue & (1 << (15 - i))) != 0)
                activeStates.Add((QuestStatus)(15 - i));
        }

        return new QuestStates(activeStates);
    }
}

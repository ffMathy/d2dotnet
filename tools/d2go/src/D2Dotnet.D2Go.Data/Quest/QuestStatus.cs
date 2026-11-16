// Port of d2go pkg/data/quest/status.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).
// Converted mechanically from the Go source; the oracle tests check every value against it.

namespace D2Dotnet.D2Go.Data.Quest;

/// <summary>Bit index of a quest status flag (Go <c>quest.Status</c>).</summary>
public enum QuestStatus : short
{
    RewardGranted = 0,
    RewardPending = 1,
    QuestInitiated = 2,
    LeaveTown = 3,
    EnterFirstArea = 4,
    InProgress1 = 5,
    InProgress2 = 6,
    InProgress3 = 7,
    InProgress4 = 8,
    InProgress5 = 9,
    InProgress6 = 10,
    InProgress7 = 11,
    UpdateQuestLogCompleted = 12,
    PrimaryGoalCompleted = 13,
    Unknown1 = 14,
    Unknown2 = 15,
}

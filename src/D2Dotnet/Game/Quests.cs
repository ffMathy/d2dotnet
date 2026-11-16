namespace D2Dotnet.Game;

/// <summary>
/// The status flags of a quest: the 16 bits the game keeps per quest (the first of its two bytes holds the high bits).
/// </summary>
[Flags]
public enum QuestStatus : ushort
{
    /// <summary>No flag set: the quest has not been started.</summary>
    None = 0,

    /// <summary>Bit 0: the reward has been granted.</summary>
    RewardGranted = 1 << 0,

    /// <summary>Bit 1: the reward is pending.</summary>
    RewardPending = 1 << 1,

    /// <summary>Bit 2: the quest has been started.</summary>
    QuestInitiated = 1 << 2,

    /// <summary>Bit 3: the player left town.</summary>
    LeaveTown = 1 << 3,

    /// <summary>Bit 4: the player entered the quest's first area.</summary>
    EnterFirstArea = 1 << 4,

    /// <summary>Bit 5: quest specific progress.</summary>
    InProgress1 = 1 << 5,

    /// <summary>Bit 6: quest specific progress.</summary>
    InProgress2 = 1 << 6,

    /// <summary>Bit 7: quest specific progress.</summary>
    InProgress3 = 1 << 7,

    /// <summary>Bit 8: quest specific progress.</summary>
    InProgress4 = 1 << 8,

    /// <summary>Bit 9: quest specific progress.</summary>
    InProgress5 = 1 << 9,

    /// <summary>Bit 10: quest specific progress.</summary>
    InProgress6 = 1 << 10,

    /// <summary>Bit 11: quest specific progress.</summary>
    InProgress7 = 1 << 11,

    /// <summary>Bit 12: the quest log shows the quest as completed.</summary>
    UpdateQuestLogCompleted = 1 << 12,

    /// <summary>Bit 13: the quest's primary goal is completed.</summary>
    PrimaryGoalCompleted = 1 << 13,

    /// <summary>Bit 14: unknown.</summary>
    Unknown1 = 1 << 14,

    /// <summary>Bit 15: unknown.</summary>
    Unknown2 = 1 << 15,
}

/// <summary>Helpers for <see cref="QuestStatus"/>.</summary>
public static class QuestStatusExtensions
{
    /// <summary>True when the quest log shows the quest as completed (<see cref="QuestStatus.UpdateQuestLogCompleted"/>).</summary>
    public static bool IsCompleted(this QuestStatus status) => (status & QuestStatus.UpdateQuestLogCompleted) != 0;
}

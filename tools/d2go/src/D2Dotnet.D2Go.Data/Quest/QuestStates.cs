// Port of d2go pkg/data/quest/quest.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace D2Dotnet.D2Go.Data.Quest;

/// <summary>The status flags that are set for a quest (Go <c>quest.States</c>).</summary>
[CollectionBuilder(typeof(QuestStates), nameof(Create))]
public sealed class QuestStates : ReadOnlyCollection<QuestStatus>
{
    public QuestStates()
        : base([])
    {
    }

    public QuestStates(IEnumerable<QuestStatus> states)
        : base([.. states])
    {
    }

    /// <summary>No status (Go's nil slice).</summary>
    public static new QuestStates Empty { get; } = new();

    /// <summary>Collection expression builder.</summary>
    public static QuestStates Create(ReadOnlySpan<QuestStatus> states) => new(states.ToArray());

    /// <summary>True when <see cref="QuestStatus.UpdateQuestLogCompleted"/> is set.</summary>
    public bool Completed() => HasStatus(QuestStatus.UpdateQuestLogCompleted);

    /// <summary>True when <paramref name="status"/> is set.</summary>
    public bool HasStatus(QuestStatus status)
    {
        foreach (var state in this)
        {
            if (state == status)
                return true;
        }

        return false;
    }
}

/// <summary>The status flags of every quest (Go <c>quest.Quests</c>, a <c>map[Quest]States</c>).</summary>
public sealed class Quests : ReadOnlyDictionary<QuestId, QuestStates>
{
    public Quests()
        : base(new Dictionary<QuestId, QuestStates>())
    {
    }

    public Quests(IEnumerable<KeyValuePair<QuestId, QuestStates>> quests)
        : base(new Dictionary<QuestId, QuestStates>(quests))
    {
    }

    /// <summary>No quests (Go's nil map).</summary>
    public static new Quests Empty { get; } = new();

    /// <summary>The states of <paramref name="quest"/>, or no states when it is missing (Go's map index <c>q[quest]</c>).</summary>
    public QuestStates StatesOf(QuestId quest) => TryGetValue(quest, out var states) ? states : QuestStates.Empty;
}

using System.Collections.Generic;
using UnityEngine;

public sealed class ServerGameEventQueue
{
    private readonly List<GameEventEntryPacket> pendingEvents = new();
    private uint nextEventSeq;

    public int Count => pendingEvents.Count;

    // - Role: Queue game started.
    public void QueueGameStarted(uint serverTick, ulong starterClientId, Vector2 position)
    {
        Queue(serverTick, GameEventType.GameStarted, starterClientId, starterClientId, ServerFeedbackType.None, position, 0f);
    }

    // - Role: Queue game ended.
    public void QueueGameEnded(uint serverTick, Vector2 position)
    {
        Queue(serverTick, GameEventType.GameEnded, 0, 0, ServerFeedbackType.None, position, 0f);
    }

    // - Role: Queue tagger changed.
    public void QueueTaggerChanged(uint serverTick, ulong oldTaggerId, ulong newTaggerId, Vector2 position)
    {
        Queue(serverTick, GameEventType.TaggerChanged, oldTaggerId, newTaggerId, ServerFeedbackType.None, position, 0f);
        QueueFeedback(serverTick, ServerFeedbackType.TaggerChanged, oldTaggerId, newTaggerId, position, 0f);
    }

    // - Role: Queue item applied.
    public void QueueItemApplied(uint serverTick, ulong playerId, uint itemId, Vector2 position)
    {
        Queue(serverTick, GameEventType.ItemApplied, playerId, itemId, ServerFeedbackType.None, position, 0f);
    }

    // - Role: Queue item collected.
    public void QueueItemCollected(uint serverTick, ulong playerId, uint itemId, Vector2 position)
    {
        QueueFeedback(serverTick, ServerFeedbackType.ItemCollected, playerId, itemId, position, 0f);
    }

    // - Role: Queue coin collected.
    public void QueueCoinCollected(uint serverTick, ulong playerId, uint coinId, Vector2 position)
    {
        QueueFeedback(serverTick, ServerFeedbackType.CoinCollected, playerId, coinId, position, 0f);
    }

    // - Role: Queue one game event.
    private void Queue(
        uint serverTick,
        GameEventType eventType,
        ulong subjectClientId,
        ulong targetClientId,
        ServerFeedbackType feedbackType,
        Vector2 position,
        float rotation)
    {
        if (eventType == GameEventType.None)
        {
            return;
        }

        pendingEvents.Add(new GameEventEntryPacket
        {
            eventSeq = nextEventSeq,
            serverTick = serverTick,
            serverTime = serverTick / GameNetProtocol.ServerTickRate,
            eventType = eventType,
            subjectClientId = subjectClientId,
            targetClientId = targetClientId,
            feedbackType = feedbackType,
            position = position,
            rotation = rotation
        });
        nextEventSeq++;
    }

    // - Role: Queue feedback.
    public void QueueFeedback(
        uint serverTick,
        ServerFeedbackType feedbackType,
        ulong subjectClientId,
        ulong targetClientId,
        Vector2 position,
        float rotation)
    {
        Queue(
            serverTick,
            GameEventType.Feedback,
            subjectClientId,
            targetClientId,
            feedbackType,
            position,
            rotation);
    }

    // - Role: Copy pending to.
    public void CopyPendingTo(List<GameEventEntryPacket> target)
    {
        target.Clear();

        int count = Mathf.Min(pendingEvents.Count, GameNetProtocol.MaxGameEventsPerBatch);
        for (int i = 0; i < count; i++)
        {
            target.Add(pendingEvents[i]);
        }
    }

    // - Role: Clear sent game events.
    public void Clear(int eventCount)
    {
        if (eventCount <= 0 || pendingEvents.Count == 0)
        {
            return;
        }

        if (eventCount >= pendingEvents.Count)
        {
            pendingEvents.Clear();
            return;
        }

        pendingEvents.RemoveRange(0, eventCount);
    }
}

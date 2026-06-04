using System.Collections.Generic;
using UnityEngine;

public sealed class ServerGameEventQueue
{
    private readonly List<GameEventEntryPacket> pendingEvents = new();
    private uint nextEventSeq;

    public int Count => pendingEvents.Count;

    public void Queue(
        uint serverTick,
        GameEventType eventType,
        ulong subjectClientId,
        ulong targetClientId,
        GameVfxType vfxType,
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
            vfxType = vfxType,
            position = position,
            rotation = rotation
        });
        nextEventSeq++;
    }

    public void QueueSpawnVfx(
        uint serverTick,
        GameVfxType vfxType,
        ulong subjectClientId,
        ulong targetClientId,
        Vector2 position,
        float rotation)
    {
        Queue(
            serverTick,
            GameEventType.SpawnVfx,
            subjectClientId,
            targetClientId,
            vfxType,
            position,
            rotation);
    }

    public void CopyPendingTo(List<GameEventEntryPacket> target)
    {
        target.Clear();

        int count = Mathf.Min(pendingEvents.Count, GameNetProtocol.MaxGameEventsPerBatch);
        for (int i = 0; i < count; i++)
        {
            target.Add(pendingEvents[i]);
        }
    }

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

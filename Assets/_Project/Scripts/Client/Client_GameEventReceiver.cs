using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Client_GameEventReceiver : MonoBehaviour
{
    private struct QueuedGameEvent
    {
        public float applyTime;
        public GameEventEntryPacket packet;
    }

    [SerializeField] private Client_NetworkDelaySimulator networkDelaySimulator;

    private readonly List<QueuedGameEvent> delayedEvents = new();
    private bool isRegistered;
    private bool hasAppliedEvent;
    private uint lastAppliedEventSeq;

    public event Action<GameEventEntryPacket> GameEventReceived;
    public bool HasAppliedEvent => hasAppliedEvent;
    public uint LastAppliedEventSeq => lastAppliedEventSeq;

    private void Start()
    {
        if (networkDelaySimulator == null)
        {
            networkDelaySimulator = GetComponent<Client_NetworkDelaySimulator>();
        }

        if (networkDelaySimulator == null)
        {
            Debug.LogWarning("[Client_GameEventReceiver] NetworkDelaySimulator is not assigned. Network delay is disabled.", this);
        }

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[Client_GameEventReceiver] NetworkManager.Singleton is null.", this);
            enabled = false;
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void Update()
    {
        TryRegisterGameEventHandler();
        FlushDelayedEvents();
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

        UnregisterGameEventHandler();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (clientId != NetworkManager.Singleton.LocalClientId)
        {
            return;
        }

        TryRegisterGameEventHandler();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (clientId != NetworkManager.Singleton.LocalClientId && clientId != NetworkManager.ServerClientId)
        {
            return;
        }

        delayedEvents.Clear();
        hasAppliedEvent = false;
        lastAppliedEventSeq = 0;

        UnregisterGameEventHandler();
    }

    private void TryRegisterGameEventHandler()
    {
        if (isRegistered)
        {
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (!NetworkManager.Singleton.IsClient)
        {
            return;
        }

        if (!NetworkManager.Singleton.IsConnectedClient)
        {
            return;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
            GameNetMessages.ServerGameEvent,
            OnServerGameEventReceived
        );

        isRegistered = true;
    }

    private void UnregisterGameEventHandler()
    {
        if (!isRegistered)
        {
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(
            GameNetMessages.ServerGameEvent
        );

        isRegistered = false;
    }

    private void OnServerGameEventReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanReceiveGameEvent())
        {
            return;
        }

        if (!GameEventBatchPacket.TryRead(ref reader, out GameEventBatchPacket packet))
        {
            return;
        }

        float delaySeconds = GetNetworkDelaySeconds();
        int eventCount = Mathf.Min(packet.eventCount, packet.events != null ? packet.events.Length : 0);
        for (int i = 0; i < eventCount; i++)
        {
            GameEventEntryPacket gameEvent = packet.events[i];
            if (delaySeconds > 0f)
            {
                delayedEvents.Add(new QueuedGameEvent
                {
                    applyTime = Time.realtimeSinceStartup + delaySeconds,
                    packet = gameEvent
                });
                continue;
            }

            ApplyGameEvent(gameEvent);
        }
    }

    private void FlushDelayedEvents()
    {
        if (delayedEvents.Count == 0)
        {
            return;
        }

        if (!CanReceiveGameEvent())
        {
            delayedEvents.Clear();
            return;
        }

        float now = Time.realtimeSinceStartup;
        for (int i = 0; i < delayedEvents.Count; i++)
        {
            QueuedGameEvent queuedEvent = delayedEvents[i];
            if (queuedEvent.applyTime > now)
            {
                continue;
            }

            delayedEvents.RemoveAt(i);
            i--;
            ApplyGameEvent(queuedEvent.packet);
        }
    }

    private void ApplyGameEvent(GameEventEntryPacket gameEvent)
    {
        if (!ShouldApplyGameEvent(gameEvent.eventSeq))
        {
            return;
        }

        hasAppliedEvent = true;
        lastAppliedEventSeq = gameEvent.eventSeq;

        GameEventReceived?.Invoke(gameEvent);
    }

    private bool ShouldApplyGameEvent(uint eventSeq)
    {
        if (!hasAppliedEvent)
        {
            return true;
        }

        if (eventSeq == lastAppliedEventSeq)
        {
            return false;
        }

        return IsNewerSequence(eventSeq, lastAppliedEventSeq);
    }

    private bool CanReceiveGameEvent()
    {
        if (NetworkManager.Singleton == null)
        {
            return false;
        }

        if (!NetworkManager.Singleton.IsClient)
        {
            return false;
        }

        if (!NetworkManager.Singleton.IsConnectedClient)
        {
            return false;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            return false;
        }

        return true;
    }

    private float GetNetworkDelaySeconds()
    {
        if (networkDelaySimulator == null)
        {
            return 0f;
        }

        return networkDelaySimulator.OneWayDelaySeconds;
    }

    private static bool IsNewerSequence(uint incomingSeq, uint currentSeq)
    {
        return unchecked((int)(incomingSeq - currentSeq)) > 0;
    }
}

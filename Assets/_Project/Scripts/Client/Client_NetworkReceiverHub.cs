using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public sealed class Client_NetworkReceiverHub : MonoBehaviour
{
    private struct QueuedServerSnapshot
    {
        public float applyTime;
        public ServerSnapshotHeaderPacket header;
        public PlayerSnapshotPacket[] players;
        public SkillSnapshotPacket[] skills;
        public ItemSnapshotPacket[] items;
    }

    private struct QueuedGameStateSnapshot
    {
        public float applyTime;
        public GameStateSnapshotPacket packet;
    }

    private struct QueuedGameEvent
    {
        public float applyTime;
        public GameEventEntryPacket packet;
    }

    private struct QueuedItemSelectionOffer
    {
        public float applyTime;
        public ItemSelectionOfferPacket packet;
    }

    private struct QueuedItemSelectionResult
    {
        public float applyTime;
        public ItemSelectionResultPacket packet;
    }

    private struct QueuedItemSelectionChoice
    {
        public float sendTime;
        public ItemSelectionChoicePacket packet;
    }

    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private Client_NetworkDelaySimulator networkDelaySimulator;

    private readonly List<QueuedServerSnapshot> delayedSnapshots = new();
    private readonly List<QueuedGameStateSnapshot> delayedGameStates = new();
    private readonly List<QueuedGameEvent> delayedGameEvents = new();
    private readonly List<QueuedItemSelectionOffer> delayedItemSelectionOffers = new();
    private readonly List<QueuedItemSelectionResult> delayedItemSelectionResults = new();
    private readonly List<QueuedItemSelectionChoice> delayedItemSelectionChoices = new();

    private FastBufferWriter itemSelectionChoiceWriter;
    private bool itemSelectionChoiceWriterCreated;
    private bool areHandlersRegistered;

    private void Awake()
    {
        if (syncManager == null)
        {
            Debug.LogError("[Client_NetworkReceiverHub] SyncManager is not assigned.", this);
            enabled = false;
            return;
        }

        itemSelectionChoiceWriter = new FastBufferWriter(GameNetProtocol.ItemSelectionPacketBufferSize, Allocator.Persistent);
        itemSelectionChoiceWriterCreated = true;
    }

    private void OnEnable()
    {
        if (syncManager != null)
        {
            syncManager.ItemSelectionChoiceRequested += OnItemSelectionChoiceRequested;
        }
    }

    private void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[Client_NetworkReceiverHub] NetworkManager.Singleton is null.", this);
            enabled = false;
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void Update()
    {
        TryRegisterMessageHandlers();
        FlushDelayedMessages();
    }

    private void OnDisable()
    {
        if (syncManager != null)
        {
            syncManager.ItemSelectionChoiceRequested -= OnItemSelectionChoiceRequested;
        }
    }

    private void OnDestroy()
    {
        UnregisterMessageHandlers();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (itemSelectionChoiceWriterCreated)
        {
            itemSelectionChoiceWriter.Dispose();
            itemSelectionChoiceWriterCreated = false;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            TryRegisterMessageHandlers();
        }
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

        ClearDelayedMessages();
        syncManager?.ClearAll();
        UnregisterMessageHandlers();
    }

    private void TryRegisterMessageHandlers()
    {
        if (areHandlersRegistered || !CanUseOnlineClientMessages())
        {
            return;
        }

        CustomMessagingManager messagingManager = NetworkManager.Singleton.CustomMessagingManager;
        messagingManager.RegisterNamedMessageHandler(GameNetMessages.ServerSnapshot, OnServerSnapshotReceived);
        messagingManager.RegisterNamedMessageHandler(GameNetMessages.ServerGameState, OnServerGameStateReceived);
        messagingManager.RegisterNamedMessageHandler(GameNetMessages.ServerGameEvent, OnServerGameEventReceived);
        messagingManager.RegisterNamedMessageHandler(GameNetMessages.ServerRoster, OnServerRosterReceived);
        messagingManager.RegisterNamedMessageHandler(GameNetMessages.ServerItemSelectionOffer, OnItemSelectionOfferReceived);
        messagingManager.RegisterNamedMessageHandler(GameNetMessages.ServerItemSelectionResult, OnItemSelectionResultReceived);
        areHandlersRegistered = true;
    }

    private void UnregisterMessageHandlers()
    {
        if (!areHandlersRegistered
            || NetworkManager.Singleton == null
            || NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        CustomMessagingManager messagingManager = NetworkManager.Singleton.CustomMessagingManager;
        messagingManager.UnregisterNamedMessageHandler(GameNetMessages.ServerSnapshot);
        messagingManager.UnregisterNamedMessageHandler(GameNetMessages.ServerGameState);
        messagingManager.UnregisterNamedMessageHandler(GameNetMessages.ServerGameEvent);
        messagingManager.UnregisterNamedMessageHandler(GameNetMessages.ServerRoster);
        messagingManager.UnregisterNamedMessageHandler(GameNetMessages.ServerItemSelectionOffer);
        messagingManager.UnregisterNamedMessageHandler(GameNetMessages.ServerItemSelectionResult);
        areHandlersRegistered = false;
    }

    private void OnServerSnapshotReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseOnlineClientMessages())
        {
            return;
        }

        if (!TryReadSnapshot(
            ref reader,
            out ServerSnapshotHeaderPacket header,
            out PlayerSnapshotPacket[] players,
            out SkillSnapshotPacket[] skills,
            out ItemSnapshotPacket[] items))
        {
            return;
        }

        float delaySeconds = GetNetworkDelaySeconds();
        if (delaySeconds > 0f)
        {
            delayedSnapshots.Add(new QueuedServerSnapshot
            {
                applyTime = Time.realtimeSinceStartup + delaySeconds,
                header = header,
                players = players,
                skills = skills,
                items = items
            });
            return;
        }

        syncManager.ApplyServerSnapshot(header, players, skills, items);
    }

    private void OnServerGameStateReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseOnlineClientMessages())
        {
            return;
        }

        if (!GameStateSnapshotPacket.TryRead(ref reader, out GameStateSnapshotPacket packet))
        {
            return;
        }

        float delaySeconds = GetNetworkDelaySeconds();
        if (delaySeconds > 0f)
        {
            delayedGameStates.Add(new QueuedGameStateSnapshot
            {
                applyTime = Time.realtimeSinceStartup + delaySeconds,
                packet = packet
            });
            return;
        }

        ApplyGameState(packet);
    }

    private void OnServerGameEventReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseOnlineClientMessages())
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
                delayedGameEvents.Add(new QueuedGameEvent
                {
                    applyTime = Time.realtimeSinceStartup + delaySeconds,
                    packet = gameEvent
                });
                continue;
            }

            syncManager.ApplyGameEvent(gameEvent);
        }
    }

    private void OnServerRosterReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseOnlineClientMessages())
        {
            return;
        }

        if (ServerRosterSnapshotPacket.TryRead(ref reader, out ServerRosterSnapshotPacket packet))
        {
            syncManager.ApplyRoster(packet);
        }
    }

    private void OnItemSelectionOfferReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseOnlineClientMessages())
        {
            return;
        }

        if (!ItemSelectionOfferPacket.TryRead(ref reader, out ItemSelectionOfferPacket packet))
        {
            return;
        }

        float delaySeconds = GetNetworkDelaySeconds();
        if (delaySeconds > 0f)
        {
            delayedItemSelectionOffers.Add(new QueuedItemSelectionOffer
            {
                applyTime = Time.realtimeSinceStartup + delaySeconds,
                packet = packet
            });
            return;
        }

        syncManager.ApplyItemSelectionOffer(packet);
    }

    private void OnItemSelectionResultReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseOnlineClientMessages())
        {
            return;
        }

        if (!ItemSelectionResultPacket.TryRead(ref reader, out ItemSelectionResultPacket packet))
        {
            return;
        }

        float delaySeconds = GetNetworkDelaySeconds();
        if (delaySeconds > 0f)
        {
            delayedItemSelectionResults.Add(new QueuedItemSelectionResult
            {
                applyTime = Time.realtimeSinceStartup + delaySeconds,
                packet = packet
            });
            return;
        }

        syncManager.ApplyItemSelectionResult(packet);
    }

    private void OnItemSelectionChoiceRequested(uint requestId, int selectedId)
    {
        ItemSelectionChoicePacket packet = new ItemSelectionChoicePacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            requestId = requestId,
            selectedId = selectedId
        };

        float delaySeconds = GetNetworkDelaySeconds();
        if (delaySeconds > 0f)
        {
            delayedItemSelectionChoices.Add(new QueuedItemSelectionChoice
            {
                sendTime = Time.realtimeSinceStartup + delaySeconds,
                packet = packet
            });
            return;
        }

        SendItemSelectionChoiceNow(packet);
    }

    private static bool TryReadSnapshot(
        ref FastBufferReader reader,
        out ServerSnapshotHeaderPacket header,
        out PlayerSnapshotPacket[] players,
        out SkillSnapshotPacket[] skills,
        out ItemSnapshotPacket[] items)
    {
        players = Array.Empty<PlayerSnapshotPacket>();
        skills = Array.Empty<SkillSnapshotPacket>();
        items = Array.Empty<ItemSnapshotPacket>();

        if (!ServerSnapshotHeaderPacket.TryRead(ref reader, out header))
        {
            return false;
        }

        players = new PlayerSnapshotPacket[header.playerCount];
        for (int i = 0; i < header.playerCount; i++)
        {
            if (!PlayerSnapshotPacket.TryRead(ref reader, out players[i]))
            {
                return false;
            }
        }

        skills = new SkillSnapshotPacket[header.skillCount];
        for (int i = 0; i < header.skillCount; i++)
        {
            if (!SkillSnapshotPacket.TryRead(ref reader, out skills[i]))
            {
                return false;
            }
        }

        items = new ItemSnapshotPacket[header.itemCount];
        for (int i = 0; i < header.itemCount; i++)
        {
            if (!ItemSnapshotPacket.TryRead(ref reader, out items[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void FlushDelayedMessages()
    {
        if (!CanUseOnlineClientMessages())
        {
            ClearDelayedMessages();
            return;
        }

        float now = Time.realtimeSinceStartup;
        FlushDelayedSnapshots(now);
        FlushDelayedGameStates(now);
        FlushDelayedGameEvents(now);
        FlushDelayedItemSelectionOffers(now);
        FlushDelayedItemSelectionResults(now);
        FlushDelayedItemSelectionChoices(now);
    }

    private void FlushDelayedSnapshots(float now)
    {
        for (int i = 0; i < delayedSnapshots.Count; i++)
        {
            QueuedServerSnapshot snapshot = delayedSnapshots[i];
            if (snapshot.applyTime > now)
            {
                continue;
            }

            delayedSnapshots.RemoveAt(i);
            i--;
            syncManager.ApplyServerSnapshot(snapshot.header, snapshot.players, snapshot.skills, snapshot.items);
        }
    }

    private void FlushDelayedGameStates(float now)
    {
        for (int i = 0; i < delayedGameStates.Count; i++)
        {
            QueuedGameStateSnapshot snapshot = delayedGameStates[i];
            if (snapshot.applyTime > now)
            {
                continue;
            }

            delayedGameStates.RemoveAt(i);
            i--;
            ApplyGameState(snapshot.packet);
        }
    }

    private void FlushDelayedGameEvents(float now)
    {
        for (int i = 0; i < delayedGameEvents.Count; i++)
        {
            QueuedGameEvent queuedEvent = delayedGameEvents[i];
            if (queuedEvent.applyTime > now)
            {
                continue;
            }

            delayedGameEvents.RemoveAt(i);
            i--;
            syncManager.ApplyGameEvent(queuedEvent.packet);
        }
    }

    private void FlushDelayedItemSelectionOffers(float now)
    {
        for (int i = 0; i < delayedItemSelectionOffers.Count; i++)
        {
            QueuedItemSelectionOffer queuedOffer = delayedItemSelectionOffers[i];
            if (queuedOffer.applyTime > now)
            {
                continue;
            }

            delayedItemSelectionOffers.RemoveAt(i);
            i--;
            syncManager.ApplyItemSelectionOffer(queuedOffer.packet);
        }
    }

    private void FlushDelayedItemSelectionResults(float now)
    {
        for (int i = 0; i < delayedItemSelectionResults.Count; i++)
        {
            QueuedItemSelectionResult queuedResult = delayedItemSelectionResults[i];
            if (queuedResult.applyTime > now)
            {
                continue;
            }

            delayedItemSelectionResults.RemoveAt(i);
            i--;
            syncManager.ApplyItemSelectionResult(queuedResult.packet);
        }
    }

    private void FlushDelayedItemSelectionChoices(float now)
    {
        for (int i = 0; i < delayedItemSelectionChoices.Count; i++)
        {
            QueuedItemSelectionChoice queuedChoice = delayedItemSelectionChoices[i];
            if (queuedChoice.sendTime > now)
            {
                continue;
            }

            delayedItemSelectionChoices.RemoveAt(i);
            i--;
            SendItemSelectionChoiceNow(queuedChoice.packet);
        }
    }

    private void ApplyGameState(GameStateSnapshotPacket packet)
    {
        syncManager.ApplyGameStateSnapshot(
            packet,
            packet.entries ?? Array.Empty<GameStateEntryPacket>(),
            packet.entryCount);
    }

    private void SendItemSelectionChoiceNow(ItemSelectionChoicePacket packet)
    {
        if (!CanUseOnlineClientMessages() || !itemSelectionChoiceWriterCreated)
        {
            return;
        }

        itemSelectionChoiceWriter.Truncate(0);
        packet.Write(ref itemSelectionChoiceWriter);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            GameNetMessages.ClientItemSelectionChoice,
            NetworkManager.ServerClientId,
            itemSelectionChoiceWriter,
            NetworkDelivery.ReliableSequenced);
    }

    private void ClearDelayedMessages()
    {
        delayedSnapshots.Clear();
        delayedGameStates.Clear();
        delayedGameEvents.Clear();
        delayedItemSelectionOffers.Clear();
        delayedItemSelectionResults.Clear();
        delayedItemSelectionChoices.Clear();
    }

    private bool CanUseOnlineClientMessages()
    {
        if (syncManager == null || syncManager.SyncMode != ClientSyncMode.Online)
        {
            return false;
        }

        if (NetworkManager.Singleton == null)
        {
            return false;
        }

        if (!NetworkManager.Singleton.IsClient || !NetworkManager.Singleton.IsConnectedClient)
        {
            return false;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            return false;
        }

        return NetworkManager.Singleton.CustomMessagingManager != null;
    }

    private float GetNetworkDelaySeconds()
    {
        return networkDelaySimulator != null ? networkDelaySimulator.OneWayDelaySeconds : 0f;
    }
}

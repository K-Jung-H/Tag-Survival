using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Client_GameStateReceiver : MonoBehaviour
{
    private struct QueuedGameStateSnapshot
    {
        public float applyTime;
        public GameStateSnapshotPacket packet;
        public GameStateEntryPacket[] entries;
        public int entryCount;
    }

    [SerializeField] private Client_NetworkDelaySimulator networkDelaySimulator;

    private readonly List<QueuedGameStateSnapshot> delayedSnapshots = new();
    private readonly List<GameStateEntryPacket> receivedEntries = new();
    private readonly Dictionary<ulong, GameStateEntryPacket> cachedEntries = new();
    private readonly List<ulong> cachedEntryKeys = new();
    private readonly List<GameStateEntryPacket> sortedEntries = new();
    private readonly GameStateEntryPacket[] sortedEntryBuffer =
        new GameStateEntryPacket[GameNetProtocol.MaxPlayers];

    private bool isRegistered;
    private bool hasGameState;
    private bool hasAppliedFullSync;
    private uint lastAppliedGameStateSeq;
    private uint lastAppliedFullSyncSeq;

    public ClientGameStateSnapshotState CurrentState { get; private set; }
    public bool HasGameState => hasGameState;
    public GameStateEntryPacket[] Entries => CurrentState.entries ?? Array.Empty<GameStateEntryPacket>();

    private void Start()
    {
        if (networkDelaySimulator == null)
        {
            networkDelaySimulator = GetComponent<Client_NetworkDelaySimulator>();
        }

        if (networkDelaySimulator == null)
        {
            Debug.LogWarning("[Client_GameStateReceiver] NetworkDelaySimulator is not assigned. Network delay is disabled.", this);
        }

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[Client_GameStateReceiver] NetworkManager.Singleton is null.", this);
            enabled = false;
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void Update()
    {
        TryRegisterGameStateHandler();
        FlushDelayedSnapshots();
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

        UnregisterGameStateHandler();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        TryRegisterGameStateHandler();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId && clientId != NetworkManager.ServerClientId)
            return;

        delayedSnapshots.Clear();
        cachedEntries.Clear();
        cachedEntryKeys.Clear();
        sortedEntries.Clear();
        ClearSortedEntryBuffer();
        CurrentState = default;
        hasGameState = false;
        hasAppliedFullSync = false;
        lastAppliedGameStateSeq = 0;
        lastAppliedFullSyncSeq = 0;

        UnregisterGameStateHandler();
    }

    private void TryRegisterGameStateHandler()
    {
        if (isRegistered)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsClient)
            return;

        if (!NetworkManager.Singleton.IsConnectedClient)
            return;

        if (NetworkManager.Singleton.IsServer)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
            GameNetMessages.ServerGameState,
            OnServerGameStateReceived
        );

        isRegistered = true;
    }

    private void UnregisterGameStateHandler()
    {
        if (!isRegistered)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(
            GameNetMessages.ServerGameState
        );

        isRegistered = false;
    }

    private void OnServerGameStateReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanReceiveGameState())
            return;

        if (!TryReadGameStatePacket(ref reader, out GameStateSnapshotPacket packet, receivedEntries))
            return;

        float delaySeconds = GetNetworkDelaySeconds();
        if (delaySeconds > 0f)
        {
            GameStateEntryPacket[] queuedEntries = new GameStateEntryPacket[receivedEntries.Count];
            for (int i = 0; i < receivedEntries.Count; i++)
            {
                queuedEntries[i] = receivedEntries[i];
            }

            delayedSnapshots.Add(new QueuedGameStateSnapshot
            {
                applyTime = Time.realtimeSinceStartup + delaySeconds,
                packet = packet,
                entries = queuedEntries,
                entryCount = queuedEntries.Length
            });
            return;
        }

        ApplyGameState(packet, receivedEntries, receivedEntries.Count);
    }

    private void FlushDelayedSnapshots()
    {
        if (delayedSnapshots.Count == 0)
            return;

        if (!CanReceiveGameState())
        {
            delayedSnapshots.Clear();
            return;
        }

        float now = Time.realtimeSinceStartup;
        for (int i = 0; i < delayedSnapshots.Count; i++)
        {
            QueuedGameStateSnapshot snapshot = delayedSnapshots[i];
            if (snapshot.applyTime > now)
                continue;

            delayedSnapshots.RemoveAt(i);
            i--;
            ApplyGameState(snapshot.packet, snapshot.entries, snapshot.entryCount);
        }
    }

    private void ApplyGameState(
        GameStateSnapshotPacket packet,
        IReadOnlyList<GameStateEntryPacket> entries,
        int entryCount)
    {
        if (!ShouldApplyGameState(packet))
            return;

        ApplyEntries(packet, entries, entryCount);
        UpdateAppliedSequences(packet);

        int sortedEntryCount = CopySortedEntriesToBuffer();

        CurrentState = new ClientGameStateSnapshotState
        {
            gameStateSeq = packet.gameStateSeq,
            serverTick = packet.serverTick,
            serverTime = packet.serverTime,
            remainingSeconds = packet.remainingSeconds,
            isGameStarted = packet.isGameStarted,
            isGameEnded = packet.isGameEnded,
            isFullSync = packet.isFullSync,
            entryCount = (ushort)sortedEntryCount,
            entries = sortedEntryBuffer,
            lastReceivedTime = Time.time
        };

        hasGameState = true;
    }

    private void ApplyEntries(
        GameStateSnapshotPacket packet,
        IReadOnlyList<GameStateEntryPacket> entries,
        int entryCount)
    {
        if (packet.isFullSync)
        {
            cachedEntries.Clear();
        }
        else
        {
            ClearCachedTaggerFlags();
        }

        int count = Mathf.Clamp(entryCount, 0, entries != null ? entries.Count : 0);
        for (int i = 0; i < count; i++)
        {
            cachedEntries[entries[i].clientId] = entries[i];
        }

        sortedEntries.Clear();
        foreach (GameStateEntryPacket entry in cachedEntries.Values)
        {
            sortedEntries.Add(entry);
        }

        sortedEntries.Sort(CompareGameStateEntries);
    }

    private void ClearCachedTaggerFlags()
    {
        cachedEntryKeys.Clear();
        foreach (ulong clientId in cachedEntries.Keys)
        {
            cachedEntryKeys.Add(clientId);
        }

        for (int i = 0; i < cachedEntryKeys.Count; i++)
        {
            ulong clientId = cachedEntryKeys[i];
            GameStateEntryPacket entry = cachedEntries[clientId];
            entry.isTagger = false;
            cachedEntries[clientId] = entry;
        }
    }

    public bool TryGetGameState(out ClientGameStateSnapshotState state)
    {
        state = CurrentState;
        return hasGameState;
    }

    private bool ShouldApplyGameState(GameStateSnapshotPacket packet)
    {
        if (!hasGameState)
            return true;

        if (packet.gameStateSeq == lastAppliedGameStateSeq)
            return false;

        if (!IsNewerSequence(packet.gameStateSeq, lastAppliedGameStateSeq))
            return false;

        if (packet.isFullSync)
        {
            if (!hasAppliedFullSync)
                return true;

            if (packet.gameStateSeq == lastAppliedFullSyncSeq)
                return false;

            return IsNewerSequence(packet.gameStateSeq, lastAppliedFullSyncSeq);
        }

        return true;
    }

    private bool TryReadGameStatePacket(
        ref FastBufferReader reader,
        out GameStateSnapshotPacket packet,
        List<GameStateEntryPacket> entries)
    {
        packet = default;
        entries.Clear();

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out uint gameStateSeq);
        reader.ReadValueSafe(out uint serverTick);
        reader.ReadValueSafe(out float serverTime);
        reader.ReadValueSafe(out ushort remainingSeconds);
        reader.ReadValueSafe(out byte isGameStarted);
        reader.ReadValueSafe(out byte isGameEnded);
        reader.ReadValueSafe(out byte isFullSync);
        reader.ReadValueSafe(out ushort entryCount);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        for (int i = 0; i < entryCount; i++)
        {
            if (!GameStateEntryPacket.TryRead(ref reader, out GameStateEntryPacket entry))
            {
                entries.Clear();
                return false;
            }

            entries.Add(entry);
        }

        packet = new GameStateSnapshotPacket
        {
            protocolVersion = protocolVersion,
            gameStateSeq = gameStateSeq,
            serverTick = serverTick,
            serverTime = serverTime,
            remainingSeconds = remainingSeconds,
            isGameStarted = isGameStarted != 0,
            isGameEnded = isGameEnded != 0,
            isFullSync = isFullSync != 0,
            entryCount = entryCount,
            entries = null
        };

        return true;
    }

    private void UpdateAppliedSequences(GameStateSnapshotPacket packet)
    {
        if (!hasGameState || IsNewerSequence(packet.gameStateSeq, lastAppliedGameStateSeq))
        {
            lastAppliedGameStateSeq = packet.gameStateSeq;
        }

        if (!packet.isFullSync)
        {
            return;
        }

        hasAppliedFullSync = true;
        lastAppliedFullSyncSeq = packet.gameStateSeq;
    }

    private static bool IsNewerSequence(uint incomingSeq, uint currentSeq)
    {
        return unchecked((int)(incomingSeq - currentSeq)) > 0;
    }

    private static int CompareGameStateEntries(
        GameStateEntryPacket first,
        GameStateEntryPacket second)
    {
        int timeComparison = first.taggerTimeMs.CompareTo(second.taggerTimeMs);
        if (timeComparison != 0)
        {
            return timeComparison;
        }

        return first.clientId.CompareTo(second.clientId);
    }

    private int CopySortedEntriesToBuffer()
    {
        int count = Mathf.Min(sortedEntries.Count, sortedEntryBuffer.Length);
        for (int i = 0; i < count; i++)
        {
            sortedEntryBuffer[i] = sortedEntries[i];
        }

        for (int i = count; i < sortedEntryBuffer.Length; i++)
        {
            sortedEntryBuffer[i] = default;
        }

        return count;
    }

    private void ClearSortedEntryBuffer()
    {
        for (int i = 0; i < sortedEntryBuffer.Length; i++)
        {
            sortedEntryBuffer[i] = default;
        }
    }

    private bool CanReceiveGameState()
    {
        if (NetworkManager.Singleton == null)
            return false;

        if (!NetworkManager.Singleton.IsClient)
            return false;

        if (!NetworkManager.Singleton.IsConnectedClient)
            return false;

        if (NetworkManager.Singleton.IsServer)
            return false;

        return true;
    }

    private float GetNetworkDelaySeconds()
    {
        if (networkDelaySimulator == null)
            return 0f;

        return networkDelaySimulator.OneWayDelaySeconds;
    }
}

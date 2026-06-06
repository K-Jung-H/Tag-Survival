using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Client_SnapshotReceiver : MonoBehaviour
{
    private struct QueuedServerSnapshot
    {
        public float applyTime;
        public ServerSnapshotHeaderPacket header;
        public PlayerSnapshotPacket[] players;
        public SkillSnapshotPacket[] skills;
        public ItemSnapshotPacket[] items;
    }

    [SerializeField] private Client_NetworkDelaySimulator networkDelaySimulator;

    private readonly Dictionary<ulong, ClientSnapshotState> snapshots = new();
    private readonly Dictionary<ulong, ClientSkillSnapshotState> skillSnapshots = new();
    private readonly Dictionary<uint, ClientItemSnapshotState> itemSnapshots = new();
    private readonly HashSet<ulong> receivedClientIds = new();
    private readonly HashSet<ulong> receivedSkillOwnerIds = new();
    private readonly HashSet<uint> receivedItemIds = new();
    private readonly List<ulong> removeTargets = new();
    private readonly List<uint> removeItemTargets = new();
    private readonly List<QueuedServerSnapshot> delayedSnapshots = new();
    private readonly Dictionary<ulong, SkillObjectSnapshotPacket[]> reusableSkillObjectBuffers = new();

    private PlayerSnapshotPacket[] reusablePlayerBuffer = Array.Empty<PlayerSnapshotPacket>();
    private SkillSnapshotPacket[] reusableSkillBuffer = Array.Empty<SkillSnapshotPacket>();
    private ItemSnapshotPacket[] reusableItemBuffer = Array.Empty<ItemSnapshotPacket>();
    private bool isRegistered;
    private bool hasAppliedSnapshot;

    public uint LastSnapshotSeq { get; private set; }
    public uint LastServerTick { get; private set; }

    public IReadOnlyDictionary<ulong, ClientSnapshotState> Snapshots => snapshots;
    public IReadOnlyDictionary<ulong, ClientSkillSnapshotState> SkillSnapshots => skillSnapshots;
    public IReadOnlyDictionary<uint, ClientItemSnapshotState> ItemSnapshots => itemSnapshots;

    // - Role: Set up this object when it starts.
    private void Start()
    {
        if (networkDelaySimulator == null)
        {
            networkDelaySimulator = GetComponent<Client_NetworkDelaySimulator>();
        }

        if (networkDelaySimulator == null)
        {
            Debug.LogWarning("[Client_SnapshotReceiver] NetworkDelaySimulator is not assigned. Network delay is disabled.", this);
        }

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[Client_SnapshotReceiver] NetworkManager.Singleton is null.");
            enabled = false;
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    // - Role: Update this object each frame.
    private void Update()
    {
        TryRegisterSnapshotHandler();
        FlushDelayedSnapshots();
    }

    // - Role: Clean up links before this object is destroyed.
    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

        UnregisterSnapshotHandler();
    }

    // - Role: Handle client connected.
    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        TryRegisterSnapshotHandler();
    }

    // - Role: Handle client disconnected.
    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId && clientId != NetworkManager.ServerClientId)
            return;

        snapshots.Clear();
        skillSnapshots.Clear();
        itemSnapshots.Clear();
        receivedClientIds.Clear();
        receivedSkillOwnerIds.Clear();
        receivedItemIds.Clear();
        removeTargets.Clear();
        removeItemTargets.Clear();
        delayedSnapshots.Clear();
        reusableSkillObjectBuffers.Clear();

        LastSnapshotSeq = 0;
        LastServerTick = 0;
        hasAppliedSnapshot = false;

        UnregisterSnapshotHandler();
    }

    // - Role: Try to register snapshot handler.
    private void TryRegisterSnapshotHandler()
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
            GameNetMessages.ServerSnapshot,
            OnServerSnapshotReceived
        );

        isRegistered = true;
    }

    // - Role: Unregister snapshot handler.
    private void UnregisterSnapshotHandler()
    {
        if (!isRegistered)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(GameNetMessages.ServerSnapshot);

        isRegistered = false;
    }

    // - Role: Handle server snapshot received.
    private void OnServerSnapshotReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanReceiveSnapshot())
            return;

        float delaySeconds = GetNetworkDelaySeconds();
        bool canReuseReadBuffers = delaySeconds <= 0f;

        if (!TryReadSnapshot(
            ref reader,
            canReuseReadBuffers,
            out ServerSnapshotHeaderPacket header,
            out PlayerSnapshotPacket[] players,
            out SkillSnapshotPacket[] skills,
            out ItemSnapshotPacket[] items))
        {
            return;
        }

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

        ApplyServerSnapshot(header, players, skills, items);
    }

    // - Role: Try to read snapshot.
    private bool TryReadSnapshot(
        ref FastBufferReader reader,
        bool canReuseReadBuffers,
        out ServerSnapshotHeaderPacket header,
        out PlayerSnapshotPacket[] players,
        out SkillSnapshotPacket[] skills,
        out ItemSnapshotPacket[] items
    )
    {
        players = null;
        skills = null;
        items = null;

        if (!ServerSnapshotHeaderPacket.TryRead(ref reader, out header))
            return false;

        players = GetPlayerReadBuffer(header.playerCount, canReuseReadBuffers);

        for (int i = 0; i < header.playerCount; i++)
        {
            if (!PlayerSnapshotPacket.TryRead(ref reader, out PlayerSnapshotPacket packet))
                return false;

            players[i] = packet;
        }

        skills = GetSkillReadBuffer(header.skillCount, canReuseReadBuffers);
        for (int i = 0; i < header.skillCount; i++)
        {
            SkillSnapshotPacket packet;
            bool didRead = canReuseReadBuffers
                ? SkillSnapshotPacket.TryRead(ref reader, out packet, reusableSkillObjectBuffers)
                : SkillSnapshotPacket.TryRead(ref reader, out packet);

            if (!didRead)
                return false;

            skills[i] = packet;
        }

        items = GetItemReadBuffer(header.itemCount, canReuseReadBuffers);
        for (int i = 0; i < header.itemCount; i++)
        {
            if (!ItemSnapshotPacket.TryRead(ref reader, out ItemSnapshotPacket packet))
                return false;

            items[i] = packet;
        }

        return true;
    }

    // - Role: Get player read buffer.
    private PlayerSnapshotPacket[] GetPlayerReadBuffer(ushort playerCount, bool canReuseReadBuffers)
    {
        if (playerCount <= 0)
        {
            return Array.Empty<PlayerSnapshotPacket>();
        }

        if (!canReuseReadBuffers)
        {
            return new PlayerSnapshotPacket[playerCount];
        }

        if (reusablePlayerBuffer.Length != playerCount)
        {
            reusablePlayerBuffer = new PlayerSnapshotPacket[playerCount];
        }

        return reusablePlayerBuffer;
    }

    // - Role: Get skill read buffer.
    private SkillSnapshotPacket[] GetSkillReadBuffer(ushort skillCount, bool canReuseReadBuffers)
    {
        if (skillCount <= 0)
        {
            return Array.Empty<SkillSnapshotPacket>();
        }

        if (!canReuseReadBuffers)
        {
            return new SkillSnapshotPacket[skillCount];
        }

        if (reusableSkillBuffer.Length != skillCount)
        {
            reusableSkillBuffer = new SkillSnapshotPacket[skillCount];
        }

        return reusableSkillBuffer;
    }

    // - Role: Get item read buffer.
    private ItemSnapshotPacket[] GetItemReadBuffer(ushort itemCount, bool canReuseReadBuffers)
    {
        if (itemCount <= 0)
        {
            return Array.Empty<ItemSnapshotPacket>();
        }

        if (!canReuseReadBuffers)
        {
            return new ItemSnapshotPacket[itemCount];
        }

        if (reusableItemBuffer.Length != itemCount)
        {
            reusableItemBuffer = new ItemSnapshotPacket[itemCount];
        }

        return reusableItemBuffer;
    }

    // - Role: Apply server snapshot.
    private void ApplyServerSnapshot(
        ServerSnapshotHeaderPacket header,
        PlayerSnapshotPacket[] players,
        SkillSnapshotPacket[] skills,
        ItemSnapshotPacket[] items
    )
    {
        if (!IsNewerSnapshot(header.snapshotSeq))
            return;

        LastSnapshotSeq = header.snapshotSeq;
        LastServerTick = header.serverTick;
        hasAppliedSnapshot = true;

        receivedClientIds.Clear();

        for (int i = 0; i < players.Length; i++)
        {
            PlayerSnapshotPacket packet = players[i];

            receivedClientIds.Add(packet.clientId);

            snapshots[packet.clientId] = new ClientSnapshotState
            {
                clientId = packet.clientId,
                snapshotSeq = header.snapshotSeq,
                serverTick = header.serverTick,
                serverTime = header.serverTime,
                position = packet.position,
                velocity = packet.velocity,
                aim = packet.aim,
                buttons = packet.buttons,
                locomotionState = packet.locomotionState,
                characterId = packet.characterId,
                skillId = packet.skillId,
                skillCooldownSeconds = packet.skillCooldownSeconds,
                facingSign = packet.facingSign,
                isTagger = packet.isTagger,
                lastReceivedTime = Time.time
            };
        }

        receivedSkillOwnerIds.Clear();

        for (int i = 0; i < skills.Length; i++)
        {
            SkillSnapshotPacket packet = skills[i];
            if (packet.skillState == SkillObjectState.None)
            {
                continue;
            }

            receivedSkillOwnerIds.Add(packet.ownerClientId);
            skillSnapshots[packet.ownerClientId] = new ClientSkillSnapshotState
            {
                ownerClientId = packet.ownerClientId,
                snapshotSeq = header.snapshotSeq,
                serverTick = header.serverTick,
                serverTime = header.serverTime,
                skillId = packet.skillId,
                skillType = packet.skillType,
                skillState = packet.skillState,
                skillObjects = packet.skillObjects,
                lastReceivedTime = Time.time
            };
        }

        receivedItemIds.Clear();

        for (int i = 0; i < items.Length; i++)
        {
            ItemSnapshotPacket packet = items[i];
            if (packet.itemType == ItemType.None)
            {
                continue;
            }

            receivedItemIds.Add(packet.itemId);
            itemSnapshots[packet.itemId] = new ClientItemSnapshotState
            {
                itemType = packet.itemType,
                position = packet.position
            };
        }

        RemoveMissingPlayers();
        RemoveMissingSkills();
        RemoveMissingItems();
    }

    // - Role: Flush delayed snapshots.
    private void FlushDelayedSnapshots()
    {
        if (delayedSnapshots.Count == 0)
            return;

        if (!CanReceiveSnapshot())
        {
            delayedSnapshots.Clear();
            return;
        }

        float now = Time.realtimeSinceStartup;

        for (int i = 0; i < delayedSnapshots.Count; i++)
        {
            QueuedServerSnapshot snapshot = delayedSnapshots[i];

            if (snapshot.applyTime > now)
                continue;

            delayedSnapshots.RemoveAt(i);
            i--;
            ApplyServerSnapshot(snapshot.header, snapshot.players, snapshot.skills, snapshot.items);
        }
    }

    // - Role: Check if receive snapshot can happen.
    private bool CanReceiveSnapshot()
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

    // - Role: Remove missing players.
    private void RemoveMissingPlayers()
    {
        removeTargets.Clear();

        foreach (ulong clientId in snapshots.Keys)
        {
            if (!receivedClientIds.Contains(clientId))
            {
                removeTargets.Add(clientId);
            }
        }

        foreach (ulong clientId in removeTargets)
        {
            snapshots.Remove(clientId);
        }
    }

    // - Role: Remove missing skills.
    private void RemoveMissingSkills()
    {
        removeTargets.Clear();

        foreach (ulong ownerClientId in skillSnapshots.Keys)
        {
            if (!receivedSkillOwnerIds.Contains(ownerClientId))
            {
                removeTargets.Add(ownerClientId);
            }
        }

        foreach (ulong ownerClientId in removeTargets)
        {
            skillSnapshots.Remove(ownerClientId);
            reusableSkillObjectBuffers.Remove(ownerClientId);
        }
    }

    // - Role: Remove missing items.
    private void RemoveMissingItems()
    {
        removeItemTargets.Clear();

        foreach (uint itemId in itemSnapshots.Keys)
        {
            if (!receivedItemIds.Contains(itemId))
            {
                removeItemTargets.Add(itemId);
            }
        }

        foreach (uint itemId in removeItemTargets)
        {
            itemSnapshots.Remove(itemId);
        }
    }

    // - Role: Try to get snapshot.
    public bool TryGetSnapshot(ulong clientId, out ClientSnapshotState state)
    {
        return snapshots.TryGetValue(clientId, out state);
    }

    // - Role: Check if newer snapshot is true.
    private bool IsNewerSnapshot(uint incomingSeq)
    {
        if (!hasAppliedSnapshot)
            return true;

        if (incomingSeq == LastSnapshotSeq)
            return false;

        return unchecked((int)(incomingSeq - LastSnapshotSeq)) > 0;
    }

    // - Role: Get network delay seconds.
    private float GetNetworkDelaySeconds()
    {
        if (networkDelaySimulator == null)
            return 0f;

        return networkDelaySimulator.OneWayDelaySeconds;
    }
}

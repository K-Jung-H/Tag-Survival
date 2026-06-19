using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public sealed class Client_RoomNetwork : MonoBehaviour
{
    [SerializeField] private Client_RoomSyncManager syncManager;
    [SerializeField] private float joinProfileRetryInterval = 0.5f;
    [SerializeField] private int maxJoinProfileSendAttempts = 10;

    private FastBufferWriter joinProfileWriter;
    private FastBufferWriter selectionWriter;
    private FastBufferWriter readyWriter;
    private FastBufferWriter settingsWriter;
    private bool writersCreated;
    private bool isBuilt;
    private string pendingNickname;
    private float joinProfileRetryTimer;
    private int joinProfileSendAttempts;

    private void Awake()
    {
        joinProfileWriter = new FastBufferWriter(RoomNetProtocol.RoomJoinProfilePacketBufferSize, Allocator.Persistent);
        selectionWriter = new FastBufferWriter(RoomNetProtocol.RoomSelectionRequestPacketBufferSize, Allocator.Persistent);
        readyWriter = new FastBufferWriter(RoomNetProtocol.RoomReadyRequestPacketBufferSize, Allocator.Persistent);
        settingsWriter = new FastBufferWriter(RoomNetProtocol.RoomSettingsRequestPacketBufferSize, Allocator.Persistent);
        writersCreated = true;
    }

    private void OnDestroy()
    {
        UnregisterHandlers();
        DisposeWriters();
    }

    private void Update()
    {
        SendJoinProfileIfNeeded();
    }

    public bool Build(Client_RoomSyncManager roomSyncManager, RoomLaunchRequest request)
    {
        syncManager = roomSyncManager;
        if (syncManager == null)
        {
            Debug.LogError("[Client_RoomNetwork] Client_RoomSyncManager is not assigned.", this);
            return false;
        }

        NetworkSessionManager session = NetworkSessionManager.Resolve();
        if (session == null || !session.IsSessionActive || !IsOnlineClient())
        {
            Debug.LogError("[Client_RoomNetwork] Active guest NetworkSession is required.", this);
            return false;
        }

        RegisterHandlers();
        pendingNickname = request.nickname;
        joinProfileRetryTimer = 0f;
        joinProfileSendAttempts = 0;
        isBuilt = true;
        return true;
    }

    public void SendSelection(byte characterId, byte skillId)
    {
        if (!isBuilt || !CanSendToServer())
        {
            return;
        }

        RoomSelectionRequestPacket packet = new RoomSelectionRequestPacket
        {
            protocolVersion = RoomNetProtocol.ProtocolVersion,
            characterId = characterId,
            skillId = skillId
        };

        selectionWriter.Truncate(0);
        packet.Write(ref selectionWriter);
        SendToServer(RoomNetMessages.ClientRoomSelectionRequest, selectionWriter);
    }

    public void SendReady(bool isReady)
    {
        if (!isBuilt || !CanSendToServer())
        {
            return;
        }

        RoomReadyRequestPacket packet = new RoomReadyRequestPacket
        {
            protocolVersion = RoomNetProtocol.ProtocolVersion,
            isReady = isReady
        };

        readyWriter.Truncate(0);
        packet.Write(ref readyWriter);
        SendToServer(RoomNetMessages.ClientRoomReadyRequest, readyWriter);
    }

    public void SendSettings(ushort stageIndex, ushort gameModeIndex)
    {
        if (!isBuilt || !CanSendToServer())
        {
            return;
        }

        RoomSettingsRequestPacket packet = new RoomSettingsRequestPacket
        {
            protocolVersion = RoomNetProtocol.ProtocolVersion,
            stageIndex = stageIndex,
            gameModeIndex = gameModeIndex
        };

        settingsWriter.Truncate(0);
        packet.Write(ref settingsWriter);
        SendToServer(RoomNetMessages.ClientRoomSettingsRequest, settingsWriter);
    }

    private void RegisterHandlers()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        CustomMessagingManager messaging = NetworkManager.Singleton.CustomMessagingManager;
        messaging.UnregisterNamedMessageHandler(RoomNetMessages.ServerRoomSnapshot);
        messaging.UnregisterNamedMessageHandler(RoomNetMessages.ServerRoomStartGameCommand);
        messaging.RegisterNamedMessageHandler(RoomNetMessages.ServerRoomSnapshot, OnRoomSnapshotReceived);
        messaging.RegisterNamedMessageHandler(RoomNetMessages.ServerRoomStartGameCommand, OnStartGameCommandReceived);
    }

    private void UnregisterHandlers()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(RoomNetMessages.ServerRoomSnapshot);
        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(RoomNetMessages.ServerRoomStartGameCommand);
    }

    private void SendJoinProfile(string nickname)
    {
        if (!CanSendToServer())
        {
            return;
        }

        FixedString64Bytes fixedNickname = new FixedString64Bytes(nickname);
        RoomJoinProfilePacket packet = new RoomJoinProfilePacket
        {
            protocolVersion = RoomNetProtocol.ProtocolVersion,
            clientId = NetworkManager.Singleton.LocalClientId,
            nickname = fixedNickname
        };

        joinProfileWriter.Truncate(0);
        packet.Write(ref joinProfileWriter);
        SendToServer(RoomNetMessages.ClientRoomJoinProfile, joinProfileWriter);
        joinProfileSendAttempts++;
        joinProfileRetryTimer = Mathf.Max(0.1f, joinProfileRetryInterval);
    }

    private void SendJoinProfileIfNeeded()
    {
        if (!isBuilt
            || HasLocalPlayerSnapshot()
            || joinProfileSendAttempts >= Mathf.Max(1, maxJoinProfileSendAttempts))
        {
            return;
        }

        joinProfileRetryTimer -= Time.deltaTime;
        if (joinProfileRetryTimer > 0f)
        {
            return;
        }

        SendJoinProfile(pendingNickname);
    }

    private bool HasLocalPlayerSnapshot()
    {
        RoomSnapshotPacket snapshot = syncManager != null ? syncManager.CurrentSnapshot : default;
        RoomPlayerStatePacket[] players = snapshot.players;
        if (players == null || snapshot.protocolVersion != RoomNetProtocol.ProtocolVersion)
        {
            return false;
        }

        ulong localClientId = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.LocalClientId
            : ulong.MaxValue;
        int count = Mathf.Min(snapshot.playerCount, players.Length);
        for (int i = 0; i < count; i++)
        {
            if (players[i].clientId == localClientId)
            {
                return true;
            }
        }

        return false;
    }

    private void OnRoomSnapshotReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!RoomSnapshotPacket.TryRead(ref reader, out RoomSnapshotPacket packet))
        {
            return;
        }

        syncManager.ApplyOnlineSnapshot(packet);
    }

    private void OnStartGameCommandReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!RoomStartGameCommandPacket.TryRead(ref reader, out RoomStartGameCommandPacket packet))
        {
            return;
        }

        syncManager.ApplyStartCommand(packet);
    }

    private static void SendToServer(string messageName, FastBufferWriter writer)
    {
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            messageName,
            NetworkManager.ServerClientId,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    private static bool CanSendToServer()
    {
        return NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsClient
            && NetworkManager.Singleton.IsConnectedClient
            && NetworkManager.Singleton.CustomMessagingManager != null;
    }

    private static bool IsOnlineClient()
    {
        return NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsClient
            && NetworkManager.Singleton.IsConnectedClient
            && !NetworkManager.Singleton.IsServer;
    }

    private void DisposeWriters()
    {
        if (!writersCreated)
        {
            return;
        }

        joinProfileWriter.Dispose();
        selectionWriter.Dispose();
        readyWriter.Dispose();
        settingsWriter.Dispose();
        writersCreated = false;
    }
}

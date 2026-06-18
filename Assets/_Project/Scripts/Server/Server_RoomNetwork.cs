using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public sealed class Server_RoomNetwork : MonoBehaviour
{
    [SerializeField] private Server_RoomManager roomManager;
    [SerializeField] private float snapshotSendIntervalSeconds = 0.2f;

    private float snapshotTimer;
    private bool isBuilt;
    private FastBufferWriter snapshotWriter;
    private FastBufferWriter startGameWriter;
    private bool snapshotWriterCreated;
    private bool startGameWriterCreated;
    private uint lastSentRoomSeq;
    private bool hasSentStartCommand;

    private void Awake()
    {
        snapshotWriter = new FastBufferWriter(RoomNetProtocol.RoomSnapshotPacketBufferSize, Allocator.Persistent);
        startGameWriter = new FastBufferWriter(RoomNetProtocol.RoomStartGameCommandPacketBufferSize, Allocator.Persistent);
        snapshotWriterCreated = true;
        startGameWriterCreated = true;
    }

    private void OnDestroy()
    {
        UnregisterHandlers();
        if (roomManager != null)
        {
            roomManager.StartRequested -= OnRoomStartRequested;
        }

        if (snapshotWriterCreated)
        {
            snapshotWriter.Dispose();
            snapshotWriterCreated = false;
        }

        if (startGameWriterCreated)
        {
            startGameWriter.Dispose();
            startGameWriterCreated = false;
        }
    }

    private void Update()
    {
        if (!isBuilt)
        {
            return;
        }

        snapshotTimer -= Time.deltaTime;
        if (snapshotTimer > 0f)
        {
            return;
        }

        snapshotTimer = Mathf.Max(0.05f, snapshotSendIntervalSeconds);
        SendSnapshotIfChanged();
    }

    public bool Build(Server_RoomManager manager)
    {
        roomManager = manager;
        if (roomManager == null)
        {
            Debug.LogError("[Server_RoomNetwork] Server_RoomManager is not assigned.", this);
            return false;
        }

        NetworkSessionManager session = NetworkSessionManager.Resolve();
        if (session == null || !session.IsSessionActive || !IsServer())
        {
            Debug.LogError("[Server_RoomNetwork] Active server NetworkSession is required.", this);
            return false;
        }

        RegisterHandlers();
        RegisterNetworkCallbacks();
        roomManager.StartRequested -= OnRoomStartRequested;
        roomManager.StartRequested += OnRoomStartRequested;
        isBuilt = true;
        SendSnapshot(force: true);
        return true;
    }

    private void RegisterHandlers()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        CustomMessagingManager messaging = NetworkManager.Singleton.CustomMessagingManager;
        messaging.UnregisterNamedMessageHandler(RoomNetMessages.ClientRoomJoinProfile);
        messaging.UnregisterNamedMessageHandler(RoomNetMessages.ClientRoomSelectionRequest);
        messaging.UnregisterNamedMessageHandler(RoomNetMessages.ClientRoomReadyRequest);
        messaging.UnregisterNamedMessageHandler(RoomNetMessages.ClientRoomSettingsRequest);

        messaging.RegisterNamedMessageHandler(RoomNetMessages.ClientRoomJoinProfile, OnJoinProfileReceived);
        messaging.RegisterNamedMessageHandler(RoomNetMessages.ClientRoomSelectionRequest, OnSelectionRequestReceived);
        messaging.RegisterNamedMessageHandler(RoomNetMessages.ClientRoomReadyRequest, OnReadyRequestReceived);
        messaging.RegisterNamedMessageHandler(RoomNetMessages.ClientRoomSettingsRequest, OnSettingsRequestReceived);
    }

    private void RegisterNetworkCallbacks()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void UnregisterHandlers()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (NetworkManager.Singleton.CustomMessagingManager != null)
        {
            CustomMessagingManager messaging = NetworkManager.Singleton.CustomMessagingManager;
            messaging.UnregisterNamedMessageHandler(RoomNetMessages.ClientRoomJoinProfile);
            messaging.UnregisterNamedMessageHandler(RoomNetMessages.ClientRoomSelectionRequest);
            messaging.UnregisterNamedMessageHandler(RoomNetMessages.ClientRoomReadyRequest);
            messaging.UnregisterNamedMessageHandler(RoomNetMessages.ClientRoomSettingsRequest);
        }

        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnJoinProfileReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!RoomJoinProfilePacket.TryRead(ref reader, out RoomJoinProfilePacket packet))
        {
            return;
        }

        roomManager.RegisterPlayer(senderClientId, packet.NicknameText);
        SendSnapshot(force: true);
    }

    private void OnSelectionRequestReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!RoomSelectionRequestPacket.TryRead(ref reader, out RoomSelectionRequestPacket packet))
        {
            return;
        }

        roomManager.TrySetSelection(senderClientId, packet.characterId, packet.skillId);
        SendSnapshot(force: true);
    }

    private void OnReadyRequestReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!RoomReadyRequestPacket.TryRead(ref reader, out RoomReadyRequestPacket packet))
        {
            return;
        }

        roomManager.TrySetReady(senderClientId, packet.isReady);
        SendSnapshot(force: true);
    }

    private void OnSettingsRequestReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!RoomSettingsRequestPacket.TryRead(ref reader, out RoomSettingsRequestPacket packet))
        {
            return;
        }

        roomManager.TrySetStageIndex(senderClientId, packet.stageIndex);
        roomManager.TrySetGameModeIndex(senderClientId, packet.gameModeIndex);
        SendSnapshot(force: true);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!isBuilt || roomManager == null || clientId == NetworkManager.ServerClientId)
        {
            return;
        }

        roomManager.RemovePlayer(clientId);
        SendSnapshot(force: true);
    }

    private void SendSnapshotIfChanged()
    {
        RoomSnapshotPacket snapshot = roomManager.CreateSnapshot();
        if (snapshot.roomSeq == lastSentRoomSeq)
        {
            return;
        }

        SendSnapshot(snapshot);
    }

    private void SendSnapshot(bool force)
    {
        RoomSnapshotPacket snapshot = roomManager.CreateSnapshot();
        if (!force && snapshot.roomSeq == lastSentRoomSeq)
        {
            return;
        }

        SendSnapshot(snapshot);
    }

    private void SendSnapshot(RoomSnapshotPacket snapshot)
    {
        if (NetworkManager.Singleton == null
            || NetworkManager.Singleton.CustomMessagingManager == null
            || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        snapshotWriter.Truncate(0);
        snapshot.Write(ref snapshotWriter);

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId == NetworkManager.ServerClientId)
            {
                continue;
            }

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                RoomNetMessages.ServerRoomSnapshot,
                client.ClientId,
                snapshotWriter,
                NetworkDelivery.ReliableSequenced);
        }

        lastSentRoomSeq = snapshot.roomSeq;
    }

    private void OnRoomStartRequested(RoomSnapshotPacket snapshot)
    {
        SendSnapshot(snapshot);
        SendStartCommand(snapshot);
    }

    private void SendStartCommand(RoomSnapshotPacket snapshot)
    {
        if (hasSentStartCommand
            || !startGameWriterCreated
            || NetworkManager.Singleton == null
            || NetworkManager.Singleton.CustomMessagingManager == null
            || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        RoomStartGameCommandPacket packet = new RoomStartGameCommandPacket
        {
            protocolVersion = RoomNetProtocol.ProtocolVersion,
            stageIndex = snapshot.stageIndex,
            gameModeIndex = snapshot.gameModeIndex
        };

        startGameWriter.Truncate(0);
        packet.Write(ref startGameWriter);

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId == NetworkManager.ServerClientId)
            {
                continue;
            }

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                RoomNetMessages.ServerRoomStartGameCommand,
                client.ClientId,
                startGameWriter,
                NetworkDelivery.ReliableSequenced);
        }

        hasSentStartCommand = true;
    }

    private static bool IsServer()
    {
        return NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsServer
            && NetworkManager.Singleton.IsListening;
    }
}

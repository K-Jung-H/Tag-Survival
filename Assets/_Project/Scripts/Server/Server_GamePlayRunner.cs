using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class Server_GamePlayRunner : MonoBehaviour
{
    [SerializeField] private StageDefinition stageDefinition;
    [SerializeField] private CharacterCatalog characterCatalog;
    [SerializeField] private SkillCatalog skillCatalog;
    [SerializeField] private float gameDurationSeconds = 180f;
    [SerializeField] private float gameStateSendInterval = 1f;
    [SerializeField] private float fullGameStateSendInterval = 30f;

    private readonly float serverDeltaTime = 1f / GameNetProtocol.ServerTickRate;
    private readonly float snapshotSendInterval = 1f / GameNetProtocol.SnapshotSendRate;

    private Server_GamePlay gamePlay;

    private FastBufferWriter snapshotWriter;
    private FastBufferWriter gameStateWriter;
    private FastBufferWriter gameEventWriter;
    private FastBufferWriter rosterWriter;
    private bool snapshotWriterCreated;
    private bool gameStateWriterCreated;
    private bool gameEventWriterCreated;
    private bool rosterWriterCreated;
    private readonly System.Collections.Generic.List<PlayerSnapshotPacket> playerSnapshots = new();
    private readonly System.Collections.Generic.List<SkillSnapshotPacket> skillSnapshots = new();
    private readonly System.Collections.Generic.List<GameStateEntryPacket> gameStateEntries = new();
    private readonly System.Collections.Generic.List<GameEventEntryPacket> gameEvents = new();
    private readonly System.Collections.Generic.List<RosterEntryPacket> rosterEntries = new();
    private readonly GameStateEntryPacket[] gameStateEntryBuffer =
        new GameStateEntryPacket[GameNetProtocol.MaxPlayers];
    private readonly GameEventEntryPacket[] gameEventBuffer =
        new GameEventEntryPacket[GameNetProtocol.MaxGameEventsPerBatch];
    private readonly RosterEntryPacket[] rosterEntryBuffer =
        new RosterEntryPacket[GameNetProtocol.MaxPlayers];

    private float tickTimer;
    private float snapshotTimer;
    private float gameStateTimer;
    private float fullGameStateTimer;
    private bool areClientMessageHandlersRegistered;
    private uint snapshotSeq;
    private uint gameStateSeq;
    private uint rosterSeq;
    private uint lastSentGameStateVersion;

    public Server_GamePlay GamePlay => gamePlay;

    // Role: 서버 게임플레이 시뮬레이션과 스냅샷 writer를 생성한다.
    private void Awake()
    {
        gamePlay = new Server_GamePlay(stageDefinition, characterCatalog, skillCatalog);
        gamePlay.SetGameDurationSeconds(gameDurationSeconds);

        snapshotWriter = new FastBufferWriter(
            GameNetProtocol.SnapshotPacketBufferSize,
            Allocator.Persistent
        );

        gameStateWriter = new FastBufferWriter(
            GameNetProtocol.GameStatePacketBufferSize,
            Allocator.Persistent
        );

        gameEventWriter = new FastBufferWriter(
            GameNetProtocol.GameEventPacketBufferSize,
            Allocator.Persistent
        );

        rosterWriter = new FastBufferWriter(
            GameNetProtocol.RosterPacketBufferSize,
            Allocator.Persistent
        );

        snapshotWriterCreated = true;
        gameStateWriterCreated = true;
        gameEventWriterCreated = true;
        rosterWriterCreated = true;
    }

    // Role: NetworkManager 연결 이벤트를 등록한다.
    private void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[Server_GamePlayRunner] NetworkManager.Singleton is null.");
            enabled = false;
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    // Role: 등록된 연결 이벤트, 입력 수신 핸들러, 스냅샷 writer를 해제한다.
    private void OnDestroy()
    {
        UnregisterClientMessageHandlers();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (snapshotWriterCreated)
        {
            snapshotWriter.Dispose();
            snapshotWriterCreated = false;
        }

        if (gameStateWriterCreated)
        {
            gameStateWriter.Dispose();
            gameStateWriterCreated = false;
        }

        if (gameEventWriterCreated)
        {
            gameEventWriter.Dispose();
            gameEventWriterCreated = false;
        }

        if (rosterWriterCreated)
        {
            rosterWriter.Dispose();
            rosterWriterCreated = false;
        }
    }

    // Role: 서버 상태에서 입력 수신 등록, 시뮬레이션, 스냅샷 송신을 처리한다.
    private void Update()
    {
        TryRegisterClientMessageHandlers();

        if (!CanRunServerLoop())
            return;

        RunServerTickLoop();
    }

    // Role: 클라이언트 접속 시 서버 시뮬레이션에 플레이어 상태를 등록한다.
    // Parameters:
    // - clientId: 접속한 클라이언트 ID
    private void OnClientConnected(ulong clientId)
    {
        if (!CanUseServerState())
            return;

        Debug.Log($"[Server_GamePlayRunner] Client connected: {clientId}. Waiting for join profile.");
    }

    // Role: 클라이언트 연결 해제 시 서버 시뮬레이션에서 플레이어 상태를 제거한다.
    // Parameters:
    // - clientId: 연결 해제된 클라이언트 ID
    private void OnClientDisconnected(ulong clientId)
    {
        if (!CanUseServerState())
            return;

        gamePlay.RemovePlayer(clientId);
        SendSnapshotToAllClients();
        SendGameStateToAllClients(isFullSync: true);
        SendRosterToAllClients();
        SendGameEventsToAllClients();

        Debug.Log($"[Server_GamePlayRunner] Client disconnected: {clientId}");
    }

    // Role: CustomMessagingManager가 준비된 뒤 입력 수신 핸들러를 등록한다.
    private void TryRegisterClientMessageHandlers()
    {
        if (areClientMessageHandlersRegistered)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsServer)
            return;

        if (!NetworkManager.Singleton.IsListening)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
            GameNetMessages.ClientJoinProfile,
            OnClientJoinProfileReceived
        );

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
            GameNetMessages.ClientInput,
            OnClientInputReceived
        );

        areClientMessageHandlersRegistered = true;
    }

    // Role: 클라이언트 입력 수신 핸들러를 해제한다.
    private void UnregisterClientMessageHandlers()
    {
        if (!areClientMessageHandlersRegistered)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(
            GameNetMessages.ClientJoinProfile
        );

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(
            GameNetMessages.ClientInput
        );

        areClientMessageHandlersRegistered = false;
    }

    private void OnClientJoinProfileReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseServerState())
            return;

        if (!ClientJoinProfilePacket.TryRead(ref reader, out ClientJoinProfilePacket packet))
            return;

        bool added = gamePlay.AddPlayer(
            senderClientId,
            packet.NicknameText,
            packet.characterId,
            packet.skillId);

        if (!added)
        {
            SendRosterToClient(senderClientId);
            return;
        }

        SendSnapshotToAllClients();
        SendGameStateToAllClients(isFullSync: true);
        SendRosterToAllClients();
        SendGameEventsToAllClients();

        Debug.Log(
            $"[Server_GamePlayRunner] Join profile accepted: " +
            $"clientId={senderClientId}, nickname={packet.NicknameText}, " +
            $"characterId={packet.characterId}, skillId={packet.skillId}");
    }

    // Role: 클라이언트 입력 패킷을 읽고 서버 게임플레이 상태에 반영한다.
    // Parameters:
    // - senderClientId: 입력 패킷을 보낸 클라이언트 ID
    // - reader: 입력 패킷 reader
    private void OnClientInputReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseServerState())
            return;

        if (!ClientInputPacket.TryRead(ref reader, out ClientInputPacket packet))
            return;

        gamePlay.SetInput(
            senderClientId,
            packet.inputSeq,
            packet.move,
            packet.aim,
            packet.buttons
        );
    }

    // Role: 누적 시간을 기준으로 서버 tick과 스냅샷 송신 타이밍을 처리한다.
    private void RunServerTickLoop()
    {
        tickTimer += Time.deltaTime;

        while (tickTimer >= serverDeltaTime)
        {
            tickTimer -= serverDeltaTime;
            snapshotTimer += serverDeltaTime;
            gameStateTimer += serverDeltaTime;
            fullGameStateTimer += serverDeltaTime;

            gamePlay.Simulate(serverDeltaTime);
            SendGameEventsToAllClients();

            if (snapshotTimer >= snapshotSendInterval)
            {
                snapshotTimer -= snapshotSendInterval;
                SendSnapshotToAllClients();
            }

            if (ShouldSendFullGameState())
            {
                gameStateTimer = 0f;
                fullGameStateTimer = 0f;
                SendGameStateToAllClients(isFullSync: true);
            }
            else if (ShouldSendPartialGameState())
            {
                gameStateTimer = 0f;
                SendGameStateToAllClients(isFullSync: false);
            }
        }
    }

    // Role: 현재 서버 월드 상태를 모든 접속 클라이언트에게 전송한다.
    private void SendSnapshotToAllClients()
    {
        if (!CanUseServerState())
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        if (NetworkManager.Singleton.ConnectedClientsList.Count == 0)
            return;

        if (!snapshotWriterCreated)
            return;

        snapshotWriter.Truncate(0);

        gamePlay.CopyPlayerSnapshotsTo(playerSnapshots);
        gamePlay.CopySkillSnapshotsTo(skillSnapshots);

        ServerSnapshotHeaderPacket header = new ServerSnapshotHeaderPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            snapshotSeq = snapshotSeq,
            serverTick = gamePlay.Tick,
            serverTime = (float)NetworkManager.Singleton.ServerTime.Time,
            playerCount = (ushort)playerSnapshots.Count,
            skillCount = (ushort)skillSnapshots.Count
        };

        snapshotSeq++;
        header.Write(ref snapshotWriter);

        for (int i = 0; i < playerSnapshots.Count; i++)
        {
            playerSnapshots[i].Write(ref snapshotWriter);
        }

        for (int i = 0; i < skillSnapshots.Count; i++)
        {
            skillSnapshots[i].Write(ref snapshotWriter);
        }

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                GameNetMessages.ServerSnapshot,
                client.ClientId,
                snapshotWriter,
                NetworkDelivery.UnreliableSequenced
            );
        }
    }

    private bool ShouldSendFullGameState()
    {
        float interval = Mathf.Max(1f, fullGameStateSendInterval);
        return fullGameStateTimer >= interval
            || gamePlay.GameStateVersion != lastSentGameStateVersion;
    }

    private bool ShouldSendPartialGameState()
    {
        float interval = Mathf.Max(0.1f, gameStateSendInterval);
        return gameStateTimer >= interval;
    }

    private void SendGameStateToAllClients(bool isFullSync)
    {
        if (!CanUseServerState())
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        if (NetworkManager.Singleton.ConnectedClientsList.Count == 0)
            return;

        if (!gameStateWriterCreated)
            return;

        gameStateWriter.Truncate(0);
        gamePlay.CopyGameStateEntriesTo(gameStateEntries, taggersOnly: !isFullSync);

        int entryCount = Mathf.Min(gameStateEntries.Count, GameNetProtocol.MaxPlayers);
        for (int i = 0; i < entryCount; i++)
        {
            gameStateEntryBuffer[i] = gameStateEntries[i];
        }

        GameStateSnapshotPacket packet = new GameStateSnapshotPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            gameStateSeq = gameStateSeq,
            serverTick = gamePlay.Tick,
            serverTime = (float)NetworkManager.Singleton.ServerTime.Time,
            remainingSeconds = (ushort)Mathf.Clamp(
                Mathf.CeilToInt(gamePlay.RemainingSeconds),
                0,
                ushort.MaxValue),
            isGameStarted = gamePlay.IsGameStarted,
            isGameEnded = gamePlay.IsGameEnded,
            isFullSync = isFullSync,
            entryCount = (ushort)entryCount,
            entries = gameStateEntryBuffer
        };

        packet.Write(ref gameStateWriter);
        gameStateSeq++;
        lastSentGameStateVersion = gamePlay.GameStateVersion;
        NetworkDelivery delivery = isFullSync
            ? NetworkDelivery.ReliableSequenced
            : NetworkDelivery.UnreliableSequenced;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                GameNetMessages.ServerGameState,
                client.ClientId,
                gameStateWriter,
                delivery
            );
        }
    }

    private void SendGameEventsToAllClients()
    {
        if (!CanUseServerState())
            return;

        if (gamePlay.PendingGameEventCount <= 0)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        if (NetworkManager.Singleton.ConnectedClientsList.Count == 0)
            return;

        if (!gameEventWriterCreated)
            return;

        gameEventWriter.Truncate(0);
        gamePlay.CopyPendingGameEventsTo(gameEvents);
        int eventCount = Mathf.Min(gameEvents.Count, GameNetProtocol.MaxGameEventsPerBatch);
        if (eventCount <= 0)
            return;

        for (int i = 0; i < eventCount; i++)
        {
            gameEventBuffer[i] = gameEvents[i];
        }

        GameEventBatchPacket packet = new GameEventBatchPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            eventCount = (ushort)eventCount,
            events = gameEventBuffer
        };

        packet.Write(ref gameEventWriter);

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                GameNetMessages.ServerGameEvent,
                client.ClientId,
                gameEventWriter,
                NetworkDelivery.ReliableSequenced
            );
        }

        gamePlay.ClearPendingGameEvents(eventCount);
    }

    private void SendRosterToAllClients()
    {
        if (!CanUseServerState())
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        if (NetworkManager.Singleton.ConnectedClientsList.Count == 0)
            return;

        if (!rosterWriterCreated)
            return;

        rosterWriter.Truncate(0);
        gamePlay.CopyRosterEntriesTo(rosterEntries);

        int entryCount = Mathf.Min(rosterEntries.Count, GameNetProtocol.MaxPlayers);
        for (int i = 0; i < entryCount; i++)
        {
            rosterEntryBuffer[i] = rosterEntries[i];
        }

        ServerRosterSnapshotPacket packet = new ServerRosterSnapshotPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            rosterSeq = rosterSeq,
            entryCount = (ushort)entryCount,
            entries = rosterEntryBuffer
        };

        packet.Write(ref rosterWriter);
        rosterSeq++;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                GameNetMessages.ServerRoster,
                client.ClientId,
                rosterWriter,
                NetworkDelivery.ReliableSequenced
            );
        }
    }

    private void SendRosterToClient(ulong clientId)
    {
        if (!CanUseServerState())
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        if (!rosterWriterCreated)
            return;

        rosterWriter.Truncate(0);
        gamePlay.CopyRosterEntriesTo(rosterEntries);

        int entryCount = Mathf.Min(rosterEntries.Count, GameNetProtocol.MaxPlayers);
        for (int i = 0; i < entryCount; i++)
        {
            rosterEntryBuffer[i] = rosterEntries[i];
        }

        ServerRosterSnapshotPacket packet = new ServerRosterSnapshotPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            rosterSeq = rosterSeq,
            entryCount = (ushort)entryCount,
            entries = rosterEntryBuffer
        };

        packet.Write(ref rosterWriter);
        rosterSeq++;

        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            GameNetMessages.ServerRoster,
            clientId,
            rosterWriter,
            NetworkDelivery.ReliableSequenced
        );
    }

    // Role: 서버 루프를 실행할 수 있는 네트워크 상태인지 판단한다.
    private bool CanRunServerLoop()
    {
        if (NetworkManager.Singleton == null)
            return false;

        if (!NetworkManager.Singleton.IsServer)
            return false;

        if (!NetworkManager.Singleton.IsListening)
            return false;

        return true;
    }

    // Role: 서버 상태 읽기/쓰기 및 패킷 송신이 가능한 네트워크 상태인지 판단한다.
    private bool CanUseServerState()
    {
        if (NetworkManager.Singleton == null)
            return false;

        if (!NetworkManager.Singleton.IsServer)
            return false;

        if (!NetworkManager.Singleton.IsListening)
            return false;

        return true;
    }
}

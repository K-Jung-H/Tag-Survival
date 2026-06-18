using Unity.Collections;
using System;
using Unity.Netcode;
using UnityEngine;

public enum ServerGamePlayRunMode
{
    NetworkServer,
    LocalSimulation
}

[DefaultExecutionOrder(0)]
public class Server_GamePlayRunner : MonoBehaviour
{
    [SerializeField] private ServerGamePlayRunMode runMode = ServerGamePlayRunMode.NetworkServer;
    [SerializeField] private StageDefinition stageDefinition;
    [SerializeField] private CharacterCatalog characterCatalog;
    [SerializeField] private SkillCatalog skillCatalog;
    [SerializeField] private ItemEffectCatalog itemEffectCatalog;
    [SerializeField] private GameModeType gameModeType = GameModeType.TimeAttack;
    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private float itemSelectionTimeoutSeconds = 10f;
    [SerializeField] private float gameStateSendInterval = 1f;
    [SerializeField] private float fullGameStateSendInterval = 30f;

    private readonly float serverDeltaTime = 1f / GameNetProtocol.ServerTickRate;
    private readonly float snapshotSendInterval = 1f / GameNetProtocol.SnapshotSendRate;

    private Server_GamePlay gamePlay;

    private FastBufferWriter snapshotWriter;
    private FastBufferWriter gameStateWriter;
    private FastBufferWriter gameEndWriter;
    private FastBufferWriter resultCommandWriter;
    private FastBufferWriter gameEventWriter;
    private FastBufferWriter itemSelectionWriter;
    private FastBufferWriter rosterWriter;
    private bool snapshotWriterCreated;
    private bool gameStateWriterCreated;
    private bool gameEndWriterCreated;
    private bool resultCommandWriterCreated;
    private bool gameEventWriterCreated;
    private bool itemSelectionWriterCreated;
    private bool rosterWriterCreated;
    private readonly System.Collections.Generic.List<PlayerSnapshotPacket> playerSnapshots = new();
    private readonly System.Collections.Generic.List<SkillSnapshotPacket> skillSnapshots = new();
    private readonly System.Collections.Generic.List<ItemSnapshotPacket> itemSnapshots = new();
    private readonly System.Collections.Generic.List<CoinSnapshotPacket> coinSnapshots = new();
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
    private bool hasLocalDirectClient;
    private ulong localDirectClientId;
    private uint snapshotSeq;
    private uint gameStateSeq;
    private uint rosterSeq;
    private uint lastSentGameStateVersion;
    private bool hasSentGameEnd;
    private bool hasSentResultCommand;
    private ulong resultAuthorityClientId = ulong.MaxValue;

    public Server_GamePlay GamePlay => gamePlay;
    public ServerGamePlayRunMode RunMode => runMode;
    public GameModeType GameModeType => gameModeType;
    public event Action<ItemSelectionOfferPacket> LocalItemSelectionOfferReady;
    public event Action<ItemSelectionResultPacket> LocalItemSelectionResultReady;
    public event Action<GameEventEntryPacket> LocalGameEventReady;
    public event Action<ServerGameEndPacket> LocalGameEndReady;
    public event Action<ServerResultCommandPacket> LocalResultCommandReady;

    public void ConfigureRunMode(ServerGamePlayRunMode mode)
    {
        runMode = mode;
    }

    public void ConfigureStageDefinition(StageDefinition definition)
    {
        if (definition == null || stageDefinition == definition)
        {
            return;
        }

        stageDefinition = definition;
        RecreateGamePlay();
    }

    public void ConfigureGameMode(GameModeType modeType)
    {
        ConfigureGameMode(modeType, gameModeConfig);
    }

    public void ConfigureGameMode(GameModeType modeType, GameModeConfig modeConfig)
    {
        gameModeType = modeType;
        gameModeConfig = IsMatchingGameModeConfig(modeConfig) ? modeConfig : null;
        RecreateGamePlay();
    }

    public void ConfigureLocalDirectClient(ulong clientId)
    {
        localDirectClientId = clientId;
        hasLocalDirectClient = true;
    }

    public void ClearLocalDirectClient()
    {
        localDirectClientId = 0;
        hasLocalDirectClient = false;
    }

    public void ConfigureResultAuthority(ulong clientId)
    {
        resultAuthorityClientId = clientId;
    }

    // - Role: Set up needed links before start.
    private void Awake()
    {
        RecreateGamePlay();

        snapshotWriter = new FastBufferWriter(GameNetProtocol.SnapshotPacketBufferSize, Allocator.Persistent);

        gameStateWriter = new FastBufferWriter(GameNetProtocol.GameStatePacketBufferSize, Allocator.Persistent);

        gameEndWriter = new FastBufferWriter(GameNetProtocol.GameEndPacketBufferSize, Allocator.Persistent);

        resultCommandWriter = new FastBufferWriter(GameNetProtocol.ResultCommandPacketBufferSize, Allocator.Persistent);

        gameEventWriter = new FastBufferWriter(GameNetProtocol.GameEventPacketBufferSize, Allocator.Persistent);

        itemSelectionWriter = new FastBufferWriter(GameNetProtocol.ItemSelectionPacketBufferSize, Allocator.Persistent);

        rosterWriter = new FastBufferWriter(GameNetProtocol.RosterPacketBufferSize, Allocator.Persistent);

        snapshotWriterCreated = true;
        gameStateWriterCreated = true;
        gameEndWriterCreated = true;
        resultCommandWriterCreated = true;
        gameEventWriterCreated = true;
        itemSelectionWriterCreated = true;
        rosterWriterCreated = true;
    }

    // - Role: Set up this object when it starts.
    private void Start()
    {
        if (runMode == ServerGamePlayRunMode.LocalSimulation)
        {
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[Server_GamePlayRunner] NetworkManager.Singleton is null.");
            enabled = false;
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    // - Role: Clean up links before this object is destroyed.
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

        if (gameEndWriterCreated)
        {
            gameEndWriter.Dispose();
            gameEndWriterCreated = false;
        }

        if (resultCommandWriterCreated)
        {
            resultCommandWriter.Dispose();
            resultCommandWriterCreated = false;
        }

        if (gameEventWriterCreated)
        {
            gameEventWriter.Dispose();
            gameEventWriterCreated = false;
        }

        if (itemSelectionWriterCreated)
        {
            itemSelectionWriter.Dispose();
            itemSelectionWriterCreated = false;
        }

        if (rosterWriterCreated)
        {
            rosterWriter.Dispose();
            rosterWriterCreated = false;
        }
    }

    // - Role: Update this object each frame.
    private void Update()
    {
        if (runMode == ServerGamePlayRunMode.LocalSimulation)
        {
            RunLocalSimulationTickLoop();
            return;
        }

        TryRegisterClientMessageHandlers();

        if (!CanRunServerLoop())
            return;

        RunServerTickLoop();
    }

    // - Role: Handle client connected.
    private void OnClientConnected(ulong clientId)
    {
        if (!CanUseServerState())
            return;

        Debug.Log($"[Server_GamePlayRunner] Client connected: {clientId}. Waiting for join profile.");
    }

    // - Role: Handle client disconnected.
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

    // - Role: Try to register client message handlers.
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

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(GameNetMessages.ClientJoinProfile, OnClientJoinProfileReceived);
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(GameNetMessages.ClientStageSyncRequest, OnClientStageSyncRequestReceived);
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(GameNetMessages.ClientInput, OnClientInputReceived);
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(GameNetMessages.ClientResultChoice, OnClientResultChoiceReceived);
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
            GameNetMessages.ClientItemSelectionChoice,
            OnClientItemSelectionChoiceReceived);

        areClientMessageHandlersRegistered = true;
    }

    // - Role: Unregister client message handlers.
    private void UnregisterClientMessageHandlers()
    {
        if (!areClientMessageHandlersRegistered)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(GameNetMessages.ClientJoinProfile);

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(GameNetMessages.ClientStageSyncRequest);

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(GameNetMessages.ClientInput);

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(GameNetMessages.ClientResultChoice);

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(GameNetMessages.ClientItemSelectionChoice);

        areClientMessageHandlersRegistered = false;
    }

    // - Role: Handle client join profile received.
    private void OnClientJoinProfileReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseServerState())
            return;

        if (!ClientJoinProfilePacket.TryRead(ref reader, out ClientJoinProfilePacket packet))
            return;

        bool added = gamePlay.AddPlayer(senderClientId, packet.NicknameText, packet.characterId, packet.skillId);

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

    private void OnClientStageSyncRequestReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseServerState())
            return;

        if (!ClientStageSyncRequestPacket.TryRead(ref reader, out _))
            return;

        SendSnapshotToClient(senderClientId);
        SendGameStateToClient(senderClientId, isFullSync: true);
        SendRosterToClient(senderClientId);
        SendGameEventsToAllClients();
    }

    private void OnClientResultChoiceReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseServerState())
            return;

        if (!ClientResultChoicePacket.TryRead(ref reader, out ClientResultChoicePacket packet))
            return;

        HandleResultChoice(senderClientId, packet.choice);
    }

    public void HandleResultChoice(ulong clientId, GameResultChoice choice)
    {
        if (choice == GameResultChoice.None || hasSentResultCommand)
        {
            return;
        }

        if (!CanControlResult(clientId))
        {
            return;
        }

        switch (choice)
        {
            case GameResultChoice.Rematch:
                SendResultCommand(GameResultCommand.RematchToRoom);
                if (!hasLocalDirectClient)
                {
                    GameFlowManager.Instance?.ReturnToRoomFromStage();
                }
                break;
            case GameResultChoice.Exit:
                SendResultCommand(GameResultCommand.RoomClosed);
                if (!hasLocalDirectClient)
                {
                    GameFlowManager.Instance?.ExitStageToOnline();
                }
                break;
        }
    }

    // - Role: Handle client input received.
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

    // - Role: Handle item selection choice received.
    private void OnClientItemSelectionChoiceReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseServerState())
            return;

        if (!ItemSelectionChoicePacket.TryRead(ref reader, out ItemSelectionChoicePacket packet))
            return;

        gamePlay.ChooseItemCandidate(senderClientId, packet.requestId, packet.selectedId);
        SendItemSelectionMessages();
    }

    // - Role: Process server ticks and send updates.
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
            SendItemSelectionMessages();
            SendGameEventsToAllClients();
            TrySendGameEnd();

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

    // - Role: Process local server ticks without network transport.
    private void RunLocalSimulationTickLoop()
    {
        if (gamePlay == null)
        {
            return;
        }

        tickTimer += Time.deltaTime;

        while (tickTimer >= serverDeltaTime)
        {
            tickTimer -= serverDeltaTime;
            gamePlay.Simulate(serverDeltaTime);
            TrySendGameEnd();
        }
    }

    // - Role: Send item selection messages.
    private void SendItemSelectionMessages()
    {
        if (!CanUseServerState())
            return;

        if (!itemSelectionWriterCreated)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        while (gamePlay.TryDequeueItemSelectionResult(out ServerItemSystem.ItemSelectionResultMessage result))
        {
            SendItemSelectionResult(result);
        }

        while (gamePlay.TryDequeueItemSelectionOffer(out ServerItemSystem.ItemSelectionOfferMessage offer))
        {
            SendItemSelectionOffer(offer);
        }
    }

    // - Role: Send item selection offer.
    private void SendItemSelectionOffer(ServerItemSystem.ItemSelectionOfferMessage message)
    {
        if (IsLocalDirectClient(message.clientId))
        {
            LocalItemSelectionOfferReady?.Invoke(message.packet);
            return;
        }

        if (!IsClientConnected(message.clientId))
            return;

        itemSelectionWriter.Truncate(0);
        message.packet.Write(ref itemSelectionWriter);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            GameNetMessages.ServerItemSelectionOffer,
            message.clientId,
            itemSelectionWriter,
            NetworkDelivery.ReliableSequenced);
    }

    // - Role: Send item selection result.
    private void SendItemSelectionResult(ServerItemSystem.ItemSelectionResultMessage message)
    {
        if (IsLocalDirectClient(message.clientId))
        {
            LocalItemSelectionResultReady?.Invoke(message.packet);
            return;
        }

        if (!IsClientConnected(message.clientId))
            return;

        itemSelectionWriter.Truncate(0);
        message.packet.Write(ref itemSelectionWriter);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            GameNetMessages.ServerItemSelectionResult,
            message.clientId,
            itemSelectionWriter,
            NetworkDelivery.ReliableSequenced);
    }

    // - Role: Send snapshot to all clients.
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
        gamePlay.CopyItemSnapshotsTo(itemSnapshots);
        gamePlay.CopyCoinSnapshotsTo(coinSnapshots);

        ServerSnapshotHeaderPacket header = new ServerSnapshotHeaderPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            snapshotSeq = snapshotSeq,
            serverTick = gamePlay.Tick,
            serverTime = (float)NetworkManager.Singleton.ServerTime.Time,
            playerCount = (ushort)playerSnapshots.Count,
            skillCount = (ushort)skillSnapshots.Count,
            itemCount = (ushort)itemSnapshots.Count,
            coinCount = (ushort)coinSnapshots.Count
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

        for (int i = 0; i < itemSnapshots.Count; i++)
        {
            itemSnapshots[i].Write(ref snapshotWriter);
        }

        for (int i = 0; i < coinSnapshots.Count; i++)
        {
            coinSnapshots[i].Write(ref snapshotWriter);
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

    private void SendSnapshotToClient(ulong clientId)
    {
        if (!CanUseServerState())
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        if (!IsClientConnected(clientId))
            return;

        if (!snapshotWriterCreated)
            return;

        snapshotWriter.Truncate(0);

        gamePlay.CopyPlayerSnapshotsTo(playerSnapshots);
        gamePlay.CopySkillSnapshotsTo(skillSnapshots);
        gamePlay.CopyItemSnapshotsTo(itemSnapshots);
        gamePlay.CopyCoinSnapshotsTo(coinSnapshots);

        ServerSnapshotHeaderPacket header = new ServerSnapshotHeaderPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            snapshotSeq = snapshotSeq,
            serverTick = gamePlay.Tick,
            serverTime = (float)NetworkManager.Singleton.ServerTime.Time,
            playerCount = (ushort)playerSnapshots.Count,
            skillCount = (ushort)skillSnapshots.Count,
            itemCount = (ushort)itemSnapshots.Count,
            coinCount = (ushort)coinSnapshots.Count
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

        for (int i = 0; i < itemSnapshots.Count; i++)
        {
            itemSnapshots[i].Write(ref snapshotWriter);
        }

        for (int i = 0; i < coinSnapshots.Count; i++)
        {
            coinSnapshots[i].Write(ref snapshotWriter);
        }

        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            GameNetMessages.ServerSnapshot,
            clientId,
            snapshotWriter,
            NetworkDelivery.ReliableSequenced);
    }

    // - Role: Check if send full game state should happen.
    private bool ShouldSendFullGameState()
    {
        float interval = Mathf.Max(1f, fullGameStateSendInterval);
        return fullGameStateTimer >= interval
            || gamePlay.GameStateVersion != lastSentGameStateVersion;
    }

    // - Role: Check if send partial game state should happen.
    private bool ShouldSendPartialGameState()
    {
        float interval = Mathf.Max(0.1f, gameStateSendInterval);
        return gameStateTimer >= interval;
    }

    // - Role: Send game state to all clients.
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
            remainingSeconds = (ushort)Mathf.Clamp(Mathf.CeilToInt(gamePlay.RemainingSeconds), 0, ushort.MaxValue),
            gameModeType = gamePlay.GameModeType,
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

    private void SendGameStateToClient(ulong clientId, bool isFullSync)
    {
        if (!CanUseServerState())
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        if (!IsClientConnected(clientId))
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
            remainingSeconds = (ushort)Mathf.Clamp(Mathf.CeilToInt(gamePlay.RemainingSeconds), 0, ushort.MaxValue),
            gameModeType = gamePlay.GameModeType,
            isGameStarted = gamePlay.IsGameStarted,
            isGameEnded = gamePlay.IsGameEnded,
            isFullSync = isFullSync,
            entryCount = (ushort)entryCount,
            entries = gameStateEntryBuffer
        };

        packet.Write(ref gameStateWriter);
        gameStateSeq++;
        lastSentGameStateVersion = gamePlay.GameStateVersion;

        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            GameNetMessages.ServerGameState,
            clientId,
            gameStateWriter,
            NetworkDelivery.ReliableSequenced);
    }

    private void TrySendGameEnd()
    {
        if (hasSentGameEnd || gamePlay == null || !gamePlay.IsGameEnded)
        {
            return;
        }

        SendGameStateToAllClients(isFullSync: true);
        SendGameEndToAllClients();
        hasSentGameEnd = true;
    }

    private void SendGameEndToAllClients()
    {
        if (!gameEndWriterCreated)
        {
            return;
        }

        gamePlay.CopyGameStateEntriesTo(gameStateEntries, taggersOnly: false);
        int entryCount = Mathf.Min(gameStateEntries.Count, GameNetProtocol.MaxPlayers);
        for (int i = 0; i < entryCount; i++)
        {
            gameStateEntryBuffer[i] = gameStateEntries[i];
        }

        ServerGameEndPacket packet = new ServerGameEndPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            gameStateSeq = gameStateSeq++,
            serverTick = gamePlay.Tick,
            serverTime = NetworkManager.Singleton != null
                ? (float)NetworkManager.Singleton.ServerTime.Time
                : gamePlay.Tick / GameNetProtocol.ServerTickRate,
            gameModeType = gamePlay.GameModeType,
            entryCount = (ushort)entryCount,
            entries = gameStateEntryBuffer
        };

        LocalGameEndReady?.Invoke(packet);

        if (!CanUseServerState() || NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        gameEndWriter.Truncate(0);
        packet.Write(ref gameEndWriter);
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                GameNetMessages.ServerGameEnd,
                client.ClientId,
                gameEndWriter,
                NetworkDelivery.ReliableSequenced);
        }
    }

    private void SendResultCommand(GameResultCommand command)
    {
        if (command == GameResultCommand.None || hasSentResultCommand)
        {
            return;
        }

        hasSentResultCommand = true;
        ServerResultCommandPacket packet = new ServerResultCommandPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            command = command
        };

        LocalResultCommandReady?.Invoke(packet);

        if (!CanUseServerState()
            || NetworkManager.Singleton.CustomMessagingManager == null
            || !resultCommandWriterCreated)
        {
            return;
        }

        resultCommandWriter.Truncate(0);
        packet.Write(ref resultCommandWriter);
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                GameNetMessages.ServerResultCommand,
                client.ClientId,
                resultCommandWriter,
                NetworkDelivery.ReliableSequenced);
        }
    }

    // - Role: Send game events to all clients.
    private void SendGameEventsToAllClients()
    {
        if (!CanUseServerState())
            return;

        if (gamePlay.PendingGameEventCount <= 0)
            return;

        if (!gameEventWriterCreated)
            return;

        gameEventWriter.Truncate(0);
        gamePlay.CopyPendingGameEventsTo(gameEvents);
        int eventCount = Mathf.Min(gameEvents.Count, GameNetProtocol.MaxGameEventsPerBatch);
        if (eventCount <= 0)
            return;

        bool canSendNetworkEvents = NetworkManager.Singleton.CustomMessagingManager != null
            && NetworkManager.Singleton.ConnectedClientsList.Count > 0;
        if (!hasLocalDirectClient && !canSendNetworkEvents)
            return;

        for (int i = 0; i < eventCount; i++)
        {
            gameEventBuffer[i] = gameEvents[i];
            LocalGameEventReady?.Invoke(gameEvents[i]);
        }

        if (canSendNetworkEvents)
        {
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
        }

        gamePlay.ClearPendingGameEvents(eventCount);
    }

    // - Role: Send roster to all clients.
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

    // - Role: Send roster to client.
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

    // - Role: Check if run server loop can happen.
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

    // - Role: Check if use server state can happen.
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

    // - Role: Check if client connected.
    private static bool IsClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return false;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId == clientId)
                return true;
        }

        return false;
    }

    private bool IsLocalDirectClient(ulong clientId)
    {
        return hasLocalDirectClient && clientId == localDirectClientId;
    }

    private bool CanControlResult(ulong clientId)
    {
        return IsLocalDirectClient(clientId) || clientId == resultAuthorityClientId;
    }

    private int GetInitialMaxActiveItemCount()
    {
        if (IsMatchingGameModeConfig(gameModeConfig))
        {
            return gameModeConfig.MaxActiveItems;
        }

        return GameNetProtocol.MaxItems;
    }

    private bool IsMatchingGameModeConfig(GameModeConfig modeConfig)
    {
        return modeConfig != null && modeConfig.ModeType == gameModeType;
    }

    private void RecreateGamePlay()
    {
        gamePlay = new Server_GamePlay(
            stageDefinition,
            characterCatalog,
            skillCatalog,
            itemEffectCatalog,
            GetInitialMaxActiveItemCount(),
            itemSelectionTimeoutSeconds,
            gameModeType,
            gameModeConfig);
        hasSentGameEnd = false;
        hasSentResultCommand = false;
    }
}

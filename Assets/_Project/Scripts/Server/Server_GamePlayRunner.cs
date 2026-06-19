using Unity.Collections;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum ServerGamePlayRunMode
{
    NetworkServer,
    LocalSimulation
}

public enum ServerStageFlowState
{
    WaitingForStageReady,
    WaitingForIntroReady,
    Countdown,
    Playing
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
    [SerializeField] private float stageReadyTimeoutSeconds = 30f;
    [SerializeField] private float introReadyTimeoutSeconds = 30f;
    [SerializeField] private float countdownSeconds = 3f;

    private readonly float serverDeltaTime = 1f / GameNetProtocol.ServerTickRate;
    private readonly float snapshotSendInterval = 1f / GameNetProtocol.SnapshotSendRate;

    private Server_GamePlay gamePlay;

    private FastBufferWriter snapshotWriter;
    private FastBufferWriter gameStateWriter;
    private FastBufferWriter gameEndWriter;
    private FastBufferWriter resultCommandWriter;
    private FastBufferWriter stageFlowCommandWriter;
    private FastBufferWriter gameEventWriter;
    private FastBufferWriter itemSelectionWriter;
    private FastBufferWriter rosterWriter;
    private bool snapshotWriterCreated;
    private bool gameStateWriterCreated;
    private bool gameEndWriterCreated;
    private bool resultCommandWriterCreated;
    private bool stageFlowCommandWriterCreated;
    private bool gameEventWriterCreated;
    private bool itemSelectionWriterCreated;
    private bool rosterWriterCreated;
    private readonly List<PlayerSnapshotPacket> playerSnapshots = new();
    private readonly List<SkillSnapshotPacket> skillSnapshots = new();
    private readonly List<ItemSnapshotPacket> itemSnapshots = new();
    private readonly List<CoinSnapshotPacket> coinSnapshots = new();
    private readonly List<GameStateEntryPacket> gameStateEntries = new();
    private readonly List<GameEventEntryPacket> gameEvents = new();
    private readonly List<RosterEntryPacket> rosterEntries = new();
    private readonly HashSet<ulong> stageReadyClientIds = new();
    private readonly HashSet<ulong> stageIntroReadyClientIds = new();
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
    private ServerStageFlowState stageFlowState = ServerStageFlowState.WaitingForStageReady;
    private float stageReadyGateStartedAt;
    private float introReadyGateStartedAt;
    private float countdownStartedAt;
    private ulong resultAuthorityClientId = ulong.MaxValue;

    public Server_GamePlay GamePlay => gamePlay;
    public ServerGamePlayRunMode RunMode => runMode;
    public GameModeType GameModeType => gameModeType;
    public event Action<ItemSelectionOfferPacket> LocalItemSelectionOfferReady;
    public event Action<ItemSelectionResultPacket> LocalItemSelectionResultReady;
    public event Action<GameEventEntryPacket> LocalGameEventReady;
    public event Action<ServerGameEndPacket> LocalGameEndReady;
    public event Action<ServerResultCommandPacket> LocalResultCommandReady;
    public event Action<ServerStageFlowCommandPacket> LocalStageFlowCommandReady;

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
        TryRecreateGamePlay();
    }

    public void ConfigureGameMode(GameModeType modeType)
    {
        ConfigureGameMode(modeType, gameModeConfig);
    }

    public void ConfigureGameMode(GameModeType modeType, GameModeConfig modeConfig)
    {
        gameModeType = modeType;
        gameModeConfig = IsMatchingGameModeConfig(modeConfig) ? modeConfig : null;
        TryRecreateGamePlay();
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
        TryRecreateGamePlay();

        snapshotWriter = new FastBufferWriter(GameNetProtocol.SnapshotPacketBufferSize, Allocator.Persistent);

        gameStateWriter = new FastBufferWriter(GameNetProtocol.GameStatePacketBufferSize, Allocator.Persistent);

        gameEndWriter = new FastBufferWriter(GameNetProtocol.GameEndPacketBufferSize, Allocator.Persistent);

        resultCommandWriter = new FastBufferWriter(GameNetProtocol.ResultCommandPacketBufferSize, Allocator.Persistent);

        stageFlowCommandWriter = new FastBufferWriter(GameNetProtocol.ServerStageFlowCommandPacketBufferSize, Allocator.Persistent);

        gameEventWriter = new FastBufferWriter(GameNetProtocol.GameEventPacketBufferSize, Allocator.Persistent);

        itemSelectionWriter = new FastBufferWriter(GameNetProtocol.ItemSelectionPacketBufferSize, Allocator.Persistent);

        rosterWriter = new FastBufferWriter(GameNetProtocol.RosterPacketBufferSize, Allocator.Persistent);

        snapshotWriterCreated = true;
        gameStateWriterCreated = true;
        gameEndWriterCreated = true;
        resultCommandWriterCreated = true;
        stageFlowCommandWriterCreated = true;
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

        if (stageFlowCommandWriterCreated)
        {
            stageFlowCommandWriter.Dispose();
            stageFlowCommandWriterCreated = false;
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
            TickStageStartGate();
            RunLocalSimulationTickLoop();
            return;
        }

        TryRegisterClientMessageHandlers();

        if (!CanRunServerLoop())
            return;

        TickStageStartGate();
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

        stageReadyClientIds.Remove(clientId);
        stageIntroReadyClientIds.Remove(clientId);
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
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(GameNetMessages.ClientStageReady, OnClientStageReadyReceived);
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(GameNetMessages.ClientStageIntroReady, OnClientStageIntroReadyReceived);
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

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(GameNetMessages.ClientStageReady);

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(GameNetMessages.ClientStageIntroReady);

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
        SendStageFlowCommandToClientIfNeeded(senderClientId);
    }

    private void OnClientStageReadyReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseServerState())
            return;

        if (!ClientStageReadyPacket.TryRead(ref reader, out _))
            return;

        MarkStageReady(senderClientId);
    }

    public void MarkStageReady(ulong clientId)
    {
        if (gamePlay == null || !gamePlay.Players.ContainsKey(clientId))
        {
            return;
        }

        stageReadyClientIds.Add(clientId);
        SendStageFlowCommandToClientIfNeeded(clientId);
    }

    private void OnClientStageIntroReadyReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseServerState())
            return;

        if (!ClientStageIntroReadyPacket.TryRead(ref reader, out _))
            return;

        MarkStageIntroReady(senderClientId);
    }

    public void MarkStageIntroReady(ulong clientId)
    {
        if (gamePlay == null || !gamePlay.Players.ContainsKey(clientId))
        {
            return;
        }

        stageIntroReadyClientIds.Add(clientId);
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

    private void TickStageStartGate()
    {
        if (gamePlay == null || gamePlay.IsGameStarted || gamePlay.IsGameEnded || gamePlay.Players.Count <= 0)
        {
            return;
        }

        switch (stageFlowState)
        {
            case ServerStageFlowState.WaitingForStageReady:
                TickStageReadyGate();
                break;
            case ServerStageFlowState.WaitingForIntroReady:
                TickIntroReadyGate();
                break;
            case ServerStageFlowState.Countdown:
                TickCountdown();
                break;
        }
    }

    private void TickStageReadyGate()
    {
        float now = Time.realtimeSinceStartup;
        if (stageReadyGateStartedAt <= 0f)
        {
            stageReadyGateStartedAt = now;
        }

        float gateElapsed = now - stageReadyGateStartedAt;
        bool isTimeout = gateElapsed >= Mathf.Max(0f, stageReadyTimeoutSeconds);
        if (!isTimeout && !AreAllStagePlayersReady())
        {
            return;
        }

        BeginStageIntroFlow();
    }

    private bool AreAllStagePlayersReady()
    {
        foreach (ulong clientId in gamePlay.Players.Keys)
        {
            if (!stageReadyClientIds.Contains(clientId))
            {
                return false;
            }
        }

        return gamePlay.Players.Count > 0;
    }

    private void BeginStageIntroFlow()
    {
        stageFlowState = ServerStageFlowState.WaitingForIntroReady;
        introReadyGateStartedAt = Time.realtimeSinceStartup;
        SendStageFlowCommandToAllClients(StageFlowCommandType.IntroStart);
    }

    private void TickIntroReadyGate()
    {
        float gateElapsed = Time.realtimeSinceStartup - introReadyGateStartedAt;
        bool isTimeout = gateElapsed >= Mathf.Max(0f, introReadyTimeoutSeconds);
        if (!isTimeout && !AreAllStagePlayersIntroReady())
        {
            return;
        }

        BeginCountdownFlow();
    }

    private bool AreAllStagePlayersIntroReady()
    {
        foreach (ulong clientId in gamePlay.Players.Keys)
        {
            if (!stageIntroReadyClientIds.Contains(clientId))
            {
                return false;
            }
        }

        return gamePlay.Players.Count > 0;
    }

    private void BeginCountdownFlow()
    {
        if (gamePlay == null || !gamePlay.BeginCountdown())
        {
            return;
        }

        stageFlowState = ServerStageFlowState.Countdown;
        countdownStartedAt = Time.realtimeSinceStartup;
        SendStageFlowCommandToAllClients(StageFlowCommandType.CountdownStart);
    }

    private void TickCountdown()
    {
        float elapsed = Time.realtimeSinceStartup - countdownStartedAt;
        if (elapsed < Mathf.Max(0f, countdownSeconds))
        {
            return;
        }

        StartGameFromStageGate();
    }

    private void StartGameFromStageGate()
    {
        if (gamePlay == null || gamePlay.IsGameStarted || gamePlay.Players.Count <= 0)
        {
            return;
        }

        ulong starterClientId = ResolveStageStarterClientId();
        if (!gamePlay.StartGame(starterClientId))
        {
            return;
        }

        stageFlowState = ServerStageFlowState.Playing;
        SendStageFlowCommandToAllClients(StageFlowCommandType.GameStart);
        SendGameEventsToAllClients();
        SendSnapshotToAllClients();
        SendGameStateToAllClients(isFullSync: true);
    }

    private ulong ResolveStageStarterClientId()
    {
        foreach (ulong clientId in stageReadyClientIds)
        {
            if (gamePlay.Players.ContainsKey(clientId))
            {
                return clientId;
            }
        }

        foreach (ulong clientId in gamePlay.Players.Keys)
        {
            return clientId;
        }

        return 0;
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

    private void SendStageFlowCommandToAllClients(StageFlowCommandType commandType)
    {
        ServerStageFlowCommandPacket packet = CreateStageFlowCommandPacket(commandType);

        if (hasLocalDirectClient)
        {
            LocalStageFlowCommandReady?.Invoke(packet);
        }

        if (!CanUseServerState()
            || NetworkManager.Singleton.CustomMessagingManager == null
            || !stageFlowCommandWriterCreated)
        {
            return;
        }

        stageFlowCommandWriter.Truncate(0);
        packet.Write(ref stageFlowCommandWriter);
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                GameNetMessages.ServerStageFlowCommand,
                client.ClientId,
                stageFlowCommandWriter,
                NetworkDelivery.ReliableSequenced);
        }
    }

    private void SendStageFlowCommandToClientIfNeeded(ulong clientId)
    {
        if (gamePlay == null)
        {
            return;
        }

        StageFlowCommandType commandType = ResolveCurrentStageFlowCommandType();
        if (commandType == StageFlowCommandType.None)
        {
            return;
        }

        ServerStageFlowCommandPacket packet = CreateStageFlowCommandPacket(commandType);
        if (IsLocalDirectClient(clientId))
        {
            LocalStageFlowCommandReady?.Invoke(packet);
            return;
        }

        if (!CanUseServerState()
            || NetworkManager.Singleton.CustomMessagingManager == null
            || !stageFlowCommandWriterCreated
            || !IsClientConnected(clientId))
        {
            return;
        }

        stageFlowCommandWriter.Truncate(0);
        packet.Write(ref stageFlowCommandWriter);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            GameNetMessages.ServerStageFlowCommand,
            clientId,
            stageFlowCommandWriter,
            NetworkDelivery.ReliableSequenced);
    }

    private StageFlowCommandType ResolveCurrentStageFlowCommandType()
    {
        if (gamePlay.IsGameStarted)
        {
            return StageFlowCommandType.GameStart;
        }

        return stageFlowState switch
        {
            ServerStageFlowState.WaitingForIntroReady => StageFlowCommandType.IntroStart,
            ServerStageFlowState.Countdown => StageFlowCommandType.CountdownStart,
            ServerStageFlowState.Playing => StageFlowCommandType.GameStart,
            _ => StageFlowCommandType.None
        };
    }

    private ServerStageFlowCommandPacket CreateStageFlowCommandPacket(StageFlowCommandType commandType)
    {
        float serverTime = NetworkManager.Singleton != null
            ? (float)NetworkManager.Singleton.ServerTime.Time
            : gamePlay.Tick / GameNetProtocol.ServerTickRate;

        return new ServerStageFlowCommandPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            commandType = commandType,
            serverTick = gamePlay.Tick,
            serverTime = serverTime,
            elapsedSeconds = ResolveStageFlowElapsedSeconds(commandType),
            countdownSeconds = Mathf.Max(0f, countdownSeconds),
            isGameStarted = gamePlay.IsGameStarted
        };
    }

    private float ResolveStageFlowElapsedSeconds(StageFlowCommandType commandType)
    {
        float now = Time.realtimeSinceStartup;
        return commandType switch
        {
            StageFlowCommandType.IntroStart => Mathf.Max(0f, now - introReadyGateStartedAt),
            StageFlowCommandType.CountdownStart => Mathf.Max(0f, now - countdownStartedAt),
            _ => 0f
        };
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
        if (gamePlay == null)
            return false;

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
        if (gamePlay == null)
            return false;

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

    private bool IsMatchingGameModeConfig(GameModeConfig modeConfig)
    {
        return modeConfig != null && modeConfig.ModeType == gameModeType;
    }

    private bool CanCreateGamePlay()
    {
        return stageDefinition != null
            && characterCatalog != null
            && skillCatalog != null
            && itemEffectCatalog != null
            && gameModeConfig != null
            && gameModeConfig.ModeType == gameModeType;
    }

    private bool TryRecreateGamePlay()
    {
        if (!CanCreateGamePlay())
        {
            gamePlay = null;
            ResetStageFlowState();
            return false;
        }

        RecreateGamePlay();
        return true;
    }

    private void RecreateGamePlay()
    {
        gamePlay = new Server_GamePlay(
            stageDefinition,
            characterCatalog,
            skillCatalog,
            itemEffectCatalog,
            itemSelectionTimeoutSeconds,
            gameModeType,
            gameModeConfig);
        ResetStageFlowState();
    }

    private void ResetStageFlowState()
    {
        hasSentGameEnd = false;
        hasSentResultCommand = false;
        stageReadyClientIds.Clear();
        stageIntroReadyClientIds.Clear();
        stageFlowState = ServerStageFlowState.WaitingForStageReady;
        stageReadyGateStartedAt = 0f;
        introReadyGateStartedAt = 0f;
        countdownStartedAt = 0f;
    }
}

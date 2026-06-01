using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class Server_GamePlayRunner : MonoBehaviour
{
    [SerializeField] private StageDefinition stageDefinition;
    [SerializeField] private CharacterCatalog characterCatalog;

    private readonly float serverDeltaTime = 1f / GameNetProtocol.ServerTickRate;
    private readonly float snapshotSendInterval = 1f / GameNetProtocol.SnapshotSendRate;

    private Server_GamePlay gamePlay;

    private FastBufferWriter snapshotWriter;
    private bool snapshotWriterCreated;

    private float tickTimer;
    private float snapshotTimer;
    private bool isInputHandlerRegistered;
    private uint snapshotSeq;

    public Server_GamePlay GamePlay => gamePlay;

    // Role: 서버 게임플레이 시뮬레이션과 스냅샷 writer를 생성한다.
    private void Awake()
    {
        gamePlay = new Server_GamePlay(stageDefinition, characterCatalog);

        snapshotWriter = new FastBufferWriter(
            GameNetProtocol.SnapshotPacketBufferSize,
            Allocator.Persistent
        );

        snapshotWriterCreated = true;
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
        UnregisterInputHandler();

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
    }

    // Role: 서버 상태에서 입력 수신 등록, 시뮬레이션, 스냅샷 송신을 처리한다.
    private void Update()
    {
        TryRegisterInputHandler();

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

        gamePlay.AddPlayer(clientId);
        SendSnapshotToAllClients();

        Debug.Log($"[Server_GamePlayRunner] Client connected: {clientId}");
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

        Debug.Log($"[Server_GamePlayRunner] Client disconnected: {clientId}");
    }

    // Role: CustomMessagingManager가 준비된 뒤 입력 수신 핸들러를 등록한다.
    private void TryRegisterInputHandler()
    {
        if (isInputHandlerRegistered)
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
            GameNetMessages.ClientInput,
            OnClientInputReceived
        );

        isInputHandlerRegistered = true;
    }

    // Role: 클라이언트 입력 수신 핸들러를 해제한다.
    private void UnregisterInputHandler()
    {
        if (!isInputHandlerRegistered)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(
            GameNetMessages.ClientInput
        );

        isInputHandlerRegistered = false;
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

            gamePlay.Simulate(serverDeltaTime);

            if (snapshotTimer >= snapshotSendInterval)
            {
                snapshotTimer -= snapshotSendInterval;
                SendSnapshotToAllClients();
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

        ServerSnapshotHeaderPacket header = new ServerSnapshotHeaderPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            snapshotSeq = snapshotSeq,
            serverTick = gamePlay.Tick,
            serverTime = (float)NetworkManager.Singleton.ServerTime.Time,
            playerCount = (ushort)gamePlay.Players.Count
        };

        snapshotSeq++;
        header.Write(ref snapshotWriter);

        foreach (var pair in gamePlay.Players)
        {
            Server_GamePlay.PlayerState player = pair.Value;
            CharacterRuntimeState characterState = player.characterStateMachine.State;

            PlayerSnapshotPacket packet = new PlayerSnapshotPacket
            {
                clientId = player.clientId,
                position = characterState.position,
                velocity = characterState.velocity,
                aim = characterState.aim,
                buttons = player.buttons,
                locomotionState = characterState.locomotionState,
                characterId = characterState.characterId,
                facingSign = characterState.facingSign
            };

            packet.Write(ref snapshotWriter);
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

using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class Client_InputSender : MonoBehaviour
{
    private struct DelayedInputPacket
    {
        public ClientInputPacket packet;
        public float sendTime;
    }

    [SerializeField] private InputProvider_Client_Base inputProvider;
    [SerializeField] private Client_NetworkDelaySimulator networkDelaySimulator;
    [SerializeField] private float maxInputAccumulatedTime = 0.15f;
    [SerializeField] private bool sendImmediatelyOnInputChanged = true;
    [SerializeField] private float inputChangeThreshold = 0.001f;
    [SerializeField] private float minImmediateSendInterval = 0.0083f;

    private readonly float inputSendInterval = 1f / GameNetProtocol.InputSendRate;
    private readonly List<DelayedInputPacket> delayedInputPackets = new();

    private FastBufferWriter inputWriter;
    private bool inputWriterCreated;

    private float inputAccumulator;
    private float immediateSendTimer;
    private ushort inputSeq;
    private uint clientTick;
    private bool hasLastSentInputState;
    private ClientInputState lastSentInputState;

    // Role: 입력 기록, 입력 Provider, 입력 패킷 writer를 준비한다.
    private void Awake()
    {
        if (inputProvider == null)
        {
            inputProvider = GetComponent<InputProvider_Client_Base>();
        }

        if (inputProvider == null)
        {
            Debug.LogError("[Client_InputSender] InputProvider is not assigned.");
            enabled = false;
            return;
        }

        if (networkDelaySimulator == null)
        {
            networkDelaySimulator = GetComponent<Client_NetworkDelaySimulator>();
        }

        inputWriter = new FastBufferWriter(
            GameNetProtocol.InputPacketBufferSize,
            Allocator.Persistent
        );

        inputWriterCreated = true;
    }

    // Role: 입력 패킷 writer 리소스를 해제한다.
    private void OnDestroy()
    {
        if (inputWriterCreated)
        {
            inputWriter.Dispose();
            inputWriterCreated = false;
        }

        delayedInputPackets.Clear();
    }

    // Role: 클라이언트 연결 상태에서 서버 tick 기준 입력 패킷을 생성하고 서버에 전송한다.
    private void Update()
    {
        if (!CanSendInput())
        {
            delayedInputPackets.Clear();
            return;
        }

        FlushDelayedInputPackets();

        float deltaTime = Time.deltaTime;
        inputAccumulator += deltaTime;
        immediateSendTimer += deltaTime;

        if (inputAccumulator > maxInputAccumulatedTime)
        {
            inputAccumulator = maxInputAccumulatedTime;
        }

        ClientInputState inputState = inputProvider.GetInputState();

        if (ShouldSendImmediately(inputState))
        {
            SendInputPacket(inputState);
            inputAccumulator = 0f;
            immediateSendTimer = 0f;
            return;
        }

        while (inputAccumulator >= inputSendInterval)
        {
            inputAccumulator -= inputSendInterval;
            SendInputPacket(inputState);
        }
    }

    // Role: 입력 상태가 바뀐 경우 정기 전송을 기다리지 않고 즉시 전송할지 판단한다.
    // Parameters:
    // - inputState: 현재 프레임의 입력 상태
    private bool ShouldSendImmediately(ClientInputState inputState)
    {
        if (!sendImmediatelyOnInputChanged)
            return false;

        if (!hasLastSentInputState)
            return true;

        if (immediateSendTimer < minImmediateSendInterval)
            return false;

        if ((inputState.move - lastSentInputState.move).sqrMagnitude > inputChangeThreshold)
            return true;

        if ((inputState.aim - lastSentInputState.aim).sqrMagnitude > inputChangeThreshold)
            return true;

        return inputState.buttons != lastSentInputState.buttons;
    }

    // Role: 현재 인스턴스가 서버로 입력 패킷을 보낼 수 있는 상태인지 판단한다.
    private bool CanSendInput()
    {
        if (NetworkManager.Singleton == null)
            return false;

        if (!NetworkManager.Singleton.IsClient)
            return false;

        if (!NetworkManager.Singleton.IsConnectedClient)
            return false;

        if (NetworkManager.Singleton.IsServer)
            return false;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return false;

        if (!inputWriterCreated)
            return false;

        if (inputProvider == null)
            return false;

        return true;
    }

    // Role: Provider에서 입력 상태를 받아 패킷으로 구성하고 서버에 전송한다.
    // Parameters:
    // - inputState: 전송할 입력 상태
    private void SendInputPacket(ClientInputState inputState)
    {
        ClientInputPacket packet = new ClientInputPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            inputSeq = inputSeq,
            clientTick = clientTick,
            move = inputState.move,
            aim = inputState.aim,
            buttons = inputState.buttons
        };

        lastSentInputState = inputState;
        hasLastSentInputState = true;

        inputSeq = unchecked((ushort)(inputSeq + 1));
        clientTick++;

        QueueOrSendInputPacket(packet);
    }

    // Role: 지연 테스트 설정에 따라 입력 패킷을 즉시 전송하거나 지연 큐에 넣는다.
    // Parameters:
    // - packet: 전송할 클라이언트 입력 패킷
    private void QueueOrSendInputPacket(ClientInputPacket packet)
    {
        float delaySeconds = GetNetworkDelaySeconds();

        if (delaySeconds <= 0f)
        {
            SendInputPacketNow(packet);
            return;
        }

        delayedInputPackets.Add(new DelayedInputPacket
        {
            packet = packet,
            sendTime = Time.realtimeSinceStartup + delaySeconds
        });
    }

    // Role: 지연 시간이 지난 입력 패킷을 실제 네트워크로 전송한다.
    private void FlushDelayedInputPackets()
    {
        if (delayedInputPackets.Count == 0)
            return;

        float now = Time.realtimeSinceStartup;

        for (int i = 0; i < delayedInputPackets.Count; i++)
        {
            DelayedInputPacket delayedPacket = delayedInputPackets[i];

            if (delayedPacket.sendTime > now)
                continue;

            delayedInputPackets.RemoveAt(i);
            i--;
            SendInputPacketNow(delayedPacket.packet);
        }
    }

    // Role: 입력 패킷을 버퍼에 기록하고 서버로 즉시 전송한다.
    // Parameters:
    // - packet: 실제 전송할 클라이언트 입력 패킷
    private void SendInputPacketNow(ClientInputPacket packet)
    {
        inputWriter.Truncate(0);
        packet.Write(ref inputWriter);

        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            GameNetMessages.ClientInput,
            NetworkManager.ServerClientId,
            inputWriter,
            NetworkDelivery.UnreliableSequenced
        );
    }

    // Role: 현재 클라이언트에 적용된 네트워크 지연 시간을 초 단위로 반환한다.
    private float GetNetworkDelaySeconds()
    {
        if (networkDelaySimulator == null)
        {
            networkDelaySimulator = GetComponent<Client_NetworkDelaySimulator>();
        }

        if (networkDelaySimulator == null)
        {
            networkDelaySimulator = FindAnyObjectByType<Client_NetworkDelaySimulator>();
        }

        if (networkDelaySimulator == null)
            return 0f;

        return networkDelaySimulator.OneWayDelaySeconds;
    }
}

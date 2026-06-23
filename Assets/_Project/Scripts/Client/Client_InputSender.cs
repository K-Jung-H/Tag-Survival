using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class Client_InputSender : MonoBehaviour
{
    [SerializeField] private InputProvider_Client_Base[] inputProviderList;
    [SerializeField] private float maxInputAccumulatedTime = 0.15f;
    [SerializeField] private bool sendImmediatelyOnInputChanged = true;
    [SerializeField] private float inputChangeThreshold = 0.001f;
    [SerializeField] private float minImmediateSendInterval = 0.0083f;

    private readonly float inputSendInterval = 1f / GameNetProtocol.InputSendRate;

    private FastBufferWriter inputWriter;
    private bool inputWriterCreated;

    private float inputAccumulator;
    private float immediateSendTimer;
    private ushort inputSeq;
    private uint clientTick;
    private bool hasLastSentInputState;
    private ClientInputState lastSentInputState;

    // - Role: Set up needed links before start.
    private void Awake()
    {
        if (!HasInputProvider())
        {
            Debug.LogError("[Client_InputSender] InputProviders are not assigned.");
            enabled = false;
            return;
        }

        inputWriter = new FastBufferWriter(GameNetProtocol.InputPacketBufferSize, Allocator.Persistent);

        inputWriterCreated = true;
    }

    // - Role: Clean up links before this object is destroyed.
    private void OnDestroy()
    {
        if (inputWriterCreated)
        {
            inputWriter.Dispose();
            inputWriterCreated = false;
        }
    }

    // - Role: Update this object each frame.
    private void Update()
    {
        if (!CanSendInput())
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        inputAccumulator += deltaTime;
        immediateSendTimer += deltaTime;

        if (inputAccumulator > maxInputAccumulatedTime)
        {
            inputAccumulator = maxInputAccumulatedTime;
        }

        ClientInputState inputState = ClientInputProviderMixer.Mix(inputProviderList);

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

    // - Role: Check if send immediately should happen.
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

    // - Role: Check if send input can happen.
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

        if (!HasInputProvider())
            return false;

        return true;
    }

    private bool HasInputProvider()
    {
        if (inputProviderList == null)
        {
            return false;
        }

        for (int i = 0; i < inputProviderList.Length; i++)
        {
            if (inputProviderList[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    // - Role: Send input packet.
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

        SendInputPacketNow(packet);
    }

    // - Role: Send input packet now.
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

}

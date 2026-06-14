using UnityEngine;
using UnityEngine.Serialization;

[DefaultExecutionOrder(-50)]
public sealed class LocalClient_InputBridge : MonoBehaviour
{
    [SerializeField] private Server_GamePlayRunner serverRunner;
    [FormerlySerializedAs("inputProviders")]
    [SerializeField] private InputProvider_Client_Base[] inputProviderList;
    [SerializeField] private ulong localClientId;
    [SerializeField] private float maxInputAccumulatedTime = 0.15f;
    [SerializeField] private bool sendImmediatelyOnInputChanged = true;
    [SerializeField] private float inputChangeThreshold = 0.001f;
    [SerializeField] private float minImmediateSendInterval = 0.0083f;

    private readonly float inputSendInterval = 1f / GameNetProtocol.InputSendRate;

    private float inputAccumulator;
    private float immediateSendTimer;
    private ushort inputSeq;
    private bool hasLastSentInputState;
    private ClientInputState lastSentInputState;

    public ulong LocalClientId => localClientId;
    public Server_GamePlayRunner ServerRunner => serverRunner;

    private void Awake()
    {
        if (!HasInputProvider())
        {
            Debug.LogError("[LocalClient_InputBridge] InputProviders are not assigned.", this);
            enabled = false;
        }
    }

    private void OnDisable()
    {
        inputAccumulator = 0f;
        immediateSendTimer = 0f;
        hasLastSentInputState = false;
        lastSentInputState = ClientInputState.Empty();
    }

    public void Configure(Server_GamePlayRunner runner, ulong clientId)
    {
        serverRunner = runner;
        localClientId = clientId;
        ResetInputState();
    }

    public void ConfigureInputProviders(InputProvider_Client_Base[] providers)
    {
        inputProviderList = providers;
        ResetInputState();
    }

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
            SendInput(inputState);
            inputAccumulator = 0f;
            immediateSendTimer = 0f;
            return;
        }

        while (inputAccumulator >= inputSendInterval)
        {
            inputAccumulator -= inputSendInterval;
            SendInput(inputState);
        }
    }

    private bool CanSendInput()
    {
        return serverRunner != null
            && serverRunner.GamePlay != null
            && HasInputProvider();
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

    private bool ShouldSendImmediately(ClientInputState inputState)
    {
        if (!sendImmediatelyOnInputChanged)
        {
            return false;
        }

        if (!hasLastSentInputState)
        {
            return true;
        }

        if (immediateSendTimer < minImmediateSendInterval)
        {
            return false;
        }

        if ((inputState.move - lastSentInputState.move).sqrMagnitude > inputChangeThreshold)
        {
            return true;
        }

        if ((inputState.aim - lastSentInputState.aim).sqrMagnitude > inputChangeThreshold)
        {
            return true;
        }

        return inputState.buttons != lastSentInputState.buttons;
    }

    private void SendInput(ClientInputState inputState)
    {
        serverRunner.GamePlay.SetInput(
            localClientId,
            inputSeq,
            inputState.move,
            inputState.aim,
            inputState.buttons);

        lastSentInputState = inputState;
        hasLastSentInputState = true;
        inputSeq = unchecked((ushort)(inputSeq + 1));
    }

    private void ResetInputState()
    {
        inputAccumulator = 0f;
        immediateSendTimer = 0f;
        inputSeq = 0;
        hasLastSentInputState = false;
        lastSentInputState = ClientInputState.Empty();
    }
}

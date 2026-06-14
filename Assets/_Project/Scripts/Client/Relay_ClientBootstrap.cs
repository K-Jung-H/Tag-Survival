using System;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;

public class Relay_ClientBootstrap : MonoBehaviour
{
    private const string RELAY_PROTOCOL = "dtls";
    private const int JoinProfileMaxSendAttempts = 8;
    private const float JoinProfileRetryInterval = 0.5f;
    private const int MaxNicknameLength = 16;
    private const string DefaultNickname = "NoName";
    private const byte DefaultCharacterId = 0;
    private const byte DefaultSkillId = 1;

    [Header("Debug GUI")]
    [SerializeField] private bool showConnectionDebugGui = true;

    [Header("Connection UI References")]
    [SerializeField] private ClientCanvasPanelController canvasPanelController;
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private TMP_InputField characterIdInput;
    [SerializeField] private TMP_InputField skillIdInput;
    [SerializeField] private Button connectButton;

    [Header("Network Delay UI References")]
    [SerializeField] private Toggle delayModeToggle;
    [SerializeField] private Slider delaySlider;
    [SerializeField] private TMP_Text delayValueText;
    [SerializeField] private Client_NetworkDelaySimulator networkDelaySimulator;


    private bool isProcessing;
    private bool isConnected;
    private string currentJoinCode = "";
    private string connectionStatus = "Idle";
    private bool inactiveClientMode;
    private bool inactiveClientModeShowHud;
    private bool hasWarnedMissingConnectionUi;
    private bool hasWarnedMissingDelayUi;
    private bool hasWarnedMissingDelaySimulator;
    private FastBufferWriter joinProfileWriter;
    private bool joinProfileWriterCreated;
    private ClientJoinProfilePacket pendingJoinProfile;
    private bool hasPendingJoinProfile;
    private float joinProfileRetryTimer;
    private int joinProfileSendAttempts;

    // - Role: Set up needed links before start.
    private void Awake()
    {
        joinProfileWriter = new FastBufferWriter(GameNetProtocol.ClientJoinProfilePacketBufferSize, Allocator.Persistent);
        joinProfileWriterCreated = true;
    }

    // - Role: Set up this object when it starts.
    private async void Start()
    {
        // - Role: Set up Unity services.
        await InitializeUnityServices();
        PrepareUI();
        BindUIEvents();
    }

    // - Role: Update this object each frame.
    private void Update()
    {
        SendPendingJoinProfileIfNeeded();
    }

    // - Role: Clean up links before this object is destroyed.
    private void OnDestroy()
    {
        if (connectButton != null)
        {
            connectButton.onClick.RemoveListener(OnConnectButtonClicked);
        }

        if (joinCodeInput != null)
        {
            joinCodeInput.onSubmit.RemoveListener(OnJoinCodeSubmitted);
        }

        if (nicknameInput != null)
        {
            nicknameInput.onSubmit.RemoveListener(OnJoinCodeSubmitted);
        }

        if (characterIdInput != null)
        {
            characterIdInput.onSubmit.RemoveListener(OnJoinCodeSubmitted);
        }

        if (skillIdInput != null)
        {
            skillIdInput.onSubmit.RemoveListener(OnJoinCodeSubmitted);
        }

        if (delayModeToggle != null)
        {
            delayModeToggle.onValueChanged.RemoveListener(OnNetworkDelayModeChanged);
        }

        if (delaySlider != null)
        {
            delaySlider.onValueChanged.RemoveListener(OnNetworkDelaySliderChanged);
        }

        if (NetworkManager.Singleton == null)
        {
            DisposeJoinProfileWriter();
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        DisposeJoinProfileWriter();
    }

    public void ConfigureInactiveClientMode(bool showHud, string joinCode = "", string status = "Local Host")
    {
        inactiveClientMode = true;
        inactiveClientModeShowHud = showHud;
        isProcessing = false;
        isConnected = false;
        currentJoinCode = string.IsNullOrWhiteSpace(joinCode) ? "" : joinCode.Trim();
        connectionStatus = status;
        UnbindNetworkCallbacks();
        ClearPendingJoinProfile();
        SetConnectionUIInteractable(false);
        ApplyPanelVisibility();
    }

    private void OnGUI()
    {
        if (!showConnectionDebugGui || (!isConnected && !inactiveClientModeShowHud))
        {
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 260, 130));
        GUILayout.BeginVertical("box");

        GUILayout.Label("[ Role: CLIENT ]", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
        GUILayout.Space(5);
        GUILayout.Label($"ConnectionState: {connectionStatus}");
        GUILayout.Label($"JoinCode: {GetJoinCodeText()}");

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    // - Role: Set up Unity services.
    private async Task InitializeUnityServices()
    {
        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }
        catch (Exception e)
        {
            connectionStatus = "Unity Services Failed";
            Debug.LogError($"[Relay_ClientBootstrap] UGS initialization failed: {e.Message}");
        }
    }

    // - Role: Prepare UI links.
    private void PrepareUI()
    {
        ResolveNetworkDelaySimulator();
        ConfigureConnectionUI();
        ConfigureNetworkDelayUI();
        ApplyPanelVisibility();
        ApplyNetworkDelaySettings();
        ApplyNetworkDelayUIState();
    }

    // - Role: Bind UI events.
    private void BindUIEvents()
    {
        if (connectButton != null)
        {
            connectButton.onClick.RemoveListener(OnConnectButtonClicked);
            connectButton.onClick.AddListener(OnConnectButtonClicked);
        }

        if (joinCodeInput != null)
        {
            joinCodeInput.lineType = TMP_InputField.LineType.SingleLine;
            joinCodeInput.onSubmit.RemoveListener(OnJoinCodeSubmitted);
            joinCodeInput.onSubmit.AddListener(OnJoinCodeSubmitted);
        }

        BindConnectionInputSubmit(nicknameInput);
        BindConnectionInputSubmit(characterIdInput);
        BindConnectionInputSubmit(skillIdInput);

        if (delayModeToggle != null)
        {
            delayModeToggle.onValueChanged.RemoveListener(OnNetworkDelayModeChanged);
            delayModeToggle.onValueChanged.AddListener(OnNetworkDelayModeChanged);
        }

        if (delaySlider != null)
        {
            delaySlider.onValueChanged.RemoveListener(OnNetworkDelaySliderChanged);
            delaySlider.onValueChanged.AddListener(OnNetworkDelaySliderChanged);
        }

        if (inactiveClientMode || NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void UnbindNetworkCallbacks()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    // - Role: Handle connect button clicked.
    private void OnConnectButtonClicked()
    {
        if (joinCodeInput == null)
            return;

        string joinCode = joinCodeInput.text.Trim();
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            Debug.LogWarning("[Relay_ClientBootstrap] JoinCode is required.");
            return;
        }

        if (!TryBuildJoinProfile(out pendingJoinProfile))
        {
            return;
        }

        hasPendingJoinProfile = true;
        joinProfileSendAttempts = 0;
        joinProfileRetryTimer = 0f;
        _ = StartClientAsync(joinCode);
    }

    // - Role: Handle join code submitted.
    private void OnJoinCodeSubmitted(string _)
    {
        if (connectButton == null || !connectButton.interactable)
            return;

        OnConnectButtonClicked();
    }

    // - Role: Bind connection submit input.
    private void BindConnectionInputSubmit(TMP_InputField inputField)
    {
        if (inputField == null)
        {
            return;
        }

        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.onSubmit.RemoveListener(OnJoinCodeSubmitted);
        inputField.onSubmit.AddListener(OnJoinCodeSubmitted);
    }

    // - Role: Try to build join profile.
    private bool TryBuildJoinProfile(out ClientJoinProfilePacket packet)
    {
        packet = default;

        if (!TryReadByteInput(characterIdInput, DefaultCharacterId, "CharacterId", out byte characterId))
        {
            return false;
        }

        if (!TryReadByteInput(skillIdInput, DefaultSkillId, "SkillId", out byte skillId))
        {
            return false;
        }

        FixedString64Bytes nickname = default;
        nickname.CopyFromTruncated(SanitizeNickname(nicknameInput != null ? nicknameInput.text : null));

        packet = new ClientJoinProfilePacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            nickname = nickname,
            characterId = characterId,
            skillId = skillId
        };

        Debug.Log(
            $"[Relay_ClientBootstrap] Join profile prepared: " +
            $"nickname={packet.NicknameText}, characterId={packet.characterId}, skillId={packet.skillId}");

        return true;
    }

    // - Role: Try to read byte input.
    private bool TryReadByteInput(
        TMP_InputField inputField,
        byte fallbackValue,
        string fieldName,
        out byte value)
    {
        value = fallbackValue;

        if (inputField == null || string.IsNullOrWhiteSpace(inputField.text))
        {
            return true;
        }

        if (byte.TryParse(inputField.text.Trim(), out value))
        {
            return true;
        }

        Debug.LogWarning($"[Relay_ClientBootstrap] {fieldName} must be a number from 0 to 255.");
        return false;
    }

    // - Role: Clean nickname text.
    private string SanitizeNickname(string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return DefaultNickname;
        }

        string trimmedNickname = nickname.Trim();
        if (trimmedNickname.Length > MaxNicknameLength)
        {
            return trimmedNickname.Substring(0, MaxNicknameLength);
        }

        return trimmedNickname;
    }

    // - Role: Start client async.
    public async Task StartClientAsync(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            Debug.LogWarning("[Relay_ClientBootstrap] JoinCode is required.");
            return;
        }

        if (isProcessing || isConnected)
            return;

        inactiveClientMode = false;
        inactiveClientModeShowHud = false;

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[Relay_ClientBootstrap] NetworkManager.Singleton is null.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
            return;

        isProcessing = true;
        currentJoinCode = joinCode.Trim();
        connectionStatus = "Joining Relay";
        ApplyNetworkDelaySettings();
        SetConnectionUIInteractable(false);

        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            var endpoint = joinAllocation.ServerEndpoints.First(e => e.ConnectionType == RELAY_PROTOCOL);
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

            transport.SetClientRelayData(
                endpoint.Host,
                (ushort)endpoint.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData,
                RELAY_PROTOCOL == "dtls"
            );

            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = false;

            bool started = NetworkManager.Singleton.StartClient();

            if (!started)
            {
                connectionStatus = "StartClient Failed";
                isProcessing = false;
                ClearPendingJoinProfile();
                SetConnectionUIInteractable(true);
                ApplyPanelVisibility();
            }
        }
        catch (Exception e)
        {
            connectionStatus = "Client Join Failed";
            Debug.LogError($"[Relay_ClientBootstrap] Client join failed: {e.Message}");
            isProcessing = false;
            ClearPendingJoinProfile();
            SetConnectionUIInteractable(true);
            ApplyPanelVisibility();
        }
    }

    // - Role: Handle client connected.
    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        isConnected = true;
        isProcessing = false;
        inactiveClientMode = false;
        inactiveClientModeShowHud = false;
        connectionStatus = $"Connected: {clientId}";

        SetConnectionUIInteractable(false);
        ApplyNetworkDelaySettings();
        ApplyNetworkDelayUIState();
        ApplyPanelVisibility();
        SendJoinProfileNow();
    }

    // - Role: Handle client disconnected.
    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId && clientId != NetworkManager.ServerClientId)
            return;

        isConnected = false;
        isProcessing = false;
        inactiveClientMode = false;
        inactiveClientModeShowHud = false;
        connectionStatus = $"Disconnected: {clientId}";
        ClearPendingJoinProfile();

        SetConnectionUIInteractable(true);
        ApplyNetworkDelayUIState();
        ApplyPanelVisibility();
    }

    // - Role: Send pending join profile if needed.
    private void SendPendingJoinProfileIfNeeded()
    {
        if (!hasPendingJoinProfile)
            return;

        if (!isConnected)
            return;

        if (joinProfileSendAttempts >= JoinProfileMaxSendAttempts)
        {
            ClearPendingJoinProfile();
            return;
        }

        joinProfileRetryTimer -= Time.deltaTime;
        if (joinProfileRetryTimer > 0f)
            return;

        SendJoinProfileNow();
    }

    // - Role: Send join profile now.
    private void SendJoinProfileNow()
    {
        if (!CanSendJoinProfile())
            return;

        joinProfileWriter.Truncate(0);
        pendingJoinProfile.Write(ref joinProfileWriter);

        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            GameNetMessages.ClientJoinProfile,
            NetworkManager.ServerClientId,
            joinProfileWriter,
            NetworkDelivery.ReliableSequenced
        );

        joinProfileSendAttempts++;
        joinProfileRetryTimer = JoinProfileRetryInterval;
    }

    // - Role: Check if send join profile can happen.
    private bool CanSendJoinProfile()
    {
        if (!hasPendingJoinProfile)
            return false;

        if (!joinProfileWriterCreated)
            return false;

        if (NetworkManager.Singleton == null)
            return false;

        if (!NetworkManager.Singleton.IsClient)
            return false;

        if (!NetworkManager.Singleton.IsConnectedClient)
            return false;

        if (NetworkManager.Singleton.IsServer)
            return false;

        return NetworkManager.Singleton.CustomMessagingManager != null;
    }

    // - Role: Clear pending join profile.
    private void ClearPendingJoinProfile()
    {
        hasPendingJoinProfile = false;
        joinProfileSendAttempts = 0;
        joinProfileRetryTimer = 0f;
        pendingJoinProfile = default;
    }

    private string GetJoinCodeText()
    {
        if (string.IsNullOrWhiteSpace(currentJoinCode))
        {
            return "-";
        }

        return currentJoinCode;
    }

    // - Role: Set connection UI interactable.
    private void SetConnectionUIInteractable(bool interactable)
    {
        if (joinCodeInput != null)
        {
            joinCodeInput.interactable = interactable;
        }

        if (nicknameInput != null)
        {
            nicknameInput.interactable = interactable;
        }

        if (characterIdInput != null)
        {
            characterIdInput.interactable = interactable;
        }

        if (skillIdInput != null)
        {
            skillIdInput.interactable = interactable;
        }

        if (connectButton != null)
        {
            connectButton.interactable = interactable;
        }
    }

    // - Role: Set up connection UI.
    private void ConfigureConnectionUI()
    {
        if (!hasWarnedMissingConnectionUi
            && (joinCodeInput == null
                || nicknameInput == null
                || characterIdInput == null
                || skillIdInput == null
                || connectButton == null))
        {
            hasWarnedMissingConnectionUi = true;
            Debug.LogWarning(
                "[Relay_ClientBootstrap] Assign JoinCode, NickName, CharacterId, SkillId InputFields, and Connect Button " +
                "to the connection UI fields in the inspector."
            );
        }
    }

    // - Role: Find network delay simulator.
    private void ResolveNetworkDelaySimulator()
    {
        if (networkDelaySimulator != null)
            return;

        networkDelaySimulator = GetComponent<Client_NetworkDelaySimulator>();

        if (!hasWarnedMissingDelaySimulator && networkDelaySimulator == null)
        {
            hasWarnedMissingDelaySimulator = true;
            Debug.LogWarning("[Relay_ClientBootstrap] Assign Client_NetworkDelaySimulator in the inspector.");
        }
    }

    // - Role: Set up network delay UI.
    private void ConfigureNetworkDelayUI()
    {
        if (delaySlider != null)
        {
            delaySlider.minValue = Client_NetworkDelaySimulator.MinRoundTripDelayMilliseconds;
            delaySlider.maxValue = Client_NetworkDelaySimulator.MaxRoundTripDelayMilliseconds;
            delaySlider.wholeNumbers = true;

            if (delaySlider.value < delaySlider.minValue || delaySlider.value > delaySlider.maxValue)
            {
                delaySlider.value = networkDelaySimulator != null
                    ? networkDelaySimulator.RoundTripDelayMilliseconds
                    : Client_NetworkDelaySimulator.MinRoundTripDelayMilliseconds;
            }
        }

        if (!hasWarnedMissingDelayUi
            && (delayModeToggle == null
                || delaySlider == null
                || delayValueText == null))
        {
            hasWarnedMissingDelayUi = true;
            Debug.LogWarning(
                "[Relay_ClientBootstrap] Assign Toggle, Slider, and TMP_Delay " +
                "to the network delay UI fields in the inspector."
            );
        }
    }

    // - Role: Apply panel visibility.
    private void ApplyPanelVisibility()
    {
        if (canvasPanelController == null)
        {
            Debug.LogError("[Relay_ClientBootstrap] ClientCanvasPanelController is not assigned.", this);
            return;
        }

        if (inactiveClientMode)
        {
            canvasPanelController.ApplyMode(ClientStageUiMode.LocalHost);
            return;
        }

        canvasPanelController.ApplyOnlineConnectionState(isConnected);
    }

    // - Role: Handle network delay mode changed.
    private void OnNetworkDelayModeChanged(bool _)
    {
        ApplyNetworkDelaySettings();
        ApplyNetworkDelayUIState();
    }

    // - Role: Handle network delay slider changed.
    private void OnNetworkDelaySliderChanged(float value)
    {
        if (networkDelaySimulator != null)
        {
            networkDelaySimulator.SetRoundTripDelayMilliseconds(value);
        }

        UpdateNetworkDelayValueText();
    }

    // - Role: Apply network delay settings.
    private void ApplyNetworkDelaySettings()
    {
        ResolveNetworkDelaySimulator();

        if (networkDelaySimulator == null)
        {
            UpdateNetworkDelayValueText();
            return;
        }

        bool enableDelay = delayModeToggle != null && delayModeToggle.isOn;

        if (delaySlider != null)
        {
            networkDelaySimulator.SetRoundTripDelayMilliseconds(delaySlider.value);
        }

        networkDelaySimulator.SetDelayMode(enableDelay);
        UpdateNetworkDelayValueText();
    }

    // - Role: Apply network delay UI state.
    private void ApplyNetworkDelayUIState()
    {
        if (delaySlider != null)
        {
            delaySlider.interactable = delayModeToggle == null || delayModeToggle.isOn;
        }

        UpdateNetworkDelayValueText();
    }

    // - Role: Update network delay value text.
    private void UpdateNetworkDelayValueText()
    {
        if (delayValueText == null)
            return;

        int delayMilliseconds = networkDelaySimulator != null
            ? networkDelaySimulator.RoundTripDelayMilliseconds
            : Client_NetworkDelaySimulator.MinRoundTripDelayMilliseconds;

        delayValueText.text = $"RTT: {delayMilliseconds} ms";
    }

    // - Role: Dispose the join profile writer.
    private void DisposeJoinProfileWriter()
    {
        if (!joinProfileWriterCreated)
            return;

        joinProfileWriter.Dispose();
        joinProfileWriterCreated = false;
    }
}

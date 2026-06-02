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

    [Header("Connection UI References")]
    [SerializeField] private GameObject HudPanel;
    [SerializeField] private GameObject connectionPanel;
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private TMP_InputField characterIdInput;
    [SerializeField] private TMP_InputField skillIdInput;
    [SerializeField] private Button connectButton;

    [Header("Network Delay UI References")]
    [SerializeField] private GameObject delayPanel;
    [SerializeField] private Toggle delayModeToggle;
    [SerializeField] private Slider delaySlider;
    [SerializeField] private TMP_Text delayValueText;
    [SerializeField] private Client_NetworkDelaySimulator networkDelaySimulator;


    private bool isProcessing;
    private bool isConnected;
    private bool hasWarnedMissingConnectionUi;
    private bool hasWarnedMissingDelayUi;
    private bool hasWarnedMissingDelaySimulator;
    private FastBufferWriter joinProfileWriter;
    private bool joinProfileWriterCreated;
    private ClientJoinProfilePacket pendingJoinProfile;
    private bool hasPendingJoinProfile;
    private float joinProfileRetryTimer;
    private int joinProfileSendAttempts;

    private void Awake()
    {
        joinProfileWriter = new FastBufferWriter(
            GameNetProtocol.ClientJoinProfilePacketBufferSize,
            Allocator.Persistent);
        joinProfileWriterCreated = true;
    }

    private async void Start()
    {
        await InitializeUnityServices();
        PrepareUI();
        BindUIEvents();
    }

    private void Update()
    {
        SendPendingJoinProfileIfNeeded();
    }

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
            Debug.LogError($"[Relay_ClientBootstrap] UGS initialization failed: {e.Message}");
        }
    }

    private void PrepareUI()
    {
        ResolveNetworkDelaySimulator();
        ConfigureConnectionUI();
        ConfigureNetworkDelayUI();
        ApplyPanelVisibility();
        ApplyNetworkDelaySettings();
        ApplyNetworkDelayUIState();
    }

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

        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

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

    private void OnJoinCodeSubmitted(string _)
    {
        if (connectButton == null || !connectButton.interactable)
            return;

        OnConnectButtonClicked();
    }

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

    public async Task StartClientAsync(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            Debug.LogWarning("[Relay_ClientBootstrap] JoinCode is required.");
            return;
        }

        if (isProcessing || isConnected)
            return;

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[Relay_ClientBootstrap] NetworkManager.Singleton is null.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
            return;

        isProcessing = true;
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
                isProcessing = false;
                ClearPendingJoinProfile();
                SetConnectionUIInteractable(true);
                ApplyPanelVisibility();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay_ClientBootstrap] Client join failed: {e.Message}");
            isProcessing = false;
            ClearPendingJoinProfile();
            SetConnectionUIInteractable(true);
            ApplyPanelVisibility();
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        isConnected = true;
        isProcessing = false;

        SetConnectionUIInteractable(false);
        ApplyNetworkDelaySettings();
        ApplyNetworkDelayUIState();
        ApplyPanelVisibility();
        SendJoinProfileNow();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId && clientId != NetworkManager.ServerClientId)
            return;

        isConnected = false;
        isProcessing = false;
        ClearPendingJoinProfile();

        SetConnectionUIInteractable(true);
        ApplyNetworkDelayUIState();
        ApplyPanelVisibility();
    }

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

    private void ClearPendingJoinProfile()
    {
        hasPendingJoinProfile = false;
        joinProfileSendAttempts = 0;
        joinProfileRetryTimer = 0f;
        pendingJoinProfile = default;
    }

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

    private void ConfigureConnectionUI()
    {
        if (!hasWarnedMissingConnectionUi
            && (connectionPanel == null
                || joinCodeInput == null
                || nicknameInput == null
                || characterIdInput == null
                || skillIdInput == null
                || connectButton == null))
        {
            hasWarnedMissingConnectionUi = true;
            Debug.LogWarning(
                "[Relay_ClientBootstrap] Assign Panel_Connection, JoinCode, NickName, CharacterId, SkillId InputFields, and Connect Button " +
                "to the connection UI fields in the inspector."
            );
        }
    }

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
            && (delayPanel == null
                || delayModeToggle == null
                || delaySlider == null
                || delayValueText == null))
        {
            hasWarnedMissingDelayUi = true;
            Debug.LogWarning(
                "[Relay_ClientBootstrap] Assign Panel_Network, Toggle, Slider, and TMP_Delay " +
                "to the network delay UI fields in the inspector."
            );
        }
    }

    private void ApplyPanelVisibility()
    {
        if (connectionPanel != null)
        {
            connectionPanel.SetActive(!isConnected);
        }

        if (delayPanel != null)
        {
            delayPanel.SetActive(isConnected);
        }

        if(HudPanel != null)
        {
            HudPanel.SetActive(isConnected);
        }
    }

    private void OnNetworkDelayModeChanged(bool _)
    {
        ApplyNetworkDelaySettings();
        ApplyNetworkDelayUIState();
    }

    private void OnNetworkDelaySliderChanged(float value)
    {
        if (networkDelaySimulator != null)
        {
            networkDelaySimulator.SetRoundTripDelayMilliseconds(value);
        }

        UpdateNetworkDelayValueText();
    }

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

    private void ApplyNetworkDelayUIState()
    {
        if (delaySlider != null)
        {
            delaySlider.interactable = delayModeToggle == null || delayModeToggle.isOn;
        }

        UpdateNetworkDelayValueText();
    }

    private void UpdateNetworkDelayValueText()
    {
        if (delayValueText == null)
            return;

        int delayMilliseconds = networkDelaySimulator != null
            ? networkDelaySimulator.RoundTripDelayMilliseconds
            : Client_NetworkDelaySimulator.MinRoundTripDelayMilliseconds;

        delayValueText.text = $"RTT: {delayMilliseconds} ms";
    }

    private void DisposeJoinProfileWriter()
    {
        if (!joinProfileWriterCreated)
            return;

        joinProfileWriter.Dispose();
        joinProfileWriterCreated = false;
    }
}

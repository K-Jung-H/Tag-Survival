using System;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
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

    [Header("Connection UI References")]
    [SerializeField] private GameObject connectionPanel;
    [SerializeField] private TMP_InputField joinCodeInput;
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

    private async void Start()
    {
        await InitializeUnityServices();
        PrepareUI();
        BindUIEvents();
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

        if (delayModeToggle != null)
        {
            delayModeToggle.onValueChanged.RemoveListener(OnNetworkDelayModeChanged);
        }

        if (delaySlider != null)
        {
            delaySlider.onValueChanged.RemoveListener(OnNetworkDelaySliderChanged);
        }

        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
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
        _ = StartClientAsync(joinCode);
    }

    private void OnJoinCodeSubmitted(string _)
    {
        if (connectButton == null || !connectButton.interactable)
            return;

        OnConnectButtonClicked();
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
                SetConnectionUIInteractable(true);
                ApplyPanelVisibility();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay_ClientBootstrap] Client join failed: {e.Message}");
            isProcessing = false;
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
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId && clientId != NetworkManager.ServerClientId)
            return;

        isConnected = false;
        isProcessing = false;

        SetConnectionUIInteractable(true);
        ApplyNetworkDelayUIState();
        ApplyPanelVisibility();
    }

    private void SetConnectionUIInteractable(bool interactable)
    {
        if (joinCodeInput != null)
        {
            joinCodeInput.interactable = interactable;
        }

        if (connectButton != null)
        {
            connectButton.interactable = interactable;
        }
    }

    private void ConfigureConnectionUI()
    {
        if (!hasWarnedMissingConnectionUi
            && (connectionPanel == null || joinCodeInput == null || connectButton == null))
        {
            hasWarnedMissingConnectionUi = true;
            Debug.LogWarning(
                "[Relay_ClientBootstrap] Assign Panel_Connection, JoinCode InputField, and Connect Button " +
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
}

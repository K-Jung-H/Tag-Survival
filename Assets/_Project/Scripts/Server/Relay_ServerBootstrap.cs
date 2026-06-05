using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class Relay_ServerBootstrap : MonoBehaviour
{
    private const int MAX_CONNECTIONS = 10;
    private const string RELAY_PROTOCOL = "dtls";

    [SerializeField] private bool autoStartServer = true;

    private string currentJoinCode = "";
    private string statusMessage = "Idle";

    private bool isProcessing;
    private bool isServerStarted;

    // - Role: Set up this object when it starts.
    private async void Start()
    {
        // - Role: Set up Unity services.
        await InitializeUnityServices();

        if (autoStartServer)
        {
            // - Role: Start server async.
            await StartServerAsync();
        }
    }

    // - Role: Draw simple debug GUI.
    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 260, 130));
        GUILayout.BeginVertical("box");

        GUILayout.Label("[ Role: SERVER ]", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
        GUILayout.Space(5);
        GUILayout.Label($"Status: {statusMessage}");
        GUILayout.Label($"JoinCode: {GetJoinCodeText()}");
        GUILayout.Label($"Players: {GetConnectedClientCount()} / {MAX_CONNECTIONS}");

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    // - Role: Set up Unity services.
    private async Task InitializeUnityServices()
    {
        try
        {
            statusMessage = "Initializing Unity Services";

            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            statusMessage = "Unity Services Ready";
        }
        catch (Exception e)
        {
            statusMessage = "Unity Services Failed";
            Debug.LogError($"[Relay_ServerBootstrap] UGS initialization failed: {e.Message}");
        }
    }

    // - Role: Start server async.
    public async Task StartServerAsync()
    {
        if (isProcessing || isServerStarted)
            return;

        if (NetworkManager.Singleton == null)
        {
            statusMessage = "NetworkManager Missing";
            return;
        }

        if (NetworkManager.Singleton.IsListening)
            return;

        isProcessing = true;
        statusMessage = "Creating Relay Allocation";

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MAX_CONNECTIONS);
            currentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            var endpoint = allocation.ServerEndpoints.First(e => e.ConnectionType == RELAY_PROTOCOL);
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

            transport.SetHostRelayData(
                endpoint.Host,
                (ushort)endpoint.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData,
                RELAY_PROTOCOL == "dtls"
            );

            statusMessage = "Starting Server";
            
            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = false;

            bool started = NetworkManager.Singleton.StartServer();

            if (!started)
            {
                statusMessage = "StartServer Failed";
                isProcessing = false;
                return;
            }

            isServerStarted = true;
            statusMessage = "Server Started";

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
        catch (Exception e)
        {
            statusMessage = "Server Start Failed";
            Debug.LogError($"[Relay_ServerBootstrap] Server start failed: {e.Message}");
        }

        isProcessing = false;
    }

    // - Role: Clean up links before this object is destroyed.
    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    // - Role: Handle client connected.
    private void OnClientConnected(ulong clientId)
    {
        statusMessage = $"Client Connected: {clientId}";
    }

    // - Role: Handle client disconnected.
    private void OnClientDisconnected(ulong clientId)
    {
        statusMessage = $"Client Disconnected: {clientId}";
    }

    // - Role: Get join code text.
    private string GetJoinCodeText()
    {
        if (string.IsNullOrWhiteSpace(currentJoinCode))
            return "Creating...";

        return currentJoinCode;
    }

    // - Role: Get connected client count.
    private int GetConnectedClientCount()
    {
        if (NetworkManager.Singleton == null)
            return 0;

        if (!NetworkManager.Singleton.IsServer)
            return 0;

        return NetworkManager.Singleton.ConnectedClientsList.Count;
    }
}
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

    private string currentJoinCode = "";
    private string statusMessage = "Idle";

    private bool isProcessing;
    private bool isServerStarted;
    private bool isInitialized;
    private bool hasInitializationFailed;
    private Task initializeTask;

    public string CurrentJoinCode => currentJoinCode;
    public string StatusMessage => statusMessage;
    public bool IsProcessing => isProcessing;
    public bool IsServerStarted => isServerStarted;
    public bool IsInitialized => isInitialized;

    // - Role: Set up this object when it starts.
    private async void Start()
    {
        await InitializeAsync();
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

    public async Task<bool> InitializeAsync()
    {
        if (initializeTask == null || hasInitializationFailed)
        {
            initializeTask = InitializeUnityServices();
        }

        await initializeTask;
        return isInitialized && !hasInitializationFailed;
    }

    private async Task InitializeUnityServices()
    {
        try
        {
            hasInitializationFailed = false;
            statusMessage = "Initializing Unity Services";

            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            statusMessage = "Unity Services Ready";
            isInitialized = true;
        }
        catch (Exception e)
        {
            statusMessage = "Unity Services Failed";
            isInitialized = false;
            hasInitializationFailed = true;
            Debug.LogError($"[Relay_ServerBootstrap] UGS initialization failed: {e.Message}");
        }
    }

    // - Role: Start server async.
    public async Task<bool> StartServerAsync()
    {
        while (isProcessing)
        {
            await Task.Yield();
        }

        if (isServerStarted)
        {
            return true;
        }

        if (!await InitializeAsync())
        {
            return false;
        }

        if (NetworkManager.Singleton == null)
        {
            statusMessage = "NetworkManager Missing";
            return false;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            isServerStarted = NetworkManager.Singleton.IsServer;
            return isServerStarted;
        }

        isProcessing = true;
        statusMessage = "Creating Relay Allocation";

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MAX_CONNECTIONS);
            currentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[Relay_ServerBootstrap] JoinCode: {currentJoinCode}");

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
                return false;
            }

            isServerStarted = true;
            statusMessage = "Server Started";
            Debug.Log($"[Relay_ServerBootstrap] Server started. JoinCode: {currentJoinCode}");

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
        catch (Exception e)
        {
            statusMessage = "Server Start Failed";
            Debug.LogError($"[Relay_ServerBootstrap] Server start failed: {e.Message}");
            isProcessing = false;
            return false;
        }

        isProcessing = false;
        return true;
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

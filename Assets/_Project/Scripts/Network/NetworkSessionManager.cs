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

public enum NetworkSessionRole
{
    None = 0,
    DedicatedServer = 1,
    Host = 2,
    Guest = 3
}

public sealed class NetworkSessionManager : MonoBehaviour
{
    private const int MaxRelayConnections = 10;
    private const string RelayProtocol = "dtls";
    private const float ClientConnectTimeoutSeconds = 12f;

    private static NetworkSessionManager instance;

    private NetworkSessionRole role = NetworkSessionRole.None;
    private string joinCode = string.Empty;
    private string statusMessage = "Idle";
    private bool isProcessing;
    private bool isInitialized;
    private bool hasInitializationFailed;
    private Task initializeTask;

    public static NetworkSessionManager Instance => instance;
    public NetworkSessionRole Role => role;
    public string JoinCode => joinCode;
    public string StatusMessage => statusMessage;
    public bool IsProcessing => isProcessing;
    public bool IsSessionActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    public ulong LocalClientId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

    public static NetworkSessionManager Resolve()
    {
        if (instance != null)
        {
            return instance;
        }

        NetworkSessionManager found = FindFirstObjectByType<NetworkSessionManager>();
        if (found != null)
        {
            return found;
        }

        Debug.LogError("[NetworkSessionManager] NetworkSessionManager is not found. Place it in Scene Online.");
        return null;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public async Task<bool> StartHostSessionAsync()
    {
        if (IsSessionActive && role == NetworkSessionRole.Host)
        {
            return true;
        }

        if (!await PrepareToStartAsync(NetworkSessionRole.Host))
        {
            return false;
        }

        isProcessing = true;
        statusMessage = "Creating Host Relay";

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxRelayConnections);
            joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            ApplyHostRelayData(allocation);

            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = false;
            bool started = NetworkManager.Singleton.StartHost();
            if (!started)
            {
                FailStart("StartHost Failed");
                return false;
            }

            role = NetworkSessionRole.Host;
            statusMessage = "Host Started";
            Debug.Log($"[NetworkSessionManager] Host started. JoinCode: {joinCode}", this);
            return true;
        }
        catch (Exception e)
        {
            FailStart($"Host Start Failed: {e.Message}");
            return false;
        }
        finally
        {
            isProcessing = false;
        }
    }

    public async Task<bool> StartDedicatedServerSessionAsync()
    {
        if (IsSessionActive && role == NetworkSessionRole.DedicatedServer)
        {
            return true;
        }

        if (!await PrepareToStartAsync(NetworkSessionRole.DedicatedServer))
        {
            return false;
        }

        isProcessing = true;
        statusMessage = "Creating Server Relay";

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxRelayConnections);
            joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            ApplyHostRelayData(allocation);

            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = false;
            bool started = NetworkManager.Singleton.StartServer();
            if (!started)
            {
                FailStart("StartServer Failed");
                return false;
            }

            role = NetworkSessionRole.DedicatedServer;
            statusMessage = "Server Started";
            Debug.Log($"[NetworkSessionManager] Dedicated server started. JoinCode: {joinCode}", this);
            return true;
        }
        catch (Exception e)
        {
            FailStart($"Server Start Failed: {e.Message}");
            return false;
        }
        finally
        {
            isProcessing = false;
        }
    }

    public async Task<bool> StartGuestSessionAsync(string roomJoinCode)
    {
        if (string.IsNullOrWhiteSpace(roomJoinCode))
        {
            statusMessage = "JoinCode Required";
            Debug.LogWarning("[NetworkSessionManager] JoinCode is required.", this);
            return false;
        }

        string sanitizedJoinCode = roomJoinCode.Trim();
        if (IsSessionActive && role == NetworkSessionRole.Guest && joinCode == sanitizedJoinCode)
        {
            return true;
        }

        if (!await PrepareToStartAsync(NetworkSessionRole.Guest))
        {
            return false;
        }

        isProcessing = true;
        joinCode = sanitizedJoinCode;
        statusMessage = "Joining Relay";

        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            ApplyClientRelayData(joinAllocation);

            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = false;
            bool started = NetworkManager.Singleton.StartClient();
            if (!started)
            {
                FailStart("StartClient Failed");
                return false;
            }

            if (!await WaitForLocalClientConnectedAsync())
            {
                FailStart("Client Connect Timeout");
                return false;
            }

            role = NetworkSessionRole.Guest;
            statusMessage = $"Guest Connected: {NetworkManager.Singleton.LocalClientId}";
            Debug.Log($"[NetworkSessionManager] Guest connected. JoinCode: {joinCode}", this);
            return true;
        }
        catch (Exception e)
        {
            FailStart($"Guest Join Failed: {e.Message}");
            return false;
        }
        finally
        {
            isProcessing = false;
        }
    }

    public void StopSession()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        role = NetworkSessionRole.None;
        joinCode = string.Empty;
        statusMessage = "Idle";
        isProcessing = false;
    }

    private async Task<bool> PrepareToStartAsync(NetworkSessionRole requestedRole)
    {
        while (isProcessing)
        {
            await Task.Yield();
        }

        if (!await InitializeAsync())
        {
            return false;
        }

        if (NetworkManager.Singleton == null)
        {
            statusMessage = "NetworkManager Missing";
            Debug.LogError("[NetworkSessionManager] NetworkManager.Singleton is null.", this);
            return false;
        }

        DontDestroyOnLoad(NetworkManager.Singleton.gameObject);

        if (NetworkManager.Singleton.IsListening)
        {
            if (role == requestedRole)
            {
                return true;
            }

            statusMessage = "Session Already Active";
            Debug.LogWarning($"[NetworkSessionManager] Cannot start {requestedRole}. Active role: {role}", this);
            return false;
        }

        role = NetworkSessionRole.None;
        joinCode = string.Empty;
        return true;
    }

    private async Task<bool> InitializeAsync()
    {
        if (initializeTask == null || hasInitializationFailed)
        {
            initializeTask = InitializeUnityServicesAsync();
        }

        await initializeTask;
        return isInitialized && !hasInitializationFailed;
    }

    private async Task InitializeUnityServicesAsync()
    {
        try
        {
            hasInitializationFailed = false;
            statusMessage = "Initializing Unity Services";

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            isInitialized = true;
            statusMessage = "Unity Services Ready";
        }
        catch (Exception e)
        {
            isInitialized = false;
            hasInitializationFailed = true;
            statusMessage = "Unity Services Failed";
            Debug.LogError($"[NetworkSessionManager] UGS initialization failed: {e.Message}", this);
        }
    }

    private static void ApplyHostRelayData(Allocation allocation)
    {
        var endpoint = allocation.ServerEndpoints.First(e => e.ConnectionType == RelayProtocol);
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetHostRelayData(
            endpoint.Host,
            (ushort)endpoint.Port,
            allocation.AllocationIdBytes,
            allocation.Key,
            allocation.ConnectionData,
            RelayProtocol == "dtls");
    }

    private static void ApplyClientRelayData(JoinAllocation allocation)
    {
        var endpoint = allocation.ServerEndpoints.First(e => e.ConnectionType == RelayProtocol);
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetClientRelayData(
            endpoint.Host,
            (ushort)endpoint.Port,
            allocation.AllocationIdBytes,
            allocation.Key,
            allocation.ConnectionData,
            allocation.HostConnectionData,
            RelayProtocol == "dtls");
    }

    private static async Task<bool> WaitForLocalClientConnectedAsync()
    {
        float startedAt = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - startedAt < ClientConnectTimeoutSeconds)
        {
            if (NetworkManager.Singleton == null)
            {
                return false;
            }

            if (NetworkManager.Singleton.IsClient && NetworkManager.Singleton.IsConnectedClient)
            {
                return true;
            }

            await Task.Yield();
        }

        return false;
    }

    private void FailStart(string message)
    {
        statusMessage = message;
        Debug.LogError($"[NetworkSessionManager] {message}", this);
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        role = NetworkSessionRole.None;
    }
}

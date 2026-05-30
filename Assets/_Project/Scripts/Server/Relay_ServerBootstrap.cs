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

    // Role: Unity Services 초기화 후 옵션에 따라 서버 Relay 연결을 자동 시작한다.
    private async void Start()
    {
        await InitializeUnityServices();

        if (autoStartServer)
        {
            await StartServerAsync();
        }
    }

    // Role: 서버 상태와 JoinCode를 좌측 상단 GUI Box에 출력한다.
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

    // Role: UGS와 익명 로그인을 초기화한다.
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

    // Role: Relay allocation을 생성하고 Dedicated Server 모드로 NetworkManager를 시작한다.
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

    // Role: 등록된 NetworkManager 연결 이벤트를 해제한다.
    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    // Role: 클라이언트 접속 시 서버 상태 메시지를 갱신한다.
    // Parameters:
    // - clientId: 접속한 클라이언트의 고유 ID
    private void OnClientConnected(ulong clientId)
    {
        statusMessage = $"Client Connected: {clientId}";
    }

    // Role: 클라이언트 연결 해제 시 서버 상태 메시지를 갱신한다.
    // Parameters:
    // - clientId: 연결 해제된 클라이언트의 고유 ID
    private void OnClientDisconnected(ulong clientId)
    {
        statusMessage = $"Client Disconnected: {clientId}";
    }

    // Role: 현재 표시할 JoinCode 문자열을 반환한다.
    private string GetJoinCodeText()
    {
        if (string.IsNullOrWhiteSpace(currentJoinCode))
            return "Creating...";

        return currentJoinCode;
    }

    // Role: 현재 서버에 연결된 클라이언트 수를 반환한다.
    private int GetConnectedClientCount()
    {
        if (NetworkManager.Singleton == null)
            return 0;

        if (!NetworkManager.Singleton.IsServer)
            return 0;

        return NetworkManager.Singleton.ConnectedClientsList.Count;
    }
}
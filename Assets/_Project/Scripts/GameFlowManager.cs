using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GameFlowManager : MonoBehaviour
{
    [Header("Flow Scenes")]
    [SerializeField] private string startSceneName = "StartScene";
    [SerializeField] private string modeSelectSceneName = "ModeSelect";
    [SerializeField] private string onlineSceneName = "Online";
    [SerializeField] private string serverRoomSceneName = "Server_Room";
    [SerializeField] private string clientRoomSceneName = "Client_Room";

    [Header("Temporary Player Profile")]
    [SerializeField] private string playerNickname = "Player";

    private static GameFlowManager instance;
    private bool isTransitioning;
    private RoomLaunchRequest currentRoomLaunchRequest;
    private Server_RoomBuilder serverRoomBuilder;
    private Client_RoomBuilder clientRoomBuilder;

    public static GameFlowManager Instance => instance;
    public string StartSceneName => startSceneName;
    public string ModeSelectSceneName => modeSelectSceneName;
    public string OnlineSceneName => onlineSceneName;
    public string ServerRoomSceneName => serverRoomSceneName;
    public string ClientRoomSceneName => clientRoomSceneName;
    public RoomLaunchRequest CurrentRoomLaunchRequest => currentRoomLaunchRequest;

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

    public void LoadStartScene()
    {
        _ = LoadSceneAsync(startSceneName);
    }

    public void LoadModeSelectScene()
    {
        _ = LoadSceneAsync(modeSelectSceneName);
    }

    public void LoadOnlineScene()
    {
        _ = LoadSceneAsync(onlineSceneName);
    }

    public void StartDedicatedServerRoom()
    {
        currentRoomLaunchRequest = RoomLaunchRequest.Create(
            RoomLaunchMode.DedicatedServer,
            string.Empty,
            playerNickname);
        _ = StartDedicatedServerRoomAsync(currentRoomLaunchRequest);
    }

    public void StartHostRoom()
    {
        currentRoomLaunchRequest = RoomLaunchRequest.Create(
            RoomLaunchMode.HostRoom,
            string.Empty,
            playerNickname);
        _ = StartHostRoomAsync(currentRoomLaunchRequest);
    }

    public void StartJoinRoom(string roomJoinCode)
    {
        currentRoomLaunchRequest = RoomLaunchRequest.Create(
            RoomLaunchMode.JoinRoom,
            roomJoinCode,
            playerNickname);
        _ = StartJoinRoomAsync(currentRoomLaunchRequest);
    }

    public void StartConnectMatchmakingServer(string serverJoinCode)
    {
        currentRoomLaunchRequest = RoomLaunchRequest.Create(
            RoomLaunchMode.ConnectMatchmakingServer,
            serverJoinCode,
            playerNickname);
        Debug.Log("[GameFlowManager] Connect matchmaking server flow is not implemented yet.", this);
    }

    public async Task<bool> LoadSceneAsync(string sceneName)
    {
        if (isTransitioning)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[GameFlowManager] Scene name is empty.", this);
            return false;
        }

        isTransitioning = true;
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (operation == null)
        {
            Debug.LogError($"[GameFlowManager] Failed to load scene: {sceneName}", this);
            isTransitioning = false;
            return false;
        }

        while (!operation.isDone)
        {
            await Task.Yield();
        }

        isTransitioning = false;
        return true;
    }

    private async Task StartDedicatedServerRoomAsync(RoomLaunchRequest request)
    {
        if (isTransitioning)
        {
            return;
        }

        isTransitioning = true;
        Scene serverRoomScene = await LoadSceneInternalAsync(serverRoomSceneName, LoadSceneMode.Single);
        if (TryFindUniqueBuilder(serverRoomScene, out serverRoomBuilder))
        {
            serverRoomBuilder.BuildDedicatedServerRoom(request);
        }

        isTransitioning = false;
    }

    private async Task StartHostRoomAsync(RoomLaunchRequest request)
    {
        if (isTransitioning)
        {
            return;
        }

        isTransitioning = true;
        Scene serverRoomScene = await LoadSceneInternalAsync(serverRoomSceneName, LoadSceneMode.Single);
        if (!TryFindUniqueBuilder(serverRoomScene, out serverRoomBuilder)
            || !serverRoomBuilder.BuildHostedRoom(request))
        {
            isTransitioning = false;
            return;
        }

        Scene clientRoomScene = await LoadSceneInternalAsync(clientRoomSceneName, LoadSceneMode.Additive);
        if (TryFindUniqueBuilder(clientRoomScene, out clientRoomBuilder))
        {
            clientRoomBuilder.BuildLocalHostRoom(request, serverRoomBuilder);
            SceneManager.SetActiveScene(clientRoomScene);
        }

        isTransitioning = false;
    }

    private async Task StartJoinRoomAsync(RoomLaunchRequest request)
    {
        if (isTransitioning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.joinCode))
        {
            Debug.LogWarning("[GameFlowManager] Room JoinCode is required.", this);
            return;
        }

        isTransitioning = true;
        Scene clientRoomScene = await LoadSceneInternalAsync(clientRoomSceneName, LoadSceneMode.Single);
        if (TryFindUniqueBuilder(clientRoomScene, out clientRoomBuilder))
        {
            clientRoomBuilder.BuildOnlineGuestRoom(request);
        }

        isTransitioning = false;
    }

    private async Task<Scene> LoadSceneInternalAsync(string sceneName, LoadSceneMode loadSceneMode)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[GameFlowManager] Scene name is empty.", this);
            return default;
        }

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, loadSceneMode);
        if (operation == null)
        {
            Debug.LogError($"[GameFlowManager] Failed to load scene: {sceneName}", this);
            return default;
        }

        while (!operation.isDone)
        {
            await Task.Yield();
        }

        return SceneManager.GetSceneByName(sceneName);
    }

    private bool TryFindUniqueBuilder<T>(Scene scene, out T builder)
        where T : Component
    {
        builder = null;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[GameFlowManager] Loaded scene is invalid.", this);
            return false;
        }

        T foundBuilder = null;
        int foundCount = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            T[] builders = roots[i].GetComponentsInChildren<T>(true);
            for (int j = 0; j < builders.Length; j++)
            {
                foundBuilder = builders[j];
                foundCount++;
            }
        }

        if (foundCount != 1)
        {
            Debug.LogError(
                $"[GameFlowManager] Scene '{scene.name}' must contain exactly one {typeof(T).Name}. Found: {foundCount}",
                this);
            return false;
        }

        builder = foundBuilder;
        return true;
    }
}

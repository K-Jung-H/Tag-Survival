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
    [SerializeField] private string serverStageSceneName = "Scene Stage Server";
    [SerializeField] private string clientStageSceneName = "Scene Stage Client";

    [Header("Catalogs")]
    [SerializeField] private GameStageCatalog gameStageCatalog;
    [SerializeField] private GameModeCatalog gameModeCatalog;

    [Header("Default Player Profile")]
    [SerializeField] private string playerNickname = "Player";

    private static GameFlowManager instance;
    private bool isTransitioning;
    private RoomLaunchRequest currentRoomLaunchRequest;
    private Server_RoomBuilder serverRoomBuilder;
    private Client_RoomBuilder clientRoomBuilder;
    private ServerStageBuilder serverStageBuilder;
    private ClientStageBuilder clientStageBuilder;
    private RoomSnapshotPacket lastStartedRoomSnapshot;

    public static GameFlowManager Instance => instance;
    public string StartSceneName => startSceneName;
    public string ModeSelectSceneName => modeSelectSceneName;
    public string OnlineSceneName => onlineSceneName;
    public string ServerRoomSceneName => serverRoomSceneName;
    public string ClientRoomSceneName => clientRoomSceneName;
    public string ServerStageSceneName => serverStageSceneName;
    public string ClientStageSceneName => clientStageSceneName;
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
        StartHostRoom(playerNickname);
    }

    public void StartHostRoom(string nickname)
    {
        currentRoomLaunchRequest = RoomLaunchRequest.Create(
            RoomLaunchMode.HostRoom,
            string.Empty,
            nickname);
        _ = StartHostRoomAsync(currentRoomLaunchRequest);
    }

    public void StartJoinRoom(string roomJoinCode)
    {
        StartJoinRoom(roomJoinCode, playerNickname);
    }

    public void StartJoinRoom(string roomJoinCode, string nickname)
    {
        currentRoomLaunchRequest = RoomLaunchRequest.Create(
            RoomLaunchMode.JoinRoom,
            roomJoinCode,
            nickname);
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

    public void StartStageFromRoom(RoomSnapshotPacket roomSnapshot)
    {
        _ = StartStageFromRoomAsync(roomSnapshot);
    }

    public void ReturnToRoomFromStage()
    {
        _ = ReturnToRoomFromStageAsync();
    }

    public void ExitStageToOnline()
    {
        _ = ExitStageToOnlineAsync();
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

        AudioManager.Instance?.PlaySceneBgm(sceneName);
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
        NetworkSessionManager session = NetworkSessionManager.Resolve();
        if (session == null)
        {
            isTransitioning = false;
            return;
        }

        if (!await session.StartDedicatedServerSessionAsync())
        {
            isTransitioning = false;
            return;
        }

        request.joinCode = session.JoinCode;
        currentRoomLaunchRequest = request;

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
        NetworkSessionManager session = NetworkSessionManager.Resolve();
        if (session == null)
        {
            isTransitioning = false;
            return;
        }

        if (!await session.StartHostSessionAsync())
        {
            isTransitioning = false;
            return;
        }

        request.joinCode = session.JoinCode;
        currentRoomLaunchRequest = request;

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
        NetworkSessionManager session = NetworkSessionManager.Resolve();
        if (session == null)
        {
            isTransitioning = false;
            return;
        }

        if (!await session.StartGuestSessionAsync(request.joinCode))
        {
            isTransitioning = false;
            return;
        }

        Scene clientRoomScene = await LoadSceneInternalAsync(clientRoomSceneName, LoadSceneMode.Single);
        if (TryFindUniqueBuilder(clientRoomScene, out clientRoomBuilder))
        {
            clientRoomBuilder.BuildOnlineGuestRoom(request);
        }

        isTransitioning = false;
    }

    private async Task StartStageFromRoomAsync(RoomSnapshotPacket roomSnapshot)
    {
        if (isTransitioning)
        {
            return;
        }

        isTransitioning = true;
        RoomSnapshotPacket resolvedRoomSnapshot = ResolveFinalRoomSnapshot(roomSnapshot);
        lastStartedRoomSnapshot = resolvedRoomSnapshot;

        if (!ResolveStageSelection(resolvedRoomSnapshot.stageIndex, out StageDefinition stageDefinition))
        {
            isTransitioning = false;
            return;
        }

        if (!ResolveGameModeSelection(resolvedRoomSnapshot.gameModeIndex, out GameModeType gameModeType, out GameModeConfig gameModeConfig))
        {
            isTransitioning = false;
            return;
        }

        switch (currentRoomLaunchRequest.mode)
        {
            case RoomLaunchMode.DedicatedServer:
                await BuildServerStageFromRoomAsync(resolvedRoomSnapshot, stageDefinition, gameModeType, gameModeConfig);
                break;
            case RoomLaunchMode.HostRoom:
                await BuildHostedStageFromRoomAsync(resolvedRoomSnapshot, stageDefinition, gameModeType, gameModeConfig);
                break;
            case RoomLaunchMode.JoinRoom:
                await BuildGuestStageFromRoomAsync(stageDefinition);
                break;
            default:
                Debug.LogWarning($"[GameFlowManager] Unsupported room launch mode for stage start: {currentRoomLaunchRequest.mode}", this);
                break;
        }

        isTransitioning = false;
    }

    private async Task ReturnToRoomFromStageAsync()
    {
        if (isTransitioning)
        {
            return;
        }

        isTransitioning = true;
        switch (currentRoomLaunchRequest.mode)
        {
            case RoomLaunchMode.HostRoom:
                await ReturnHostedRoomAsync();
                break;
            case RoomLaunchMode.JoinRoom:
                await ReturnGuestRoomAsync();
                break;
            case RoomLaunchMode.DedicatedServer:
                await ReturnDedicatedServerRoomAsync();
                break;
            default:
                Debug.LogWarning($"[GameFlowManager] Unsupported rematch return mode: {currentRoomLaunchRequest.mode}", this);
                break;
        }

        isTransitioning = false;
    }

    private async Task ExitStageToOnlineAsync()
    {
        if (isTransitioning)
        {
            return;
        }

        isTransitioning = true;
        if (currentRoomLaunchRequest.mode == RoomLaunchMode.HostRoom)
        {
            await Task.Delay(250);
        }

        NetworkSessionManager.Instance?.StopSession();
        await LoadSceneInternalAsync(onlineSceneName, LoadSceneMode.Single);
        isTransitioning = false;
    }

    private async Task ReturnHostedRoomAsync()
    {
        Scene serverRoomScene = await LoadSceneInternalAsync(serverRoomSceneName, LoadSceneMode.Single);
        if (!TryFindUniqueBuilder(serverRoomScene, out serverRoomBuilder)
            || !serverRoomBuilder.BuildHostedRoom(currentRoomLaunchRequest))
        {
            return;
        }

        serverRoomBuilder.RoomManager.ConfigureRematchState(lastStartedRoomSnapshot);

        Scene clientRoomScene = await LoadSceneInternalAsync(clientRoomSceneName, LoadSceneMode.Additive);
        if (TryFindUniqueBuilder(clientRoomScene, out clientRoomBuilder))
        {
            clientRoomBuilder.BuildLocalHostRoom(currentRoomLaunchRequest, serverRoomBuilder);
            SceneManager.SetActiveScene(clientRoomScene);
        }
    }

    private async Task ReturnGuestRoomAsync()
    {
        Scene clientRoomScene = await LoadSceneInternalAsync(clientRoomSceneName, LoadSceneMode.Single);
        if (TryFindUniqueBuilder(clientRoomScene, out clientRoomBuilder))
        {
            clientRoomBuilder.BuildOnlineGuestRoom(currentRoomLaunchRequest);
            SceneManager.SetActiveScene(clientRoomScene);
        }
    }

    private async Task ReturnDedicatedServerRoomAsync()
    {
        Scene serverRoomScene = await LoadSceneInternalAsync(serverRoomSceneName, LoadSceneMode.Single);
        if (TryFindUniqueBuilder(serverRoomScene, out serverRoomBuilder)
            && serverRoomBuilder.BuildDedicatedServerRoom(currentRoomLaunchRequest))
        {
            serverRoomBuilder.RoomManager.ConfigureRematchState(lastStartedRoomSnapshot);
            SceneManager.SetActiveScene(serverRoomScene);
        }
    }

    private async Task BuildServerStageFromRoomAsync(
        RoomSnapshotPacket roomSnapshot,
        StageDefinition stageDefinition,
        GameModeType gameModeType,
        GameModeConfig gameModeConfig)
    {
        Scene serverStageScene = await LoadSceneInternalAsync(serverStageSceneName, LoadSceneMode.Single);
        if (TryFindUniqueBuilder(serverStageScene, out serverStageBuilder))
        {
            serverStageBuilder.ConfigureStageDefinition(stageDefinition);
            serverStageBuilder.BuildNetworkServer(gameModeType, gameModeConfig, roomSnapshot, useLocalDirectClient: false, localDirectClientId: 0);
            SceneManager.SetActiveScene(serverStageScene);
        }
    }

    private async Task BuildHostedStageFromRoomAsync(
        RoomSnapshotPacket roomSnapshot,
        StageDefinition stageDefinition,
        GameModeType gameModeType,
        GameModeConfig gameModeConfig)
    {
        Scene serverStageScene = await LoadSceneInternalAsync(serverStageSceneName, LoadSceneMode.Single);
        if (!TryFindUniqueBuilder(serverStageScene, out serverStageBuilder))
        {
            return;
        }

        serverStageBuilder.ConfigureStageDefinition(stageDefinition);
        serverStageBuilder.BuildNetworkServer(gameModeType, gameModeConfig, roomSnapshot, useLocalDirectClient: true, localDirectClientId: 0);

        Scene clientStageScene = await LoadSceneInternalAsync(clientStageSceneName, LoadSceneMode.Additive);
        if (!TryFindUniqueBuilder(clientStageScene, out clientStageBuilder))
        {
            return;
        }

        clientStageBuilder.ConfigureStageDefinition(stageDefinition);
        clientStageBuilder.BuildLocalHostClient(
            serverStageBuilder.GamePlayRunner,
            ResolveRoomPlayerProfile(roomSnapshot, 0, currentRoomLaunchRequest.nickname));
        SceneManager.SetActiveScene(clientStageScene);
    }

    private async Task BuildGuestStageFromRoomAsync(StageDefinition stageDefinition)
    {
        Scene clientStageScene = await LoadSceneInternalAsync(clientStageSceneName, LoadSceneMode.Single);
        if (!TryFindUniqueBuilder(clientStageScene, out clientStageBuilder))
        {
            return;
        }

        clientStageBuilder.ConfigureStageDefinition(stageDefinition);
        clientStageBuilder.BuildOnlineGuest();
        SceneManager.SetActiveScene(clientStageScene);
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

        AudioManager.Instance?.PlaySceneBgm(sceneName);
        return SceneManager.GetSceneByName(sceneName);
    }

    private bool ResolveStageSelection(ushort stageIndex, out StageDefinition stageDefinition)
    {
        stageDefinition = null;
        if (gameStageCatalog == null)
        {
            Debug.LogError("[GameFlowManager] GameStageCatalog is not assigned.", this);
            return false;
        }

        if (!gameStageCatalog.TryGetByIndex(stageIndex, out GameStageCatalogEntry entry))
        {
            Debug.LogError($"[GameFlowManager] Stage index is invalid. index={stageIndex}", this);
            return false;
        }

        if (entry.StageDefinition == null)
        {
            Debug.LogError($"[GameFlowManager] StageDefinition is not assigned. index={stageIndex}, name={entry.DisplayName}", this);
            return false;
        }

        stageDefinition = entry.StageDefinition;
        return true;
    }

    private bool ResolveGameModeSelection(
        ushort gameModeIndex,
        out GameModeType gameModeType,
        out GameModeConfig gameModeConfig)
    {
        gameModeType = GameModeType.TimeAttack;
        gameModeConfig = null;

        if (gameModeCatalog == null)
        {
            Debug.LogError("[GameFlowManager] GameModeCatalog is not assigned.", this);
            return false;
        }

        if (!gameModeCatalog.TryGetByIndex(gameModeIndex, out GameModeCatalogEntry entry))
        {
            Debug.LogError($"[GameFlowManager] GameMode index is invalid. index={gameModeIndex}", this);
            return false;
        }

        if (entry.Config == null)
        {
            Debug.LogError($"[GameFlowManager] GameModeConfig is not assigned. index={gameModeIndex}, name={entry.DisplayName}", this);
            return false;
        }

        gameModeType = entry.Config.ModeType;
        gameModeConfig = entry.Config;
        return true;
    }

    private RoomSnapshotPacket ResolveFinalRoomSnapshot(RoomSnapshotPacket roomSnapshot)
    {
        if (gameStageCatalog != null
            && gameStageCatalog.TryGetByIndex(roomSnapshot.stageIndex, out GameStageCatalogEntry stageEntry)
            && stageEntry.IsRandom
            && gameStageCatalog.TryGetRandomResolvedIndex(out ushort resolvedStageIndex))
        {
            roomSnapshot.stageIndex = resolvedStageIndex;
        }

        if (gameModeCatalog != null
            && gameModeCatalog.TryGetByIndex(roomSnapshot.gameModeIndex, out GameModeCatalogEntry modeEntry)
            && modeEntry.IsRandom
            && gameModeCatalog.TryGetRandomResolvedIndex(out ushort resolvedGameModeIndex))
        {
            roomSnapshot.gameModeIndex = resolvedGameModeIndex;
        }

        return roomSnapshot;
    }

    private static GameSessionPlayerProfile ResolveRoomPlayerProfile(
        RoomSnapshotPacket roomSnapshot,
        ulong clientId,
        string requestNickname)
    {
        RoomPlayerStatePacket[] players = roomSnapshot.players;
        if (players != null)
        {
            for (int i = 0; i < roomSnapshot.playerCount && i < players.Length; i++)
            {
                if (players[i].clientId == clientId)
                {
                    return new GameSessionPlayerProfile
                    {
                        clientId = players[i].clientId,
                        nickname = players[i].NicknameText,
                        characterId = players[i].characterId,
                        skillId = players[i].skillId
                    };
                }
            }
        }

        return new GameSessionPlayerProfile
        {
            clientId = clientId,
            nickname = requestNickname,
            characterId = 0,
            skillId = 1
        };
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

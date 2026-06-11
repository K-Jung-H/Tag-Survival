using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public enum GameSessionMode
{
    OnlineGuest,
    PlayerHostedLocal,
    DedicatedServer
}

[System.Serializable]
public struct GameSessionPlayerProfile
{
    public ulong clientId;
    public string nickname;
    public byte characterId;
    public byte skillId;
}

public sealed class GameSessionManager : MonoBehaviour
{
    [Header("Scenes")]
    [SerializeField] private string serverStageSceneName = "Server_Stage";
    [SerializeField] private string clientStageSceneName = "Client_Stage";
    [SerializeField] private bool unloadStartSceneAfterBuild;

    [Header("Local Host Player")]
    [SerializeField] private GameSessionPlayerProfile localHostProfile = new GameSessionPlayerProfile
    {
        clientId = 0,
        nickname = "Host",
        characterId = 0,
        skillId = 1
    };
    [SerializeField] private bool registerLocalHostPlayer = true;

    private static GameSessionManager instance;

    private string startSceneName;
    private bool isBuildingSession;
    private ServerStageBuilder serverStageBuilder;
    private ClientStageBuilder clientStageBuilder;

    public static GameSessionManager Instance => instance;
    public GameSessionMode CurrentMode { get; private set; }
    public ServerStageBuilder ServerStageBuilder => serverStageBuilder;
    public ClientStageBuilder ClientStageBuilder => clientStageBuilder;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        startSceneName = gameObject.scene.name;
        DontDestroyOnLoad(gameObject);
    }

    public void ConfigureLocalHostProfile(
        ulong clientId,
        string nickname,
        byte characterId,
        byte skillId)
    {
        localHostProfile.clientId = clientId;
        localHostProfile.nickname = nickname;
        localHostProfile.characterId = characterId;
        localHostProfile.skillId = skillId;
    }

    public void StartPlayerHostSession()
    {
        _ = StartPlayerHostSessionAsync();
    }

    public void StartGuestSession()
    {
        _ = StartGuestSessionAsync();
    }

    public void StartDedicatedServerSession()
    {
        _ = StartDedicatedServerSessionAsync();
    }

    public async Task StartPlayerHostSessionAsync()
    {
        if (isBuildingSession)
        {
            return;
        }

        isBuildingSession = true;
        CurrentMode = GameSessionMode.PlayerHostedLocal;

        Scene serverScene = await LoadStageSceneAsync(serverStageSceneName);
        if (!TryFindUniqueBuilder(serverScene, out serverStageBuilder))
        {
            isBuildingSession = false;
            return;
        }

        if (!await serverStageBuilder.BuildPlayerHostedServerAsync(localHostProfile, registerLocalHostPlayer))
        {
            isBuildingSession = false;
            return;
        }

        SetStartSceneEventSystemsEnabled(false);
        Scene clientScene = await LoadStageSceneAsync(clientStageSceneName);
        if (!TryFindUniqueBuilder(clientScene, out clientStageBuilder))
        {
            SetStartSceneEventSystemsEnabled(true);
            isBuildingSession = false;
            return;
        }

        if (!clientStageBuilder.BuildLocalHostClient(
            serverStageBuilder.GamePlayRunner,
            localHostProfile,
            serverStageBuilder.CurrentJoinCode))
        {
            SetStartSceneEventSystemsEnabled(true);
            isBuildingSession = false;
            return;
        }

        SceneManager.SetActiveScene(clientScene);
        await UnloadStartSceneIfNeededAsync();
        isBuildingSession = false;
    }

    public async Task StartGuestSessionAsync()
    {
        if (isBuildingSession)
        {
            return;
        }

        isBuildingSession = true;
        CurrentMode = GameSessionMode.OnlineGuest;

        SetStartSceneEventSystemsEnabled(false);
        Scene clientScene = await LoadStageSceneAsync(clientStageSceneName);
        if (!TryFindUniqueBuilder(clientScene, out clientStageBuilder))
        {
            SetStartSceneEventSystemsEnabled(true);
            isBuildingSession = false;
            return;
        }

        if (!clientStageBuilder.BuildOnlineGuest())
        {
            SetStartSceneEventSystemsEnabled(true);
            isBuildingSession = false;
            return;
        }

        SceneManager.SetActiveScene(clientScene);
        await UnloadStartSceneIfNeededAsync();
        isBuildingSession = false;
    }

    public async Task StartDedicatedServerSessionAsync()
    {
        if (isBuildingSession)
        {
            return;
        }

        isBuildingSession = true;
        CurrentMode = GameSessionMode.DedicatedServer;

        Scene serverScene = await LoadStageSceneAsync(serverStageSceneName);
        if (!TryFindUniqueBuilder(serverScene, out serverStageBuilder))
        {
            isBuildingSession = false;
            return;
        }

        if (!await serverStageBuilder.BuildDedicatedServerAsync())
        {
            isBuildingSession = false;
            return;
        }

        SceneManager.SetActiveScene(serverScene);
        await UnloadStartSceneIfNeededAsync();
        isBuildingSession = false;
    }

    private async Task<Scene> LoadStageSceneAsync(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[GameSessionManager] Scene name is empty.", this);
            return default;
        }

        Scene existingScene = SceneManager.GetSceneByName(sceneName);
        if (existingScene.IsValid() && existingScene.isLoaded)
        {
            return existingScene;
        }

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (operation == null)
        {
            Debug.LogError($"[GameSessionManager] Failed to load scene: {sceneName}", this);
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
            Debug.LogError("[GameSessionManager] Loaded scene is invalid.", this);
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
                $"[GameSessionManager] Scene '{scene.name}' must contain exactly one {typeof(T).Name}. Found: {foundCount}",
                this);
            return false;
        }

        builder = foundBuilder;
        return true;
    }

    private async Task UnloadStartSceneIfNeededAsync()
    {
        if (!unloadStartSceneAfterBuild || string.IsNullOrWhiteSpace(startSceneName))
        {
            return;
        }

        Scene startScene = SceneManager.GetSceneByName(startSceneName);
        if (!startScene.IsValid() || !startScene.isLoaded)
        {
            return;
        }

        AsyncOperation operation = SceneManager.UnloadSceneAsync(startScene);
        if (operation == null)
        {
            return;
        }

        while (!operation.isDone)
        {
            await Task.Yield();
        }
    }

    private void SetStartSceneEventSystemsEnabled(bool enabledValue)
    {
        if (string.IsNullOrWhiteSpace(startSceneName))
        {
            return;
        }

        Scene startScene = SceneManager.GetSceneByName(startSceneName);
        if (!startScene.IsValid() || !startScene.isLoaded)
        {
            return;
        }

        GameObject[] roots = startScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            EventSystem[] eventSystems = roots[i].GetComponentsInChildren<EventSystem>(true);
            for (int j = 0; j < eventSystems.Length; j++)
            {
                eventSystems[j].enabled = enabledValue;
            }

            AudioListener[] audioListeners = roots[i].GetComponentsInChildren<AudioListener>(true);
            for (int j = 0; j < audioListeners.Length; j++)
            {
                audioListeners[j].enabled = enabledValue;
            }
        }
    }
}

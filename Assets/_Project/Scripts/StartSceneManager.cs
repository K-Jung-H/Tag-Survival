using UnityEngine;

public class StartSceneManager : MonoBehaviour
{
    public string serverSceneName = "";
    public string clientSceneName = "";
    [SerializeField] private GameFlowManager gameFlowManager;
    [SerializeField] private GameSessionManager gameSessionManager;
    [SerializeField] private GameModeType gameModeType = GameModeType.TimeAttack;
    [SerializeField] private GameModeConfig gameModeConfig;

    public void SelectModeScene()
    {
        if (gameFlowManager == null)
        {
            Debug.LogError("[StartSceneManager] GameFlowManager is not assigned.", this);
            return;
        }

        gameFlowManager.LoadModeSelectScene();
    }

    public void SelectOnlineScene()
    {
        if (gameFlowManager == null)
        {
            Debug.LogError("[StartSceneManager] GameFlowManager is not assigned.", this);
            return;
        }

        gameFlowManager.LoadOnlineScene();
    }

    // - Role: Select server mode.
    public void SelectServer()
    {
        if (gameSessionManager == null)
        {
            Debug.LogError("[StartSceneManager] GameSessionManager is not assigned.", this);
            return;
        }

        gameSessionManager.ConfigureGameMode(gameModeType, gameModeConfig);
        gameSessionManager.StartDedicatedServerSession();
    }

    // - Role: Select client mode.
    public void SelectClient()
    {
        if (gameFlowManager == null)
        {
            Debug.LogError("[StartSceneManager] GameFlowManager is not assigned.", this);
            return;
        }

        _ = gameFlowManager.LoadSceneAsync(clientSceneName);
    }

    public void SelectPlayerHost()
    {
        if (gameSessionManager == null)
        {
            Debug.LogError("[StartSceneManager] GameSessionManager is not assigned.", this);
            return;
        }

        gameSessionManager.ConfigureGameMode(gameModeType, gameModeConfig);
        gameSessionManager.StartPlayerHostSession();
    }

    public void SelectGuest()
    {
        if (gameSessionManager == null)
        {
            Debug.LogError("[StartSceneManager] GameSessionManager is not assigned.", this);
            return;
        }

        gameSessionManager.ConfigureGameMode(gameModeType, gameModeConfig);
        gameSessionManager.StartGuestSession();
    }
}

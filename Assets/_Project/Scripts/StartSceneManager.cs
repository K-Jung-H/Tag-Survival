using UnityEngine;
using UnityEngine.SceneManagement;

public class StartSceneManager : MonoBehaviour
{
    public string serverSceneName = "";
    public string clientSceneName = "";
    [SerializeField] private GameSessionManager gameSessionManager;
    [SerializeField] private GameModeType gameModeType = GameModeType.TimeAttack;
    [SerializeField] private GameModeConfig gameModeConfig;

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
        SceneManager.LoadScene(clientSceneName);
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

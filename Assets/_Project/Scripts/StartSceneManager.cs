using UnityEngine;
using UnityEngine.SceneManagement;

public class StartSceneManager : MonoBehaviour
{
    public string serverSceneName = "";
    public string clientSceneName = "";
    [SerializeField] private GameSessionManager gameSessionManager;

    // - Role: Select server mode.
    public void SelectServer()
    {
        if (gameSessionManager == null)
        {
            Debug.LogError("[StartSceneManager] GameSessionManager is not assigned.", this);
            return;
        }

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

        gameSessionManager.StartPlayerHostSession();
    }

    public void SelectGuest()
    {
        if (gameSessionManager == null)
        {
            Debug.LogError("[StartSceneManager] GameSessionManager is not assigned.", this);
            return;
        }

        gameSessionManager.StartGuestSession();
    }
}

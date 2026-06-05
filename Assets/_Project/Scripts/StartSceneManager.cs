using UnityEngine;
using UnityEngine.SceneManagement;

public class StartSceneManager : MonoBehaviour
{
    public string serverSceneName = "";
    public string clientSceneName = "";

    // - Role: Select server mode.
    public void SelectServer()
    {
        SceneManager.LoadScene(serverSceneName);
    }

    // - Role: Select client mode.
    public void SelectClient()
    {
        SceneManager.LoadScene(clientSceneName);
    }
}

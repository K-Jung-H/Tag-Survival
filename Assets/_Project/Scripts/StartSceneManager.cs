using UnityEngine;
using UnityEngine.SceneManagement;

public class StartSceneManager : MonoBehaviour
{
    public string serverSceneName = "";
    public string clientSceneName = "";

    public void SelectServer()
    {
        SceneManager.LoadScene(serverSceneName);
    }

    public void SelectClient()
    {
        SceneManager.LoadScene(clientSceneName);
    }
}

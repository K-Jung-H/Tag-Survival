using UnityEngine;

public sealed class GameFlowButtonRouter : MonoBehaviour
{
    public void LoadStartScene()
    {
        GameFlowManager manager = GameFlowManager.Instance;
        if (manager == null)
        {
            Debug.LogError("[GameFlowButtonRouter] GameFlowManager is not available.", this);
            return;
        }

        manager.LoadStartScene();
    }

    public void LoadModeSelectScene()
    {
        GameFlowManager manager = GameFlowManager.Instance;
        if (manager == null)
        {
            Debug.LogError("[GameFlowButtonRouter] GameFlowManager is not available.", this);
            return;
        }

        manager.LoadModeSelectScene();
    }

    public void LoadOnlineScene()
    {
        GameFlowManager manager = GameFlowManager.Instance;
        if (manager == null)
        {
            Debug.LogError("[GameFlowButtonRouter] GameFlowManager is not available.", this);
            return;
        }

        manager.LoadOnlineScene();
    }

    public void GoBack()
    {
        GameFlowManager manager = GameFlowManager.Instance;
        if (manager == null)
        {
            Debug.LogError("[GameFlowButtonRouter] GameFlowManager is not available.", this);
            return;
        }

        manager.GoBack();
    }

    public void ReturnToStart()
    {
        GameFlowManager manager = GameFlowManager.Instance;
        if (manager == null)
        {
            Debug.LogError("[GameFlowButtonRouter] GameFlowManager is not available.", this);
            return;
        }

        manager.ReturnToStart();
    }

    public void OpenSettingsPanel()
    {
        GameFlowManager manager = GameFlowManager.Instance;
        if (manager == null)
        {
            Debug.LogError("[GameFlowButtonRouter] GameFlowManager is not available.", this);
            return;
        }

        manager.OpenSettingsPanel();
    }
}

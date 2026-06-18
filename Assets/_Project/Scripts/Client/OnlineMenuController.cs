using TMPro;
using UnityEngine;

public sealed class OnlineMenuController : MonoBehaviour
{
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private TMP_InputField connectServerJoinCodeInput;
    [SerializeField] private TMP_InputField connectRoomJoinCodeInput;

    public void SelectServerMode()
    {
        GameFlowManager manager = ResolveGameFlowManager();
        if (manager == null)
        {
            return;
        }

        manager.StartDedicatedServerRoom();
    }

    public void SelectCreateRoom()
    {
        GameFlowManager manager = ResolveGameFlowManager();
        if (manager == null)
        {
            return;
        }

        manager.StartHostRoom(GetInputText(nicknameInput));
    }

    public void SelectConnectRoom()
    {
        GameFlowManager manager = ResolveGameFlowManager();
        if (manager == null)
        {
            return;
        }

        manager.StartJoinRoom(GetInputText(connectRoomJoinCodeInput), GetInputText(nicknameInput));
    }

    public void SelectConnectServer()
    {
        GameFlowManager manager = ResolveGameFlowManager();
        if (manager == null)
        {
            return;
        }

        manager.StartConnectMatchmakingServer(GetInputText(connectServerJoinCodeInput));
    }

    private GameFlowManager ResolveGameFlowManager()
    {
        GameFlowManager manager = GameFlowManager.Instance;
        if (manager == null)
        {
            Debug.LogError("[OnlineMenuController] GameFlowManager is not available.", this);
        }

        return manager;
    }

    private static string GetInputText(TMP_InputField inputField)
    {
        return inputField != null ? inputField.text : string.Empty;
    }
}

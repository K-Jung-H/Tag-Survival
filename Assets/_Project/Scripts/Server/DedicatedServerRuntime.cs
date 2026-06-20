using UnityEngine;

public sealed class DedicatedServerRuntime : MonoBehaviour
{
    [SerializeField] private Server_RoomDirectory roomDirectory;
    [SerializeField] private Server_RoomDebugHudView debugHudView;
    [SerializeField] private GameObject[] dedicatedPresentationObjects;
    [SerializeField] private int maxPlayers = 10;

    private RoomLaunchRequest launchRequest;

    public bool IsBuilt { get; private set; }
    public RoomLaunchRequest LaunchRequest => launchRequest;
    public Server_RoomDirectory RoomDirectory => roomDirectory;

    public bool Build(RoomLaunchRequest request, Server_RoomManager roomManager)
    {
        if (roomDirectory == null)
        {
            Debug.LogError("[DedicatedServerRuntime] Server_RoomDirectory is not assigned.", this);
            return false;
        }

        if (!roomDirectory.ConfigureSingleRoom(roomManager))
        {
            return false;
        }

        roomManager.ConfigureMaxPlayers(maxPlayers);
        launchRequest = request;
        debugHudView?.Configure(roomDirectory);
        SetDebugHudVisible(true);
        IsBuilt = true;
        return true;
    }

    public void SetDebugHudVisible(bool isVisible)
    {
        debugHudView?.SetVisible(isVisible);
        SetDedicatedPresentationVisible(isVisible);
    }

    private void SetDedicatedPresentationVisible(bool isVisible)
    {
        if (dedicatedPresentationObjects == null)
        {
            return;
        }

        for (int i = 0; i < dedicatedPresentationObjects.Length; i++)
        {
            if (dedicatedPresentationObjects[i] != null)
            {
                dedicatedPresentationObjects[i].SetActive(isVisible);
            }
        }
    }
}

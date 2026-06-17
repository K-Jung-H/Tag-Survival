using UnityEngine;

public sealed class Server_RoomBuilder : MonoBehaviour
{
    [SerializeField] private Server_RoomManager roomManager;

    public bool IsBuilt { get; private set; }
    public RoomLaunchRequest CurrentRequest { get; private set; }
    public Server_RoomManager RoomManager => roomManager;

    public bool BuildDedicatedServerRoom(RoomLaunchRequest request)
    {
        if (!ValidateReferences())
        {
            return false;
        }

        CurrentRequest = request;
        IsBuilt = true;
        Debug.Log("[Server_RoomBuilder] Dedicated server room build placeholder.", this);
        return true;
    }

    public bool BuildHostedRoom(RoomLaunchRequest request)
    {
        if (!ValidateReferences())
        {
            return false;
        }

        CurrentRequest = request;
        roomManager.RegisterPlayer(0, request.nickname);
        IsBuilt = true;
        Debug.Log("[Server_RoomBuilder] Hosted room build placeholder.", this);
        return true;
    }

    private bool ValidateReferences()
    {
        if (roomManager == null)
        {
            Debug.LogError("[Server_RoomBuilder] Server_RoomManager is not assigned.", this);
            return false;
        }

        return true;
    }
}

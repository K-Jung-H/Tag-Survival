using UnityEngine;

public sealed class Server_RoomBuilder : MonoBehaviour
{
    [SerializeField] private Server_RoomManager roomManager;
    [SerializeField] private Server_RoomNetwork roomNetwork;

    public bool IsBuilt { get; private set; }
    public Server_RoomManager RoomManager => roomManager;

    public bool BuildDedicatedServerRoom(RoomLaunchRequest request)
    {
        if (!ValidateReferences())
        {
            return false;
        }

        roomManager.StartRequested -= OnRoomStartRequested;
        roomManager.StartRequested += OnRoomStartRequested;
        if (!roomNetwork.Build(roomManager))
        {
            return false;
        }

        IsBuilt = true;
        Debug.Log("[Server_RoomBuilder] Dedicated server room built.", this);
        return true;
    }

    public bool BuildHostedRoom(RoomLaunchRequest request)
    {
        if (!ValidateReferences())
        {
            return false;
        }

        roomManager.RegisterPlayer(0, request.nickname);
        roomManager.StartRequested -= OnRoomStartRequested;
        roomManager.StartRequested += OnRoomStartRequested;
        if (!roomNetwork.Build(roomManager))
        {
            return false;
        }

        IsBuilt = true;
        Debug.Log("[Server_RoomBuilder] Hosted room built.", this);
        return true;
    }

    private bool ValidateReferences()
    {
        if (roomManager == null)
        {
            Debug.LogError("[Server_RoomBuilder] Server_RoomManager is not assigned.", this);
            return false;
        }

        if (roomNetwork == null)
        {
            Debug.LogError("[Server_RoomBuilder] Server_RoomNetwork is not assigned.", this);
            return false;
        }

        return true;
    }

    private void OnDestroy()
    {
        if (roomManager != null)
        {
            roomManager.StartRequested -= OnRoomStartRequested;
        }
    }

    private void OnRoomStartRequested(RoomSnapshotPacket snapshot)
    {
        GameFlowManager.Instance?.StartStageFromRoom(snapshot);
    }
}

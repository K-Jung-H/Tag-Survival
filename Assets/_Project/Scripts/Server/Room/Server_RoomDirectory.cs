using UnityEngine;

public sealed class Server_RoomDirectory : MonoBehaviour
{
    [SerializeField] private Server_RoomManager singleRoom;
    [SerializeField] private string singleRoomCode = "Room-1";

    public int RoomCount => singleRoom != null ? 1 : 0;
    public Server_RoomManager SingleRoom => singleRoom;
    public string SingleRoomCode => string.IsNullOrWhiteSpace(singleRoomCode) ? "Room-1" : singleRoomCode.Trim();

    public bool ConfigureSingleRoom(Server_RoomManager roomManager)
    {
        if (roomManager == null)
        {
            Debug.LogError("[Server_RoomDirectory] Server_RoomManager is not assigned.", this);
            return false;
        }

        singleRoom = roomManager;
        return true;
    }

    public bool TryAssignPlayerToRoom(ulong clientId, string nickname, out string failReason)
    {
        failReason = string.Empty;
        if (singleRoom == null)
        {
            failReason = "Room Missing";
            return false;
        }

        if (!singleRoom.ContainsPlayer(clientId) && singleRoom.RoomState != RoomState.Waiting)
        {
            failReason = $"Room Not Lobby: {singleRoom.RoomState}";
            return false;
        }

        if (!singleRoom.ContainsPlayer(clientId) && singleRoom.PlayerCount >= singleRoom.MaxPlayers)
        {
            failReason = "Room Full";
            return false;
        }

        if (!singleRoom.RegisterPlayer(clientId, nickname))
        {
            failReason = "Register Failed";
            return false;
        }

        return true;
    }
}

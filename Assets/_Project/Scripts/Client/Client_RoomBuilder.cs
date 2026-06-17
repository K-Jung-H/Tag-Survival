using UnityEngine;

public sealed class Client_RoomBuilder : MonoBehaviour
{
    [SerializeField] private Client_RoomSyncManager syncManager;
    [SerializeField] private Client_RoomView roomView;

    private readonly Client_RoomInputSender inputSender = new();

    public bool IsBuilt { get; private set; }
    public RoomLaunchRequest CurrentRequest { get; private set; }
    public Server_RoomBuilder LocalServerRoomBuilder { get; private set; }

    public bool BuildLocalHostRoom(RoomLaunchRequest request, Server_RoomBuilder serverRoomBuilder)
    {
        if (!ValidateReferences())
        {
            return false;
        }

        if (serverRoomBuilder == null || serverRoomBuilder.RoomManager == null)
        {
            Debug.LogError("[Client_RoomBuilder] Server_RoomBuilder or Server_RoomManager is not provided.", this);
            return false;
        }

        CurrentRequest = request;
        LocalServerRoomBuilder = serverRoomBuilder;
        syncManager.ConfigureLocalServer(serverRoomBuilder.RoomManager, 0);
        inputSender.ConfigureLocalServer(serverRoomBuilder.RoomManager, syncManager);
        roomView?.Configure(syncManager, inputSender, request);
        IsBuilt = true;
        Debug.Log("[Client_RoomBuilder] Local host room build placeholder.", this);
        return true;
    }

    public bool BuildOnlineGuestRoom(RoomLaunchRequest request)
    {
        if (!ValidateReferences())
        {
            return false;
        }

        CurrentRequest = request;
        LocalServerRoomBuilder = null;
        syncManager.ConfigureOnline(0);
        inputSender.ConfigureOnline(syncManager);
        roomView?.Configure(syncManager, inputSender, request);
        IsBuilt = true;
        Debug.Log("[Client_RoomBuilder] Online guest room build placeholder.", this);
        return true;
    }

    private bool ValidateReferences()
    {
        if (syncManager == null)
        {
            Debug.LogError("[Client_RoomBuilder] Client_RoomSyncManager is not assigned.", this);
            return false;
        }

        return true;
    }
}

public sealed class Client_RoomInputSender
{
    private Client_RoomSyncManager syncManager;
    private Server_RoomManager localServerRoomManager;
    private byte selectedCharacterId;
    private byte selectedSkillId;

    public void ConfigureLocalServer(Server_RoomManager serverRoomManager, Client_RoomSyncManager roomSyncManager)
    {
        localServerRoomManager = serverRoomManager;
        syncManager = roomSyncManager;
        ResolveLocalSelectionFromSnapshot();
    }

    public void ConfigureOnline(Client_RoomSyncManager roomSyncManager)
    {
        localServerRoomManager = null;
        syncManager = roomSyncManager;
        ResolveLocalSelectionFromSnapshot();
    }

    public void SelectCharacter(int characterId)
    {
        selectedCharacterId = (byte)Mathf.Clamp(characterId, byte.MinValue, byte.MaxValue);
        SendSelection();
    }

    public void SelectSkill(int skillId)
    {
        selectedSkillId = (byte)Mathf.Clamp(skillId, byte.MinValue, byte.MaxValue);
        SendSelection();
    }

    public void SetReady(bool isReady)
    {
        if (syncManager == null)
        {
            return;
        }

        if (localServerRoomManager != null)
        {
            localServerRoomManager.TrySetReady(syncManager.LocalClientId, isReady);
            return;
        }

        Debug.Log("[Client_RoomInputSender] Online ready request is not implemented yet.");
    }

    public void ToggleReady()
    {
        if (!TryGetLocalPlayer(out RoomPlayerStatePacket localPlayer))
        {
            return;
        }

        SetReady(!localPlayer.isReady);
    }

    private void SendSelection()
    {
        if (syncManager == null)
        {
            return;
        }

        if (localServerRoomManager != null)
        {
            localServerRoomManager.TrySetSelection(syncManager.LocalClientId, selectedCharacterId, selectedSkillId);
            return;
        }

        Debug.Log("[Client_RoomInputSender] Online selection request is not implemented yet.");
    }

    private void ResolveLocalSelectionFromSnapshot()
    {
        if (TryGetLocalPlayer(out RoomPlayerStatePacket localPlayer))
        {
            selectedCharacterId = localPlayer.characterId;
            selectedSkillId = localPlayer.skillId;
        }
    }

    private bool TryGetLocalPlayer(out RoomPlayerStatePacket localPlayer)
    {
        localPlayer = default;
        if (syncManager == null)
        {
            return false;
        }

        RoomSnapshotPacket snapshot = syncManager.CurrentSnapshot;
        RoomPlayerStatePacket[] players = snapshot.players;
        if (players == null)
        {
            return false;
        }

        for (int i = 0; i < snapshot.playerCount && i < players.Length; i++)
        {
            if (players[i].clientId == syncManager.LocalClientId)
            {
                localPlayer = players[i];
                return true;
            }
        }

        return false;
    }
}

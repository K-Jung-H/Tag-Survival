using UnityEngine;

public sealed class Client_RoomBuilder : MonoBehaviour
{
    [SerializeField] private Client_RoomSyncManager syncManager;
    [SerializeField] private Client_RoomNetwork roomNetwork;
    [SerializeField] private Client_RoomView roomView;

    private readonly Client_RoomInputSender inputSender = new();

    public bool IsBuilt { get; private set; }

    public bool BuildLocalHostRoom(RoomLaunchRequest request, Server_RoomBuilder serverRoomBuilder)
    {
        if (!ValidateReferences(requireRoomNetwork: false))
        {
            return false;
        }

        if (serverRoomBuilder == null || serverRoomBuilder.RoomManager == null)
        {
            Debug.LogError("[Client_RoomBuilder] Server_RoomBuilder or Server_RoomManager is not provided.", this);
            return false;
        }

        syncManager.ConfigureLocalServer(serverRoomBuilder.RoomManager, 0);
        inputSender.ConfigureLocalServer(serverRoomBuilder.RoomManager, syncManager);
        syncManager.StartRequested -= OnRoomStartRequested;
        syncManager.StartRequested += OnRoomStartRequested;
        roomView?.Configure(syncManager, inputSender, request);
        IsBuilt = true;
        Debug.Log("[Client_RoomBuilder] Local host room built.", this);
        return true;
    }

    public bool BuildOnlineGuestRoom(RoomLaunchRequest request)
    {
        if (!ValidateReferences(requireRoomNetwork: true))
        {
            return false;
        }

        ulong localClientId = NetworkSessionManager.Instance != null
            ? NetworkSessionManager.Instance.LocalClientId
            : 0;
        syncManager.ConfigureOnline(localClientId);
        if (!roomNetwork.Build(syncManager, request))
        {
            return false;
        }

        inputSender.ConfigureOnline(syncManager, roomNetwork);
        syncManager.StartRequested -= OnRoomStartRequested;
        syncManager.StartRequested += OnRoomStartRequested;
        roomView?.Configure(syncManager, inputSender, request);
        IsBuilt = true;
        Debug.Log("[Client_RoomBuilder] Online guest room built.", this);
        return true;
    }

    private bool ValidateReferences(bool requireRoomNetwork)
    {
        if (syncManager == null)
        {
            Debug.LogError("[Client_RoomBuilder] Client_RoomSyncManager is not assigned.", this);
            return false;
        }

        if (requireRoomNetwork && roomNetwork == null)
        {
            Debug.LogError("[Client_RoomBuilder] Client_RoomNetwork is not assigned.", this);
            return false;
        }

        return true;
    }

    private void OnDestroy()
    {
        if (syncManager != null)
        {
            syncManager.StartRequested -= OnRoomStartRequested;
        }
    }

    private void OnRoomStartRequested(RoomSnapshotPacket snapshot)
    {
        GameFlowManager.Instance?.StartStageFromRoom(snapshot);
    }
}

public sealed class Client_RoomInputSender
{
    private Client_RoomSyncManager syncManager;
    private Server_RoomManager localServerRoomManager;
    private Client_RoomNetwork roomNetwork;
    private byte selectedCharacterId;
    private byte selectedSkillId;

    public void ConfigureLocalServer(Server_RoomManager serverRoomManager, Client_RoomSyncManager roomSyncManager)
    {
        localServerRoomManager = serverRoomManager;
        roomNetwork = null;
        syncManager = roomSyncManager;
        ResolveLocalSelectionFromSnapshot();
    }

    public void ConfigureOnline(Client_RoomSyncManager roomSyncManager, Client_RoomNetwork clientRoomNetwork)
    {
        localServerRoomManager = null;
        roomNetwork = clientRoomNetwork;
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

    public void SelectStage(ushort stageIndex)
    {
        if (syncManager == null)
        {
            return;
        }

        if (localServerRoomManager != null)
        {
            localServerRoomManager.TrySetStageIndex(syncManager.LocalClientId, stageIndex);
            return;
        }

        if (roomNetwork != null)
        {
            roomNetwork.SendSettings(stageIndex, GetCurrentGameModeIndex());
        }
    }

    public void SelectGameMode(ushort gameModeIndex)
    {
        if (syncManager == null)
        {
            return;
        }

        if (localServerRoomManager != null)
        {
            localServerRoomManager.TrySetGameModeIndex(syncManager.LocalClientId, gameModeIndex);
            return;
        }

        if (roomNetwork != null)
        {
            roomNetwork.SendSettings(GetCurrentStageIndex(), gameModeIndex);
        }
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

        if (roomNetwork != null)
        {
            roomNetwork.SendReady(isReady);
        }
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

        if (roomNetwork != null)
        {
            roomNetwork.SendSelection(selectedCharacterId, selectedSkillId);
        }
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

    private ushort GetCurrentStageIndex()
    {
        if (syncManager == null || syncManager.CurrentSnapshot.protocolVersion != RoomNetProtocol.ProtocolVersion)
        {
            return 0;
        }

        return syncManager.CurrentSnapshot.stageIndex;
    }

    private ushort GetCurrentGameModeIndex()
    {
        if (syncManager == null || syncManager.CurrentSnapshot.protocolVersion != RoomNetProtocol.ProtocolVersion)
        {
            return 0;
        }

        return syncManager.CurrentSnapshot.gameModeIndex;
    }
}

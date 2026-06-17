using System;
using UnityEngine;

public enum ClientRoomSyncMode
{
    None = 0,
    LocalServer = 1,
    Online = 2
}

public sealed class Client_RoomSyncManager : MonoBehaviour
{
    [SerializeField] private ClientRoomSyncMode syncMode = ClientRoomSyncMode.None;

    private Server_RoomManager localServerRoomManager;
    private RoomSnapshotPacket currentSnapshot;
    private uint lastRoomSeq;
    private ulong localClientId;

    public event Action<RoomSnapshotPacket> SnapshotChanged;

    public ClientRoomSyncMode SyncMode => syncMode;
    public ulong LocalClientId => localClientId;
    public RoomSnapshotPacket CurrentSnapshot => currentSnapshot;

    public void ConfigureLocalServer(Server_RoomManager serverRoomManager, ulong clientId)
    {
        localServerRoomManager = serverRoomManager;
        localClientId = clientId;
        syncMode = ClientRoomSyncMode.LocalServer;
        lastRoomSeq = 0;
        PollLocalServerSnapshot(force: true);
    }

    public void ConfigureOnline(ulong clientId)
    {
        localServerRoomManager = null;
        localClientId = clientId;
        syncMode = ClientRoomSyncMode.Online;
        lastRoomSeq = 0;
        currentSnapshot = default;
    }

    public void ApplyOnlineSnapshot(RoomSnapshotPacket snapshot)
    {
        if (syncMode != ClientRoomSyncMode.Online)
        {
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void Update()
    {
        if (syncMode == ClientRoomSyncMode.LocalServer)
        {
            PollLocalServerSnapshot(force: false);
        }
    }

    private void PollLocalServerSnapshot(bool force)
    {
        if (localServerRoomManager == null)
        {
            return;
        }

        RoomSnapshotPacket snapshot = localServerRoomManager.CreateSnapshot();
        if (!force && snapshot.roomSeq == lastRoomSeq)
        {
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(RoomSnapshotPacket snapshot)
    {
        currentSnapshot = snapshot;
        lastRoomSeq = snapshot.roomSeq;
        SnapshotChanged?.Invoke(currentSnapshot);
    }
}

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public sealed class Client_RosterReceiver : MonoBehaviour
{
    private readonly Dictionary<ulong, RosterEntryPacket> rosterEntries = new();

    private bool isRegistered;
    private bool hasRoster;
    private uint lastRosterSeq;

    public event Action RosterUpdated;

    public bool HasRoster => hasRoster;

    private void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[Client_RosterReceiver] NetworkManager.Singleton is null.", this);
            enabled = false;
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void Update()
    {
        TryRegisterRosterHandler();
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

        UnregisterRosterHandler();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        TryRegisterRosterHandler();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId && clientId != NetworkManager.ServerClientId)
            return;

        rosterEntries.Clear();
        hasRoster = false;
        lastRosterSeq = 0;
        RosterUpdated?.Invoke();

        UnregisterRosterHandler();
    }

    public bool TryGetEntry(ulong clientId, out RosterEntryPacket entry)
    {
        return rosterEntries.TryGetValue(clientId, out entry);
    }

    public bool TryGetNickname(ulong clientId, out string nickname)
    {
        nickname = null;

        if (!rosterEntries.TryGetValue(clientId, out RosterEntryPacket entry))
        {
            return false;
        }

        nickname = entry.NicknameText;
        return !string.IsNullOrWhiteSpace(nickname);
    }

    private void TryRegisterRosterHandler()
    {
        if (isRegistered)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsClient)
            return;

        if (!NetworkManager.Singleton.IsConnectedClient)
            return;

        if (NetworkManager.Singleton.IsServer)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
            GameNetMessages.ServerRoster,
            OnServerRosterReceived
        );

        isRegistered = true;
    }

    private void UnregisterRosterHandler()
    {
        if (!isRegistered)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(
            GameNetMessages.ServerRoster
        );

        isRegistered = false;
    }

    private void OnServerRosterReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanReceiveRoster())
            return;

        if (!ServerRosterSnapshotPacket.TryRead(ref reader, out ServerRosterSnapshotPacket packet))
            return;

        if (hasRoster && !IsNewerSequence(packet.rosterSeq, lastRosterSeq))
            return;

        rosterEntries.Clear();

        int count = packet.entries != null
            ? Mathf.Min(packet.entryCount, packet.entries.Length)
            : 0;
        for (int i = 0; i < count; i++)
        {
            rosterEntries[packet.entries[i].clientId] = packet.entries[i];
        }

        hasRoster = true;
        lastRosterSeq = packet.rosterSeq;
        RosterUpdated?.Invoke();
    }

    private bool CanReceiveRoster()
    {
        if (NetworkManager.Singleton == null)
            return false;

        if (!NetworkManager.Singleton.IsClient)
            return false;

        if (!NetworkManager.Singleton.IsConnectedClient)
            return false;

        return !NetworkManager.Singleton.IsServer;
    }

    private static bool IsNewerSequence(uint incomingSeq, uint currentSeq)
    {
        return unchecked((int)(incomingSeq - currentSeq)) > 0;
    }
}

using System;
using UnityEngine;

public sealed class ClientStageBuilder : MonoBehaviour
{
    [Header("Stage Roots")]
    [SerializeField] private GameObject networkRuntimeRoot;

    [Header("Client Components")]
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private ClientCanvasPanelController canvasPanelController;
    [SerializeField] private LocalClient_InputBridge localInputBridge;
    [SerializeField] private Client_NetworkReceiverHub networkReceiverHub;
    [SerializeField] private Client_InputSender networkInputSender;
    [SerializeField] private Relay_ClientBootstrap relayClientBootstrap;

    public event Action<ClientStageBuilder> BuildCompleted;
    public event Action<ClientStageBuilder, string> BuildFailed;

    public bool IsBuilt { get; private set; }

    public bool BuildLocalHostClient(
        Server_GamePlayRunner serverRunner,
        GameSessionPlayerProfile localPlayer,
        string joinCode)
    {
        IsBuilt = false;

        if (!ValidateLocalHostReferences(serverRunner))
        {
            return false;
        }

        SetGameObjectActive(networkRuntimeRoot, false);
        canvasPanelController.ApplyMode(ClientStageUiMode.LocalHost);

        syncManager.ConfigureLocalServer(serverRunner, localPlayer.clientId);
        localInputBridge.Configure(serverRunner, localPlayer.clientId);
        relayClientBootstrap?.ConfigureInactiveClientMode(
            showHud: true,
            joinCode: joinCode,
            status: "Local Host");

        SetBehaviourEnabled(syncManager, true);
        SetBehaviourEnabled(localInputBridge, true);
        SetBehaviourEnabled(networkReceiverHub, false);
        SetBehaviourEnabled(networkInputSender, false);
        SetBehaviourEnabled(relayClientBootstrap, true);

        IsBuilt = true;
        BuildCompleted?.Invoke(this);
        return true;
    }

    public bool BuildOnlineGuest()
    {
        IsBuilt = false;

        if (!ValidateOnlineGuestReferences())
        {
            return false;
        }

        SetGameObjectActive(networkRuntimeRoot, true);
        canvasPanelController.ApplyMode(ClientStageUiMode.OnlineGuest);

        syncManager.ConfigureOnline();

        SetBehaviourEnabled(syncManager, true);
        SetBehaviourEnabled(localInputBridge, false);
        SetBehaviourEnabled(networkReceiverHub, true);
        SetBehaviourEnabled(networkInputSender, true);
        SetBehaviourEnabled(relayClientBootstrap, true);

        IsBuilt = true;
        BuildCompleted?.Invoke(this);
        return true;
    }

    private bool ValidateLocalHostReferences(Server_GamePlayRunner serverRunner)
    {
        if (serverRunner == null)
        {
            Fail("Server_GamePlayRunner is not provided.");
            return false;
        }

        if (serverRunner.GamePlay == null)
        {
            Fail("Server_GamePlayRunner.GamePlay is not ready.");
            return false;
        }

        if (syncManager == null)
        {
            Fail("Client_SyncManager is not assigned.");
            return false;
        }

        if (!ValidateStageReferences())
        {
            return false;
        }

        if (localInputBridge == null)
        {
            Fail("LocalClient_InputBridge is not assigned.");
            return false;
        }

        return true;
    }

    private bool ValidateOnlineGuestReferences()
    {
        if (syncManager == null)
        {
            Fail("Client_SyncManager is not assigned.");
            return false;
        }

        if (!ValidateStageReferences())
        {
            return false;
        }

        if (networkReceiverHub == null)
        {
            Fail("Client_NetworkReceiverHub is not assigned.");
            return false;
        }

        if (networkInputSender == null)
        {
            Fail("Client_InputSender is not assigned.");
            return false;
        }

        if (relayClientBootstrap == null)
        {
            Fail("Relay_ClientBootstrap is not assigned.");
            return false;
        }

        return true;
    }

    private bool ValidateStageReferences()
    {
        if (networkRuntimeRoot == null)
        {
            Fail("Network runtime root is not assigned.");
            return false;
        }

        if (canvasPanelController == null)
        {
            Fail("ClientCanvasPanelController is not assigned.");
            return false;
        }

        return true;
    }

    private static void SetGameObjectActive(GameObject target, bool active)
    {
        if (target == null)
        {
            return;
        }

        target.SetActive(active);
    }

    private static void SetBehaviourEnabled(Behaviour behaviour, bool enabledValue)
    {
        if (behaviour == null)
        {
            return;
        }

        behaviour.enabled = enabledValue;
    }

    private void Fail(string reason)
    {
        Debug.LogError($"[ClientStageBuilder] {reason}", this);
        BuildFailed?.Invoke(this, reason);
    }
}

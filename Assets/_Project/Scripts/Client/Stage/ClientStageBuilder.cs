using System;
using UnityEngine;

public sealed class ClientStageBuilder : MonoBehaviour
{
    [Header("Stage Presentation")]
    [SerializeField] private StageDefinition stageDefinition;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Client_StageRenderer stageRenderer;
    [SerializeField] private Client_CameraController cameraController;
    [SerializeField] private OffScreenIndicatorView indicatorView;
    [SerializeField] private Client_WorldView worldView;

    [Header("Client Components")]
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private ClientCanvasPanelController canvasPanelController;
    [SerializeField] private LocalClient_InputBridge localInputBridge;
    [SerializeField] private Client_NetworkReceiverHub networkReceiverHub;
    [SerializeField] private Client_InputSender networkInputSender;
    [SerializeField] private Client_StageReadyReporter stageReadyReporter;
    [SerializeField] private Client_StageIntroDirector stageIntroDirector;

    public event Action<ClientStageBuilder> BuildCompleted;
    public event Action<ClientStageBuilder, string> BuildFailed;

    public bool IsBuilt { get; private set; }

    public void ConfigureStageDefinition(StageDefinition newStageDefinition)
    {
        if (newStageDefinition != null)
        {
            stageDefinition = newStageDefinition;
        }
    }

    public bool BuildLocalHostClient(
        Server_GamePlayRunner serverRunner,
        GameSessionPlayerProfile localPlayer)
    {
        IsBuilt = false;

        if (!ValidateLocalHostReferences(serverRunner))
        {
            return false;
        }

        canvasPanelController.ApplyMode(ClientStageUiMode.LocalHost);
        ApplyStagePresentationReferences();

        syncManager.ConfigureLocalServer(serverRunner, localPlayer.clientId);
        networkReceiverHub?.ResetOnlineMessageSession();
        localInputBridge.Configure(serverRunner, localPlayer.clientId);

        SetBehaviourEnabled(syncManager, true);
        SetBehaviourEnabled(localInputBridge, true);
        SetBehaviourEnabled(networkReceiverHub, false);
        SetBehaviourEnabled(networkInputSender, false);
        stageReadyReporter.ResetReadyState();
        SetBehaviourEnabled(stageReadyReporter, true);
        SetBehaviourEnabled(stageIntroDirector, true);

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

        canvasPanelController.ApplyMode(ClientStageUiMode.OnlineGuest);
        ApplyStagePresentationReferences();

        syncManager.ConfigureOnline();
        networkReceiverHub.ResetOnlineMessageSession();

        SetBehaviourEnabled(syncManager, true);
        SetBehaviourEnabled(localInputBridge, false);
        SetBehaviourEnabled(networkReceiverHub, true);
        SetBehaviourEnabled(networkInputSender, true);
        stageReadyReporter.ResetReadyState();
        SetBehaviourEnabled(stageReadyReporter, true);
        SetBehaviourEnabled(stageIntroDirector, true);

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

        return true;
    }

    private bool ValidateStageReferences()
    {
        if (canvasPanelController == null)
        {
            Fail("ClientCanvasPanelController is not assigned.");
            return false;
        }

        if (!canvasPanelController.HasRequiredReferences(out string missingCanvasReference))
        {
            Fail($"ClientCanvasPanelController missing reference: {missingCanvasReference}.");
            return false;
        }

        if (stageDefinition == null)
        {
            Fail("StageDefinition is not assigned.");
            return false;
        }

        if (mainCamera == null)
        {
            Fail("MainCamera is not assigned.");
            return false;
        }

        if (stageRenderer == null)
        {
            Fail("Client_StageRenderer is not assigned.");
            return false;
        }

        if (cameraController == null)
        {
            Fail("Client_CameraController is not assigned.");
            return false;
        }

        if (worldView == null)
        {
            Fail("Client_WorldView is not assigned.");
            return false;
        }

        if (stageReadyReporter == null)
        {
            Fail("Client_StageReadyReporter is not assigned.");
            return false;
        }

        if (stageIntroDirector == null)
        {
            Fail("Client_StageIntroDirector is not assigned.");
            return false;
        }

        if (!stageIntroDirector.HasRequiredReferences(out string missingIntroReference))
        {
            Fail($"Client_StageIntroDirector missing reference: {missingIntroReference}.");
            return false;
        }

        return true;
    }

    // - Role: Pass stage presentation references to client components.
    private void ApplyStagePresentationReferences()
    {
        stageRenderer.Configure(stageDefinition, mainCamera);
        cameraController.BindCamera(mainCamera);
        cameraController.BindSyncManager(syncManager);
        cameraController.StageDefinition = stageDefinition;
        indicatorView?.BindCamera(mainCamera);
        worldView.BindAudioListener(mainCamera.transform);
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

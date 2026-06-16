using System.Collections.Generic;
using UnityEngine;

public sealed class Client_FeedbackRouter : MonoBehaviour
{
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private Client_WorldView worldView;
    [SerializeField] private Client_WorldFeedbackPlayer worldFeedbackPlayer;
    [SerializeField] private Client_ScreenOverlayFeedbackPlayer screenOverlayFeedbackPlayer;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private GameFeedbackCatalog feedbackCatalog;

    private readonly Dictionary<ulong, LocomotionState> previousLocomotionStates = new();
    private readonly List<ulong> removedClientIds = new();
    private readonly HashSet<ulong> activeClientIds = new();
    private readonly HashSet<ServerFeedbackType> warnedMissingServerProfiles = new();
    private readonly HashSet<ClientFeedbackType> warnedMissingClientProfiles = new();
    private readonly HashSet<ScreenOverlayFeedbackType> warnedMissingScreenOverlayProfiles = new();

    private bool hasLocalStunnedState;
    private bool previousLocalStunnedState;
    private Vector2 localStunnedScreenUv = new Vector2(0.5f, 0.5f);

    private void OnEnable()
    {
        ClearWarningCache();

        if (syncManager != null)
        {
            syncManager.GameEventReceived += OnGameEventReceived;
        }
    }

    private void OnDisable()
    {
        if (syncManager != null)
        {
            syncManager.GameEventReceived -= OnGameEventReceived;
        }
    }

    private void LateUpdate()
    {
        if (syncManager == null || !syncManager.IsReadyForView)
        {
            if (hasLocalStunnedState && previousLocalStunnedState)
            {
                SetLocalStunnedOverlay(false, localStunnedScreenUv);
            }

            previousLocomotionStates.Clear();
            hasLocalStunnedState = false;
            return;
        }

        RouteSnapshotFeedback();
    }

    // - Role: Handle synchronized server feedback event.
    private void OnGameEventReceived(GameEventEntryPacket gameEvent)
    {
        if (gameEvent.eventType != GameEventType.Feedback)
        {
            return;
        }

        RouteServerFeedback(gameEvent);
    }

    // - Role: Route server event feedback.
    private void RouteServerFeedback(GameEventEntryPacket gameEvent)
    {
        if (feedbackCatalog == null || !feedbackCatalog.TryGet(gameEvent.feedbackType, out ServerFeedbackProfile profile))
        {
            WarnMissingServerProfile(gameEvent.feedbackType);
            return;
        }

        if (TryRouteServerFeedbackToPlayerAudio(profile, gameEvent))
        {
            return;
        }

        if (!TryResolveServerFeedbackPosition(profile, gameEvent, out Vector2 position, out Transform followTarget))
        {
            position = gameEvent.position;
        }

        worldFeedbackPlayer?.Play(profile.data, position, gameEvent.rotation, followTarget);
    }

    // - Role: Route server feedback that should be played on player audio.
    private bool TryRouteServerFeedbackToPlayerAudio(ServerFeedbackProfile profile, GameEventEntryPacket gameEvent)
    {
        if (profile.type != ServerFeedbackType.PortalTeleport)
        {
            return false;
        }

        worldView?.TryPlayPlayerFeedback(gameEvent.targetClientId, profile.data);
        return true;
    }

    // - Role: Route snapshot derived feedback.
    private void RouteSnapshotFeedback()
    {
        activeClientIds.Clear();
        foreach (var pair in syncManager.Snapshots)
        {
            ulong clientId = pair.Key;
            ClientSnapshotState snapshot = pair.Value;
            activeClientIds.Add(clientId);

            if (previousLocomotionStates.TryGetValue(clientId, out LocomotionState previousState)
                && snapshot.locomotionState != previousState)
            {
                if (snapshot.locomotionState == LocomotionState.BlinkEnter)
                {
                    RouteClientFeedback(ClientFeedbackType.BlinkEnter, clientId, snapshot.position);
                }
                else if (snapshot.locomotionState == LocomotionState.BlinkExit)
                {
                    RouteClientFeedback(ClientFeedbackType.BlinkExit, clientId, snapshot.position);
                }
            }

            previousLocomotionStates[clientId] = snapshot.locomotionState;
        }

        RemoveMissingPlayerStateCache();
        RouteLocalStunnedOverlay();
    }

    // - Role: Route client-local feedback.
    private void RouteClientFeedback(ClientFeedbackType feedbackType, ulong clientId, Vector2 fallbackPosition)
    {
        if (feedbackCatalog == null || !feedbackCatalog.TryGet(feedbackType, out ClientFeedbackProfile profile))
        {
            WarnMissingClientProfile(feedbackType);
            return;
        }

        if (!TryResolveClientFeedbackPosition(profile, clientId, fallbackPosition, out Vector2 position, out Transform followTarget))
        {
            position = fallbackPosition;
        }

        worldFeedbackPlayer?.Play(profile.data, position, 0f, followTarget);
    }

    // - Role: Route local stunned screen overlay.
    private void RouteLocalStunnedOverlay()
    {
        if (!syncManager.TryGetSnapshot(syncManager.LocalClientId, out ClientSnapshotState snapshot))
        {
            if (hasLocalStunnedState && previousLocalStunnedState)
            {
                SetLocalStunnedOverlay(false, localStunnedScreenUv);
            }

            hasLocalStunnedState = false;
            return;
        }

        bool isStunned = snapshot.isTagger && snapshot.locomotionState == LocomotionState.Stunned;
        localStunnedScreenUv = ResolveScreenUv(snapshot.position);

        if (!hasLocalStunnedState)
        {
            previousLocalStunnedState = isStunned;
            hasLocalStunnedState = true;
            if (isStunned)
            {
                SetLocalStunnedOverlay(true, localStunnedScreenUv);
            }

            return;
        }

        if (isStunned == previousLocalStunnedState)
        {
            if (isStunned)
            {
                UpdateLocalStunnedOverlay(localStunnedScreenUv);
            }

            return;
        }

        SetLocalStunnedOverlay(isStunned, localStunnedScreenUv);
        previousLocalStunnedState = isStunned;
    }

    // - Role: Set local stunned overlay state.
    private void SetLocalStunnedOverlay(bool active, Vector2 centerUv)
    {
        if (feedbackCatalog == null
            || !feedbackCatalog.TryGet(ScreenOverlayFeedbackType.TaggerStunned, out ScreenOverlayFeedbackProfile profile))
        {
            WarnMissingScreenOverlayProfile(ScreenOverlayFeedbackType.TaggerStunned);
            return;
        }

        screenOverlayFeedbackPlayer?.SetActive(profile, active, centerUv);
    }

    // - Role: Update local stunned overlay shader values.
    private void UpdateLocalStunnedOverlay(Vector2 centerUv)
    {
        screenOverlayFeedbackPlayer?.UpdateOverlay(
            ScreenOverlayFeedbackType.TaggerStunned,
            centerUv);
    }

    // - Role: Resolve a world position to screen UV.
    private Vector2 ResolveScreenUv(Vector2 worldPosition)
    {
        if (targetCamera == null)
        {
            return new Vector2(0.5f, 0.5f);
        }

        Vector3 screenPosition = targetCamera.WorldToScreenPoint(worldPosition);
        float screenWidth = Mathf.Max(1f, Screen.width);
        float screenHeight = Mathf.Max(1f, Screen.height);
        return new Vector2(
            Mathf.Clamp01(screenPosition.x / screenWidth),
            Mathf.Clamp01(screenPosition.y / screenHeight));
    }

    // - Role: Remove state cache for missing players.
    private void RemoveMissingPlayerStateCache()
    {
        removedClientIds.Clear();
        foreach (var pair in previousLocomotionStates)
        {
            if (!activeClientIds.Contains(pair.Key))
            {
                removedClientIds.Add(pair.Key);
            }
        }

        for (int i = 0; i < removedClientIds.Count; i++)
        {
            previousLocomotionStates.Remove(removedClientIds[i]);
        }
    }

    // - Role: Resolve server feedback position.
    private bool TryResolveServerFeedbackPosition(
        ServerFeedbackProfile profile,
        GameEventEntryPacket gameEvent,
        out Vector2 position,
        out Transform followTarget)
    {
        position = gameEvent.position;
        return TryResolvePosition(profile.spawnMode, gameEvent.subjectClientId, gameEvent.targetClientId, position, out position, out followTarget);
    }

    // - Role: Resolve client feedback position.
    private bool TryResolveClientFeedbackPosition(
        ClientFeedbackProfile profile,
        ulong clientId,
        Vector2 fallbackPosition,
        out Vector2 position,
        out Transform followTarget)
    {
        return TryResolvePosition(profile.spawnMode, clientId, clientId, fallbackPosition, out position, out followTarget);
    }

    // - Role: Resolve feedback position by spawn mode.
    private bool TryResolvePosition(
        GameFeedbackSpawnMode spawnMode,
        ulong subjectClientId,
        ulong targetClientId,
        Vector2 fallbackPosition,
        out Vector2 position,
        out Transform followTarget)
    {
        position = fallbackPosition;
        followTarget = null;

        ulong clientId = spawnMode switch
        {
            GameFeedbackSpawnMode.SubjectPlayer => subjectClientId,
            GameFeedbackSpawnMode.TargetPlayer => targetClientId,
            GameFeedbackSpawnMode.LocalPlayer => syncManager != null ? syncManager.LocalClientId : ulong.MaxValue,
            _ => ulong.MaxValue
        };

        if (clientId == ulong.MaxValue)
        {
            return spawnMode == GameFeedbackSpawnMode.EventPosition;
        }

        if (worldView != null && worldView.TryGetPlayerViewRoot(clientId, out followTarget) && followTarget != null)
        {
            position = followTarget.position;
            return true;
        }

        if (syncManager != null && syncManager.TryGetSnapshot(clientId, out ClientSnapshotState snapshot))
        {
            position = snapshot.position;
            return true;
        }

        return false;
    }

    private void ClearWarningCache()
    {
        warnedMissingServerProfiles.Clear();
        warnedMissingClientProfiles.Clear();
        warnedMissingScreenOverlayProfiles.Clear();
    }

    private void WarnMissingServerProfile(ServerFeedbackType feedbackType)
    {
        if (warnedMissingServerProfiles.Add(feedbackType))
        {
            Debug.LogWarning($"[Client_FeedbackRouter] Server feedback profile is missing for {feedbackType}.", this);
        }
    }

    private void WarnMissingClientProfile(ClientFeedbackType feedbackType)
    {
        if (warnedMissingClientProfiles.Add(feedbackType))
        {
            Debug.LogWarning($"[Client_FeedbackRouter] Client feedback profile is missing for {feedbackType}.", this);
        }
    }

    private void WarnMissingScreenOverlayProfile(ScreenOverlayFeedbackType feedbackType)
    {
        if (warnedMissingScreenOverlayProfiles.Add(feedbackType))
        {
            Debug.LogWarning($"[Client_FeedbackRouter] Screen overlay feedback profile is missing for {feedbackType}.", this);
        }
    }
}

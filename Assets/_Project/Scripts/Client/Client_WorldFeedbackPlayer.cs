using System.Collections.Generic;
using UnityEngine;

public sealed class Client_WorldFeedbackPlayer : MonoBehaviour
{
    private const float DefaultFeedbackLifetimeSeconds = 2f;

    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private Client_WorldView worldView;
    [SerializeField] private Transform feedbackRoot;
    [SerializeField] private AudioSource audioSourcePrefab;
    [SerializeField] private GameFeedbackCatalog feedbackCatalog;

    private readonly HashSet<ServerFeedbackType> warnedMissingServerProfiles = new();
    private readonly HashSet<ClientFeedbackType> warnedMissingClientProfiles = new();
    private readonly Dictionary<ulong, LocomotionState> previousLocomotionStates = new();
    private readonly List<ulong> removedClientIds = new();
    private readonly HashSet<ulong> activeClientIds = new();

    private bool hasLocalStunnedState;
    private bool previousLocalStunnedState;
    private bool warnedMissingAudioSourcePrefab;

    private void Awake()
    {
        ClearWarningCache();
    }

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
            previousLocomotionStates.Clear();
            hasLocalStunnedState = false;
            return;
        }

        PlayPlayerStateFeedback();
    }

    private void OnGameEventReceived(GameEventEntryPacket gameEvent)
    {
        if (gameEvent.eventType == GameEventType.Feedback)
        {
            PlayServerFeedback(gameEvent.feedbackType, gameEvent);
        }
    }

    private void PlayPlayerStateFeedback()
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
                    PlayClientFeedback(ClientFeedbackType.BlinkEnter, snapshot.position);
                }
                else if (snapshot.locomotionState == LocomotionState.BlinkExit)
                {
                    PlayClientFeedback(ClientFeedbackType.BlinkExit, snapshot.position);
                }
            }

            previousLocomotionStates[clientId] = snapshot.locomotionState;
        }

        RemoveMissingPlayerStateCache();
        PlayLocalStunnedFeedback();
    }

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

    private void PlayLocalStunnedFeedback()
    {
        if (!syncManager.TryGetSnapshot(syncManager.LocalClientId, out ClientSnapshotState snapshot))
        {
            hasLocalStunnedState = false;
            return;
        }

        bool isStunned = snapshot.isTagger && snapshot.locomotionState == LocomotionState.Stunned;
        if (!hasLocalStunnedState)
        {
            previousLocalStunnedState = isStunned;
            hasLocalStunnedState = true;
            return;
        }

        if (isStunned != previousLocalStunnedState)
        {
            PlayClientFeedback(
                isStunned ? ClientFeedbackType.TaggerStunnedStart : ClientFeedbackType.TaggerStunnedEnd,
                snapshot.position);
        }

        previousLocalStunnedState = isStunned;
    }

    private void PlayServerFeedback(ServerFeedbackType feedbackType, GameEventEntryPacket gameEvent)
    {
        if (TryRouteServerFeedbackToPlayerAudio(feedbackType, gameEvent))
        {
            return;
        }

        if (!TryResolveFeedbackPosition(gameEvent, out Vector2 position, out Transform followTarget))
        {
            position = gameEvent.position;
        }

        PlayServerFeedback(feedbackType, position, gameEvent.rotation, followTarget);
    }

    private bool TryRouteServerFeedbackToPlayerAudio(ServerFeedbackType feedbackType, GameEventEntryPacket gameEvent)
    {
        if (feedbackType != ServerFeedbackType.PortalTeleport)
        {
            return false;
        }

        if (feedbackCatalog != null
            && feedbackCatalog.TryGet(feedbackType, out GameFeedbackData data)
            && worldView != null)
        {
            worldView.TryPlayPlayerFeedback(gameEvent.targetClientId, data);
        }

        return true;
    }

    public void PlayServerFeedback(
        ServerFeedbackType feedbackType,
        Vector2 position,
        float rotation = 0f,
        Transform followTarget = null)
    {
        if (feedbackType == ServerFeedbackType.None)
        {
            return;
        }

        if (feedbackCatalog == null || !feedbackCatalog.TryGet(feedbackType, out GameFeedbackData data))
        {
            WarnMissingServerProfile(feedbackType);
            return;
        }

        PlayFeedbackData(data, position, rotation, followTarget);
    }

    public void PlayClientFeedback(
        ClientFeedbackType feedbackType,
        Vector2 position,
        float rotation = 0f,
        Transform followTarget = null)
    {
        if (feedbackType == ClientFeedbackType.None)
        {
            return;
        }

        if (feedbackCatalog == null || !feedbackCatalog.TryGet(feedbackType, out GameFeedbackData data))
        {
            WarnMissingClientProfile(feedbackType);
            return;
        }

        PlayFeedbackData(data, position, rotation, followTarget);
    }

    private void PlayFeedbackData(
        GameFeedbackData data,
        Vector2 position,
        float rotation,
        Transform followTarget)
    {
        Transform parent = feedbackRoot != null ? feedbackRoot : transform;
        Vector3 spawnPosition = new Vector3(position.x, position.y, data.spawnZ);
        Quaternion spawnRotation = Quaternion.Euler(0f, 0f, data.useServerRotation ? rotation : 0f);

        if (data.visualPrefab != null)
        {
            SpawnVisual(data, spawnPosition, spawnRotation, parent, followTarget);
        }

        if (data.sound.clip != null)
        {
            SpawnAudio(data.sound, data.spawnMode, spawnPosition, parent, followTarget, data.followTarget, data.lifetimeSeconds);
        }
    }

    private void SpawnVisual(
        GameFeedbackData data,
        Vector3 position,
        Quaternion rotation,
        Transform parent,
        Transform followTarget)
    {
        if (data.spawnMode == GameFeedbackSpawnMode.ScreenOverlay)
        {
            GameObject overlayInstance = Instantiate(data.visualPrefab, parent, false);
            overlayInstance.SetActive(true);
            PlayParticleSystems(overlayInstance);

            float overlayLifetime = ResolveLifetime(overlayInstance, data.lifetimeSeconds);
            if (overlayLifetime > 0f)
            {
                Destroy(overlayInstance, overlayLifetime);
            }

            return;
        }

        Transform visualParent = data.followTarget && followTarget != null ? followTarget : parent;
        GameObject instance = Instantiate(data.visualPrefab, position, rotation, visualParent);
        instance.SetActive(true);
        PlayParticleSystems(instance);

        float lifetime = ResolveLifetime(instance, data.lifetimeSeconds);
        if (lifetime > 0f)
        {
            Destroy(instance, lifetime);
        }
    }

    private void SpawnAudio(
        GameFeedbackSound sound,
        GameFeedbackSpawnMode spawnMode,
        Vector3 position,
        Transform parent,
        Transform followTarget,
        bool followTargetEnabled,
        float lifetimeSeconds)
    {
        if (audioSourcePrefab == null)
        {
            if (!warnedMissingAudioSourcePrefab)
            {
                Debug.LogWarning("[Client_WorldFeedbackPlayer] AudioSource prefab is not assigned.", this);
                warnedMissingAudioSourcePrefab = true;
            }

            return;
        }

        Transform audioParent = followTargetEnabled && followTarget != null ? followTarget : parent;
        Vector3 audioPosition = ResolveAudioPosition(position, sound.space);
        AudioSource audioSource = spawnMode == GameFeedbackSpawnMode.ScreenOverlay
            ? Instantiate(audioSourcePrefab, audioParent, false)
            : Instantiate(audioSourcePrefab, audioPosition, Quaternion.identity, audioParent);
        audioSource.playOnAwake = false;
        audioSource.Stop();
        audioSource.clip = sound.clip;
        audioSource.spatialBlend = sound.space == GameFeedbackSoundSpace.World ? 1f : 0f;
        audioSource.dopplerLevel = 0f;
        audioSource.volume *= sound.Volume;
        audioSource.gameObject.SetActive(true);
        audioSource.Play();

        float clipLength = sound.clip.length / Mathf.Max(0.01f, Mathf.Abs(audioSource.pitch));
        float lifetime = lifetimeSeconds > 0f
            ? lifetimeSeconds
            : clipLength + 0.1f;
        Destroy(audioSource.gameObject, lifetime);
    }

    private static Vector3 ResolveAudioPosition(Vector3 position, GameFeedbackSoundSpace soundSpace)
    {
        if (soundSpace != GameFeedbackSoundSpace.World)
        {
            return position;
        }

        AudioListener listener = UnityEngine.Object.FindFirstObjectByType<AudioListener>();
        if (listener == null)
        {
            return position;
        }

        position.z = listener.transform.position.z;
        return position;
    }

    private bool TryResolveFeedbackPosition(
        GameEventEntryPacket gameEvent,
        out Vector2 position,
        out Transform followTarget)
    {
        position = gameEvent.position;
        followTarget = null;

        if (feedbackCatalog == null || !feedbackCatalog.TryGet(gameEvent.feedbackType, out GameFeedbackData data))
        {
            return false;
        }

        ulong clientId = data.spawnMode switch
        {
            GameFeedbackSpawnMode.SubjectPlayer => gameEvent.subjectClientId,
            GameFeedbackSpawnMode.TargetPlayer => gameEvent.targetClientId,
            GameFeedbackSpawnMode.LocalPlayer => syncManager != null ? syncManager.LocalClientId : ulong.MaxValue,
            _ => ulong.MaxValue
        };

        if (clientId == ulong.MaxValue)
        {
            return data.spawnMode == GameFeedbackSpawnMode.EventPosition
                || data.spawnMode == GameFeedbackSpawnMode.ScreenOverlay;
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
    }

    private void WarnMissingServerProfile(ServerFeedbackType feedbackType)
    {
        if (!warnedMissingServerProfiles.Add(feedbackType))
        {
            return;
        }

        if (feedbackCatalog == null)
        {
            Debug.LogWarning($"[Client_WorldFeedbackPlayer] Feedback catalog is not assigned. Cannot play {feedbackType}.", this);
            return;
        }

        Debug.LogWarning($"[Client_WorldFeedbackPlayer] Server feedback profile is missing for {feedbackType}.", this);
    }

    private void WarnMissingClientProfile(ClientFeedbackType feedbackType)
    {
        if (!warnedMissingClientProfiles.Add(feedbackType))
        {
            return;
        }

        if (feedbackCatalog == null)
        {
            Debug.LogWarning($"[Client_WorldFeedbackPlayer] Feedback catalog is not assigned. Cannot play {feedbackType}.", this);
            return;
        }

        Debug.LogWarning($"[Client_WorldFeedbackPlayer] Client feedback profile is missing for {feedbackType}.", this);
    }

    private static void PlayParticleSystems(GameObject instance)
    {
        ParticleSystem[] particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
            {
                continue;
            }

            if (!particleSystem.gameObject.activeSelf)
            {
                particleSystem.gameObject.SetActive(true);
            }

            particleSystem.Play(withChildren: false);
        }
    }

    private static float ResolveLifetime(GameObject instance, float configuredLifetime)
    {
        if (configuredLifetime > 0f)
        {
            return configuredLifetime;
        }

        float particleLifetime = GetParticleLifetime(instance);
        return particleLifetime > 0f ? particleLifetime : DefaultFeedbackLifetimeSeconds;
    }

    private static float GetParticleLifetime(GameObject instance)
    {
        ParticleSystem[] particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
        float maxLifetime = 0f;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particleSystem.main;
            if (main.loop)
            {
                continue;
            }

            maxLifetime = Mathf.Max(maxLifetime, main.duration + GetStartLifetimeMax(main.startLifetime));
        }

        return maxLifetime;
    }

    private static float GetStartLifetimeMax(ParticleSystem.MinMaxCurve lifetime)
    {
        return lifetime.mode switch
        {
            ParticleSystemCurveMode.Constant => lifetime.constant,
            ParticleSystemCurveMode.TwoConstants => lifetime.constantMax,
            ParticleSystemCurveMode.Curve => GetLastKeyTime(lifetime.curve, lifetime.constant),
            ParticleSystemCurveMode.TwoCurves => GetLastKeyTime(lifetime.curveMax, lifetime.constantMax),
            _ => lifetime.constant
        };
    }

    private static float GetLastKeyTime(AnimationCurve curve, float fallback)
    {
        if (curve == null || curve.length == 0)
        {
            return fallback;
        }

        return curve.keys[curve.length - 1].time;
    }
}

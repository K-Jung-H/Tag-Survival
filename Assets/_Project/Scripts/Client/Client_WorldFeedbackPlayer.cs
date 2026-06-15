using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;

[MovedFrom(true, null, null, "Client_WorldVfxSpawner")]
public sealed class Client_WorldFeedbackPlayer : MonoBehaviour
{
    private const float DefaultFeedbackLifetimeSeconds = 2f;

    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private Client_WorldView worldView;
    [SerializeField] private Transform feedbackRoot;
    [SerializeField] private AudioSource audioSourcePrefab;
    [FormerlySerializedAs("vfxCatalog")]
    [SerializeField] private GameFeedbackCatalog feedbackCatalog;

    private readonly Dictionary<GameFeedbackType, GameFeedbackProfile> profilesByType = new();
    private readonly HashSet<GameFeedbackType> warnedMissingProfiles = new();
    private readonly HashSet<GameFeedbackType> warnedDuplicateProfiles = new();
    private readonly Dictionary<ulong, LocomotionState> previousLocomotionStates = new();
    private readonly List<ulong> removedClientIds = new();
    private readonly HashSet<ulong> activeClientIds = new();

    private bool hasLocalStunnedState;
    private bool previousLocalStunnedState;
    private bool warnedMissingAudioSourcePrefab;

    // - Role: Set up needed links before start.
    private void Awake()
    {
        CacheProfiles();
    }

    // - Role: Turn on links when this object is enabled.
    private void OnEnable()
    {
        CacheProfiles();

        if (syncManager != null)
        {
            syncManager.GameEventReceived += OnGameEventReceived;
        }
    }

    // - Role: Turn off links when this object is disabled.
    private void OnDisable()
    {
        if (syncManager != null)
        {
            syncManager.GameEventReceived -= OnGameEventReceived;
        }
    }

    // - Role: Update derived feedback after snapshots are applied.
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

    // - Role: Handle game event received.
    private void OnGameEventReceived(GameEventEntryPacket gameEvent)
    {
        if (gameEvent.eventType == GameEventType.Feedback)
        {
            PlayFeedback(gameEvent.feedbackType, gameEvent);
        }
    }

    // - Role: Play player state feedback from snapshot diff.
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
                    PlayFeedbackAt(GameFeedbackType.BlinkEnter, snapshot.position, 0f);
                }
                else if (snapshot.locomotionState == LocomotionState.BlinkExit)
                {
                    PlayFeedbackAt(GameFeedbackType.BlinkExit, snapshot.position, 0f);
                }
            }

            previousLocomotionStates[clientId] = snapshot.locomotionState;
        }

        RemoveMissingPlayerStateCache();
        PlayLocalStunnedFeedback();
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

    // - Role: Play local stunned screen feedback.
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
            PlayFeedbackAt(
                isStunned ? GameFeedbackType.TaggerStunnedStart : GameFeedbackType.TaggerStunnedEnd,
                snapshot.position,
                0f);
        }

        previousLocalStunnedState = isStunned;
    }

    // - Role: Play feedback from server event.
    private void PlayFeedback(GameFeedbackType feedbackType, GameEventEntryPacket gameEvent)
    {
        if (!TryResolveFeedbackPosition(gameEvent, out Vector2 position, out Transform followTarget))
        {
            position = gameEvent.position;
        }

        PlayFeedbackAt(feedbackType, position, gameEvent.rotation, followTarget);
    }

    // - Role: Play feedback at a world position.
    private void PlayFeedbackAt(
        GameFeedbackType feedbackType,
        Vector2 position,
        float rotation,
        Transform followTarget = null)
    {
        if (feedbackType == GameFeedbackType.None)
        {
            return;
        }

        if (!profilesByType.TryGetValue(feedbackType, out GameFeedbackProfile profile))
        {
            WarnMissingProfile(feedbackType);
            return;
        }

        Transform parent = feedbackRoot != null ? feedbackRoot : transform;
        Vector3 spawnPosition = new Vector3(position.x, position.y, profile.spawnZ);
        Quaternion spawnRotation = Quaternion.Euler(0f, 0f, profile.useServerRotation ? rotation : 0f);

        if (profile.visualPrefab != null)
        {
            SpawnVisual(profile, spawnPosition, spawnRotation, parent, followTarget);
        }

        if (profile.sound.clip != null)
        {
            SpawnAudio(profile.sound, profile.spawnMode, spawnPosition, parent, followTarget, profile.followTarget, profile.lifetimeSeconds);
        }
    }

    // - Role: Spawn visual feedback.
    private void SpawnVisual(
        GameFeedbackProfile profile,
        Vector3 position,
        Quaternion rotation,
        Transform parent,
        Transform followTarget)
    {
        if (profile.spawnMode == GameFeedbackSpawnMode.ScreenOverlay)
        {
            GameObject overlayInstance = Instantiate(profile.visualPrefab, parent, false);
            overlayInstance.SetActive(true);
            PlayParticleSystems(overlayInstance);

            float overlayLifetime = ResolveLifetime(overlayInstance, profile.lifetimeSeconds);
            if (overlayLifetime > 0f)
            {
                Destroy(overlayInstance, overlayLifetime);
            }

            return;
        }

        Transform visualParent = profile.followTarget && followTarget != null ? followTarget : parent;
        GameObject instance = Instantiate(profile.visualPrefab, position, rotation, visualParent);
        instance.SetActive(true);
        PlayParticleSystems(instance);

        float lifetime = ResolveLifetime(instance, profile.lifetimeSeconds);
        if (lifetime > 0f)
        {
            Destroy(instance, lifetime);
        }
    }

    // - Role: Spawn audio feedback.
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
        float pitchMin = Mathf.Min(sound.PitchMin, sound.PitchMax);
        float pitchMax = Mathf.Max(sound.PitchMin, sound.PitchMax);
        audioSource.pitch = Random.Range(pitchMin, pitchMax);
        audioSource.gameObject.SetActive(true);
        audioSource.Play();

        float clipLength = sound.clip.length / Mathf.Max(0.01f, Mathf.Abs(audioSource.pitch));
        float lifetime = lifetimeSeconds > 0f
            ? lifetimeSeconds
            : clipLength + 0.1f;
        Destroy(audioSource.gameObject, lifetime);
    }

    // - Role: Resolve audio source position.
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

    // - Role: Try to resolve feedback position.
    private bool TryResolveFeedbackPosition(
        GameEventEntryPacket gameEvent,
        out Vector2 position,
        out Transform followTarget)
    {
        position = gameEvent.position;
        followTarget = null;

        if (!profilesByType.TryGetValue(gameEvent.feedbackType, out GameFeedbackProfile profile))
        {
            return false;
        }

        ulong clientId = profile.spawnMode switch
        {
            GameFeedbackSpawnMode.SubjectPlayer => gameEvent.subjectClientId,
            GameFeedbackSpawnMode.TargetPlayer => gameEvent.targetClientId,
            GameFeedbackSpawnMode.LocalPlayer => syncManager != null ? syncManager.LocalClientId : ulong.MaxValue,
            _ => ulong.MaxValue
        };

        if (clientId == ulong.MaxValue)
        {
            return profile.spawnMode == GameFeedbackSpawnMode.EventPosition
                || profile.spawnMode == GameFeedbackSpawnMode.ScreenOverlay;
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

    // - Role: Cache feedback profiles.
    private void CacheProfiles()
    {
        profilesByType.Clear();
        warnedDuplicateProfiles.Clear();

        if (feedbackCatalog == null)
        {
            return;
        }

        GameFeedbackProfile[] profiles = feedbackCatalog.Profiles;
        for (int i = 0; i < profiles.Length; i++)
        {
            GameFeedbackProfile profile = profiles[i];
            if (profile.type == GameFeedbackType.None)
            {
                continue;
            }

            if (profilesByType.ContainsKey(profile.type))
            {
                WarnDuplicateProfile(profile.type, i);
                continue;
            }

            profilesByType.Add(profile.type, profile);
        }
    }

    // - Role: Warn about missing profile.
    private void WarnMissingProfile(GameFeedbackType feedbackType)
    {
        if (!warnedMissingProfiles.Add(feedbackType))
        {
            return;
        }

        if (feedbackCatalog == null)
        {
            Debug.LogWarning($"[Client_WorldFeedbackPlayer] Feedback catalog is not assigned. Cannot play {feedbackType}.", this);
            return;
        }

        Debug.LogWarning($"[Client_WorldFeedbackPlayer] Feedback profile is missing for {feedbackType}.", this);
    }

    // - Role: Warn about duplicate profile.
    private void WarnDuplicateProfile(GameFeedbackType feedbackType, int index)
    {
        if (!warnedDuplicateProfiles.Add(feedbackType))
        {
            return;
        }

        Debug.LogWarning(
            $"[Client_WorldFeedbackPlayer] Duplicate feedback profile for {feedbackType} found at index {index}. The first profile will be used.",
            this);
    }

    // - Role: Play particle systems.
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

    // - Role: Find lifetime.
    private static float ResolveLifetime(GameObject instance, float configuredLifetime)
    {
        if (configuredLifetime > 0f)
        {
            return configuredLifetime;
        }

        float particleLifetime = GetParticleLifetime(instance);
        return particleLifetime > 0f ? particleLifetime : DefaultFeedbackLifetimeSeconds;
    }

    // - Role: Get particle lifetime.
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

    // - Role: Get start lifetime max.
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

    // - Role: Get last key time.
    private static float GetLastKeyTime(AnimationCurve curve, float fallback)
    {
        if (curve == null || curve.length == 0)
        {
            return fallback;
        }

        return curve.keys[curve.length - 1].time;
    }
}

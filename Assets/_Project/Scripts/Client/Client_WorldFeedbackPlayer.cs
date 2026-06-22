using System.Collections.Generic;
using UnityEngine;

public sealed class Client_WorldFeedbackPlayer : MonoBehaviour
{
    private const float DefaultFeedbackLifetimeSeconds = 2f;

    [SerializeField] private Transform feedbackRoot;
    [SerializeField] private AudioSource audioSourcePrefab;
    [SerializeField] private Transform audioListenerTransform;

    private readonly List<AudioSource> activeAudioSources = new();
    private bool warnedMissingAudioSourcePrefab;

    private void OnEnable()
    {
        AudioManager.StageSfxEnabledChanged += OnStageSfxEnabledChanged;
    }

    private void OnDisable()
    {
        AudioManager.StageSfxEnabledChanged -= OnStageSfxEnabledChanged;
    }

    // - Role: Bind scene-level audio listener transform.
    public void BindAudioListener(Transform listenerTransform)
    {
        audioListenerTransform = listenerTransform;
    }

    // - Role: Play world-positioned feedback data.
    public void Play(
        GameFeedbackData data,
        Vector2 position,
        float rotation = 0f,
        Transform followTarget = null)
    {
        Transform parent = feedbackRoot != null ? feedbackRoot : transform;
        Vector3 spawnPosition = new Vector3(position.x, position.y, data.spawnZ);
        Quaternion spawnRotation = Quaternion.Euler(0f, 0f, data.useServerRotation ? rotation : 0f);

        if (data.visualPrefab != null)
        {
            SpawnVisual(data, spawnPosition, spawnRotation, parent, followTarget);
        }

        if (data.sound.clip != null && AudioManager.CanPlayStageSfx)
        {
            SpawnAudio(data.sound, spawnPosition, parent, followTarget, data.followTarget, data.lifetimeSeconds);
        }
    }

    // - Role: Spawn visual feedback.
    private void SpawnVisual(
        GameFeedbackData data,
        Vector3 position,
        Quaternion rotation,
        Transform parent,
        Transform followTarget)
    {
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

    // - Role: Spawn audio feedback.
    private void SpawnAudio(
        GameFeedbackSound sound,
        Vector3 position,
        Transform parent,
        Transform followTarget,
        bool followTargetEnabled,
        float lifetimeSeconds)
    {
        if (!AudioManager.CanPlayStageSfx)
        {
            return;
        }

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
        AudioSource audioSource = Instantiate(audioSourcePrefab, audioPosition, Quaternion.identity, audioParent);
        audioSource.playOnAwake = false;
        audioSource.Stop();
        audioSource.clip = sound.clip;
        audioSource.spatialBlend = sound.space == GameFeedbackSoundSpace.World ? 1f : 0f;
        audioSource.dopplerLevel = 0f;
        audioSource.volume *= sound.Volume;
        audioSource.gameObject.SetActive(true);
        RegisterActiveAudioSource(audioSource);
        audioSource.Play();

        float clipLength = sound.clip.length / Mathf.Max(0.01f, Mathf.Abs(audioSource.pitch));
        float lifetime = lifetimeSeconds > 0f
            ? lifetimeSeconds
            : clipLength + 0.1f;
        Destroy(audioSource.gameObject, lifetime);
    }

    // - Role: Resolve audio source position.
    private Vector3 ResolveAudioPosition(Vector3 position, GameFeedbackSoundSpace soundSpace)
    {
        if (soundSpace != GameFeedbackSoundSpace.World || audioListenerTransform == null)
        {
            return position;
        }

        position.z = audioListenerTransform.position.z;
        return position;
    }

    // - Role: Stop currently playing stage sound sources.
    private void OnStageSfxEnabledChanged(bool enabled)
    {
        if (!enabled)
        {
            StopActiveAudioSources();
        }
    }

    // - Role: Register spawned audio source for stage mute.
    private void RegisterActiveAudioSource(AudioSource audioSource)
    {
        PruneActiveAudioSources();
        activeAudioSources.Add(audioSource);
    }

    // - Role: Stop active audio sources.
    private void StopActiveAudioSources()
    {
        for (int i = activeAudioSources.Count - 1; i >= 0; i--)
        {
            AudioSource audioSource = activeAudioSources[i];
            if (audioSource != null)
            {
                Destroy(audioSource.gameObject);
            }
        }

        activeAudioSources.Clear();
    }

    // - Role: Remove destroyed audio sources.
    private void PruneActiveAudioSources()
    {
        for (int i = activeAudioSources.Count - 1; i >= 0; i--)
        {
            if (activeAudioSources[i] == null)
            {
                activeAudioSources.RemoveAt(i);
            }
        }
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
    private static float GetLastKeyTime(AnimationCurve curve, float defaultValue)
    {
        if (curve == null || curve.length == 0)
        {
            return defaultValue;
        }

        return curve.keys[curve.length - 1].time;
    }
}

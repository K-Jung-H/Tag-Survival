using UnityEngine;

public sealed class Client_WorldFeedbackPlayer : MonoBehaviour
{
    private const float DefaultFeedbackLifetimeSeconds = 2f;

    [SerializeField] private Transform feedbackRoot;
    [SerializeField] private AudioSource audioSourcePrefab;

    private bool warnedMissingAudioSourcePrefab;

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

        if (data.sound.clip != null)
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

        AudioListener listener = Object.FindFirstObjectByType<AudioListener>();
        if (listener == null)
        {
            return position;
        }

        position.z = listener.transform.position.z;
        return position;
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

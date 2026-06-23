using UnityEngine;

public sealed class Client_WorldFeedbackPlayer : MonoBehaviour
{
    [SerializeField] private Transform feedbackRoot;
    [SerializeField] private AudioSource audioSourcePrefab;
    [SerializeField] private Transform audioListenerTransform;

    private FeedbackVisualPool visualPool;
    private FeedbackAudioSourcePool audioPool;
    private bool warnedMissingAudioSourcePrefab;

    private void Awake()
    {
        EnsurePools();
    }

    private void OnEnable()
    {
        AudioManager.StageSfxEnabledChanged += OnStageSfxEnabledChanged;
    }

    private void OnDisable()
    {
        AudioManager.StageSfxEnabledChanged -= OnStageSfxEnabledChanged;
        visualPool?.StopAll();
        audioPool?.StopAll();
    }

    private void Update()
    {
        visualPool?.Tick(Time.deltaTime);
        audioPool?.Tick(Time.deltaTime);
    }

    private void OnDestroy()
    {
        visualPool?.Dispose();
        audioPool?.Dispose();
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
        EnsurePools();

        FeedbackVisualPoolItem item = visualPool.Rent(data.visualPrefab, position, rotation, visualParent);
        float lifetime = item.ResolveLifetime(data.lifetimeSeconds);
        visualPool.ScheduleReturn(item, lifetime);
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
        EnsurePools();

        AudioSource audioSource = audioPool.Rent(audioSourcePrefab, audioParent, audioPosition, Quaternion.identity);
        audioSource.playOnAwake = false;
        audioSource.Stop();
        audioSource.clip = sound.clip;
        audioSource.spatialBlend = sound.space == GameFeedbackSoundSpace.World ? 1f : 0f;
        audioSource.dopplerLevel = 0f;
        audioSource.volume = audioSourcePrefab.volume * sound.Volume;
        audioSource.gameObject.SetActive(true);
        audioSource.Play();

        float clipLength = sound.clip.length / Mathf.Max(0.01f, Mathf.Abs(audioSource.pitch));
        float lifetime = lifetimeSeconds > 0f
            ? lifetimeSeconds
            : clipLength + 0.1f;
        audioPool.ScheduleReturn(audioSource, lifetime);
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
            audioPool?.StopAll();
        }
    }

    private void EnsurePools()
    {
        if (visualPool == null)
        {
            visualPool = new FeedbackVisualPool(transform, "World Feedback Visual Pool");
        }

        if (audioPool == null)
        {
            audioPool = new FeedbackAudioSourcePool(transform, "World Feedback Audio Pool");
        }
    }
}

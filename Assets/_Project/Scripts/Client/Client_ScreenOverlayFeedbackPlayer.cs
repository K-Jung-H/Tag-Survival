using System.Collections.Generic;
using UnityEngine;

public sealed class Client_ScreenOverlayFeedbackPlayer : MonoBehaviour
{
    [SerializeField] private Transform overlayRoot;
    [SerializeField] private AudioSource audioSourcePrefab;

    private readonly Dictionary<ScreenOverlayFeedbackType, Client_ScreenOverlayFeedbackPanel> panelsByType = new();
    private FeedbackAudioSourcePool audioPool;
    private bool warnedMissingAudioSourcePrefab;

    private void Awake()
    {
        EnsureAudioPool();
    }

    private void OnEnable()
    {
        AudioManager.StageSfxEnabledChanged += OnStageSfxEnabledChanged;
    }

    private void OnDisable()
    {
        AudioManager.StageSfxEnabledChanged -= OnStageSfxEnabledChanged;
        audioPool?.StopAll();
    }

    private void Update()
    {
        audioPool?.Tick(Time.deltaTime);
    }

    private void OnDestroy()
    {
        audioPool?.Dispose();
    }

    // - Role: Set overlay active state.
    public void SetActive(
        ScreenOverlayFeedbackProfile profile,
        bool active,
        Vector2 centerUv)
    {
        if (profile.type == ScreenOverlayFeedbackType.None || profile.panelPrefab == null)
        {
            return;
        }

        if (!active
            && (!panelsByType.TryGetValue(profile.type, out Client_ScreenOverlayFeedbackPanel existingPanel)
                || existingPanel == null))
        {
            return;
        }

        if (!TryGetOrCreatePanel(profile, out Client_ScreenOverlayFeedbackPanel panel))
        {
            return;
        }

        if (active)
        {
            panel.Show(profile.data, centerUv);
            PlaySound(profile.data);
        }
        else
        {
            panel.Hide(profile.data, centerUv);
        }
    }

    // - Role: Update values for an active overlay.
    public void UpdateOverlay(ScreenOverlayFeedbackType type, Vector2 centerUv)
    {
        if (panelsByType.TryGetValue(type, out Client_ScreenOverlayFeedbackPanel panel) && panel != null)
        {
            panel.SetCenter(centerUv);
        }
    }

    // - Role: Get or create panel instance.
    private bool TryGetOrCreatePanel(
        ScreenOverlayFeedbackProfile profile,
        out Client_ScreenOverlayFeedbackPanel panel)
    {
        if (panelsByType.TryGetValue(profile.type, out panel) && panel != null)
        {
            return true;
        }

        Transform parent = overlayRoot != null ? overlayRoot : transform;
        GameObject instance = Instantiate(profile.panelPrefab, parent, false);
        panel = instance.GetComponent<Client_ScreenOverlayFeedbackPanel>();
        if (panel == null)
        {
            Debug.LogWarning(
                $"[Client_ScreenOverlayFeedbackPlayer] Panel prefab for {profile.type} must have Client_ScreenOverlayFeedbackPanel.",
                this);
            Destroy(instance);
            return false;
        }

        instance.SetActive(false);
        panelsByType[profile.type] = panel;
        return true;
    }

    // - Role: Play overlay sound.
    private void PlaySound(GameFeedbackData data)
    {
        if (data.sound.clip == null || !AudioManager.CanPlayStageSfx)
        {
            return;
        }

        if (audioSourcePrefab == null)
        {
            if (!warnedMissingAudioSourcePrefab)
            {
                Debug.LogWarning("[Client_ScreenOverlayFeedbackPlayer] AudioSource prefab is not assigned.", this);
                warnedMissingAudioSourcePrefab = true;
            }

            return;
        }

        Transform parent = overlayRoot != null ? overlayRoot : transform;
        EnsureAudioPool();

        AudioSource audioSource = audioPool.Rent(audioSourcePrefab, parent, parent.position, Quaternion.identity);
        audioSource.playOnAwake = false;
        audioSource.Stop();
        audioSource.clip = data.sound.clip;
        audioSource.spatialBlend = 0f;
        audioSource.dopplerLevel = 0f;
        audioSource.volume = audioSourcePrefab.volume * data.sound.Volume;
        audioSource.gameObject.SetActive(true);
        audioSource.Play();

        float clipLength = data.sound.clip.length / Mathf.Max(0.01f, Mathf.Abs(audioSource.pitch));
        float lifetime = data.lifetimeSeconds > 0f ? data.lifetimeSeconds : clipLength + 0.1f;
        audioPool.ScheduleReturn(audioSource, lifetime);
    }

    // - Role: Stop currently playing stage sound sources.
    private void OnStageSfxEnabledChanged(bool enabled)
    {
        if (!enabled)
        {
            audioPool?.StopAll();
        }
    }

    private void EnsureAudioPool()
    {
        if (audioPool == null)
        {
            audioPool = new FeedbackAudioSourcePool(transform, "Screen Overlay Audio Pool");
        }
    }
}

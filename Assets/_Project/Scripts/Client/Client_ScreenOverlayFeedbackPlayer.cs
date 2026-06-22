using System.Collections.Generic;
using UnityEngine;

public sealed class Client_ScreenOverlayFeedbackPlayer : MonoBehaviour
{
    [SerializeField] private Transform overlayRoot;
    [SerializeField] private AudioSource audioSourcePrefab;

    private readonly Dictionary<ScreenOverlayFeedbackType, Client_ScreenOverlayFeedbackPanel> panelsByType = new();
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
        AudioSource audioSource = Instantiate(audioSourcePrefab, parent, false);
        audioSource.playOnAwake = false;
        audioSource.Stop();
        audioSource.clip = data.sound.clip;
        audioSource.spatialBlend = 0f;
        audioSource.dopplerLevel = 0f;
        audioSource.volume *= data.sound.Volume;
        audioSource.gameObject.SetActive(true);
        RegisterActiveAudioSource(audioSource);
        audioSource.Play();

        float clipLength = data.sound.clip.length / Mathf.Max(0.01f, Mathf.Abs(audioSource.pitch));
        float lifetime = data.lifetimeSeconds > 0f ? data.lifetimeSeconds : clipLength + 0.1f;
        Destroy(audioSource.gameObject, lifetime);
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
}

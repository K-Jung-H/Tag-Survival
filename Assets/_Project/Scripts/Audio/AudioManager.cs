using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-100)]
public sealed class AudioManager : MonoBehaviour
{
    private const string BgmEnabledPrefsKey = "Audio.BgmEnabled";
    private const string SfxEnabledPrefsKey = "Audio.SfxEnabled";

    [SerializeField] private AudioCatalog catalog;
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource uiSfxSource;
    [SerializeField, Range(0f, 1f)] private float bgmVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float uiSfxVolume = 1f;
    [SerializeField] private bool bgmEnabled = true;
    [SerializeField] private bool sfxEnabled = true;

    private static AudioManager instance;
    private Coroutine bgmRoutine;

    public static AudioManager Instance => instance;
    public bool IsBgmEnabled => bgmEnabled;
    public bool IsSfxEnabled => sfxEnabled;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        LoadSavedSettings();
        ApplyAudioSettings();

        if (bgmSource != null)
        {
            bgmSource.loop = true;
            bgmSource.volume = bgmVolume;
        }
    }

    private void Start()
    {
        PlaySceneBgm(SceneManager.GetActiveScene().name);
    }

    public void PlaySceneBgm(string sceneName)
    {
        if (catalog == null || !catalog.TryGetSceneBgm(sceneName, out SceneBgmEntry entry))
        {
            return;
        }

        PlayBgm(entry.bgmClip, entry.fadeSeconds);
    }

    public void PlayBgm(AudioClip clip, float fadeSeconds)
    {
        if (bgmSource == null)
        {
            return;
        }

        if (bgmSource.clip == clip && bgmSource.isPlaying)
        {
            return;
        }

        StopBgmRoutine();
        bgmRoutine = StartCoroutine(ChangeBgmRoutine(clip, Mathf.Max(0f, fadeSeconds)));
    }

    public void StopBgm(float fadeSeconds)
    {
        if (bgmSource == null)
        {
            return;
        }

        StopBgmRoutine();
        bgmRoutine = StartCoroutine(ChangeBgmRoutine(null, Mathf.Max(0f, fadeSeconds)));
    }

    public void PlayButtonClick(AudioClip overrideClip = null)
    {
        AudioClip clip = overrideClip != null
            ? overrideClip
            : catalog != null ? catalog.DefaultButtonClickClip : null;
        PlayUiSfx(clip);
    }

    public void PlayUiSfx(AudioClip clip)
    {
        if (!sfxEnabled || uiSfxSource == null || clip == null)
        {
            return;
        }

        uiSfxSource.PlayOneShot(clip, uiSfxVolume);
    }

    public void SetBgmEnabled(bool enabled)
    {
        if (bgmEnabled == enabled)
        {
            return;
        }

        bgmEnabled = enabled;
        PlayerPrefs.SetInt(BgmEnabledPrefsKey, bgmEnabled ? 1 : 0);
        PlayerPrefs.Save();
        ApplyAudioSettings();
    }

    public void SetSfxEnabled(bool enabled)
    {
        if (sfxEnabled == enabled)
        {
            return;
        }

        sfxEnabled = enabled;
        PlayerPrefs.SetInt(SfxEnabledPrefsKey, sfxEnabled ? 1 : 0);
        PlayerPrefs.Save();
        ApplyAudioSettings();
    }

    public void ToggleBgmEnabled()
    {
        SetBgmEnabled(!bgmEnabled);
    }

    public void ToggleSfxEnabled()
    {
        SetSfxEnabled(!sfxEnabled);
    }

    private IEnumerator ChangeBgmRoutine(AudioClip nextClip, float fadeSeconds)
    {
        if (fadeSeconds > 0f && bgmSource.isPlaying && bgmSource.volume > 0f)
        {
            float startVolume = bgmSource.volume;
            float elapsed = 0f;
            while (elapsed < fadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                bgmSource.volume = Mathf.Lerp(startVolume, 0f, Mathf.Clamp01(elapsed / fadeSeconds));
                yield return null;
            }
        }

        bgmSource.Stop();
        bgmSource.clip = nextClip;

        if (nextClip == null)
        {
            bgmSource.volume = bgmVolume;
            bgmRoutine = null;
            yield break;
        }

        bgmSource.volume = fadeSeconds > 0f ? 0f : bgmVolume;
        bgmSource.Play();

        if (fadeSeconds > 0f)
        {
            float elapsed = 0f;
            while (elapsed < fadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                bgmSource.volume = Mathf.Lerp(0f, bgmVolume, Mathf.Clamp01(elapsed / fadeSeconds));
                yield return null;
            }
        }

        bgmSource.volume = bgmVolume;
        bgmRoutine = null;
    }

    private void StopBgmRoutine()
    {
        if (bgmRoutine == null)
        {
            return;
        }

        StopCoroutine(bgmRoutine);
        bgmRoutine = null;
    }

    private void LoadSavedSettings()
    {
        bgmEnabled = PlayerPrefs.GetInt(BgmEnabledPrefsKey, bgmEnabled ? 1 : 0) != 0;
        sfxEnabled = PlayerPrefs.GetInt(SfxEnabledPrefsKey, sfxEnabled ? 1 : 0) != 0;
    }

    private void ApplyAudioSettings()
    {
        if (bgmSource != null)
        {
            bgmSource.mute = !bgmEnabled;
            bgmSource.volume = bgmVolume;
        }

        if (uiSfxSource != null)
        {
            uiSfxSource.mute = !sfxEnabled;
            uiSfxSource.volume = uiSfxVolume;
        }
    }
}

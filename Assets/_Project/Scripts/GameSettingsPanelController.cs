using UnityEngine;
using UnityEngine.UI;

public sealed class GameSettingsPanelController : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;

    [Header("Toggles")]
    [SerializeField] private Toggle bgmToggle;
    [SerializeField] private SettingsToggleSpriteView bgmToggleView;
    [SerializeField] private Toggle sfxToggle;
    [SerializeField] private SettingsToggleSpriteView sfxToggleView;
    [SerializeField] private Toggle stageSfxToggle;
    [SerializeField] private SettingsToggleSpriteView stageSfxToggleView;

    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    private void OnEnable()
    {
        GameFlowManager.Instance?.RegisterSettingsPanel(this);
        Refresh();
    }

    private void OnDisable()
    {
        GameFlowManager.Instance?.UnregisterSettingsPanel(this);
    }

    public void Open()
    {
        if (panelRoot == null)
        {
            Debug.LogError("[GameSettingsPanelController] Panel Root is not assigned.", this);
            return;
        }

        panelRoot.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (panelRoot == null)
        {
            Debug.LogError("[GameSettingsPanelController] Panel Root is not assigned.", this);
            return;
        }

        panelRoot.SetActive(false);
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        Open();
    }

    public void ClickBgmToggle()
    {
        AudioManager.Instance?.ToggleBgmEnabled();
        Refresh();
    }

    public void ClickSfxToggle()
    {
        AudioManager.Instance?.ToggleSfxEnabled();
        Refresh();
    }

    public void ClickStageSfxToggle()
    {
        AudioManager.Instance?.ToggleStageSfxEnabled();
        Refresh();
    }

    public void SetBgmEnabled(bool enabled)
    {
        AudioManager.Instance?.SetBgmEnabled(enabled);
        Refresh();
    }

    public void SetSfxEnabled(bool enabled)
    {
        AudioManager.Instance?.SetSfxEnabled(enabled);
        Refresh();
    }

    public void SetStageSfxEnabled(bool enabled)
    {
        AudioManager.Instance?.SetStageSfxEnabled(enabled);
        Refresh();
    }

    public void ClickBack()
    {
        GameFlowManager.Instance?.GoBack();
    }

    public void ClickHome()
    {
        GameFlowManager.Instance?.ReturnToStart();
    }

    public void Refresh()
    {
        AudioManager audioManager = AudioManager.Instance;
        if (audioManager == null)
        {
            SyncToggle(bgmToggle, bgmToggleView, false);
            SyncToggle(sfxToggle, sfxToggleView, false);
            SyncToggle(stageSfxToggle, stageSfxToggleView, false);
            return;
        }

        SyncToggle(bgmToggle, bgmToggleView, audioManager.IsBgmEnabled);
        SyncToggle(sfxToggle, sfxToggleView, audioManager.IsSfxEnabled);
        SyncToggle(stageSfxToggle, stageSfxToggleView, audioManager.IsStageSfxEnabled);
    }

    private static void SyncToggle(Toggle targetToggle, SettingsToggleSpriteView spriteView, bool isEnabled)
    {
        if (targetToggle != null)
        {
            targetToggle.SetIsOnWithoutNotify(isEnabled);
        }

        if (spriteView != null)
        {
            spriteView.Refresh();
        }
    }
}

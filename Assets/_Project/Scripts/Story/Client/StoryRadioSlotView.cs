using System;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Toggle))]
[RequireComponent(typeof(Image))]
public sealed class StoryRadioSlotView : MonoBehaviour
{
    [SerializeField] private GameObject lockOverlay;
    [SerializeField] private bool locked;

    private Action<StoryRadioSlotView, bool> changed;
    private Toggle toggle;
    private Image backgroundImage;
    private Color normalColor = new(1f, 1f, 1f, 0.85f);
    private Color selectedColor = new(0.45f, 0.95f, 1f, 1f);
    private Color lockedColor = new(0.35f, 0.35f, 0.35f, 0.8f);

    public bool IsLocked => locked;
    public Toggle Toggle
    {
        get
        {
            CacheComponents();
            return toggle;
        }
    }

    private void Awake()
    {
        CacheComponents();
        ApplyLockState();
    }

    private void OnDestroy()
    {
        if (toggle != null)
        {
            toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
        }
    }

    public void Bind(ToggleGroup group, Action<StoryRadioSlotView, bool> onChanged)
    {
        CacheComponents();
        changed = onChanged;

        if (toggle != null)
        {
            toggle.group = group;
            toggle.interactable = !locked;
            toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
            toggle.onValueChanged.AddListener(OnToggleValueChanged);
        }

        ApplyLockState();
        SetSelected(toggle != null && toggle.isOn);
    }

    public void SetLocked(bool isLocked)
    {
        locked = isLocked;
        ApplyLockState();
        SetSelected(toggle != null && toggle.isOn);
    }

    public void SetColorPalette(Color normal, Color selected, Color lockedState)
    {
        CacheComponents();
        normalColor = normal;
        selectedColor = selected;
        lockedColor = lockedState;
        SetSelected(toggle != null && toggle.isOn);
    }

    public void SetSelected(bool selected)
    {
        CacheComponents();

        if (toggle != null)
        {
            toggle.SetIsOnWithoutNotify(selected);
        }

        if (backgroundImage != null)
        {
            backgroundImage.color = locked ? lockedColor : selected ? selectedColor : normalColor;
        }
    }

    private void OnToggleValueChanged(bool isOn)
    {
        changed?.Invoke(this, isOn);
    }

    public void Unbind()
    {
        changed = null;

        if (toggle != null)
        {
            toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
            toggle.group = null;
        }
    }

    private void ApplyLockState()
    {
        CacheComponents();

        if (toggle != null)
        {
            toggle.interactable = !locked;
        }

        if (lockOverlay != null)
        {
            lockOverlay.SetActive(locked);
        }
    }

    private void CacheComponents()
    {
        if (toggle == null)
        {
            toggle = GetComponent<Toggle>();
        }

        if (toggle != null)
        {
            toggle.transition = Selectable.Transition.None;
        }

        if (backgroundImage == null)
        {
            backgroundImage = GetComponent<Image>();
        }
    }
}

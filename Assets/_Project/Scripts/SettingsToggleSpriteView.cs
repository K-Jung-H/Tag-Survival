using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class SettingsToggleSpriteView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [SerializeField] private Toggle toggle;
    [SerializeField] private Image targetImage;

    [Header("Off")]
    [SerializeField] private Sprite offNormalSprite;
    [SerializeField] private Sprite offPressedSprite;

    [Header("On")]
    [SerializeField] private Sprite onNormalSprite;
    [SerializeField] private Sprite onPressedSprite;

    [Header("Disabled")]
    [SerializeField] private Sprite disabledSprite;

    private bool isPressed;

    private void OnEnable()
    {
        if (!HasRequiredReferences())
        {
            return;
        }

        toggle.onValueChanged.AddListener(OnToggleValueChanged);
        Refresh();
    }

    private void OnDisable()
    {
        if (toggle != null)
        {
            toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
        }

        isPressed = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanInteract())
        {
            return;
        }

        isPressed = true;
        Refresh();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
        Refresh();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPressed = false;
        Refresh();
    }

    public void Refresh()
    {
        if (targetImage == null || toggle == null)
        {
            return;
        }

        targetImage.sprite = ResolveSprite();
    }

    private void OnToggleValueChanged(bool _)
    {
        Refresh();
    }

    private Sprite ResolveSprite()
    {
        if (!CanInteract() && disabledSprite != null)
        {
            return disabledSprite;
        }

        if (toggle.isOn)
        {
            return isPressed && onPressedSprite != null ? onPressedSprite : onNormalSprite;
        }

        return isPressed && offPressedSprite != null ? offPressedSprite : offNormalSprite;
    }

    private bool CanInteract()
    {
        return toggle != null && toggle.interactable;
    }

    private bool HasRequiredReferences()
    {
        if (toggle == null)
        {
            Debug.LogError("[SettingsToggleSpriteView] Toggle is not assigned.", this);
            return false;
        }

        if (targetImage == null)
        {
            Debug.LogError("[SettingsToggleSpriteView] Target Image is not assigned.", this);
            return false;
        }

        return true;
    }
}

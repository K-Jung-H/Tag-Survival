using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public sealed class ItemSelectionSlot : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float selectedAlpha = 1f;
    [SerializeField] private float unselectedAlpha = 0f;
    [SerializeField] private float alphaFadeSpeed = 4f;

    private bool hasData;
    private bool isFadingAlpha;
    private float targetAlpha = 1f;
    private AnimationClip activeIconAnimationClip;
    private float activeIconAnimationTime;

    // - Role: Set up slot links.
    private void Awake()
    {
        ResolveLinks();
        ResetAlpha();
    }

    // - Role: Update alpha fade.
    private void Update()
    {
        TickIconAnimation();

        if (!isFadingAlpha || canvasGroup == null)
        {
            return;
        }

        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, alphaFadeSpeed * Time.deltaTime);
        if (Mathf.Abs(canvasGroup.alpha - targetAlpha) <= 0.001f)
        {
            canvasGroup.alpha = targetAlpha;
            isFadingAlpha = false;
        }
    }

    // - Role: Set item data.
    public void SetData(ItemData itemData, UnityAction onClick)
    {
        ResolveLinks();
        hasData = true;
        ResetAlpha();

        if (iconImage != null)
        {
            ApplyIcon(itemData.icon);
        }

        if (titleText != null)
        {
            titleText.text = string.IsNullOrWhiteSpace(itemData.title) ? FormatFallbackTitle(itemData) : itemData.title;
        }

        if (descriptionText != null)
        {
            descriptionText.text = itemData.description;
        }

        if (button != null)
        {
            button.interactable = true;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);
        }
    }

    // - Role: Clear slot data.
    public void SetMissing()
    {
        ResolveLinks();
        hasData = false;
        ResetAlpha();

        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }
        StopIconAnimation();

        if (titleText != null)
        {
            titleText.text = "-";
        }

        if (descriptionText != null)
        {
            descriptionText.text = string.Empty;
        }

        if (button != null)
        {
            button.interactable = false;
            button.onClick.RemoveAllListeners();
        }
    }

    // - Role: Set button active.
    public void SetInteractable(bool interactable)
    {
        if (button != null)
        {
            button.interactable = interactable && hasData;
        }
    }

    // - Role: Reset alpha state.
    public void ResetAlpha()
    {
        ResolveLinks();
        targetAlpha = selectedAlpha;
        isFadingAlpha = false;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = selectedAlpha;
        }
    }

    // - Role: Play selected alpha effect.
    public void PlaySelectionEffect(bool selected)
    {
        ResolveLinks();
        if (selected)
        {
            targetAlpha = selectedAlpha;
            isFadingAlpha = false;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = selectedAlpha;
            }

            return;
        }

        targetAlpha = unselectedAlpha;
        isFadingAlpha = canvasGroup != null;
    }

    // - Role: Resolve child links.
    private void ResolveLinks()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (button == null)
        {
            button = GetComponentInChildren<Button>(true);
        }

        if (iconImage == null && button != null)
        {
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != button.targetGraphic)
                {
                    iconImage = images[i];
                    break;
                }
            }
        }

        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        if (titleText == null && texts.Length > 0)
        {
            titleText = texts[0];
        }

        if (descriptionText == null && texts.Length > 1)
        {
            descriptionText = texts[1];
        }
    }

    // - Role: Format fallback title.
    private static string FormatFallbackTitle(ItemData itemData)
    {
        return itemData.GetFallbackEffectLabel();
    }

    // - Role: Apply item icon.
    private void ApplyIcon(ItemIconData icon)
    {
        StopIconAnimation();

        if (icon.viewType == ItemIconViewType.AnimationClip && icon.animationClip != null)
        {
            iconImage.sprite = icon.sprite;
            iconImage.enabled = true;
            activeIconAnimationClip = icon.animationClip;
            activeIconAnimationTime = 0f;
            activeIconAnimationClip.SampleAnimation(iconImage.gameObject, 0f);
            return;
        }

        iconImage.sprite = icon.sprite;
        iconImage.enabled = icon.sprite != null;
    }

    // - Role: Update item icon animation.
    private void TickIconAnimation()
    {
        if (activeIconAnimationClip == null || iconImage == null)
        {
            return;
        }

        float length = Mathf.Max(0.0001f, activeIconAnimationClip.length);
        activeIconAnimationTime += Time.deltaTime;
        float sampleTime = activeIconAnimationClip.isLooping
            ? Mathf.Repeat(activeIconAnimationTime, length)
            : Mathf.Min(activeIconAnimationTime, length);
        activeIconAnimationClip.SampleAnimation(iconImage.gameObject, sampleTime);
    }

    // - Role: Stop item icon animation.
    private void StopIconAnimation()
    {
        activeIconAnimationClip = null;
        activeIconAnimationTime = 0f;
    }
}

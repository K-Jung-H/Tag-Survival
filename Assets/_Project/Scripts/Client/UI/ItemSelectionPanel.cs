using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public sealed class ItemSelectionPanel : MonoBehaviour
{
    private const int CandidateCount = 3;

    [SerializeField] private TextMeshProUGUI itemTypeText;
    [SerializeField] private Image timerFillImage;
    [SerializeField] private ItemSelectionSlot[] optionSlots = new ItemSelectionSlot[CandidateCount];

    // - Role: Set up option slots.
    private void Awake()
    {
        EnsureSlots();
        PrepareTimerImage();
        SetVisible(false);
    }

    // - Role: Show panel for type.
    public void Open(ItemType itemType)
    {
        EnsureSlots();
        PrepareTimerImage();
        SetVisible(true);
        SetItemType(itemType);
        SetTimer(1f);
        ResetSlotEffects();
    }

    // - Role: Set panel visible.
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    // - Role: Set timer fill.
    public void SetTimer(float fillAmount)
    {
        float safeFillAmount = Mathf.Clamp01(fillAmount);
        if (timerFillImage != null)
        {
            PrepareTimerImage();
            timerFillImage.fillAmount = safeFillAmount;
        }
    }

    // - Role: Set option data.
    public void SetOption(int index, ItemData itemData, UnityAction onClick)
    {
        ItemSelectionSlot slot = GetSlot(index);
        if (slot != null)
        {
            slot.SetData(itemData, onClick);
        }
    }

    // - Role: Clear one option.
    public void SetMissingOption(int index)
    {
        ItemSelectionSlot slot = GetSlot(index);
        if (slot != null)
        {
            slot.SetMissing();
        }
    }

    // - Role: Set all buttons active.
    public void SetButtonsInteractable(bool interactable)
    {
        EnsureSlots();
        for (int i = 0; i < optionSlots.Length; i++)
        {
            ItemSelectionSlot slot = optionSlots[i];
            if (slot != null)
            {
                slot.SetInteractable(interactable);
            }
        }
    }

    // - Role: Mark selected option index.
    public void MarkSelected(int selectedIndex)
    {
        EnsureSlots();
        for (int i = 0; i < optionSlots.Length; i++)
        {
            ItemSelectionSlot slot = optionSlots[i];
            if (slot != null)
            {
                slot.PlaySelectionEffect(i == selectedIndex);
            }
        }
    }

    // - Role: Set item type text.
    private void SetItemType(ItemType itemType)
    {
        if (itemTypeText != null)
        {
            itemTypeText.text = itemType.ToString();
        }
    }

    // - Role: Prepare timer image.
    private void PrepareTimerImage()
    {
        if (timerFillImage == null)
        {
            return;
        }

        timerFillImage.type = Image.Type.Filled;
        timerFillImage.fillMethod = Image.FillMethod.Horizontal;
        timerFillImage.fillOrigin = 0;
    }

    // - Role: Reset all slot effects.
    private void ResetSlotEffects()
    {
        EnsureSlots();
        for (int i = 0; i < optionSlots.Length; i++)
        {
            ItemSelectionSlot slot = optionSlots[i];
            if (slot != null)
            {
                slot.ResetAlpha();
            }
        }
    }

    // - Role: Get option slot.
    private ItemSelectionSlot GetSlot(int index)
    {
        EnsureSlots();
        return index >= 0 && index < optionSlots.Length ? optionSlots[index] : null;
    }

    // - Role: Ensure slot array.
    private void EnsureSlots()
    {
        if (optionSlots == null || optionSlots.Length != CandidateCount)
        {
            optionSlots = new ItemSelectionSlot[CandidateCount];
        }
    }
}

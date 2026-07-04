using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(StoryRadioSlotView))]
public sealed class StorySkillSlotView : MonoBehaviour
{
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private Image iconImage;
    [SerializeField] private SkillDefinition skillDefinition;

    private StoryRadioSlotView radioSlot;

    public StoryRadioSlotView RadioSlot
    {
        get
        {
            CacheComponents();
            return radioSlot;
        }
    }
    public SkillDefinition SkillDefinition => skillDefinition;
    public bool IsSelectable => radioSlot != null && !radioSlot.IsLocked && skillDefinition != null;

    public void Configure(SkillDefinition definition)
    {
        CacheComponents();
        skillDefinition = definition;

        if (labelText != null)
        {
            labelText.text = definition != null ? definition.DisplayName : string.Empty;
        }

        if (iconImage != null)
        {
            iconImage.sprite = definition != null ? definition.Icon : null;
            iconImage.enabled = iconImage.sprite != null;
        }

        if (radioSlot != null)
        {
            radioSlot.SetLocked(definition == null);
        }
    }

    private void CacheComponents()
    {
        if (radioSlot == null)
        {
            radioSlot = GetComponent<StoryRadioSlotView>();
        }
    }
}

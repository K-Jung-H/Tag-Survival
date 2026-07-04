using TMPro;
using UnityEngine;

[RequireComponent(typeof(StoryRadioSlotView))]
public sealed class StoryStageSlotView : MonoBehaviour
{
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private StoryStageConfig storyStageConfig;

    private StoryRadioSlotView radioSlot;

    public StoryRadioSlotView RadioSlot
    {
        get
        {
            CacheComponents();
            return radioSlot;
        }
    }
    public StoryStageConfig StoryStageConfig => storyStageConfig;
    public bool IsSelectable => radioSlot != null && !radioSlot.IsLocked && storyStageConfig != null;

    public void Configure(StoryStageConfig config, string displayName, bool locked)
    {
        CacheComponents();
        storyStageConfig = config;

        if (labelText != null)
        {
            labelText.text = string.IsNullOrWhiteSpace(displayName)
                ? config != null ? config.name : string.Empty
                : displayName;
        }

        if (radioSlot != null)
        {
            radioSlot.SetLocked(locked);
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

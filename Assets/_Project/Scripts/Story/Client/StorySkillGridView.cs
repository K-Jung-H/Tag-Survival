using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class StorySkillGridView : MonoBehaviour
{
    [SerializeField] private StorySkillSlotView[] slots = Array.Empty<StorySkillSlotView>();
    [SerializeField] private ToggleGroup toggleGroup;

    public event Action<SkillDefinition> SelectionChanged;
    public SkillDefinition SelectedDefinition { get; private set; }

    public void Configure(StorySkillSlotView[] newSlots, ToggleGroup newToggleGroup)
    {
        slots = newSlots ?? Array.Empty<StorySkillSlotView>();
        toggleGroup = newToggleGroup;
    }

    private void OnEnable()
    {
        Initialize();
    }

    private void OnDisable()
    {
        if (slots == null)
        {
            return;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            StorySkillSlotView slot = slots[i];
            if (slot == null)
            {
                continue;
            }

            slot.RadioSlot?.Unbind();
        }
    }

    public void Initialize()
    {
        BindSlots();
        ApplySelection(FindFirstAvailableIndex());
    }

    private void OnSlotChanged(StoryRadioSlotView radioSlot, bool isOn)
    {
        if (!isOn || radioSlot == null)
        {
            return;
        }

        ApplySelection(FindSlotIndex(radioSlot));
    }

    private void BindSlots()
    {
        if (slots == null)
        {
            return;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
            {
                slots[i].RadioSlot?.Bind(toggleGroup, OnSlotChanged);
            }
        }
    }

    private int FindFirstAvailableIndex()
    {
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && slots[i].IsSelectable)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private void ApplySelection(int selectedIndex)
    {
        if (slots == null || selectedIndex < 0 || selectedIndex >= slots.Length || slots[selectedIndex] == null)
        {
            SelectedDefinition = null;
            SelectionChanged?.Invoke(null);
            return;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
            {
                slots[i].RadioSlot?.SetSelected(i == selectedIndex);
            }
        }

        SelectedDefinition = slots[selectedIndex].SkillDefinition;
        SelectionChanged?.Invoke(SelectedDefinition);
    }

    private int FindSlotIndex(StoryRadioSlotView radioSlot)
    {
        if (slots == null)
        {
            return -1;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].RadioSlot == radioSlot)
            {
                return i;
            }
        }

        return -1;
    }
}

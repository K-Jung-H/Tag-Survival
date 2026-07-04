using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class StoryStageGridView : MonoBehaviour
{
    [SerializeField] private StoryStageSlotView[] slots = Array.Empty<StoryStageSlotView>();
    [SerializeField] private ToggleGroup toggleGroup;

    public event Action<StoryStageConfig> SelectionChanged;
    public StoryStageConfig SelectedConfig { get; private set; }

    public void Configure(StoryStageSlotView[] newSlots, ToggleGroup newToggleGroup)
    {
        slots = newSlots ?? Array.Empty<StoryStageSlotView>();
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
            StoryStageSlotView slot = slots[i];
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
        SelectFirstAvailable();
    }

    private void OnSlotChanged(StoryRadioSlotView radioSlot, bool isOn)
    {
        if (!isOn || radioSlot == null || radioSlot.IsLocked)
        {
            return;
        }

        int index = FindSlotIndex(radioSlot);
        if (index < 0)
        {
            return;
        }

        ApplySelection(index);
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

    private void SelectFirstAvailable()
    {
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && slots[i].IsSelectable)
                {
                    ApplySelection(i);
                    return;
                }
            }
        }

        SelectedConfig = null;
        SelectionChanged?.Invoke(null);
    }

    private void ApplySelection(int selectedIndex)
    {
        if (slots == null || selectedIndex < 0 || selectedIndex >= slots.Length || slots[selectedIndex] == null)
        {
            SelectedConfig = null;
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

        SelectedConfig = slots[selectedIndex].StoryStageConfig;
        SelectionChanged?.Invoke(SelectedConfig);
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

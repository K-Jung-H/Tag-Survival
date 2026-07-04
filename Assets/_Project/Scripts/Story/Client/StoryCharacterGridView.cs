using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class StoryCharacterGridView : MonoBehaviour
{
    [SerializeField] private StoryCharacterSlotView[] slots = Array.Empty<StoryCharacterSlotView>();
    [SerializeField] private ToggleGroup toggleGroup;

    public event Action<CharacterDefinition> SelectionChanged;
    public CharacterDefinition SelectedDefinition { get; private set; }

    public void Configure(StoryCharacterSlotView[] newSlots, ToggleGroup newToggleGroup)
    {
        slots = newSlots ?? Array.Empty<StoryCharacterSlotView>();
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
            StoryCharacterSlotView slot = slots[i];
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
                slots[i].RefreshVisuals();
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

        SelectedDefinition = slots[selectedIndex].CharacterDefinition;
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

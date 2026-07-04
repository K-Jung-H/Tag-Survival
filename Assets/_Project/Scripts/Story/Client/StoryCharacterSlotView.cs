using UnityEngine;

[RequireComponent(typeof(StoryRadioSlotView))]
public sealed class StoryCharacterSlotView : MonoBehaviour
{
    [SerializeField] private CharacterDefinition characterDefinition;

    private const float NormalAlpha = 0.85f;
    private const float SelectedAlpha = 1f;

    private readonly Color lockedColor = new(0.35f, 0.35f, 0.35f, 0.8f);
    private StoryRadioSlotView radioSlot;

    public StoryRadioSlotView RadioSlot
    {
        get
        {
            CacheComponents();
            return radioSlot;
        }
    }
    public CharacterDefinition CharacterDefinition => characterDefinition;
    public bool IsSelectable => RadioSlot != null && !RadioSlot.IsLocked && characterDefinition != null;

    private void Awake()
    {
        RefreshVisuals();
    }

    private void OnValidate()
    {
        RefreshVisuals();
    }

    public void Configure(CharacterDefinition definition)
    {
        CacheComponents();
        characterDefinition = definition;
        RefreshVisuals();
    }

    public void RefreshVisuals()
    {
        CacheComponents();
        ApplyCharacterColor();

        if (radioSlot != null)
        {
            radioSlot.SetLocked(characterDefinition == null);
        }
    }

    private void CacheComponents()
    {
        if (radioSlot == null)
        {
            radioSlot = GetComponent<StoryRadioSlotView>();
        }
    }

    private void ApplyCharacterColor()
    {
        if (radioSlot == null)
        {
            return;
        }

        Color baseColor = characterDefinition != null ? characterDefinition.CharacterColor : Color.white;
        Color normal = WithAlpha(Color.Lerp(Color.black, baseColor, 0.72f), NormalAlpha);
        Color selected = WithAlpha(Color.Lerp(baseColor, Color.white, 0.35f), SelectedAlpha);
        radioSlot.SetColorPalette(normal, selected, lockedColor);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}

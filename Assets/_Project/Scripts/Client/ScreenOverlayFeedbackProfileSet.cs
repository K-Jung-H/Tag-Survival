using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Feedback/Screen Overlay Feedback Profile Set")]
public sealed class ScreenOverlayFeedbackProfileSet : ScriptableObject
{
    [SerializeField] private ScreenOverlayFeedbackProfile[] profiles = Array.Empty<ScreenOverlayFeedbackProfile>();

    public ScreenOverlayFeedbackProfile[] Profiles => profiles ?? Array.Empty<ScreenOverlayFeedbackProfile>();

    public bool TryGet(ScreenOverlayFeedbackType type, out ScreenOverlayFeedbackProfile profile)
    {
        ScreenOverlayFeedbackProfile[] profileArray = Profiles;
        for (int i = 0; i < profileArray.Length; i++)
        {
            if (profileArray[i].type != type)
            {
                continue;
            }

            profile = profileArray[i];
            return true;
        }

        profile = default;
        return false;
    }

    private void OnValidate()
    {
        HashSet<ScreenOverlayFeedbackType> registeredTypes = new();
        ScreenOverlayFeedbackProfile[] profileArray = Profiles;
        for (int i = 0; i < profileArray.Length; i++)
        {
            ScreenOverlayFeedbackType type = profileArray[i].type;
            if (type == ScreenOverlayFeedbackType.None)
            {
                continue;
            }

            if (!registeredTypes.Add(type))
            {
                Debug.LogWarning(
                    $"[ScreenOverlayFeedbackProfileSet] Duplicate feedback type '{type}' found at index {i}. The first profile will be used.",
                    this);
            }
        }
    }
}

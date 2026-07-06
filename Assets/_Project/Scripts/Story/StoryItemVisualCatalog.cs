using System;
using UnityEngine;

[Serializable]
public struct StoryItemVisualData
{
    [SerializeField] private int visualIndex;
    [SerializeField] private AnimationClip idleClip;

    public int VisualIndex => visualIndex;
    public AnimationClip IdleClip => idleClip;
}

[CreateAssetMenu(menuName = "Tag Survival/Story/Story Item Visual Catalog")]
public sealed class StoryItemVisualCatalog : ScriptableObject
{
    [SerializeField] private StoryItemVisualData[] visuals = Array.Empty<StoryItemVisualData>();

    public bool TryGetVisual(int visualIndex, out StoryItemVisualData visual)
    {
        StoryItemVisualData[] safeVisuals = visuals ?? Array.Empty<StoryItemVisualData>();
        for (int i = 0; i < safeVisuals.Length; i++)
        {
            if (safeVisuals[i].VisualIndex != visualIndex)
            {
                continue;
            }

            visual = safeVisuals[i];
            return true;
        }

        visual = default;
        return false;
    }
}

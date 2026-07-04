using System;
using UnityEngine;

[Serializable]
public struct StoryStageCatalogEntry
{
    [SerializeField] private StoryStageConfig stageConfig;
    [SerializeField] private Sprite thumbnail;
    [SerializeField] private string displayName;
    [SerializeField] private bool unlockedByDefault;
    [SerializeField] private string unlockPlayerPrefsKey;

    public StoryStageConfig StageConfig => stageConfig;
    public Sprite Thumbnail => thumbnail;
    public string DisplayName => !string.IsNullOrWhiteSpace(displayName)
        ? displayName
        : stageConfig != null ? stageConfig.name : "Story Stage";

    public bool IsUnlocked()
    {
        return unlockedByDefault
            || (!string.IsNullOrWhiteSpace(unlockPlayerPrefsKey)
                && PlayerPrefs.GetInt(unlockPlayerPrefsKey, 0) != 0);
    }
}

[CreateAssetMenu(menuName = "Tag Survival/Story/Story Stage Catalog")]
public sealed class StoryStageCatalog : ScriptableObject
{
    [SerializeField] private StoryStageCatalogEntry[] entries = Array.Empty<StoryStageCatalogEntry>();

    public int Count => entries != null ? entries.Length : 0;

    public bool TryGetByIndex(int index, out StoryStageCatalogEntry entry)
    {
        if (entries != null && index >= 0 && index < entries.Length)
        {
            entry = entries[index];
            return true;
        }

        entry = default;
        return false;
    }
}

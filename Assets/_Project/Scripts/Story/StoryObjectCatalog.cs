using System;
using UnityEngine;

[Serializable]
public struct StoryObjectCatalogEntry
{
    [SerializeField] private int index;
    [SerializeField] private GameObject prefab;

    public int Index => index;
    public GameObject Prefab => prefab;
}

[CreateAssetMenu(menuName = "Tag Survival/Story/Story Object Catalog")]
public sealed class StoryObjectCatalog : ScriptableObject
{
    public const int GoalObjectIndex = 0;

    [SerializeField] private StoryObjectCatalogEntry[] entries = Array.Empty<StoryObjectCatalogEntry>();

    public bool TryGetPrefab(int index, out GameObject prefab)
    {
        StoryObjectCatalogEntry[] safeEntries = entries ?? Array.Empty<StoryObjectCatalogEntry>();
        for (int i = 0; i < safeEntries.Length; i++)
        {
            if (safeEntries[i].Index != index)
            {
                continue;
            }

            prefab = safeEntries[i].Prefab;
            return prefab != null;
        }

        prefab = null;
        return false;
    }

    public bool TryGetGoalPrefab(out GameObject prefab)
    {
        return TryGetPrefab(GoalObjectIndex, out prefab);
    }
}

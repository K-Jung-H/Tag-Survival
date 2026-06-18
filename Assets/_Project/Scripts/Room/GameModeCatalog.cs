using System;
using UnityEngine;

[Serializable]
public struct GameModeCatalogEntry
{
    [SerializeField] private bool isRandom;
    [SerializeField] private Sprite icon;
    [SerializeField] private string displayName;
    [SerializeField] private GameModeType modeType;
    [SerializeField] private GameModeConfig config;

    public bool IsRandom => isRandom;
    public Sprite Icon => icon;
    public string DisplayName => !string.IsNullOrWhiteSpace(displayName)
        ? displayName
        : isRandom ? "Random Mode" : modeType.ToString();
    public GameModeType ModeType => modeType;
    public GameModeConfig Config => config;
}

[CreateAssetMenu(menuName = "Tag Survival/Game Mode/Game Mode Catalog")]
public sealed class GameModeCatalog : ScriptableObject
{
    [SerializeField] private GameModeCatalogEntry[] entries = Array.Empty<GameModeCatalogEntry>();

    public int Count => entries != null ? entries.Length : 0;

    public bool TryGetByIndex(int index, out GameModeCatalogEntry entry)
    {
        if (entries != null && index >= 0 && index < entries.Length)
        {
            entry = entries[index];
            return true;
        }

        entry = default;
        return false;
    }

    public bool TryGetRandomIndex(out ushort index)
    {
        if (entries != null)
        {
            for (int i = 0; i < entries.Length && i <= ushort.MaxValue; i++)
            {
                if (entries[i].IsRandom)
                {
                    index = (ushort)i;
                    return true;
                }
            }
        }

        index = 0;
        return Count > 0;
    }

    public bool TryGetRandomResolvedIndex(out ushort index)
    {
        if (entries == null || entries.Length == 0)
        {
            index = 0;
            return false;
        }

        int candidateCount = 0;
        for (int i = 0; i < entries.Length && i <= ushort.MaxValue; i++)
        {
            if (!entries[i].IsRandom)
            {
                candidateCount++;
            }
        }

        if (candidateCount <= 0)
        {
            index = 0;
            return false;
        }

        int selectedCandidate = UnityEngine.Random.Range(0, candidateCount);
        for (int i = 0; i < entries.Length && i <= ushort.MaxValue; i++)
        {
            if (entries[i].IsRandom)
            {
                continue;
            }

            if (selectedCandidate == 0)
            {
                index = (ushort)i;
                return true;
            }

            selectedCandidate--;
        }

        index = 0;
        return false;
    }
}

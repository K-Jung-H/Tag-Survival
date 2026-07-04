using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct ItemTypeWeightConfig
{
    public ItemType itemType;
    public int weight;
}

public abstract class GameModeConfig : ScriptableObject
{
    [SerializeField] private float gameDurationSeconds = 180f;
    [SerializeField] private int minActiveItems = GameNetProtocol.MaxItems;
    [SerializeField] private int maxActiveItems = GameNetProtocol.MaxItems;
    [SerializeField] private List<ItemTypeWeightConfig> itemTypeWeights = new()
    {
        new ItemTypeWeightConfig { itemType = ItemType.Stats, weight = 1 },
        new ItemTypeWeightConfig { itemType = ItemType.Skill, weight = 1 }
    };

    public abstract GameModeType ModeType { get; }
    public float GameDurationSeconds => Mathf.Max(0f, gameDurationSeconds);
    public int MinActiveItems => Mathf.Clamp(minActiveItems, 0, GameNetProtocol.MaxItems);
    public int MaxActiveItems => Mathf.Clamp(maxActiveItems, MinActiveItems, GameNetProtocol.MaxItems);
    public IReadOnlyList<ItemTypeWeightConfig> ItemTypeWeights => itemTypeWeights;
}

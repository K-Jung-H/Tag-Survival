using System;
using UnityEngine;

[Serializable]
public struct StoryCollectibleSpawnData
{
    public string id;
    public Vector2 position;
}

[Serializable]
public struct StoryEnemySpawnData
{
    public string id;
    public Vector2 position;
    public byte characterId;
}

[CreateAssetMenu(menuName = "Tag Survival/Story/Story Stage Config")]
public sealed class StoryStageConfig : ScriptableObject
{
    [SerializeField] private string stageId;
    [SerializeField] private StageDefinition stageDefinition;
    [SerializeField] private Vector2 playerSpawnPosition;
    [SerializeField] private StoryCollectibleSpawnData[] collectibles = Array.Empty<StoryCollectibleSpawnData>();
    [SerializeField] private Vector2 goalPosition;
    [SerializeField] private StoryEnemySpawnData[] enemies = Array.Empty<StoryEnemySpawnData>();
    [SerializeField] private float stageTimeLimitSeconds = 180f;
    [SerializeField] private float bonusStarTimeLimitSeconds = 90f;

    public string StageId => !string.IsNullOrWhiteSpace(stageId) ? stageId : name;
    public StageDefinition StageDefinition => stageDefinition;
    public Vector2 PlayerSpawnPosition => playerSpawnPosition;
    public StoryCollectibleSpawnData[] Collectibles => collectibles ?? Array.Empty<StoryCollectibleSpawnData>();
    public Vector2 GoalPosition => goalPosition;
    public StoryEnemySpawnData[] Enemies => enemies ?? Array.Empty<StoryEnemySpawnData>();
    public float StageTimeLimitSeconds => Mathf.Max(0f, stageTimeLimitSeconds);
    public float BonusStarTimeLimitSeconds => Mathf.Max(0f, bonusStarTimeLimitSeconds);
}

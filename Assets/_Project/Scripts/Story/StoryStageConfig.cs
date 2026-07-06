using System;
using UnityEngine;

[Serializable]
public struct StoryGoalData
{
    public Vector2 position;
    public Vector2 colliderOffset;
    public Vector2 colliderSize;
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
    [SerializeField] private StoryGoalData goal;
    [SerializeField] private StoryEnemySpawnData[] enemies = Array.Empty<StoryEnemySpawnData>();
    [SerializeField] private float stageTimeLimitSeconds = 180f;
    [SerializeField] private float bonusStarTimeLimitSeconds = 90f;

    public string StageId => !string.IsNullOrWhiteSpace(stageId) ? stageId : name;
    public StageDefinition StageDefinition => stageDefinition;
    public Vector2 PlayerSpawnPosition => playerSpawnPosition;
    public StoryGoalData Goal => goal;
    public StoryEnemySpawnData[] Enemies => enemies ?? Array.Empty<StoryEnemySpawnData>();
    public float StageTimeLimitSeconds => Mathf.Max(0f, stageTimeLimitSeconds);
    public float BonusStarTimeLimitSeconds => Mathf.Max(0f, bonusStarTimeLimitSeconds);

#if UNITY_EDITOR
    public void SetStageBakeResult(
        StageDefinition newStageDefinition,
        Vector2 newPlayerSpawnPosition,
        StoryGoalData newGoal)
    {
        stageDefinition = newStageDefinition;
        playerSpawnPosition = newPlayerSpawnPosition;
        goal = newGoal;
    }
#endif
}

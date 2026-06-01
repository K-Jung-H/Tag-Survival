using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Stage/Stage Definition")]
public sealed class StageDefinition : ScriptableObject
{
    [SerializeField] private StageBakeData stageBakeData;
    [SerializeField] private Grid stageGridPrefab;
    [SerializeField] private float gravityScale = 1f;
    [SerializeField] private float maxFallSpeedMultiplier = 1f;

    public StageBakeData StageBakeData => stageBakeData;
    public Grid StageGridPrefab => stageGridPrefab;
    public float GravityScale => Mathf.Max(0f, gravityScale);
    public float MaxFallSpeedMultiplier => Mathf.Max(0.0001f, maxFallSpeedMultiplier);
}

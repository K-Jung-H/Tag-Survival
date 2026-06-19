using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Stage/Stage Bake Data")]
public sealed class StageBakeData : ScriptableObject
{
    [SerializeField] private string stageId = "Stage";
    [SerializeField] private Vector2Int stageOffsetPosition;
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private int uniformGridSize = 8;
    [SerializeField] private StageBoundsData bounds;
    [SerializeField] private StageTileCellData[] cells = new StageTileCellData[0];
    [SerializeField] private StageColliderData[] colliders = new StageColliderData[0];
    [SerializeField] private StageSpatialBucketData[] spatialBuckets = new StageSpatialBucketData[0];
    [SerializeField] private StageSpawnPoint[] spawnPoints = new StageSpawnPoint[0];

    public string StageId => stageId;
    public Vector2Int StageOffsetPosition => stageOffsetPosition;
    public float CellSize => cellSize;
    public int UniformGridSize => uniformGridSize;
    public StageBoundsData Bounds => bounds;
    public StageTileCellData[] Cells => cells;
    public StageColliderData[] Colliders => colliders;
    public StageSpatialBucketData[] SpatialBuckets => spatialBuckets;
    public StageSpawnPoint[] SpawnPoints => spawnPoints;

    // - Role: Set bake result.
    public void SetBakeResult(
        string newStageId,
        Vector2Int newStageOffsetPosition,
        float newCellSize,
        int newUniformGridSize,
        StageBoundsData newBounds,
        StageTileCellData[] newCells,
        StageColliderData[] newColliders,
        StageSpatialBucketData[] newSpatialBuckets,
        StageSpawnPoint[] newSpawnPoints)
    {
        stageId = string.IsNullOrWhiteSpace(newStageId) ? name : newStageId;
        stageOffsetPosition = newStageOffsetPosition;
        cellSize = Mathf.Max(0.0001f, newCellSize);
        uniformGridSize = Mathf.Max(1, newUniformGridSize);
        bounds = newBounds;
        cells = newCells ?? new StageTileCellData[0];
        colliders = newColliders ?? new StageColliderData[0];
        spatialBuckets = newSpatialBuckets ?? new StageSpatialBucketData[0];
        spawnPoints = newSpawnPoints ?? new StageSpawnPoint[0];
    }
}

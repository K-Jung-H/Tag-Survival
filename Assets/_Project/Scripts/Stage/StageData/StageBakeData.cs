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

    public string StageId => stageId;
    public Vector2Int StageOffsetPosition => stageOffsetPosition;
    public float CellSize => cellSize;
    public int UniformGridSize => uniformGridSize;
    public StageBoundsData Bounds => bounds;
    public StageTileCellData[] Cells => cells;
    public StageColliderData[] Colliders => colliders;
    public StageSpatialBucketData[] SpatialBuckets => spatialBuckets;

    // Role: StageBaker가 생성한 결과를 이 ScriptableObject에 덮어쓴다.
    // Parameters:
    // - newStageId: 저장할 Stage 식별자
    // - newStageOffsetPosition: 원본 Tilemap bounds의 좌측 하단 셀 좌표
    // - newCellSize: Bake 기준 셀 크기
    // - newUniformGridSize: 공간 분할 버킷의 셀 단위 크기
    // - newBounds: Stage 경계와 크기 데이터
    // - newCells: Bake된 타일 셀 데이터
    // - newColliders: 서버 충돌에 사용할 충돌체 데이터
    // - newSpatialBuckets: 충돌체 조회 최적화용 공간 분할 데이터
    public void SetBakeResult(
        string newStageId,
        Vector2Int newStageOffsetPosition,
        float newCellSize,
        int newUniformGridSize,
        StageBoundsData newBounds,
        StageTileCellData[] newCells,
        StageColliderData[] newColliders,
        StageSpatialBucketData[] newSpatialBuckets)
    {
        stageId = string.IsNullOrWhiteSpace(newStageId) ? name : newStageId;
        stageOffsetPosition = newStageOffsetPosition;
        cellSize = Mathf.Max(0.0001f, newCellSize);
        uniformGridSize = Mathf.Max(1, newUniformGridSize);
        bounds = newBounds;
        cells = newCells ?? new StageTileCellData[0];
        colliders = newColliders ?? new StageColliderData[0];
        spatialBuckets = newSpatialBuckets ?? new StageSpatialBucketData[0];
    }
}

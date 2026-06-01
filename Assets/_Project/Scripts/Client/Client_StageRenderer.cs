using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class Client_StageRenderer : MonoBehaviour
{
    [SerializeField] private StageDefinition stageDefinition;

    private Grid runtimeStageGrid;

    // Role: 실행 시 렌더링에 사용할 Grid를 준비하고 Bake 기준 좌표 보정을 적용한다.
    private void Awake()
    {
        EnsureRuntimeStageGrid();
        ApplyStageOffset();
        EnableTilemapRenderers();
    }

    // Role: StageBakeData의 좌측 하단 기준 오프셋에 맞춰 Grid 위치를 보정한다.
    public void ApplyStageOffset()
    {
        Grid targetGrid = GetTargetGrid();
        if (targetGrid == null)
        {
            Debug.LogWarning("[Client_StageRenderer] Stage Grid is not assigned.", this);
            return;
        }

        StageBakeData stageBakeData = stageDefinition != null ? stageDefinition.StageBakeData : null;
        if (stageBakeData == null)
        {
            Debug.LogWarning("[Client_StageRenderer] StageBakeData is not assigned.", this);
            return;
        }

        Vector2Int offset = stageBakeData.StageOffsetPosition;
        Vector3 cellSize = targetGrid.cellSize;
        targetGrid.transform.localPosition = new Vector3(
            -offset.x * cellSize.x,
            -offset.y * cellSize.y,
            targetGrid.transform.localPosition.z);
    }

    // Role: 씬 Grid 또는 프리팹 Grid를 런타임 렌더링 대상으로 준비한다.
    private void EnsureRuntimeStageGrid()
    {
        Grid stageGridPrefab = stageDefinition != null ? stageDefinition.StageGridPrefab : null;
        if (stageGridPrefab == null)
        {
            Debug.LogWarning("[Client_StageRenderer] StageDefinition or Stage Grid Prefab is not assigned.", this);
            return;
        }

        if (stageGridPrefab.gameObject.scene.IsValid())
        {
            runtimeStageGrid = stageGridPrefab;
            return;
        }

        runtimeStageGrid = Instantiate(stageGridPrefab, transform);
        runtimeStageGrid.name = stageGridPrefab.name;
        runtimeStageGrid.gameObject.SetActive(true);
    }

    // Role: 현재 렌더링에 사용할 Grid 인스턴스를 반환한다.
    private Grid GetTargetGrid()
    {
        if (runtimeStageGrid != null)
        {
            return runtimeStageGrid;
        }

        return stageDefinition != null ? stageDefinition.StageGridPrefab : null;
    }

    // Role: Grid 하위 TilemapRenderer들을 활성화하여 타일맵을 화면에 표시한다.
    private void EnableTilemapRenderers()
    {
        Grid targetGrid = GetTargetGrid();
        if (targetGrid == null)
        {
            return;
        }

        targetGrid.gameObject.SetActive(true);

        TilemapRenderer[] renderers = targetGrid.GetComponentsInChildren<TilemapRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].gameObject.SetActive(true);
            renderers[i].enabled = true;
        }
    }
}

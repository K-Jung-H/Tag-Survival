using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class Client_StageRenderer : MonoBehaviour
{
    [SerializeField] private StageDefinition stageDefinition;

    private Grid runtimeStageGrid;

    // - Role: Set up needed links before start.
    private void Awake()
    {
        EnsureRuntimeStageGrid();
        ApplyStageOffset();
        EnableTilemapRenderers();
    }

    // - Role: Apply stage offset.
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

    // - Role: Make sure the runtime stage grid exists.
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

    // - Role: Get target grid.
    private Grid GetTargetGrid()
    {
        if (runtimeStageGrid != null)
        {
            return runtimeStageGrid;
        }

        return stageDefinition != null ? stageDefinition.StageGridPrefab : null;
    }

    // - Role: Enable tilemap renderers.
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

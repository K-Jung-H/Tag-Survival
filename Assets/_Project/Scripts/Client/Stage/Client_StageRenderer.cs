using UnityEngine;

public sealed class Client_StageRenderer : MonoBehaviour
{
    private StageDefinition stageDefinition;
    private Camera targetCamera;
    private StageRenderBinding runtimeStageRender;
    private bool isConfigured;

    // - Role: Configure and build stage rendering.
    public void Configure(StageDefinition newStageDefinition, Camera newTargetCamera)
    {
        stageDefinition = newStageDefinition;
        targetCamera = newTargetCamera;
        isConfigured = true;

        EnsureRuntimeStageRender();
        ApplyStageOffset();
        EnableRenderers();
        BindParallaxLayers();
    }

    // - Role: Apply stage offset.
    public void ApplyStageOffset()
    {
        if (!isConfigured)
        {
            return;
        }

        StageRenderBinding targetStageRender = GetTargetStageRender();
        if (targetStageRender == null || targetStageRender.Grid == null)
        {
            Debug.LogWarning("[Client_StageRenderer] Stage render prefab or Grid binding is not assigned.", this);
            return;
        }

        StageBakeData stageBakeData = stageDefinition != null ? stageDefinition.StageBakeData : null;
        if (stageBakeData == null)
        {
            Debug.LogWarning("[Client_StageRenderer] StageBakeData is not assigned.", this);
            return;
        }

        Vector2Int offset = stageBakeData.StageOffsetPosition;
        Vector3 cellSize = targetStageRender.Grid.cellSize;
        Transform stageRoot = targetStageRender.transform;
        stageRoot.localPosition = new Vector3(
            -offset.x * cellSize.x,
            -offset.y * cellSize.y,
            stageRoot.localPosition.z);
    }

    // - Role: Make sure the runtime stage render exists.
    private void EnsureRuntimeStageRender()
    {
        if (runtimeStageRender != null)
        {
            return;
        }

        GameObject stageRenderPrefab = stageDefinition != null ? stageDefinition.StageRenderPrefab : null;
        if (stageRenderPrefab == null)
        {
            Debug.LogWarning("[Client_StageRenderer] StageDefinition or Stage Render Prefab is not assigned.", this);
            return;
        }

        if (stageRenderPrefab.scene.IsValid())
        {
            runtimeStageRender = stageRenderPrefab.GetComponent<StageRenderBinding>();
            return;
        }

        GameObject instance = Instantiate(stageRenderPrefab, transform);
        instance.name = stageRenderPrefab.name;
        instance.SetActive(true);
        runtimeStageRender = instance.GetComponent<StageRenderBinding>();
        if (runtimeStageRender == null)
        {
            Debug.LogWarning("[Client_StageRenderer] Stage Render Prefab has no StageRenderBinding.", this);
        }
    }

    // - Role: Get target stage render.
    private StageRenderBinding GetTargetStageRender()
    {
        if (runtimeStageRender != null)
        {
            return runtimeStageRender;
        }

        GameObject stageRenderPrefab = stageDefinition != null ? stageDefinition.StageRenderPrefab : null;
        return stageRenderPrefab != null ? stageRenderPrefab.GetComponent<StageRenderBinding>() : null;
    }

    // - Role: Enable stage renderers.
    private void EnableRenderers()
    {
        StageRenderBinding targetStageRender = GetTargetStageRender();
        if (targetStageRender == null)
        {
            return;
        }

        targetStageRender.gameObject.SetActive(true);

        Renderer[] renderers = targetStageRender.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].gameObject.SetActive(true);
            renderers[i].enabled = true;
        }
    }

    // - Role: Bind camera to parallax layers.
    private void BindParallaxLayers()
    {
        StageRenderBinding targetStageRender = GetTargetStageRender();
        if (targetStageRender == null || targetStageRender.BackgroundRoot == null)
        {
            return;
        }

        Camera resolvedCamera = ResolveTargetCamera();
        if (resolvedCamera == null)
        {
            Debug.LogWarning("[Client_StageRenderer] Target camera is not assigned. Stage parallax layers will not move.", this);
            return;
        }

        StageParallaxLayer[] parallaxLayers = targetStageRender.BackgroundRoot.GetComponentsInChildren<StageParallaxLayer>(true);
        for (int i = 0; i < parallaxLayers.Length; i++)
        {
            parallaxLayers[i].BindCamera(resolvedCamera.transform);
        }
    }

    // - Role: Resolve target camera.
    private Camera ResolveTargetCamera()
    {
        return targetCamera;
    }
}

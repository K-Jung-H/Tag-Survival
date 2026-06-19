using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(300)]
public sealed class Client_CameraController : MonoBehaviour
{
    [SerializeField] private Camera cameraComponent;
    [SerializeField] private float lerpFactor = 5f;
    [Header("Idle Zoom")]
    [SerializeField] private bool useIdleZoom = true;
    [SerializeField] private float gameplayZoomSize = 7f;
    [SerializeField] private float idleZoomSize = 5f;
    [SerializeField] private float idleHoldSeconds = 2f;
    [SerializeField] private float idleZoomLerpFactor = 1.5f;
    [SerializeField] private float activeZoomLerpFactor = 8f;
    [SerializeField] private float idleVelocityThreshold = 0.05f;

    private Transform target;
    private StageDefinition stageDefinition;
    private bool followEnabled = true;
    private Coroutine moveRoutine;
    private Coroutine zoomRoutine;
    private Client_SyncManager syncManager;
    private float idleElapsedSeconds;

    // - Role: Get or set the camera target.
    public Transform Target
    {
        get => target;
        set => target = value;
    }

    public bool FollowEnabled
    {
        get => followEnabled;
        set => followEnabled = value;
    }

    public StageDefinition StageDefinition
    {
        get => stageDefinition;
        set => stageDefinition = value;
    }

    // - Role: Bind the camera this follow component controls.
    public void BindCamera(Camera nextCamera)
    {
        cameraComponent = nextCamera;
    }

    // - Role: Bind the sync source used by camera-local effects.
    public void BindSyncManager(Client_SyncManager nextSyncManager)
    {
        syncManager = nextSyncManager;
    }

    // - Role: Enable or disable target follow.
    public void SetFollowEnabled(bool enabledValue)
    {
        followEnabled = enabledValue;
    }

    // - Role: Move camera immediately to a world position.
    public void SnapTo(Vector3 worldPosition)
    {
        StopMove();
        transform.position = ClampToSolidStageBounds(WithCurrentZ(worldPosition));
    }

    // - Role: Move camera smoothly to a world position.
    public void LerpTo(Vector3 worldPosition, float durationSeconds)
    {
        StopMove();
        if (durationSeconds <= 0f)
        {
            SnapTo(worldPosition);
            return;
        }

        moveRoutine = StartCoroutine(LerpPositionRoutine(WithCurrentZ(worldPosition), durationSeconds));
    }

    // - Role: Set orthographic zoom immediately.
    public void SetZoom(float orthographicSize)
    {
        StopZoom();
        ApplyZoom(orthographicSize);
    }

    // - Role: Set orthographic zoom smoothly.
    public void LerpZoom(float orthographicSize, float durationSeconds)
    {
        StopZoom();
        if (durationSeconds <= 0f)
        {
            ApplyZoom(orthographicSize);
            return;
        }

        zoomRoutine = StartCoroutine(LerpZoomRoutine(orthographicSize, durationSeconds));
    }

    // - Role: Smoothly return to gameplay zoom.
    public void LerpToGameplayZoom(float durationSeconds)
    {
        LerpZoom(gameplayZoomSize, durationSeconds);
    }

    // - Role: Immediately return to gameplay zoom.
    public void SetGameplayZoom()
    {
        SetZoom(gameplayZoomSize);
    }

    // - Role: Stop camera movement and zoom routines.
    public void StopManualMotion()
    {
        StopMove();
        StopZoom();
    }

    // - Role: Update this object after normal updates.
    private void LateUpdate()
    {
        if (!followEnabled || target == null)
        {
            return;
        }

        Vector3 currentPosition = transform.position;
        Vector3 targetPosition = new Vector3(target.position.x, target.position.y, currentPosition.z);

        float t = 1f - Mathf.Exp(-Mathf.Max(0f, lerpFactor) * Time.deltaTime);
        Vector3 nextPosition = Vector3.Lerp(currentPosition, targetPosition, t);
        transform.position = ClampToSolidStageBounds(nextPosition);
        UpdateIdleZoom();
    }

    private void UpdateIdleZoom()
    {
        if (!useIdleZoom || zoomRoutine != null || cameraComponent == null || !cameraComponent.orthographic)
        {
            return;
        }

        bool isIdle = TryGetLocalSnapshot(out ClientSnapshotState snapshot)
            && snapshot.locomotionState == LocomotionState.Idle
            && snapshot.velocity.sqrMagnitude <= idleVelocityThreshold * idleVelocityThreshold;
        if (isIdle)
        {
            idleElapsedSeconds += Time.deltaTime;
        }
        else
        {
            idleElapsedSeconds = 0f;
        }

        float targetSize = idleElapsedSeconds >= Mathf.Max(0f, idleHoldSeconds)
            ? idleZoomSize
            : gameplayZoomSize;
        float zoomLerpFactor = targetSize < cameraComponent.orthographicSize
            ? idleZoomLerpFactor
            : activeZoomLerpFactor;
        float t = 1f - Mathf.Exp(-Mathf.Max(0f, zoomLerpFactor) * Time.deltaTime);
        ApplyZoom(Mathf.Lerp(cameraComponent.orthographicSize, targetSize, t));
    }

    private bool TryGetLocalSnapshot(out ClientSnapshotState snapshot)
    {
        snapshot = default;
        if (syncManager == null)
        {
            return false;
        }

        return syncManager.Snapshots.TryGetValue(syncManager.LocalClientId, out snapshot);
    }

    private IEnumerator LerpPositionRoutine(Vector3 targetPosition, float durationSeconds)
    {
        Vector3 startPosition = transform.position;
        float elapsed = 0f;

        while (elapsed < durationSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / durationSeconds);
            transform.position = ClampToSolidStageBounds(Vector3.Lerp(startPosition, targetPosition, t));
            yield return null;
        }

        transform.position = ClampToSolidStageBounds(targetPosition);
        moveRoutine = null;
    }

    private IEnumerator LerpZoomRoutine(float targetSize, float durationSeconds)
    {
        if (cameraComponent == null || !cameraComponent.orthographic)
        {
            yield break;
        }

        float startSize = cameraComponent.orthographicSize;
        float elapsed = 0f;

        while (elapsed < durationSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / durationSeconds);
            ApplyZoom(Mathf.Lerp(startSize, targetSize, t));
            yield return null;
        }

        ApplyZoom(targetSize);
        zoomRoutine = null;
    }

    private void ApplyZoom(float orthographicSize)
    {
        if (cameraComponent == null || !cameraComponent.orthographic)
        {
            return;
        }

        cameraComponent.orthographicSize = Mathf.Max(0.01f, orthographicSize);
        transform.position = ClampToSolidStageBounds(transform.position);
    }

    private void StopMove()
    {
        if (moveRoutine == null)
        {
            return;
        }

        StopCoroutine(moveRoutine);
        moveRoutine = null;
    }

    private void StopZoom()
    {
        if (zoomRoutine == null)
        {
            return;
        }

        StopCoroutine(zoomRoutine);
        zoomRoutine = null;
    }

    private Vector3 WithCurrentZ(Vector3 worldPosition)
    {
        return new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
    }

    // - Role: Keep camera view inside solid stage bounds.
    private Vector3 ClampToSolidStageBounds(Vector3 position)
    {
        StageBakeData stageBakeData = stageDefinition != null ? stageDefinition.StageBakeData : null;
        if (stageBakeData == null || cameraComponent == null || !cameraComponent.orthographic)
        {
            return position;
        }

        StageBoundsData bounds = stageBakeData.Bounds;
        Vector2 stageSize = new Vector2(
            bounds.sizeInCells.x * stageBakeData.CellSize,
            bounds.sizeInCells.y * stageBakeData.CellSize);
        float halfHeight = cameraComponent.orthographicSize;
        float halfWidth = halfHeight * cameraComponent.aspect;

        if (bounds.left == StageBoundaryMode.Solid && bounds.right == StageBoundaryMode.Solid)
        {
            position.x = stageSize.x <= halfWidth * 2f
                ? stageSize.x * 0.5f
                : Mathf.Clamp(position.x, halfWidth, stageSize.x - halfWidth);
        }
        else
        {
            if (bounds.left == StageBoundaryMode.Solid)
            {
                position.x = Mathf.Max(position.x, halfWidth);
            }

            if (bounds.right == StageBoundaryMode.Solid)
            {
                position.x = Mathf.Min(position.x, stageSize.x - halfWidth);
            }
        }

        if (bounds.bottom == StageBoundaryMode.Solid && bounds.top == StageBoundaryMode.Solid)
        {
            position.y = stageSize.y <= halfHeight * 2f
                ? stageSize.y * 0.5f
                : Mathf.Clamp(position.y, halfHeight, stageSize.y - halfHeight);
        }
        else
        {
            if (bounds.bottom == StageBoundaryMode.Solid)
            {
                position.y = Mathf.Max(position.y, halfHeight);
            }

            if (bounds.top == StageBoundaryMode.Solid)
            {
                position.y = Mathf.Min(position.y, stageSize.y - halfHeight);
            }
        }

        return position;
    }
}

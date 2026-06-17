using UnityEngine;

[DefaultExecutionOrder(300)]
public sealed class Client_CameraFollow : MonoBehaviour
{
    [SerializeField] private float lerpFactor = 5f;

    private Transform target;
    private StageDefinition stageDefinition;
    private Camera cachedCamera;

    // - Role: Get or set the camera target.
    public Transform Target
    {
        get => target;
        set => target = value;
    }

    public StageDefinition StageDefinition
    {
        get => stageDefinition;
        set => stageDefinition = value;
    }

    // - Role: Cache components.
    private void Awake()
    {
        cachedCamera = GetComponent<Camera>();
    }

    // - Role: Update this object after normal updates.
    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 currentPosition = transform.position;
        Vector3 targetPosition = new Vector3(target.position.x, target.position.y, currentPosition.z);

        float t = 1f - Mathf.Exp(-Mathf.Max(0f, lerpFactor) * Time.deltaTime);
        Vector3 nextPosition = Vector3.Lerp(currentPosition, targetPosition, t);
        transform.position = ClampToSolidStageBounds(nextPosition);
    }

    // - Role: Keep camera view inside solid stage bounds.
    private Vector3 ClampToSolidStageBounds(Vector3 position)
    {
        StageBakeData stageBakeData = stageDefinition != null ? stageDefinition.StageBakeData : null;
        if (stageBakeData == null || cachedCamera == null || !cachedCamera.orthographic)
        {
            return position;
        }

        StageBoundsData bounds = stageBakeData.Bounds;
        Vector2 stageSize = new Vector2(
            bounds.sizeInCells.x * stageBakeData.CellSize,
            bounds.sizeInCells.y * stageBakeData.CellSize);
        float halfHeight = cachedCamera.orthographicSize;
        float halfWidth = halfHeight * cachedCamera.aspect;

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

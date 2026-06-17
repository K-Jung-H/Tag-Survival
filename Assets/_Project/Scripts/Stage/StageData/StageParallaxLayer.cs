using UnityEngine;

public sealed class StageParallaxLayer : MonoBehaviour
{
    [SerializeField] private Vector2 factor = new Vector2(0.2f, 0.2f);

    private Transform cameraTransform;
    private Vector3 startPosition;
    private Vector3 cameraStartPosition;

    public Vector2 Factor => factor;

    // - Role: Bind the camera used as the parallax reference.
    public void BindCamera(Transform newCameraTransform)
    {
        cameraTransform = newCameraTransform;
        startPosition = transform.position;

        if (cameraTransform != null)
        {
            cameraStartPosition = cameraTransform.position;
        }
    }

    // - Role: Move this layer by camera delta.
    private void LateUpdate()
    {
        if (cameraTransform == null)
        {
            return;
        }

        Vector3 cameraDelta = cameraTransform.position - cameraStartPosition;
        transform.position = new Vector3(
            startPosition.x + cameraDelta.x * factor.x,
            startPosition.y + cameraDelta.y * factor.y,
            startPosition.z);
    }
}

using UnityEngine;

[DefaultExecutionOrder(300)]
public sealed class Client_CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float lerpFactor = 5f;

    // - Role: Get or set the camera target.
    public Transform Target
    {
        get => target;
        set => target = value;
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
        transform.position = Vector3.Lerp(currentPosition, targetPosition, t);
    }
}

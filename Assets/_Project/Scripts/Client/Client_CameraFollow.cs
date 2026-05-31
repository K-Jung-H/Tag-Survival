using UnityEngine;

public sealed class Client_CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float lerpFactor = 5f;

    // Role: 카메라가 추적할 대상을 조회하거나 설정한다.
    public Transform Target
    {
        get => target;
        set => target = value;
    }

    // Role: 렌더링 직전에 target 위치를 보간하여 따라간다.
    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 currentPosition = transform.position;
        Vector3 targetPosition = new Vector3(
            target.position.x,
            target.position.y,
            currentPosition.z);

        float t = 1f - Mathf.Exp(-Mathf.Max(0f, lerpFactor) * Time.deltaTime);
        transform.position = Vector3.Lerp(currentPosition, targetPosition, t);
    }
}

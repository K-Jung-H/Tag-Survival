using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class Client_MobileJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, ICancelHandler
{
    [SerializeField] private RectTransform areaRectTransform;
    [SerializeField] private Image areaBackgroundImage;
    [SerializeField] private RectTransform handle;
    [SerializeField] private float deadZone = 0.08f;
    [SerializeField] private float maxHandleDistance = 56f;

    private RectTransform rectTransform;
    private int activePointerId = int.MinValue;
    private bool hadInputDuringPress;
    private bool hasPendingRelease;
    private Vector2 lastInputDuringPress;
    private Vector2 pendingReleaseValue;

    public Vector2 Value { get; private set; }
    public bool IsPressed => activePointerId != int.MinValue;
    public bool HasInput => Value.sqrMagnitude > 0.0001f;

    // - Role: Set up needed links before start.
    private void Awake()
    {
        rectTransform = transform as RectTransform;
        ResetJoystick(clearPendingRelease: true);
    }

    // - Role: Check editor values after they change.
    private void OnValidate()
    {
        rectTransform = transform as RectTransform;
    }

    // - Role: Turn off links when this object is disabled.
    private void OnDisable()
    {
        ResetJoystick(clearPendingRelease: true);
    }

    // - Role: Handle pointer down.
    public void OnPointerDown(PointerEventData eventData)
    {
        if (IsPressed && activePointerId != eventData.pointerId)
            return;

        activePointerId = eventData.pointerId;
        hadInputDuringPress = false;
        lastInputDuringPress = Vector2.zero;
        UpdateValue(eventData);
    }

    // - Role: Handle drag.
    public void OnDrag(PointerEventData eventData)
    {
        if (activePointerId != eventData.pointerId)
            return;

        UpdateValue(eventData);
    }

    // - Role: Handle pointer up.
    public void OnPointerUp(PointerEventData eventData)
    {
        if (activePointerId != eventData.pointerId)
            return;

        hasPendingRelease = hadInputDuringPress;
        pendingReleaseValue = hadInputDuringPress ? lastInputDuringPress : Vector2.zero;
        ResetJoystick(clearPendingRelease: false);
    }

    // - Role: Handle cancel.
    public void OnCancel(BaseEventData eventData)
    {
        ResetJoystick(clearPendingRelease: true);
    }

    // - Role: Consume the saved release input.
    public bool ConsumeRelease(out Vector2 releaseValue)
    {
        releaseValue = pendingReleaseValue;

        bool result = hasPendingRelease && releaseValue.sqrMagnitude > 0.0001f;
        hasPendingRelease = false;
        pendingReleaseValue = Vector2.zero;
        return result;
    }

    // - Role: Set cooldown ready progress.
    public void SetCooldownReadyProgress(float readyProgress)
    {
        if (areaBackgroundImage == null)
            return;

        areaBackgroundImage.type = Image.Type.Filled;
        areaBackgroundImage.fillMethod = Image.FillMethod.Radial360;
        areaBackgroundImage.fillOrigin = (int)Image.Origin360.Top;
        areaBackgroundImage.fillClockwise = true;
        areaBackgroundImage.fillAmount = Mathf.Clamp01(readyProgress);
    }

    // - Role: Update value.
    private void UpdateValue(PointerEventData eventData)
    {
        if (rectTransform == null)
            return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return;
        }

        float radius = Mathf.Max(1f, Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f);
        Vector2 rawValue = Vector2.ClampMagnitude(localPoint / radius, 1f);
        Value = ApplyDeadZone(rawValue);

        if (HasInput)
        {
            hadInputDuringPress = true;
            lastInputDuringPress = Value.normalized;
        }

        SetHandlePosition(Value);
    }

    // - Role: Apply dead zone.
    private Vector2 ApplyDeadZone(Vector2 rawValue)
    {
        float magnitude = rawValue.magnitude;
        if (magnitude <= deadZone)
            return Vector2.zero;

        float normalizedMagnitude = Mathf.InverseLerp(deadZone, 1f, magnitude);
        return rawValue.normalized * normalizedMagnitude;
    }

    // - Role: Reset joystick state.
    private void ResetJoystick(bool clearPendingRelease)
    {
        activePointerId = int.MinValue;
        Value = Vector2.zero;
        hadInputDuringPress = false;
        lastInputDuringPress = Vector2.zero;

        if (clearPendingRelease)
        {
            hasPendingRelease = false;
            pendingReleaseValue = Vector2.zero;
        }

        SetHandlePosition(Vector2.zero);
    }

    // - Role: Set handle position.
    private void SetHandlePosition(Vector2 value)
    {
        if (handle == null)
            return;

        handle.anchoredPosition = Vector2.ClampMagnitude(value, 1f) * maxHandleDistance;
    }
}

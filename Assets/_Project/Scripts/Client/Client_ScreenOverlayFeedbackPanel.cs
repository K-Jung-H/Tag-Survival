using System.Collections.Generic;
using UnityEngine;

public sealed class Client_ScreenOverlayFeedbackPanel : MonoBehaviour
{
    [Header("Optional Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private List<string> showTriggerNames = new();
    [SerializeField] private List<string> hideTriggerNames = new();

    [Header("Optional Overlay View")]
    [SerializeField] private Client_TaggerStunnedOverlayView taggerStunnedOverlayView;

    private float hideTimer;
    private bool waitingForHide;

    // - Role: Apply shader values received from the router.
    public void SetCenter(Vector2 centerUv)
    {
        taggerStunnedOverlayView?.SetCenter(centerUv);
    }

    // - Role: Show this overlay panel.
    public void Show(GameFeedbackData data, Vector2 centerUv)
    {
        waitingForHide = false;
        hideTimer = 0f;
        gameObject.SetActive(true);
        SetCenter(centerUv);
        taggerStunnedOverlayView?.Show();
        SetTriggers(showTriggerNames);
    }

    // - Role: Hide this overlay panel.
    public void Hide(GameFeedbackData data, Vector2 centerUv)
    {
        if (!gameObject.activeSelf)
        {
            return;
        }

        SetCenter(centerUv);
        SetTriggers(hideTriggerNames);
        taggerStunnedOverlayView?.Hide();
        hideTimer = ResolveHideDuration(data);
        waitingForHide = true;

        if (hideTimer <= 0f)
        {
            gameObject.SetActive(false);
            waitingForHide = false;
        }
    }

    private void Update()
    {
        if (!waitingForHide)
        {
            return;
        }

        hideTimer -= Time.unscaledDeltaTime;
        if (hideTimer > 0f)
        {
            return;
        }

        waitingForHide = false;
        gameObject.SetActive(false);
    }

    private void SetTriggers(List<string> triggerNames)
    {
        if (animator == null || triggerNames == null)
        {
            return;
        }

        for (int i = 0; i < triggerNames.Count; i++)
        {
            string triggerName = triggerNames[i];
            if (string.IsNullOrWhiteSpace(triggerName))
            {
                continue;
            }

            animator.SetTrigger(triggerName);
        }
    }

    private float ResolveHideDuration(GameFeedbackData data)
    {
        if (data.lifetimeSeconds > 0f)
        {
            return data.lifetimeSeconds;
        }

        return taggerStunnedOverlayView != null ? taggerStunnedOverlayView.HideDuration : 0f;
    }
}

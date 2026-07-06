using UnityEngine;

public sealed class StoryItemView : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip baseIdleClip;
    [SerializeField] private string collectStateName = "Collect";
    [SerializeField] private int layerIndex;
    [SerializeField] private float transitionDurationSeconds;

    private RuntimeAnimatorController baseController;
    private AnimatorOverrideController overrideController;
    private bool isWaitingForCollectStateEnd;
    private bool hasEnteredCollectState;

    public int ItemIndex { get; private set; }

    private void Update()
    {
        if (!isWaitingForCollectStateEnd || animator == null)
        {
            return;
        }

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);
        bool isCollectState = stateInfo.IsName(collectStateName);
        if (!hasEnteredCollectState)
        {
            hasEnteredCollectState = isCollectState;
            return;
        }

        if (!isCollectState || animator.IsInTransition(layerIndex) || stateInfo.normalizedTime < 1f)
        {
            return;
        }

        isWaitingForCollectStateEnd = false;
        gameObject.SetActive(false);
    }

    public void Configure(int itemIndex, StoryItemVisualCatalog visualCatalog, int visualIndex)
    {
        ItemIndex = itemIndex;
        isWaitingForCollectStateEnd = false;
        hasEnteredCollectState = false;

        if (visualCatalog == null)
        {
            Debug.LogError("[StoryItemView] StoryItemVisualCatalog is not assigned.", this);
            return;
        }

        if (!visualCatalog.TryGetVisual(visualIndex, out StoryItemVisualData visual))
        {
            Debug.LogError($"[StoryItemView] Item visual is not registered. visualIndex={visualIndex}", this);
            return;
        }

        ApplyIdleClip(visual.IdleClip);
    }

    private void ApplyIdleClip(AnimationClip idleClip)
    {
        if (animator == null)
        {
            Debug.LogError("[StoryItemView] Animator is not assigned.", this);
            return;
        }

        if (baseIdleClip == null)
        {
            Debug.LogError("[StoryItemView] Base idle clip is not assigned.", this);
            return;
        }

        if (idleClip == null)
        {
            Debug.LogError("[StoryItemView] Idle clip is not assigned.", this);
            return;
        }

        if (baseController == null)
        {
            baseController = animator.runtimeAnimatorController;
        }

        if (baseController == null)
        {
            Debug.LogError("[StoryItemView] Animator controller is not assigned.", this);
            return;
        }

        overrideController = new AnimatorOverrideController(baseController);
        overrideController[baseIdleClip.name] = idleClip;
        animator.runtimeAnimatorController = overrideController;
    }

    public void SetCollected(bool collected)
    {
        if (!collected)
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            ResetToIdle();
            return;
        }

        PlayCollect();
    }

    private void ResetToIdle()
    {
        isWaitingForCollectStateEnd = false;
        hasEnteredCollectState = false;

        if (animator == null)
        {
            return;
        }

        animator.Rebind();
        animator.Update(0f);
    }

    private void PlayCollect()
    {
        if (isWaitingForCollectStateEnd || !gameObject.activeSelf)
        {
            return;
        }

        if (animator == null)
        {
            Debug.LogError("[StoryItemView] Animator is not assigned.", this);
            gameObject.SetActive(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(collectStateName))
        {
            Debug.LogError("[StoryItemView] Collect state name is empty.", this);
            gameObject.SetActive(false);
            return;
        }

        hasEnteredCollectState = false;
        isWaitingForCollectStateEnd = true;
        if (transitionDurationSeconds > 0f)
        {
            animator.CrossFade(collectStateName, transitionDurationSeconds, layerIndex, 0f);
        }
        else
        {
            animator.Play(collectStateName, layerIndex, 0f);
        }
    }
}

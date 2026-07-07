using UnityEngine;

public sealed class StoryItemView : MonoBehaviour
{
    private enum VisualState
    {
        WorldIdle,
        CollectingToFollow,
        Following,
        ReturningCollect,
        ReturningMove
    }

    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip baseIdleClip;
    [SerializeField] private string collectStateName = "Collect";
    [SerializeField] private int layerIndex;
    [SerializeField] private float transitionDurationSeconds;
    [SerializeField] private Vector3 worldScale = new(4f, 4f, 1f);
    [SerializeField] private Vector3 followScale = new(2f, 2f, 1f);
    [SerializeField] private Vector3 returnStartScale = new(1f, 1f, 1f);

    private RuntimeAnimatorController baseController;
    private AnimatorOverrideController overrideController;
    private bool isWaitingForCollectStateEnd;
    private bool hasEnteredCollectState;
    private VisualState visualState;
    private StoryItemFollowChainView followChain;
    private Vector3 worldPosition;
    private Vector3 returnStartPosition;
    private float returnMoveSeconds;
    private float returnMoveElapsedSeconds;

    public int ItemIndex { get; private set; }

    private void Update()
    {
        TickCollectAnimation();
        TickFollow();
        TickReturnMove();
    }

    public void Configure(int itemIndex, StoryItemVisualCatalog visualCatalog, int visualIndex)
    {
        ItemIndex = itemIndex;
        visualState = VisualState.WorldIdle;
        worldPosition = transform.position;
        transform.localScale = worldScale;
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
        ResetToIdle();
    }

    public void SetServerState(
        StoryItemState serverState,
        float newReturnMoveSeconds,
        StoryItemFollowChainView newFollowChain)
    {
        followChain = newFollowChain;
        returnMoveSeconds = Mathf.Max(0f, newReturnMoveSeconds);

        switch (serverState)
        {
            case StoryItemState.Collected:
                if (visualState != VisualState.CollectingToFollow && visualState != VisualState.Following)
                {
                    BeginCollectToFollow();
                }

                break;
            case StoryItemState.ReturningLocked:
                if (visualState != VisualState.ReturningCollect
                    && visualState != VisualState.ReturningMove
                    && visualState != VisualState.WorldIdle)
                {
                    BeginReturnCollect();
                }

                break;
            default:
                if (visualState != VisualState.WorldIdle && visualState != VisualState.ReturningMove)
                {
                    BeginWorldIdle();
                }

                break;
        }
    }

    private void TickCollectAnimation()
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

        CompleteCollectAnimation();
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

    private void BeginCollectToFollow()
    {
        visualState = VisualState.CollectingToFollow;
        gameObject.SetActive(true);
        PlayCollect();
    }

    private void BeginReturnCollect()
    {
        followChain?.RemoveFollower(this);
        visualState = VisualState.ReturningCollect;
        gameObject.SetActive(true);
        PlayCollect();
    }

    private void BeginReturnMove()
    {
        visualState = VisualState.ReturningMove;
        returnStartPosition = transform.position;
        returnMoveElapsedSeconds = 0f;
        transform.localScale = returnStartScale;
        ResetToIdle();
    }

    private void BeginFollowing()
    {
        visualState = VisualState.Following;
        transform.localScale = followScale;
        ResetToIdle();
        followChain?.AddFollower(this);
    }

    private void BeginWorldIdle()
    {
        followChain?.RemoveFollower(this);
        visualState = VisualState.WorldIdle;
        gameObject.SetActive(true);
        transform.position = worldPosition;
        transform.localScale = worldScale;
        ResetToIdle();
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

    private void CompleteCollectAnimation()
    {
        isWaitingForCollectStateEnd = false;
        if (visualState == VisualState.CollectingToFollow)
        {
            BeginFollowing();
            return;
        }

        if (visualState == VisualState.ReturningCollect)
        {
            BeginReturnMove();
        }
    }

    private void PlayCollect()
    {
        if (isWaitingForCollectStateEnd)
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

    private void TickFollow()
    {
        if (visualState != VisualState.Following
            || followChain == null
            || !followChain.TryGetFollowTargetPosition(this, out Vector3 targetPosition))
        {
            return;
        }

        Vector3 current = transform.position;
        targetPosition.z = current.z;
        Vector3 offset = current - targetPosition;
        if (offset.sqrMagnitude <= 0.0001f)
        {
            offset = Vector3.left;
        }

        Vector3 desired = targetPosition + offset.normalized * followChain.FollowDistance;
        transform.position = Vector3.Lerp(
            current,
            desired,
            Mathf.Clamp01(followChain.FollowLerpSpeed * Time.deltaTime));
    }

    private void TickReturnMove()
    {
        if (visualState != VisualState.ReturningMove)
        {
            return;
        }

        if (returnMoveSeconds <= 0f)
        {
            BeginWorldIdle();
            return;
        }

        returnMoveElapsedSeconds = Mathf.Min(returnMoveSeconds, returnMoveElapsedSeconds + Time.deltaTime);
        float progress = Mathf.Clamp01(returnMoveElapsedSeconds / returnMoveSeconds);
        transform.position = Vector3.Lerp(returnStartPosition, worldPosition, progress);
        transform.localScale = Vector3.Lerp(returnStartScale, worldScale, progress);

        if (progress >= 1f)
        {
            BeginWorldIdle();
        }
    }
}

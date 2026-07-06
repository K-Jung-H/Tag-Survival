using System;
using UnityEngine;

public sealed class StoryGoalView : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private string clearStateName = "Clear";
    [SerializeField] private string clearedLoopStateName = "Cleared";
    [SerializeField] private int layerIndex;
    [SerializeField] private float transitionDurationSeconds;

    private bool isWaitingForClearStateEnd;
    private bool hasEnteredClearState;

    public event Action ClearAnimationFinished;

    private void Update()
    {
        if (!isWaitingForClearStateEnd || animator == null)
        {
            return;
        }

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);
        bool isClearState = stateInfo.IsName(clearStateName);
        if (!hasEnteredClearState)
        {
            hasEnteredClearState = isClearState;
            return;
        }

        if (!isClearState || animator.IsInTransition(layerIndex) || stateInfo.normalizedTime < 1f)
        {
            return;
        }

        isWaitingForClearStateEnd = false;
        PlayClearedLoop();
        ClearAnimationFinished?.Invoke();
    }

    public void PlayClear()
    {
        if (animator == null)
        {
            Debug.LogError("[StoryGoalView] Animator is not assigned.", this);
            ClearAnimationFinished?.Invoke();
            return;
        }

        if (string.IsNullOrWhiteSpace(clearStateName))
        {
            Debug.LogError("[StoryGoalView] Clear state name is empty.", this);
            ClearAnimationFinished?.Invoke();
            return;
        }

        hasEnteredClearState = false;
        isWaitingForClearStateEnd = true;
        if (transitionDurationSeconds > 0f)
        {
            animator.CrossFade(clearStateName, transitionDurationSeconds, layerIndex, 0f);
        }
        else
        {
            animator.Play(clearStateName, layerIndex, 0f);
        }
    }

    private void PlayClearedLoop()
    {
        if (animator == null || string.IsNullOrWhiteSpace(clearedLoopStateName))
        {
            return;
        }

        if (transitionDurationSeconds > 0f)
        {
            animator.CrossFade(clearedLoopStateName, transitionDurationSeconds, layerIndex, 0f);
        }
        else
        {
            animator.Play(clearedLoopStateName, layerIndex, 0f);
        }
    }
}

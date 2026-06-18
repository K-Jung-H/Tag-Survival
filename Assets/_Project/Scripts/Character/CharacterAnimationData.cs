using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Character/Character Animation Data")]
public sealed class CharacterAnimationData : ScriptableObject
{
    [SerializeField] private RuntimeAnimatorController animatorController;
    [SerializeField] private AnimationClip roomIdleClip;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip runClip;
    [SerializeField] private AnimationClip jumpClip;
    [SerializeField] private AnimationClip fallClip;
    [SerializeField] private AnimationClip wallStickClip;
    [SerializeField] private AnimationClip stunClip;
    [SerializeField] private AnimationClip blinkEnterClip;
    [SerializeField] private AnimationClip blinkExitClip;

    public RuntimeAnimatorController AnimatorController => animatorController;
    public AnimationClip RoomIdleClip => roomIdleClip;
    public AnimationClip IdleClip => idleClip;
    public AnimationClip RunClip => runClip;
    public AnimationClip JumpClip => jumpClip;
    public AnimationClip FallClip => fallClip;
    public AnimationClip WallStickClip => wallStickClip;
    public AnimationClip StunClip => stunClip;
    public AnimationClip BlinkEnterClip => blinkEnterClip;
    public AnimationClip BlinkExitClip => blinkExitClip;

    // - Role: Get clip.
    public AnimationClip GetClip(LocomotionState state)
    {
        switch (state)
        {
            case LocomotionState.Run:
                return runClip;
            case LocomotionState.Jump:
                return jumpClip;
            case LocomotionState.Fall:
                return fallClip;
            case LocomotionState.WallStick:
                return wallStickClip;
            case LocomotionState.Stunned:
                return stunClip;
            case LocomotionState.BlinkEnter:
                return blinkEnterClip;
            case LocomotionState.BlinkExit:
                return blinkExitClip;
            default:
                return idleClip;
        }
    }
}

using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "Tag Survival/Character/Character Animation Data")]
public sealed class CharacterAnimationData : ScriptableObject
{
    [SerializeField] private RuntimeAnimatorController animatorController;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip runClip;
    [SerializeField] private AnimationClip jumpClip;
    [SerializeField] private AnimationClip fallClip;
    [SerializeField] private AnimationClip wallStickClip;
    [SerializeField] private AnimationClip stunClip;

    public RuntimeAnimatorController AnimatorController => animatorController;
    public AnimationClip IdleClip => idleClip;
    public AnimationClip RunClip => runClip;
    public AnimationClip JumpClip => jumpClip;
    public AnimationClip FallClip => fallClip;
    public AnimationClip WallStickClip => wallStickClip;
    public AnimationClip StunClip => stunClip;

    // 이동 상태에 대응하는 캐릭터 애니메이션 클립을 반환합니다.
    public AnimationClip GetClip(PlayerLocomotionState state)
    {
        switch (state)
        {
            case PlayerLocomotionState.Run:
                return runClip;
            case PlayerLocomotionState.Jump:
                return jumpClip;
            case PlayerLocomotionState.Fall:
                return fallClip;
            case PlayerLocomotionState.WallStick:
                return wallStickClip;
            case PlayerLocomotionState.Stunned:
                return stunClip;
            default:
                return idleClip;
        }
    }
}

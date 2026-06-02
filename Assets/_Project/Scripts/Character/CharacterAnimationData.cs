using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Character/Character Animation Data")]
public sealed class CharacterAnimationData : ScriptableObject
{
    [SerializeField] private RuntimeAnimatorController animatorController;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip runClip;
    [SerializeField] private AnimationClip jumpClip;
    [SerializeField] private AnimationClip fallClip;
    [SerializeField] private AnimationClip wallStickClip;
    [SerializeField] private AnimationClip deathClip;

    public RuntimeAnimatorController AnimatorController => animatorController;
    public AnimationClip IdleClip => idleClip;
    public AnimationClip RunClip => runClip;
    public AnimationClip JumpClip => jumpClip;
    public AnimationClip FallClip => fallClip;
    public AnimationClip WallStickClip => wallStickClip;
    public AnimationClip DeathClip => deathClip;

    // Role: LocomotionState에 대응되는 캐릭터 애니메이션 클립을 반환한다.
    // Parameters:
    // - state: 조회할 이동 상태
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
            case PlayerLocomotionState.Death:
                return deathClip;
            default:
                return idleClip;
        }
    }
}

using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Skill/Hook Skill Config")]
public sealed class HookSkillConfig : SkillConfig
{
    [SerializeField] private float hookSpeed = 18f;
    [SerializeField] private float returnSpeedMultiplier = 2f;
    [SerializeField] private float hookHitHalfExtent = 0.08f;
    [SerializeField] private float returnCompleteDistance = 0.18f;
    [SerializeField] private float swingInputAcceleration = 18f;
    [SerializeField] private float swingDetachBoost = 15f;
    [SerializeField] private float swingDampingPerSecond = 0.85f;
    [SerializeField] private float ropeTautTolerance = 0.03f;

    public override SkillType SkillType => SkillType.HookGrappling;
    public float HookSpeed => Mathf.Max(0f, hookSpeed);
    public float ReturnSpeedMultiplier => Mathf.Max(0f, returnSpeedMultiplier);
    public float HookHitHalfExtent => Mathf.Max(0.0001f, hookHitHalfExtent);
    public float ReturnCompleteDistance => Mathf.Max(0.0001f, returnCompleteDistance);
    public float SwingInputAcceleration => Mathf.Max(0f, swingInputAcceleration);
    public float SwingDetachBoost => Mathf.Max(0f, swingDetachBoost);
    public float SwingDampingPerSecond => Mathf.Clamp01(swingDampingPerSecond);
    public float RopeTautTolerance => Mathf.Max(0f, ropeTautTolerance);
}

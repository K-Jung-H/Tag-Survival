using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Skill/Hook Skill Config")]
public sealed class HookSkillConfig : SkillConfig
{
    [SerializeField] private float hookSpeed = 18f;
    [SerializeField] private float returnSpeedMultiplier = 2f;
    [SerializeField] private float hookHitHalfExtent = 0.08f;
    [SerializeField] private float returnCompleteDistance = 0.18f;
    [SerializeField] private float swingGravity = 50f;
    [SerializeField] private float swingForwardAcceleration = 18f;
    [SerializeField] private float swingBrakeAcceleration = 24f;
    [SerializeField] private float swingMaxTangentSpeed = 18f;
    [SerializeField] private float swingIdleDampingPerSecond = 0.98f;
    [SerializeField] private float swingDetachBoost = 15f;
    [SerializeField] private float ropeTautTolerance = 0.03f;
    [SerializeField] private float reelSpeed = 5f;
    [SerializeField] private float minRopeLength = 1.5f;
    [Header("Render")]
    [SerializeField] private float ropeSegmentLength = 2f;
    [SerializeField] private int maxRopeSegmentCount = 20;

    public override SkillType SkillType => SkillType.HookGrappling;
    public float HookSpeed => Mathf.Max(0f, hookSpeed);
    public float ReturnSpeedMultiplier => Mathf.Max(0f, returnSpeedMultiplier);
    public float HookHitHalfExtent => Mathf.Max(0.0001f, hookHitHalfExtent);
    public float ReturnCompleteDistance => Mathf.Max(0.0001f, returnCompleteDistance);
    public float SwingGravity => Mathf.Max(0f, swingGravity);
    public float SwingForwardAcceleration => Mathf.Max(0f, swingForwardAcceleration);
    public float SwingBrakeAcceleration => Mathf.Max(0f, swingBrakeAcceleration);
    public float SwingMaxTangentSpeed => Mathf.Max(0f, swingMaxTangentSpeed);
    public float SwingIdleDampingPerSecond => Mathf.Clamp01(swingIdleDampingPerSecond);
    public float SwingDetachBoost => Mathf.Max(0f, swingDetachBoost);
    public float RopeTautTolerance => Mathf.Max(0f, ropeTautTolerance);
    public float ReelSpeed => Mathf.Max(0f, reelSpeed);
    public float MinRopeLength => Mathf.Max(0.0001f, minRopeLength);
    public float RopeSegmentLength => Mathf.Max(0.0001f, ropeSegmentLength);
    public int MaxRopeSegmentCount => Mathf.Max(1, maxRopeSegmentCount);
}

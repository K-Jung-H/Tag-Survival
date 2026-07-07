using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Story/Story Enemy AI Config")]
public sealed class StoryEnemyAiConfig : ScriptableObject
{
    [Header("Detection")]
    [SerializeField] private float thinkIntervalSeconds = 0.15f;
    [SerializeField] private float sightRange = 9f;
    [SerializeField] private float targetLostSeconds = 1f;
    [SerializeField] private float lineOfSightSampleStep = 0.25f;

    [Header("Movement")]
    [SerializeField] private float arriveDistance = 0.4f;
    [SerializeField] private float lastSeenGiveUpSeconds = 4f;
    [SerializeField] private float obstacleProbeDistance = 0.6f;

    [Header("Jump")]
    [SerializeField] private float jumpClearanceHeight = 1.2f;
    [SerializeField] private float targetAboveJumpThreshold = 0.75f;
    [SerializeField] private float targetAboveMaxHorizontalDistance = 5f;
    [SerializeField] private float higherTargetJumpHeightMargin = 0.15f;
    [SerializeField] private float jumpCooldownSeconds = 0.8f;
    [SerializeField] private float jumpInputHoldSeconds = 0.12f;
    [SerializeField] private float repeatedJumpPositionTolerance = 0.45f;
    [SerializeField] private int maxRepeatedJumpAttempts = 2;
    [SerializeField] private float repeatedJumpBlockSeconds = 1.2f;

    [Header("Wall")]
    [SerializeField] private float wallClimbVerticalInput = 1f;

    [Header("Stuck Recovery")]
    [SerializeField] private float stuckCheckSeconds = 0.45f;
    [SerializeField] private float stuckMinMoveDistance = 0.08f;
    [SerializeField] private float stuckReverseSeconds = 0.35f;
    [SerializeField] private float stuckGiveUpSeconds = 2.5f;

    public float ThinkIntervalSeconds => Mathf.Max(0.01f, thinkIntervalSeconds);
    public float SightRange => Mathf.Max(0f, sightRange);
    public float TargetLostSeconds => Mathf.Max(0f, targetLostSeconds);
    public float ArriveDistance => Mathf.Max(0f, arriveDistance);
    public float LastSeenGiveUpSeconds => Mathf.Max(0f, lastSeenGiveUpSeconds);
    public float LineOfSightSampleStep => Mathf.Max(0.01f, lineOfSightSampleStep);
    public float ObstacleProbeDistance => Mathf.Max(0f, obstacleProbeDistance);
    public float JumpClearanceHeight => Mathf.Max(0f, jumpClearanceHeight);
    public float TargetAboveJumpThreshold => Mathf.Max(0f, targetAboveJumpThreshold);
    public float TargetAboveMaxHorizontalDistance => Mathf.Max(0f, targetAboveMaxHorizontalDistance);
    public float HigherTargetJumpHeightMargin => Mathf.Max(0f, higherTargetJumpHeightMargin);
    public float JumpCooldownSeconds => Mathf.Max(0f, jumpCooldownSeconds);
    public float JumpInputHoldSeconds => Mathf.Max(0f, jumpInputHoldSeconds);
    public float RepeatedJumpPositionTolerance => Mathf.Max(0f, repeatedJumpPositionTolerance);
    public int MaxRepeatedJumpAttempts => Mathf.Max(0, maxRepeatedJumpAttempts);
    public float RepeatedJumpBlockSeconds => Mathf.Max(0f, repeatedJumpBlockSeconds);
    public float WallClimbVerticalInput => Mathf.Clamp(wallClimbVerticalInput, -1f, 1f);
    public float StuckCheckSeconds => Mathf.Max(0.01f, stuckCheckSeconds);
    public float StuckMinMoveDistance => Mathf.Max(0f, stuckMinMoveDistance);
    public float StuckReverseSeconds => Mathf.Max(0f, stuckReverseSeconds);
    public float StuckGiveUpSeconds => Mathf.Max(0f, stuckGiveUpSeconds);
}

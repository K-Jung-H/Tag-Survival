using UnityEngine;

public struct CharacterMovementStats
{
    public float moveSpeed;
    public float jumpHeight;
    public float timeToApex;
    public float timeToFall;
    public float jumpVelocity;
    public float upGravity;
    public float downGravity;
    public float maxFallSpeed;
    public float groundAcceleration;
    public float groundDeceleration;
    public float airAcceleration;
    public float airDeceleration;
    public float overSpeedDeceleration;
    public float wallMoveSpeedMultiplier;
    public float coyoteTime;

    // - Role: Create character movement stats.
    public CharacterMovementStats(
        float moveSpeed,
        float jumpHeight,
        float timeToApex,
        float timeToFall,
        float maxFallSpeed,
        float groundAcceleration,
        float groundDeceleration,
        float airAcceleration,
        float airDeceleration,
        float overSpeedDeceleration,
        float wallMoveSpeedMultiplier,
        float coyoteTime)
    {
        float safeJumpHeight = Mathf.Max(0.0001f, jumpHeight);
        float safeTimeToApex = Mathf.Max(0.0001f, timeToApex);
        float safeTimeToFall = Mathf.Max(0.0001f, timeToFall);

        this.moveSpeed = Mathf.Max(0f, moveSpeed);
        this.jumpHeight = safeJumpHeight;
        this.timeToApex = safeTimeToApex;
        this.timeToFall = safeTimeToFall;
        this.jumpVelocity = 2f * safeJumpHeight / safeTimeToApex;
        this.upGravity = -(2f * safeJumpHeight) / (safeTimeToApex * safeTimeToApex);
        this.downGravity = -(2f * safeJumpHeight) / (safeTimeToFall * safeTimeToFall);
        this.maxFallSpeed = Mathf.Max(0.0001f, maxFallSpeed);
        this.groundAcceleration = Mathf.Max(0f, groundAcceleration);
        this.groundDeceleration = Mathf.Max(0f, groundDeceleration);
        this.airAcceleration = Mathf.Max(0f, airAcceleration);
        this.airDeceleration = Mathf.Max(0f, airDeceleration);
        this.overSpeedDeceleration = Mathf.Max(0f, overSpeedDeceleration);
        this.wallMoveSpeedMultiplier = Mathf.Max(0f, wallMoveSpeedMultiplier);
        this.coyoteTime = Mathf.Max(0f, coyoteTime);
    }

    public static CharacterMovementStats Default => Create(
        GameSimulationConfig.PlayerMoveSpeed,
        2.25f,
        0.35f,
        0.28f,
        GameSimulationConfig.PlayerMaxFallSpeed,
        80f,
        70f,
        35f,
        12f,
        18f,
        GameSimulationConfig.PlayerWallMoveSpeedMultiplier,
        0.08f);

    // - Role: Create movement stats from simple values.
    public static CharacterMovementStats Create(
        float moveSpeed,
        float jumpHeight,
        float timeToApex,
        float timeToFall,
        float maxFallSpeed,
        float groundAcceleration,
        float groundDeceleration,
        float airAcceleration,
        float airDeceleration,
        float overSpeedDeceleration,
        float wallMoveSpeedMultiplier,
        float coyoteTime)
    {
        return new CharacterMovementStats(
            moveSpeed,
            jumpHeight,
            timeToApex,
            timeToFall,
            maxFallSpeed,
            groundAcceleration,
            groundDeceleration,
            airAcceleration,
            airDeceleration,
            overSpeedDeceleration,
            wallMoveSpeedMultiplier,
            coyoteTime);
    }
}

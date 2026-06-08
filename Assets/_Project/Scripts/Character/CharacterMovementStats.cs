using UnityEngine;

public struct CharacterMovementStats
{
    public float moveSpeed;
    public float jumpHeight;
    public float jumpRiseTime;
    public float fallTime;
    public float jumpStartSpeed;
    public float jumpGravity;
    public float fallGravity;
    public float maxFallSpeed;
    public float moveAccel;
    public float moveDecel;
    public float airAccel;
    public float airDecel;
    public float overSpeedDecel;
    public float wallMoveRate;
    public float lateJumpTime;

    // - Role: Create character movement stats.
    public CharacterMovementStats(
        float moveSpeed,
        float jumpHeight,
        float jumpRiseTime,
        float fallTime,
        float maxFallSpeed,
        float moveAccel,
        float moveDecel,
        float airAccel,
        float airDecel,
        float overSpeedDecel,
        float wallMoveRate,
        float lateJumpTime)
    {
        float safeJumpHeight = Mathf.Max(0.0001f, jumpHeight);
        float safeJumpRiseTime = Mathf.Max(0.0001f, jumpRiseTime);
        float safeFallTime = Mathf.Max(0.0001f, fallTime);

        this.moveSpeed = Mathf.Max(0f, moveSpeed);
        this.jumpHeight = safeJumpHeight;
        this.jumpRiseTime = safeJumpRiseTime;
        this.fallTime = safeFallTime;
        this.jumpStartSpeed = 2f * safeJumpHeight / safeJumpRiseTime;
        this.jumpGravity = -(2f * safeJumpHeight) / (safeJumpRiseTime * safeJumpRiseTime);
        this.fallGravity = -(2f * safeJumpHeight) / (safeFallTime * safeFallTime);
        this.maxFallSpeed = Mathf.Max(0.0001f, maxFallSpeed);
        this.moveAccel = Mathf.Max(0f, moveAccel);
        this.moveDecel = Mathf.Max(0f, moveDecel);
        this.airAccel = Mathf.Max(0f, airAccel);
        this.airDecel = Mathf.Max(0f, airDecel);
        this.overSpeedDecel = Mathf.Max(0f, overSpeedDecel);
        this.wallMoveRate = Mathf.Max(0f, wallMoveRate);
        this.lateJumpTime = Mathf.Max(0f, lateJumpTime);
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
        GameSimulationConfig.PlayerWallMoveRate,
        0.08f);

    // - Role: Create movement stats from simple values.
    public static CharacterMovementStats Create(
        float moveSpeed,
        float jumpHeight,
        float jumpRiseTime,
        float fallTime,
        float maxFallSpeed,
        float moveAccel,
        float moveDecel,
        float airAccel,
        float airDecel,
        float overSpeedDecel,
        float wallMoveRate,
        float lateJumpTime)
    {
        return new CharacterMovementStats(
            moveSpeed,
            jumpHeight,
            jumpRiseTime,
            fallTime,
            maxFallSpeed,
            moveAccel,
            moveDecel,
            airAccel,
            airDecel,
            overSpeedDecel,
            wallMoveRate,
            lateJumpTime);
    }
}

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

    // Role: CharacterDefinition에 입력된 값으로 서버에서 바로 사용할 이동 계산값을 만든다.
    // Parameters:
    // - moveSpeed: 수평 이동 속도
    // - jumpHeight: 목표 점프 높이
    // - timeToApex: 최고점까지 걸리는 시간
    // - timeToFall: 같은 높이를 하강하는 데 걸리는 시간
    // - maxFallSpeed: 최대 하강 속도
    // - wallMoveSpeedMultiplier: WallStick 중 벽면 이동/약한 이탈 속도 배율
    // - coyoteTime: 발판에서 벗어난 뒤 점프를 허용할 시간
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
        float safeJumpHeight = Mathf.Max(0.0001f, jumpHeight);
        float safeTimeToApex = Mathf.Max(0.0001f, timeToApex);
        float safeTimeToFall = Mathf.Max(0.0001f, timeToFall);

        return new CharacterMovementStats
        {
            moveSpeed = Mathf.Max(0f, moveSpeed),
            jumpHeight = safeJumpHeight,
            timeToApex = safeTimeToApex,
            timeToFall = safeTimeToFall,
            jumpVelocity = 2f * safeJumpHeight / safeTimeToApex,
            upGravity = -(2f * safeJumpHeight) / (safeTimeToApex * safeTimeToApex),
            downGravity = -(2f * safeJumpHeight) / (safeTimeToFall * safeTimeToFall),
            maxFallSpeed = Mathf.Max(0.0001f, maxFallSpeed),
            groundAcceleration = Mathf.Max(0f, groundAcceleration),
            groundDeceleration = Mathf.Max(0f, groundDeceleration),
            airAcceleration = Mathf.Max(0f, airAcceleration),
            airDeceleration = Mathf.Max(0f, airDeceleration),
            overSpeedDeceleration = Mathf.Max(0f, overSpeedDeceleration),
            wallMoveSpeedMultiplier = Mathf.Max(0f, wallMoveSpeedMultiplier),
            coyoteTime = Mathf.Max(0f, coyoteTime),
        };
    }
}

using UnityEngine;

public static class PlayerMovementController
{
    private const float FacingDirectionThreshold = 0.0001f;
    private const float JumpInputThreshold = 0.5f;

    // - Role: Apply platformer velocity.
    public static void ApplyPlatformerVelocity(
        PlayerObject player,
        StageDefinition stageDefinition,
        float horizontalInput,
        float verticalInput,
        float deltaTime)
    {
        if (player.isWallSticking && player.wallNormalX != 0)
        {
            if (verticalInput > JumpInputThreshold
                && !IsMovingIntoWall(horizontalInput, player.wallNormalX))
            {
                float wallExitSpeedMultiplier = IsMovingAwayFromWall(horizontalInput, player.wallNormalX)
                    ? 1f
                    : player.movementStats.wallMoveSpeedMultiplier;

                player.velocity.x = player.wallNormalX * player.speed * wallExitSpeedMultiplier;
                player.velocity.y = player.movementStats.jumpVelocity
                    * PlayerPhysicsModifierResolver.ResolveJump(player, stageDefinition).JumpVelocityMultiplier;
                player.isGrounded = false;
                player.isWallSticking = false;
                player.wallNormalX = 0;
                player.wallSurfacePhysicType = StageSurfacePhysicType.Normal;
                player.jumpQueued = false;
                player.coyoteTimeRemaining = 0f;
                return;
            }

            if (IsMovingIntoWall(horizontalInput, player.wallNormalX))
            {
                player.velocity.x = horizontalInput * player.speed;
                player.velocity.y = GetWallStickVerticalSpeed(player, stageDefinition, verticalInput, deltaTime);
                player.isGrounded = false;
                player.jumpQueued = false;
                return;
            }

            player.isWallSticking = false;
            player.wallNormalX = 0;
            player.wallSurfacePhysicType = StageSurfacePhysicType.Normal;
        }

        ApplyPlatformerHorizontalVelocity(player, stageDefinition, horizontalInput, deltaTime);
        ApplyJumpAndGravity(player, stageDefinition, deltaTime);
    }

    // - Role: Update wall stick after stage move.
    public static void UpdateWallStickAfterStageMove(
        PlayerObject player,
        StageDefinition stageDefinition,
        StageCollisionMoveResult moveResult,
        float horizontalInput,
        float verticalInput,
        float deltaTime)
    {
        bool canWallStick = !moveResult.isGrounded
            && !moveResult.hitCeiling
            && moveResult.hitWall
            && moveResult.wallNormalX != 0
            && IsMovingIntoWall(horizontalInput, moveResult.wallNormalX);

        if (!canWallStick)
        {
            if (moveResult.isGrounded || moveResult.hitCeiling || !moveResult.hitWall)
            {
                player.isWallSticking = false;
                player.wallNormalX = 0;
                player.wallSurfacePhysicType = StageSurfacePhysicType.Normal;
            }

            return;
        }

        player.isWallSticking = true;
        player.wallNormalX = moveResult.wallNormalX;
        player.wallSurfacePhysicType = moveResult.wallSurfacePhysicType;
        player.isGrounded = false;
        player.coyoteTimeRemaining = 0f;
        player.velocity.x = 0f;
        player.velocity.y = GetWallStickVerticalSpeed(player, stageDefinition, verticalInput, deltaTime);
    }

    // - Role: Get platformer horizontal input.
    public static float GetPlatformerHorizontalInput(Vector2 input)
    {
        float horizontal = Mathf.Clamp(input.x, -1f, 1f);
        if (Mathf.Abs(horizontal) > 0.5f)
        {
            return Mathf.Sign(horizontal);
        }

        return horizontal;
    }

    // - Role: Get platformer vertical input.
    public static float GetPlatformerVerticalInput(Vector2 input)
    {
        float vertical = Mathf.Clamp(input.y, -1f, 1f);
        if (Mathf.Abs(vertical) > 0.5f)
        {
            return Mathf.Sign(vertical);
        }

        return vertical;
    }

    // - Role: Apply jump and gravity.
    private static void ApplyJumpAndGravity(
        PlayerObject player,
        StageDefinition stageDefinition,
        float deltaTime)
    {
        bool wantsGroundJump = player.isGrounded && player.isJumpPressed;
        bool wantsCoyoteJump = !player.isGrounded
            && player.coyoteTimeRemaining > 0f
            && player.jumpQueued;

        if (wantsGroundJump || wantsCoyoteJump)
        {
            StagePhysicsModifier jumpModifier = PlayerPhysicsModifierResolver.ResolveJump(player, stageDefinition);
            player.velocity.y = player.movementStats.jumpVelocity * jumpModifier.JumpVelocityMultiplier;
            player.isGrounded = false;
            player.isWallSticking = false;
            player.wallNormalX = 0;
            player.wallSurfacePhysicType = StageSurfacePhysicType.Normal;
            player.coyoteTimeRemaining = 0f;
        }

        player.jumpQueued = false;

        float gravity = player.velocity.y > 0f
            ? player.movementStats.upGravity
            : player.movementStats.downGravity;

        StagePhysicsModifier airModifier = PlayerPhysicsModifierResolver.ResolveAir(stageDefinition);
        gravity *= airModifier.GravityScale;
        player.velocity.y += gravity * deltaTime;

        float maxFallSpeed = player.movementStats.maxFallSpeed * airModifier.MaxFallSpeedMultiplier;
        player.velocity.y = Mathf.Max(player.velocity.y, -maxFallSpeed);
    }

    // - Role: Apply platformer horizontal velocity.
    private static void ApplyPlatformerHorizontalVelocity(
        PlayerObject player,
        StageDefinition stageDefinition,
        float horizontalInput,
        float deltaTime)
    {
        StagePhysicsModifier groundModifier = PlayerPhysicsModifierResolver.ResolveGround(player, stageDefinition);
        float targetVelocityX = horizontalInput * player.speed;
        if (player.isGrounded)
        {
            targetVelocityX *= groundModifier.MoveSpeedMultiplier;
        }

        float currentVelocityX = player.velocity.x;
        float inputMagnitude = Mathf.Abs(horizontalInput);

        if (inputMagnitude <= FacingDirectionThreshold)
        {
            float deceleration = player.isGrounded
                ? player.movementStats.groundDeceleration
                : player.movementStats.airDeceleration;
            if (player.isGrounded)
            {
                deceleration *= groundModifier.GroundDecelerationMultiplier;
            }

            player.velocity.x = Mathf.MoveTowards(currentVelocityX, 0f, deceleration * deltaTime);
            return;
        }

        float acceleration = player.isGrounded
            ? player.movementStats.groundAcceleration
            : player.movementStats.airAcceleration;
        if (player.isGrounded)
        {
            acceleration *= groundModifier.GroundAccelerationMultiplier;
        }

        bool sameDirection = Mathf.Sign(currentVelocityX) == Mathf.Sign(targetVelocityX);
        bool isOverTargetSpeed = sameDirection
            && Mathf.Abs(currentVelocityX) > Mathf.Abs(targetVelocityX);

        float overSpeedDeceleration = player.movementStats.overSpeedDeceleration;
        if (player.isGrounded)
        {
            overSpeedDeceleration *= groundModifier.OverSpeedDecelerationMultiplier;
        }

        float maxDelta = isOverTargetSpeed
            ? overSpeedDeceleration * deltaTime
            : acceleration * deltaTime;

        player.velocity.x = Mathf.MoveTowards(currentVelocityX, targetVelocityX, maxDelta);
    }

    // - Role: Get wall stick vertical speed.
    private static float GetWallStickVerticalSpeed(
        PlayerObject player,
        StageDefinition stageDefinition,
        float verticalInput,
        float deltaTime)
    {
        StagePhysicsModifier wallModifier = PlayerPhysicsModifierResolver.ResolveWall(player, stageDefinition);
        float wallMoveSpeed = player.speed * player.movementStats.wallMoveSpeedMultiplier;
        if (verticalInput > JumpInputThreshold)
        {
            return wallMoveSpeed * wallModifier.WallUpMoveMultiplier;
        }

        if (verticalInput < -JumpInputThreshold)
        {
            return -wallMoveSpeed * wallModifier.WallDownMoveMultiplier;
        }

        if (wallModifier.WallIdleSlideAcceleration <= 0f || wallModifier.WallMaxSlideSpeed <= 0f)
        {
            return 0f;
        }

        return Mathf.MoveTowards(
            player.velocity.y,
            -wallModifier.WallMaxSlideSpeed,
            wallModifier.WallIdleSlideAcceleration * Mathf.Max(0f, deltaTime));
    }

    // - Role: Check if moving into wall is true.
    private static bool IsMovingIntoWall(float horizontalInput, sbyte wallNormalX)
    {
        return wallNormalX != 0
            && Mathf.Abs(horizontalInput) > FacingDirectionThreshold
            && Mathf.Sign(horizontalInput) == -wallNormalX;
    }

    // - Role: Check if moving away from wall is true.
    private static bool IsMovingAwayFromWall(float horizontalInput, sbyte wallNormalX)
    {
        return wallNormalX != 0
            && Mathf.Abs(horizontalInput) > FacingDirectionThreshold
            && Mathf.Sign(horizontalInput) == wallNormalX;
    }
}

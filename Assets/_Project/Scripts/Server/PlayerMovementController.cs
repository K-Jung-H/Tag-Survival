using UnityEngine;

public static class PlayerMovementController
{
    private const float FacingDirectionThreshold = 0.0001f;
    private const float JumpInputThreshold = 0.5f;

    // - Role: Apply velocity.
    public static void ApplyVelocity(PlayerObject player, StageDefinition stageDefinition, float deltaTime)
    {
        float horizontalInput = GetHorizontalInput(player.input);
        float verticalInput = GetVerticalInput(player.input);
        if (player.isOnWall && player.wallDirX != 0)
        {
            if (verticalInput > JumpInputThreshold
                && !IsMovingIntoWall(horizontalInput, player.wallDirX))
            {
                float wallExitRate = IsMovingAwayFromWall(horizontalInput, player.wallDirX)
                    ? 1f
                    : player.moveStats.wallMoveRate;

                player.velocity.x = player.wallDirX * player.moveStats.moveSpeed * wallExitRate;
                player.velocity.y = player.moveStats.jumpStartSpeed
                    * PlayerPhysicsModifierResolver.ResolveJump(player, stageDefinition).JumpStartSpeedRate;
                player.isGrounded = false;
                player.isOnWall = false;
                player.wallDirX = 0;
                player.wallSurface = StageSurfaceType.Normal;
                player.jumpQueued = false;
                player.lateJumpTimer = 0f;
                return;
            }

            if (IsMovingIntoWall(horizontalInput, player.wallDirX))
            {
                player.velocity.x = horizontalInput * player.moveStats.moveSpeed;
                player.velocity.y = GetWallStickVerticalSpeed(player, stageDefinition, verticalInput, deltaTime);
                player.isGrounded = false;
                player.jumpQueued = false;
                return;
            }

            player.isOnWall = false;
            player.wallDirX = 0;
            player.wallSurface = StageSurfaceType.Normal;
        }

        ApplyHorizontalVelocity(player, stageDefinition, horizontalInput, deltaTime);
        ApplyJumpAndGravity(player, stageDefinition, deltaTime);
    }

    // - Role: Update wall stick.
    public static void UpdateWallStick(PlayerObject player, StageDefinition stageDefinition, StageCollisionMoveResult moveResult, float deltaTime)
    {
        float horizontalInput = GetHorizontalInput(player.input);
        float verticalInput = GetVerticalInput(player.input);
        bool canWallStick = !moveResult.isGrounded
            && !moveResult.hitCeiling
            && moveResult.hitWall
            && moveResult.wallDirX != 0
            && IsMovingIntoWall(horizontalInput, moveResult.wallDirX);

        if (!canWallStick)
        {
            if (moveResult.isGrounded || moveResult.hitCeiling || !moveResult.hitWall)
            {
                player.isOnWall = false;
                player.wallDirX = 0;
                player.wallSurface = StageSurfaceType.Normal;
            }

            return;
        }

        player.isOnWall = true;
        player.wallDirX = moveResult.wallDirX;
        player.wallSurface = moveResult.wallSurface;
        player.isGrounded = false;
        player.lateJumpTimer = 0f;
        player.velocity.x = 0f;
        player.velocity.y = GetWallStickVerticalSpeed(player, stageDefinition, verticalInput, deltaTime);
    }

    // - Role: Get horizontal input.
    public static float GetHorizontalInput(Vector2 input)
    {
        float horizontal = Mathf.Clamp(input.x, -1f, 1f);
        if (Mathf.Abs(horizontal) > 0.5f)
        {
            return Mathf.Sign(horizontal);
        }

        return horizontal;
    }

    // - Role: Get vertical input.
    public static float GetVerticalInput(Vector2 input)
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
        bool wantsLateJump = !player.isGrounded
            && player.lateJumpTimer > 0f
            && player.jumpQueued;

        if (wantsGroundJump || wantsLateJump)
        {
            StagePhysicsModifier jumpModifier = PlayerPhysicsModifierResolver.ResolveJump(player, stageDefinition);
            player.velocity.y = player.moveStats.jumpStartSpeed * jumpModifier.JumpStartSpeedRate;
            player.isGrounded = false;
            player.isOnWall = false;
            player.wallDirX = 0;
            player.wallSurface = StageSurfaceType.Normal;
            player.lateJumpTimer = 0f;
        }

        player.jumpQueued = false;

        float gravity = player.velocity.y > 0f
            ? player.moveStats.jumpGravity
            : player.moveStats.fallGravity;

        StagePhysicsModifier airModifier = PlayerPhysicsModifierResolver.ResolveAir(stageDefinition);
        gravity *= airModifier.GravityScale;
        player.velocity.y += gravity * deltaTime;

        float maxFallSpeed = player.moveStats.maxFallSpeed * airModifier.MaxFallSpeedRate;
        player.velocity.y = Mathf.Max(player.velocity.y, -maxFallSpeed);
    }

    // - Role: Apply horizontal velocity.
    private static void ApplyHorizontalVelocity(
        PlayerObject player,
        StageDefinition stageDefinition,
        float horizontalInput,
        float deltaTime)
    {
        StagePhysicsModifier groundModifier = PlayerPhysicsModifierResolver.ResolveGround(player, stageDefinition);
        float targetVelocityX = horizontalInput * player.moveStats.moveSpeed;
        if (player.isGrounded)
        {
            targetVelocityX *= groundModifier.MoveSpeedRate;
        }

        float currentVelocityX = player.velocity.x;
        float inputMagnitude = Mathf.Abs(horizontalInput);

        if (inputMagnitude <= FacingDirectionThreshold)
        {
            float deceleration = player.isGrounded
                ? player.moveStats.moveDecel
                : player.moveStats.airDecel;
            if (player.isGrounded)
            {
                deceleration *= groundModifier.MoveDecelRate;
            }

            player.velocity.x = Mathf.MoveTowards(currentVelocityX, 0f, deceleration * deltaTime);
            return;
        }

        float acceleration = player.isGrounded
            ? player.moveStats.moveAccel
            : player.moveStats.airAccel;
        if (player.isGrounded)
        {
            acceleration *= groundModifier.MoveAccelRate;
        }

        bool sameDirection = Mathf.Sign(currentVelocityX) == Mathf.Sign(targetVelocityX);
        bool isOverTargetSpeed = sameDirection
            && Mathf.Abs(currentVelocityX) > Mathf.Abs(targetVelocityX);

        float overSpeedDecel = player.moveStats.overSpeedDecel;
        if (player.isGrounded)
        {
            overSpeedDecel *= groundModifier.OverSpeedDecelRate;
        }

        float maxDelta = isOverTargetSpeed
            ? overSpeedDecel * deltaTime
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
        float wallMoveSpeed = player.moveStats.moveSpeed * player.moveStats.wallMoveRate;
        if (verticalInput > JumpInputThreshold)
        {
            return wallMoveSpeed * wallModifier.WallUpMoveRate;
        }

        if (verticalInput < -JumpInputThreshold)
        {
            return -wallMoveSpeed * wallModifier.WallDownMoveRate;
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
    private static bool IsMovingIntoWall(float horizontalInput, sbyte wallDirX)
    {
        return wallDirX != 0
            && Mathf.Abs(horizontalInput) > FacingDirectionThreshold
            && Mathf.Sign(horizontalInput) == -wallDirX;
    }

    // - Role: Check if moving away from wall is true.
    private static bool IsMovingAwayFromWall(float horizontalInput, sbyte wallDirX)
    {
        return wallDirX != 0
            && Mathf.Abs(horizontalInput) > FacingDirectionThreshold
            && Mathf.Sign(horizontalInput) == wallDirX;
    }
}

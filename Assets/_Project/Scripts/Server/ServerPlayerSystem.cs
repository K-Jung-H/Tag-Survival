using System.Collections.Generic;
using UnityEngine;
using PlayerRenderState = PlayerRuntimeState;

public sealed class ServerPlayerSystem
{
    private const float MovementStateThresholdSqr = 0.0001f;
    private const float FacingDirectionThreshold = 0.0001f;
    private const float JumpInputThreshold = 0.5f;

    private readonly Dictionary<ulong, PlayerObject> playersById = new();
    private readonly List<PlayerObject> players = new();
    private readonly List<ulong> simulationTargets = new();

    public IReadOnlyList<PlayerObject> Players => players;

    public PlayerObject Create(ulong playerId, byte characterId, byte skillId)
    {
        if (playersById.TryGetValue(playerId, out PlayerObject existing))
        {
            existing.characterId = characterId;
            existing.skillId = skillId;
            return existing;
        }

        PlayerObject player = new PlayerObject
        {
            playerId = playerId,
            characterId = characterId,
            skillId = skillId
        };
        playersById.Add(playerId, player);
        players.Add(player);
        return player;
    }

    public bool Remove(ulong playerId)
    {
        if (!playersById.TryGetValue(playerId, out PlayerObject player))
        {
            return false;
        }

        playersById.Remove(playerId);
        players.Remove(player);
        return true;
    }

    public bool TryGet(ulong playerId, out PlayerObject player)
    {
        return playersById.TryGetValue(playerId, out player);
    }

    public void UpdateInput(
        ulong playerId,
        Vector2 input,
        Vector2 aim,
        PlayerInputButtons buttons)
    {
        if (!playersById.TryGetValue(playerId, out PlayerObject player))
        {
            return;
        }

        player.input = input;
        player.aim = aim;
        player.buttons = buttons;
    }

    public void ApplyQueuedInputs(
        Dictionary<ulong, PlayerState> sourcePlayers,
        ServerInputBuffer inputBuffer)
    {
        if (sourcePlayers == null || inputBuffer == null)
        {
            return;
        }

        simulationTargets.Clear();
        foreach (ulong clientId in sourcePlayers.Keys)
        {
            simulationTargets.Add(clientId);
        }

        for (int i = 0; i < simulationTargets.Count; i++)
        {
            ulong clientId = simulationTargets[i];
            if (!inputBuffer.TryConsumeInput(clientId, out ServerPlayerInputCommand command))
            {
                continue;
            }

            PlayerState player = sourcePlayers[clientId];
            if (player.stunnedTimer > 0f)
            {
                ClearInput(ref player);
                UpdateInput(clientId, player.input, player.aim, player.buttons);
                sourcePlayers[clientId] = player;
                continue;
            }

            player.input = command.input;
            bool isJumpPressed = command.input.y > JumpInputThreshold;
            if (isJumpPressed && !player.isJumpPressed)
            {
                player.jumpQueued = true;
            }

            player.isJumpPressed = isJumpPressed;

            bool isSkillPressed = (command.buttons & PlayerInputButtons.Skill1) != 0;
            if (isSkillPressed && !player.isSkillPressed)
            {
                player.skillQueued = true;
            }

            player.isSkillPressed = isSkillPressed;

            bool hasAimInput = command.aim.sqrMagnitude > FacingDirectionThreshold;
            player.hasAimInput = hasAimInput;
            if (hasAimInput)
            {
                player.aim = command.aim.normalized;
            }

            player.buttons = command.buttons;
            UpdateInput(clientId, player.input, player.aim, player.buttons);
            sourcePlayers[clientId] = player;
        }
    }

    public void SimulatePlayers(
        Dictionary<ulong, PlayerState> sourcePlayers,
        ServerSkillSystem skillSystem,
        StageCollisionSystem collisionSystem,
        StageDefinition stageDefinition,
        float deltaTime)
    {
        if (sourcePlayers == null || skillSystem == null || collisionSystem == null)
        {
            return;
        }

        simulationTargets.Clear();
        foreach (ulong clientId in sourcePlayers.Keys)
        {
            simulationTargets.Add(clientId);
        }

        for (int i = 0; i < simulationTargets.Count; i++)
        {
            ulong clientId = simulationTargets[i];
            PlayerState player = sourcePlayers[clientId];

            UpdateStunTimerBeforeMove(ref player, deltaTime);
            UpdateCoyoteTimeBeforeMove(ref player, deltaTime);

            float horizontalInput = GetPlatformerHorizontalInput(player.input);
            float verticalInput = GetPlatformerVerticalInput(player.input);
            ApplyPlatformerVelocity(ref player, skillSystem, stageDefinition, horizontalInput, verticalInput, deltaTime);
            PrepareSkillMovement(ref player, skillSystem, collisionSystem, deltaTime);

            Vector2 collisionCenter = player.position + player.collisionOffset;
            StageCollisionMoveResult moveResult = collisionSystem.MovePlayerWithStageCollisionDetailed(
                collisionCenter,
                player.velocity * deltaTime,
                player.collisionHalfExtent
            );

            player.position = moveResult.position - player.collisionOffset;
            player.isGrounded = moveResult.isGrounded;
            if (moveResult.isGrounded)
            {
                player.groundSurfacePhysicType = moveResult.groundSurfacePhysicType;
            }

            if (moveResult.isGrounded && player.velocity.y < 0f)
            {
                player.velocity.y = 0f;
                player.coyoteTimeRemaining = player.movementStats.coyoteTime;
            }

            if (moveResult.hitCeiling && player.velocity.y > 0f)
            {
                player.velocity.y = 0f;
            }

            UpdateWallStickAfterStageMove(ref player, stageDefinition, moveResult, horizontalInput, verticalInput, deltaTime);
            SimulateSkill(ref player, skillSystem, collisionSystem, deltaTime);
            UpdateRenderState(ref player);
            UpdateCharacterStateMachine(ref player);

            sourcePlayers[clientId] = player;
        }
    }

    public void ResolvePlayerCollisions(
        Dictionary<ulong, PlayerState> sourcePlayers,
        StageCollisionSystem collisionSystem,
        List<WorldCollisionEvent> collisionEvents)
    {
        if (sourcePlayers == null || collisionSystem == null || collisionEvents == null)
        {
            return;
        }

        collisionEvents.Clear();

        simulationTargets.Clear();
        foreach (ulong clientId in sourcePlayers.Keys)
        {
            simulationTargets.Add(clientId);
        }

        for (int i = 0; i < simulationTargets.Count; i++)
        {
            for (int j = i + 1; j < simulationTargets.Count; j++)
            {
                ulong firstId = simulationTargets[i];
                ulong secondId = simulationTargets[j];

                PlayerState first = sourcePlayers[firstId];
                PlayerState second = sourcePlayers[secondId];

                if (!collisionSystem.TryGetPlayerSatCollision(
                    first.position + first.collisionOffset,
                    second.position + second.collisionOffset,
                    first.collisionHalfExtent,
                    second.collisionHalfExtent,
                    firstId,
                    secondId,
                    out Vector2 normal,
                    out float penetration))
                {
                    continue;
                }

                Vector2 correction = normal * (penetration * 0.5f);

                first.position -= correction;
                second.position += correction;

                first.velocity = collisionSystem.RemoveVelocityIntoNormal(first.velocity, normal);
                second.velocity = collisionSystem.RemoveVelocityIntoNormal(second.velocity, -normal);

                ApplyPlayerGroundContact(ref first, ref second, normal);

                sourcePlayers[firstId] = first;
                sourcePlayers[secondId] = second;

                PlayerObject firstObject = SyncFromPlayerState(first);
                PlayerObject secondObject = SyncFromPlayerState(second);
                collisionEvents.Add(new WorldCollisionEvent(firstObject, secondObject, normal, penetration));
            }
        }
    }

    public void SyncFromPlayerStates(IReadOnlyDictionary<ulong, PlayerState> sourcePlayers)
    {
        foreach (var pair in sourcePlayers)
        {
            SyncFromPlayerState(pair.Value);
        }
    }

    public static void UpdateRenderState(ref PlayerState player)
    {
        bool isMovingHorizontally = Mathf.Abs(player.velocity.x) > MovementStateThresholdSqr;
        PlayerLocomotionState locomotionState;
        if (player.stunnedTimer > 0f)
        {
            locomotionState = PlayerLocomotionState.Stunned;
        }
        else if (player.isWallSticking)
        {
            locomotionState = PlayerLocomotionState.WallStick;
        }
        else if (!player.isGrounded)
        {
            locomotionState = player.velocity.y > 0f
                ? PlayerLocomotionState.Jump
                : PlayerLocomotionState.Fall;
        }
        else
        {
            locomotionState = isMovingHorizontally ? PlayerLocomotionState.Run : PlayerLocomotionState.Idle;
        }

        PlayerRenderState renderState = player.characterStateMachine.State;
        renderState.locomotionState = locomotionState;
        renderState.facingSign = ResolveFacingSign(player, renderState.facingSign);

        player.characterStateMachine.ApplyState(renderState);
    }

    public static void UpdateCharacterStateMachine(ref PlayerState player)
    {
        if (player.characterStateMachine == null)
        {
            return;
        }

        PlayerRuntimeState runtimeState = player.characterStateMachine.State;
        runtimeState.clientId = player.clientId;
        runtimeState.position = player.position;
        runtimeState.velocity = player.velocity;
        runtimeState.aim = player.aim;
        player.characterStateMachine.ApplyState(runtimeState);
    }

    public static void ClearInput(ref PlayerState player)
    {
        player.input = Vector2.zero;
        player.buttons = PlayerInputButtons.None;
        player.isJumpPressed = false;
        player.jumpQueued = false;
        player.isSkillPressed = false;
        player.skillQueued = false;
        player.hasAimInput = false;
    }

    private static sbyte ResolveFacingSign(PlayerState player, sbyte currentFacingSign)
    {
        if (player.hasAimInput)
        {
            if (Mathf.Abs(player.aim.x) > FacingDirectionThreshold)
            {
                return player.aim.x > 0f ? (sbyte)1 : (sbyte)-1;
            }

            return currentFacingSign == 0 ? (sbyte)1 : currentFacingSign;
        }

        float horizontalInput = GetPlatformerHorizontalInput(player.input);
        if (horizontalInput > FacingDirectionThreshold)
        {
            return 1;
        }

        if (horizontalInput < -FacingDirectionThreshold)
        {
            return -1;
        }

        return currentFacingSign == 0 ? (sbyte)1 : currentFacingSign;
    }

    private static void SimulateSkill(
        ref PlayerState player,
        ServerSkillSystem skillSystem,
        StageCollisionSystem collisionSystem,
        float deltaTime)
    {
        if (player.skill == null || player.skill.StateMachine == null)
        {
            player.skillQueued = false;
            return;
        }

        bool skillPressedThisTick = player.skillQueued;
        player.skillQueued = false;

        skillSystem.Simulate(ref player, collisionSystem, deltaTime, skillPressedThisTick);
    }

    private static void PrepareSkillMovement(
        ref PlayerState player,
        ServerSkillSystem skillSystem,
        StageCollisionSystem collisionSystem,
        float deltaTime)
    {
        if (player.skill == null || player.skill.StateMachine == null)
        {
            return;
        }

        skillSystem.PrepareMovement(ref player, collisionSystem, deltaTime);
    }

    private static void UpdateStunTimerBeforeMove(ref PlayerState player, float deltaTime)
    {
        if (player.stunnedTimer <= 0f)
        {
            return;
        }

        player.stunnedTimer = Mathf.Max(0f, player.stunnedTimer - deltaTime);
        ClearInput(ref player);
    }

    private static void ApplyPlayerGroundContact(
        ref PlayerState first,
        ref PlayerState second,
        Vector2 normal)
    {
        if (normal.y > 0.5f)
        {
            second.isGrounded = true;
            second.groundSurfacePhysicType = StageSurfacePhysicType.Normal;
            second.isWallSticking = false;
            second.wallNormalX = 0;
            second.wallSurfacePhysicType = StageSurfacePhysicType.Normal;
            second.coyoteTimeRemaining = second.movementStats.coyoteTime;
            if (second.velocity.y < 0f)
            {
                second.velocity.y = 0f;
            }
        }
        else if (normal.y < -0.5f)
        {
            first.isGrounded = true;
            first.groundSurfacePhysicType = StageSurfacePhysicType.Normal;
            first.isWallSticking = false;
            first.wallNormalX = 0;
            first.wallSurfacePhysicType = StageSurfacePhysicType.Normal;
            first.coyoteTimeRemaining = first.movementStats.coyoteTime;
            if (first.velocity.y < 0f)
            {
                first.velocity.y = 0f;
            }
        }
    }

    private static void UpdateCoyoteTimeBeforeMove(ref PlayerState player, float deltaTime)
    {
        if (player.isGrounded)
        {
            player.coyoteTimeRemaining = player.movementStats.coyoteTime;
            return;
        }

        if (player.isWallSticking)
        {
            player.coyoteTimeRemaining = 0f;
            return;
        }

        player.coyoteTimeRemaining = Mathf.Max(0f, player.coyoteTimeRemaining - deltaTime);
    }

    private static void ApplyJumpAndGravity(
        ref PlayerState player,
        StageDefinition stageDefinition,
        float deltaTime)
    {
        bool wantsGroundJump = player.isGrounded && player.isJumpPressed;
        bool wantsCoyoteJump = !player.isGrounded
            && player.coyoteTimeRemaining > 0f
            && player.jumpQueued;

        if (wantsGroundJump || wantsCoyoteJump)
        {
            StagePhysicsModifier jumpModifier = ResolveJumpPhysicsModifier(player, stageDefinition);
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

        StagePhysicsModifier airModifier = ResolveAirPhysicsModifier(stageDefinition);
        gravity *= airModifier.GravityScale;
        player.velocity.y += gravity * deltaTime;

        float maxFallSpeed = player.movementStats.maxFallSpeed * airModifier.MaxFallSpeedMultiplier;
        player.velocity.y = Mathf.Max(player.velocity.y, -maxFallSpeed);
    }

    private static void ApplyPlatformerVelocity(
        ref PlayerState player,
        ServerSkillSystem skillSystem,
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
                    * ResolveJumpPhysicsModifier(player, stageDefinition).JumpVelocityMultiplier;
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

        if (IsUsingSwingMovement(player, skillSystem))
        {
            ApplySwingGravity(ref player, stageDefinition, deltaTime);
            return;
        }

        ApplyPlatformerHorizontalVelocity(ref player, stageDefinition, horizontalInput, deltaTime);
        ApplyJumpAndGravity(ref player, stageDefinition, deltaTime);
    }

    private static bool IsUsingSwingMovement(
        PlayerState player,
        ServerSkillSystem skillSystem)
    {
        return !player.isGrounded
            && skillSystem.UsesSwingMovement(player.clientId);
    }

    private static void ApplySwingGravity(
        ref PlayerState player,
        StageDefinition stageDefinition,
        float deltaTime)
    {
        player.jumpQueued = false;

        StagePhysicsModifier airModifier = ResolveAirPhysicsModifier(stageDefinition);
        float gravity = player.movementStats.downGravity * airModifier.GravityScale;
        player.velocity.y += gravity * deltaTime;

        float maxFallSpeed = player.movementStats.maxFallSpeed * airModifier.MaxFallSpeedMultiplier;
        player.velocity.y = Mathf.Max(player.velocity.y, -maxFallSpeed);
    }

    private static void ApplyPlatformerHorizontalVelocity(
        ref PlayerState player,
        StageDefinition stageDefinition,
        float horizontalInput,
        float deltaTime)
    {
        StagePhysicsModifier groundModifier = ResolveGroundPhysicsModifier(player, stageDefinition);
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

    private static void UpdateWallStickAfterStageMove(
        ref PlayerState player,
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

    private static float GetWallStickVerticalSpeed(
        PlayerState player,
        StageDefinition stageDefinition,
        float verticalInput,
        float deltaTime)
    {
        StagePhysicsModifier wallModifier = ResolveWallPhysicsModifier(player, stageDefinition);
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

    private static float GetPlatformerHorizontalInput(Vector2 input)
    {
        float horizontal = Mathf.Clamp(input.x, -1f, 1f);
        if (Mathf.Abs(horizontal) > 0.5f)
        {
            return Mathf.Sign(horizontal);
        }

        return horizontal;
    }

    private static float GetPlatformerVerticalInput(Vector2 input)
    {
        float vertical = Mathf.Clamp(input.y, -1f, 1f);
        if (Mathf.Abs(vertical) > 0.5f)
        {
            return Mathf.Sign(vertical);
        }

        return vertical;
    }

    private static bool IsMovingIntoWall(float horizontalInput, sbyte wallNormalX)
    {
        return wallNormalX != 0
            && Mathf.Abs(horizontalInput) > FacingDirectionThreshold
            && Mathf.Sign(horizontalInput) == -wallNormalX;
    }

    private static bool IsMovingAwayFromWall(float horizontalInput, sbyte wallNormalX)
    {
        return wallNormalX != 0
            && Mathf.Abs(horizontalInput) > FacingDirectionThreshold
            && Mathf.Sign(horizontalInput) == wallNormalX;
    }

    private static StagePhysicsModifier ResolveGroundPhysicsModifier(
        PlayerState player,
        StageDefinition stageDefinition)
    {
        if (!player.isGrounded)
        {
            return ResolveSurfacePhysicsModifier(stageDefinition, StageSurfacePhysicType.Normal);
        }

        return ResolveSurfacePhysicsModifier(stageDefinition, player.groundSurfacePhysicType);
    }

    private static StagePhysicsModifier ResolveJumpPhysicsModifier(
        PlayerState player,
        StageDefinition stageDefinition)
    {
        if (player.isGrounded || player.coyoteTimeRemaining > 0f)
        {
            return ResolveSurfacePhysicsModifier(stageDefinition, player.groundSurfacePhysicType);
        }

        return ResolveSurfacePhysicsModifier(stageDefinition, StageSurfacePhysicType.Normal);
    }

    private static StagePhysicsModifier ResolveWallPhysicsModifier(
        PlayerState player,
        StageDefinition stageDefinition)
    {
        if (!player.isWallSticking)
        {
            return ResolveSurfacePhysicsModifier(stageDefinition, StageSurfacePhysicType.Normal);
        }

        return ResolveSurfacePhysicsModifier(stageDefinition, player.wallSurfacePhysicType);
    }

    private static StagePhysicsModifier ResolveAirPhysicsModifier(StageDefinition stageDefinition)
    {
        return ResolveSurfacePhysicsModifier(stageDefinition, StageSurfacePhysicType.Normal);
    }

    private static StagePhysicsModifier ResolveSurfacePhysicsModifier(
        StageDefinition stageDefinition,
        StageSurfacePhysicType surfacePhysicType)
    {
        if (stageDefinition == null)
        {
            return StagePhysicsModifier.Normal;
        }

        return stageDefinition.ResolvePhysicsModifier(surfacePhysicType);
    }

    private PlayerObject SyncFromPlayerState(PlayerState state)
    {
        PlayerObject player = Create(state.clientId, ResolveCharacterId(state), state.skillId);
        PlayerRuntimeState runtimeState = state.characterStateMachine != null
            ? state.characterStateMachine.State
            : default;
        player.characterId = state.characterStateMachine != null
            ? runtimeState.characterId
            : player.characterId;
        player.position = state.position;
        player.velocity = state.velocity;
        player.aim = state.aim;
        player.facingSign = runtimeState.facingSign;
        player.locomotionState = runtimeState.locomotionState;
        player.input = state.input;
        player.buttons = state.buttons;
        player.isTagger = state.isTagger;
        player.stunnedTimer = state.stunnedTimer;
        player.taggerAccumulatedTime = state.taggerAccumulatedTime;
        player.collider = new WorldCollider(state.collisionOffset, state.collisionHalfExtent);
        return player;
    }

    private static byte ResolveCharacterId(PlayerState state)
    {
        return state.characterStateMachine != null
            ? state.characterStateMachine.State.characterId
            : (byte)0;
    }
}

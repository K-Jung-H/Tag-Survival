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

    // - Role: Register a player object.
    public PlayerObject Register(PlayerObject player)
    {
        if (player == null)
        {
            return null;
        }

        if (playersById.TryGetValue(player.playerId, out PlayerObject existing))
        {
            return existing;
        }

        playersById.Add(player.playerId, player);
        players.Add(player);
        return player;
    }

    // - Role: Remove a player object.
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

    // - Role: Try to get a player object.
    public bool TryGet(ulong playerId, out PlayerObject player)
    {
        return playersById.TryGetValue(playerId, out player);
    }

    // - Role: Update input.
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

    // - Role: Apply queued inputs.
    public void ApplyQueuedInputs(ServerInputBuffer inputBuffer)
    {
        if (inputBuffer == null)
        {
            return;
        }

        simulationTargets.Clear();
        for (int i = 0; i < players.Count; i++)
        {
            simulationTargets.Add(players[i].playerId);
        }

        for (int i = 0; i < simulationTargets.Count; i++)
        {
            ulong clientId = simulationTargets[i];
            if (!playersById.TryGetValue(clientId, out PlayerObject player)
                || !inputBuffer.TryConsumeInput(clientId, out ServerPlayerInputCommand command))
            {
                continue;
            }

            if (player.stunnedTimer > 0f)
            {
                ClearInput(player);
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
        }
    }

    // - Role: Simulate all players.
    public void SimulatePlayers(
        ServerSkillSystem skillSystem,
        StageCollisionSystem collisionSystem,
        StageDefinition stageDefinition,
        float deltaTime)
    {
        if (skillSystem == null || collisionSystem == null)
        {
            return;
        }

        simulationTargets.Clear();
        for (int i = 0; i < players.Count; i++)
        {
            simulationTargets.Add(players[i].playerId);
        }

        for (int i = 0; i < simulationTargets.Count; i++)
        {
            ulong clientId = simulationTargets[i];
            if (!playersById.TryGetValue(clientId, out PlayerObject player))
            {
                continue;
            }

            UpdateStunTimer(player, deltaTime);
            UpdateItemEffects(player, deltaTime);
            UpdateCoyoteTime(player, deltaTime);

            float horizontalInput = PlayerMovementController.GetPlatformerHorizontalInput(player.input);
            float verticalInput = PlayerMovementController.GetPlatformerVerticalInput(player.input);
            PlayerMovementController.ApplyPlatformerVelocity(
                player,
                stageDefinition,
                horizontalInput,
                verticalInput,
                deltaTime);
            skillSystem.Tick(player, deltaTime);
            skillSystem.Constrain(player, deltaTime);

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
                player.coyoteTimeRemaining = player.effectiveMovementStats.coyoteTime;
            }

            if (moveResult.hitCeiling && player.velocity.y > 0f)
            {
                player.velocity.y = 0f;
            }

            PlayerMovementController.UpdateWallStickAfterStageMove(
                player,
                stageDefinition,
                moveResult,
                horizontalInput,
                verticalInput,
                deltaTime);
            UpdateRenderState(player);
            UpdateCharacterStateMachine(player);
        }
    }

    // - Role: Copy world objects to.
    public void CopyWorldObjectsTo(List<IWorldObject> target)
    {
        if (target == null)
        {
            return;
        }

        for (int i = 0; i < players.Count; i++)
        {
            target.Add(players[i]);
        }
    }

    // - Role: Update render state.
    public static void UpdateRenderState(PlayerObject player)
    {
        if (player == null || player.characterStateMachine == null)
        {
            return;
        }

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
        player.locomotionState = locomotionState;
        player.facingSign = ResolveFacingSign(player, renderState.facingSign);
        renderState.locomotionState = player.locomotionState;
        renderState.facingSign = player.facingSign;

        player.characterStateMachine.ApplyState(renderState);
    }

    // - Role: Update character state machine.
    public static void UpdateCharacterStateMachine(PlayerObject player)
    {
        player?.SyncCharacterStateMachine();
    }

    // - Role: Clear input.
    public static void ClearInput(PlayerObject player)
    {
        if (player == null)
        {
            return;
        }

        player.input = Vector2.zero;
        player.buttons = PlayerInputButtons.None;
        player.isJumpPressed = false;
        player.jumpQueued = false;
        player.isSkillPressed = false;
        player.skillQueued = false;
        player.hasAimInput = false;
    }

    // - Role: Find facing sign.
    private static sbyte ResolveFacingSign(PlayerObject player, sbyte currentFacingSign)
    {
        sbyte fallbackFacingSign = currentFacingSign == 0 ? (sbyte)1 : currentFacingSign;
        float horizontalInput = PlayerMovementController.GetPlatformerHorizontalInput(player.input);

        if (player.isWallSticking)
        {
            if (horizontalInput > FacingDirectionThreshold) return 1;
            if (horizontalInput < -FacingDirectionThreshold) return -1;

            return fallbackFacingSign;
        }

        if (player.hasAimInput)
        {
            if (Mathf.Abs(player.aim.x) > FacingDirectionThreshold)
                return player.aim.x > 0f ? (sbyte)1 : (sbyte)-1;

            return fallbackFacingSign;
        }

        if (horizontalInput > FacingDirectionThreshold) return 1;
        if (horizontalInput < -FacingDirectionThreshold) return -1;

        return fallbackFacingSign;
    }

    // - Role: Update stun timer.
    private static void UpdateStunTimer(PlayerObject player, float deltaTime)
    {
        if (player.stunnedTimer <= 0f)
        {
            return;
        }

        player.stunnedTimer = Mathf.Max(0f, player.stunnedTimer - deltaTime);
        ClearInput(player);
    }

    // - Role: Update coyote time.
    private static void UpdateCoyoteTime(PlayerObject player, float deltaTime)
    {
        if (player.isGrounded)
        {
            player.coyoteTimeRemaining = player.effectiveMovementStats.coyoteTime;
            return;
        }

        if (player.isWallSticking)
        {
            player.coyoteTimeRemaining = 0f;
            return;
        }

        player.coyoteTimeRemaining = Mathf.Max(0f, player.coyoteTimeRemaining - deltaTime);
    }

    // - Role: Update item effects.
    private static void UpdateItemEffects(PlayerObject player, float deltaTime)
    {
        if (player.itemEffects == null)
        {
            player.itemEffects = new PlayerItemEffects();
        }

        player.itemEffects.Tick(deltaTime);
        player.effectiveMovementStats = player.itemEffects.ApplyMovementStats(player.movementStats);
        player.speed = player.effectiveMovementStats.moveSpeed;
    }
}

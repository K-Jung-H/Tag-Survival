using System.Collections.Generic;
using UnityEngine;
using CharacterRenderState = CharacterRuntimeState;

public sealed class ServerPlayerSystem
{
    private const float MovementStateThresholdSqr = 0.0001f;
    private const float FacingDirectionThreshold = 0.0001f;
    private const float JumpInputThreshold = 0.5f;

    private readonly Dictionary<ulong, PlayerObject> playersById = new();
    private readonly List<PlayerObject> players = new();
    private readonly List<ulong> simulationTargets = new();
    private ServerSkillSystem skillSystem;
    private StageCollisionSystem collisionSystem;
    private StageDefinition stageDefinition;

    public IReadOnlyList<PlayerObject> Players => players;

    // - Role: Bind needed links.
    public void Bind(ServerSkillSystem skillSystem, StageCollisionSystem collisionSystem, StageDefinition stageDefinition)
    {
        this.skillSystem = skillSystem;
        this.collisionSystem = collisionSystem;
        this.stageDefinition = stageDefinition;
    }

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

            if (player.stunnedTimer > 0f || player.BlocksInput)
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
    public void Simulate(float deltaTime)
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
            UpdateLateJumpTimer(player, deltaTime);

            if (player.BlocksMovement)
            {
                ClearInput(player);
                skillSystem.Tick(player, deltaTime);
                skillSystem.Constrain(player, deltaTime);
                UpdateRenderState(player);
                UpdateCharacterStateMachine(player);
                continue;
            }

            bool skillTicked = false;
            MovementOverrideMode movementMode = skillSystem.GetOwnerMovementOverride(player);
            if (movementMode == MovementOverrideMode.FullOverride)
            {
                skillSystem.Tick(player, deltaTime);
                skillTicked = true;
                movementMode = skillSystem.GetOwnerMovementOverride(player);
                if (movementMode == MovementOverrideMode.FullOverride
                    && skillSystem.TrySimulateOwnerMovement(
                        player,
                        collisionSystem,
                        stageDefinition,
                        deltaTime,
                        out StageCollisionMoveResult skillMoveResult))
                {
                    ApplyCollisionStateAfterMovement(player, skillMoveResult, deltaTime, allowWallStick: false);
                    UpdateRenderState(player);
                    UpdateCharacterStateMachine(player);
                    continue;
                }
            }

            PlayerMovementController.ApplyVelocity(
                player,
                stageDefinition,
                deltaTime,
                movementMode == MovementOverrideMode.SuppressHorizontalOnly);
            if (!skillTicked)
            {
                skillSystem.Tick(player, deltaTime);
            }

            skillSystem.Constrain(player, deltaTime);
            StageCollisionMoveResult moveResult = MovePlayerWithStageCollision(player, deltaTime);
            ApplyCollisionStateAfterMovement(player, moveResult, deltaTime, allowWallStick: true);
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
            PlayerObject player = players[i];
            if (player != null && player.AllowsWorldCollision)
            {
                target.Add(player);
            }
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
        LocomotionState locomotionState;
        if (player.BlocksMovement
            && (player.locomotionState == LocomotionState.BlinkEnter
                || player.locomotionState == LocomotionState.BlinkExit))
        {
            locomotionState = player.locomotionState;
        }
        else if (player.stunnedTimer > 0f)
        {
            locomotionState = LocomotionState.Stunned;
        }
        else if (player.isOnWall)
        {
            locomotionState = LocomotionState.WallStick;
        }
        else if (!player.isGrounded)
        {
            locomotionState = player.velocity.y > 0f
                ? LocomotionState.Jump
                : LocomotionState.Fall;
        }
        else
        {
            locomotionState = isMovingHorizontally ? LocomotionState.Run : LocomotionState.Idle;
        }

        CharacterRenderState renderState = player.characterStateMachine.State;
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
        sbyte retainedFacingSign = currentFacingSign == 0 ? (sbyte)1 : currentFacingSign;
        float horizontalInput = PlayerMovementController.GetHorizontalInput(player.input);

        if (player.isOnWall)
        {
            if (horizontalInput > FacingDirectionThreshold) return 1;
            if (horizontalInput < -FacingDirectionThreshold) return -1;

            return retainedFacingSign;
        }

        if (player.hasAimInput)
        {
            if (Mathf.Abs(player.aim.x) > FacingDirectionThreshold)
                return player.aim.x > 0f ? (sbyte)1 : (sbyte)-1;

            return retainedFacingSign;
        }

        if (horizontalInput > FacingDirectionThreshold) return 1;
        if (horizontalInput < -FacingDirectionThreshold) return -1;

        return retainedFacingSign;
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

    // - Role: Update late jump timer.
    private static void UpdateLateJumpTimer(PlayerObject player, float deltaTime)
    {
        if (player.isGrounded)
        {
            player.lateJumpTimer = player.moveStats.lateJumpTime;
            return;
        }

        if (player.isOnWall)
        {
            player.lateJumpTimer = 0f;
            return;
        }

        player.lateJumpTimer = Mathf.Max(0f, player.lateJumpTimer - deltaTime);
    }

    // - Role: Update item effects.
    private static void UpdateItemEffects(PlayerObject player, float deltaTime)
    {
        if (player.itemEffects == null)
        {
            player.itemEffects = new PlayerItemEffects();
        }

        player.itemEffects.Tick(deltaTime);
        player.moveStats = player.itemEffects.ApplyMovementStats(player.baseMoveStats);
    }

    // - Role: Move player with stage collision.
    private StageCollisionMoveResult MovePlayerWithStageCollision(PlayerObject player, float deltaTime)
    {
        Vector2 collisionCenter = player.position + player.collisionOffset;
        StageCollisionMoveResult moveResult = collisionSystem.MoveDetailed(
            collisionCenter,
            player.velocity * deltaTime,
            player.collisionHalfExtent);

        player.position = moveResult.position - player.collisionOffset;
        player.isGrounded = moveResult.isGrounded;
        if (moveResult.isGrounded)
        {
            player.groundSurface = moveResult.groundSurface;
        }

        return moveResult;
    }

    // - Role: Apply collision side effects after movement.
    private void ApplyCollisionStateAfterMovement(
        PlayerObject player,
        StageCollisionMoveResult moveResult,
        float deltaTime,
        bool allowWallStick)
    {
        if (player.isGrounded && player.velocity.y < 0f)
        {
            player.velocity.y = 0f;
            player.lateJumpTimer = player.moveStats.lateJumpTime;
        }

        if (moveResult.hitCeiling && player.velocity.y > 0f)
        {
            player.velocity.y = 0f;
        }

        if (allowWallStick)
        {
            PlayerMovementController.UpdateWallStick(player, stageDefinition, moveResult, deltaTime);
        }
    }
}

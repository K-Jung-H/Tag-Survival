using System.Collections.Generic;
using UnityEngine;

public enum StoryEnemyAwarenessState
{
    Idle = 0,
    ChasingPlayer = 1,
    MovingToLastSeen = 2,
    ReturningHome = 3
}

public enum StoryEnemyLocomotionPlan
{
    Idle = 0,
    DirectRun = 1
}

public enum StoryEnemyLocomotionIntent
{
    None = 0,
    DirectApproach = 1,
    JumpToHigherTarget = 2,
    ClimbCurrentWall = 3,
    RecoverFromStuck = 4
}

public sealed class StoryEnemyAiSystem
{
    private const float DefaultThinkIntervalSeconds = 0.15f;
    private const float DefaultSightRange = 9f;
    private const float DefaultTargetLostSeconds = 1f;
    private const float DefaultArriveDistance = 0.4f;
    private const float DefaultLastSeenGiveUpSeconds = 4f;
    private const float DefaultLineOfSightSampleStep = 0.25f;
    private const float DefaultObstacleProbeDistance = 0.6f;
    private const float DefaultJumpClearanceHeight = 1.2f;
    private const float DefaultTargetAboveJumpThreshold = 0.75f;
    private const float DefaultTargetAboveMaxHorizontalDistance = 5f;
    private const float DefaultHigherTargetJumpHeightMargin = 0.15f;
    private const float DefaultJumpCooldownSeconds = 0.8f;
    private const float DefaultJumpInputHoldSeconds = 0.12f;
    private const float DefaultRepeatedJumpPositionTolerance = 0.45f;
    private const int DefaultMaxRepeatedJumpAttempts = 2;
    private const float DefaultRepeatedJumpBlockSeconds = 1.2f;
    private const float DefaultWallClimbVerticalInput = 1f;
    private const float HomeReturnHorizontalInput = 0.5f;
    private const float PostStunLastSeenGiveUpMultiplier = 0.5f;
    private const float DefaultStuckCheckSeconds = 0.45f;
    private const float DefaultStuckMinMoveDistance = 0.08f;
    private const float DefaultStuckReverseSeconds = 0.35f;
    private const float DefaultStuckGiveUpSeconds = 2.5f;

    private readonly Dictionary<ulong, StoryEnemyAiAgentState> agentStates = new();
    private readonly List<ulong> removeTargets = new();

    private float thinkIntervalSeconds = DefaultThinkIntervalSeconds;
    private float sightRange = DefaultSightRange;
    private float targetLostSeconds = DefaultTargetLostSeconds;
    private float arriveDistance = DefaultArriveDistance;
    private float lastSeenGiveUpSeconds = DefaultLastSeenGiveUpSeconds;
    private float lineOfSightSampleStep = DefaultLineOfSightSampleStep;
    private float obstacleProbeDistance = DefaultObstacleProbeDistance;
    private float jumpClearanceHeight = DefaultJumpClearanceHeight;
    private float targetAboveJumpThreshold = DefaultTargetAboveJumpThreshold;
    private float targetAboveMaxHorizontalDistance = DefaultTargetAboveMaxHorizontalDistance;
    private float higherTargetJumpHeightMargin = DefaultHigherTargetJumpHeightMargin;
    private float jumpCooldownSeconds = DefaultJumpCooldownSeconds;
    private float jumpInputHoldSeconds = DefaultJumpInputHoldSeconds;
    private float repeatedJumpPositionTolerance = DefaultRepeatedJumpPositionTolerance;
    private int maxRepeatedJumpAttempts = DefaultMaxRepeatedJumpAttempts;
    private float repeatedJumpBlockSeconds = DefaultRepeatedJumpBlockSeconds;
    private float wallClimbVerticalInput = DefaultWallClimbVerticalInput;
    private float stuckCheckSeconds = DefaultStuckCheckSeconds;
    private float stuckMinMoveDistance = DefaultStuckMinMoveDistance;
    private float stuckReverseSeconds = DefaultStuckReverseSeconds;
    private float stuckGiveUpSeconds = DefaultStuckGiveUpSeconds;

    public void Configure(StoryStageConfig stageConfig)
    {
        if (stageConfig == null || stageConfig.EnemyAiConfig == null)
        {
            ResetRuntimeConfig();
            Clear();
            return;
        }

        StoryEnemyAiConfig aiConfig = stageConfig.EnemyAiConfig;
        thinkIntervalSeconds = aiConfig.ThinkIntervalSeconds;
        sightRange = aiConfig.SightRange;
        targetLostSeconds = aiConfig.TargetLostSeconds;
        arriveDistance = aiConfig.ArriveDistance;
        lastSeenGiveUpSeconds = aiConfig.LastSeenGiveUpSeconds;
        lineOfSightSampleStep = aiConfig.LineOfSightSampleStep;
        obstacleProbeDistance = aiConfig.ObstacleProbeDistance;
        jumpClearanceHeight = aiConfig.JumpClearanceHeight;
        targetAboveJumpThreshold = aiConfig.TargetAboveJumpThreshold;
        targetAboveMaxHorizontalDistance = aiConfig.TargetAboveMaxHorizontalDistance;
        higherTargetJumpHeightMargin = aiConfig.HigherTargetJumpHeightMargin;
        jumpCooldownSeconds = aiConfig.JumpCooldownSeconds;
        jumpInputHoldSeconds = aiConfig.JumpInputHoldSeconds;
        repeatedJumpPositionTolerance = aiConfig.RepeatedJumpPositionTolerance;
        maxRepeatedJumpAttempts = aiConfig.MaxRepeatedJumpAttempts;
        repeatedJumpBlockSeconds = aiConfig.RepeatedJumpBlockSeconds;
        wallClimbVerticalInput = aiConfig.WallClimbVerticalInput;
        stuckCheckSeconds = aiConfig.StuckCheckSeconds;
        stuckMinMoveDistance = aiConfig.StuckMinMoveDistance;
        stuckReverseSeconds = aiConfig.StuckReverseSeconds;
        stuckGiveUpSeconds = aiConfig.StuckGiveUpSeconds;
        Clear();
    }

    private void ResetRuntimeConfig()
    {
        thinkIntervalSeconds = DefaultThinkIntervalSeconds;
        sightRange = DefaultSightRange;
        targetLostSeconds = DefaultTargetLostSeconds;
        arriveDistance = DefaultArriveDistance;
        lastSeenGiveUpSeconds = DefaultLastSeenGiveUpSeconds;
        lineOfSightSampleStep = DefaultLineOfSightSampleStep;
        obstacleProbeDistance = DefaultObstacleProbeDistance;
        jumpClearanceHeight = DefaultJumpClearanceHeight;
        targetAboveJumpThreshold = DefaultTargetAboveJumpThreshold;
        targetAboveMaxHorizontalDistance = DefaultTargetAboveMaxHorizontalDistance;
        higherTargetJumpHeightMargin = DefaultHigherTargetJumpHeightMargin;
        jumpCooldownSeconds = DefaultJumpCooldownSeconds;
        jumpInputHoldSeconds = DefaultJumpInputHoldSeconds;
        repeatedJumpPositionTolerance = DefaultRepeatedJumpPositionTolerance;
        maxRepeatedJumpAttempts = DefaultMaxRepeatedJumpAttempts;
        repeatedJumpBlockSeconds = DefaultRepeatedJumpBlockSeconds;
        wallClimbVerticalInput = DefaultWallClimbVerticalInput;
        stuckCheckSeconds = DefaultStuckCheckSeconds;
        stuckMinMoveDistance = DefaultStuckMinMoveDistance;
        stuckReverseSeconds = DefaultStuckReverseSeconds;
        stuckGiveUpSeconds = DefaultStuckGiveUpSeconds;
    }

    public void Clear()
    {
        agentStates.Clear();
        removeTargets.Clear();
    }

    public void Tick(
        IReadOnlyDictionary<ulong, PlayerObject> players,
        StoryGameMode storyGameMode,
        StageCollisionSystem collisionSystem,
        float deltaTime)
    {
        if (players == null || storyGameMode == null)
        {
            Clear();
            return;
        }

        PlayerObject targetPlayer = FindTargetPlayer(players, storyGameMode);
        if (targetPlayer == null
            || storyGameMode.Phase != GamePhase.Playing
            || storyGameMode.IsCleared
            || storyGameMode.IsGameEnded)
        {
            ClearEnemyInputs(players, storyGameMode);
            return;
        }

        foreach (var pair in players)
        {
            PlayerObject enemy = pair.Value;
            if (enemy == null || !storyGameMode.IsEnemyPlayer(enemy.playerId))
            {
                continue;
            }

            TickAgent(enemy, targetPlayer, collisionSystem, Mathf.Max(0f, deltaTime));
        }

        RemoveMissingAgents(players);
    }

    private void TickAgent(
        PlayerObject enemy,
        PlayerObject targetPlayer,
        StageCollisionSystem collisionSystem,
        float deltaTime)
    {
        StoryEnemyAiAgentState state = GetOrCreateState(enemy);
        TickActionTimers(state, deltaTime);
        state.thinkTimer -= deltaTime;
        if (state.thinkTimer <= 0f)
        {
            state.thinkTimer = thinkIntervalSeconds;
            Think(enemy, targetPlayer, collisionSystem, state);
        }

        ApplyInput(enemy, state);
    }

    private void Think(
        PlayerObject enemy,
        PlayerObject targetPlayer,
        StageCollisionSystem collisionSystem,
        StoryEnemyAiAgentState state)
    {
        bool isStunned = enemy.stunnedTimer > 0f;
        if (!isStunned && state.wasStunned)
        {
            state.wasStunned = false;
            state.usePostStunLastSeenGiveUp = true;
        }

        Vector2 enemyEye = enemy.position + enemy.collisionOffset;
        Vector2 targetEye = targetPlayer.position + targetPlayer.collisionOffset;
        Vector2 toTarget = targetEye - enemyEye;
        float distanceToTarget = toTarget.magnitude;
        bool inRange = distanceToTarget <= sightRange;
        bool lineBlocked = inRange
            && collisionSystem != null
            && collisionSystem.IsLineBlocked(enemyEye, targetEye, lineOfSightSampleStep);
        bool canAcquireTarget = inRange && !lineBlocked;
        bool canTrackTarget = state.awareness == StoryEnemyAwarenessState.ChasingPlayer
            ? inRange
            : canAcquireTarget;

        switch (state.awareness)
        {
            case StoryEnemyAwarenessState.ChasingPlayer:
                UpdateChasingState(state, targetPlayer.position, canTrackTarget);
                break;
            case StoryEnemyAwarenessState.MovingToLastSeen:
                UpdateMoveToLastSeenState(enemy, state);
                if (canAcquireTarget)
                {
                    EnterChase(state, targetPlayer.position);
                }

                break;
            case StoryEnemyAwarenessState.ReturningHome:
                UpdateReturningHomeState(enemy, state);
                if (canAcquireTarget)
                {
                    EnterChase(state, targetPlayer.position);
                }

                break;
            default:
                if (canAcquireTarget)
                {
                    EnterChase(state, targetPlayer.position);
                }
                else
                {
                    state.plan = StoryEnemyLocomotionPlan.Idle;
                }

                break;
        }

        if (isStunned || enemy.BlocksInput || enemy.BlocksMovement)
        {
            if (isStunned)
            {
                state.wasStunned = true;
            }

            state.plan = StoryEnemyLocomotionPlan.Idle;
            ResetMovementPlan(state);
            ServerPlayerSystem.ClearInput(enemy);
            return;
        }

        UpdatePlan(enemy, state);
        UpdateStuckState(enemy, state);
        UpdateTerrainPlan(enemy, collisionSystem, state);
    }

    private void UpdateChasingState(StoryEnemyAiAgentState state, Vector2 targetPosition, bool canSeeTarget)
    {
        if (canSeeTarget)
        {
            state.usePostStunLastSeenGiveUp = false;
            state.targetLostSightSeconds = 0f;
            state.lastSeenPosition = targetPosition;
            state.hasLastSeenPosition = true;
            return;
        }

        state.targetLostSightSeconds += thinkIntervalSeconds;
        if (state.targetLostSightSeconds < targetLostSeconds)
        {
            return;
        }

        if (state.hasLastSeenPosition)
        {
            state.awareness = StoryEnemyAwarenessState.MovingToLastSeen;
            state.lastSeenElapsedSeconds = 0f;
            state.lastSeenStartDistance = float.PositiveInfinity;
            return;
        }

        state.awareness = StoryEnemyAwarenessState.Idle;
    }

    private void UpdateMoveToLastSeenState(PlayerObject enemy, StoryEnemyAiAgentState state)
    {
        if (!state.hasLastSeenPosition)
        {
            state.awareness = StoryEnemyAwarenessState.Idle;
            return;
        }

        state.lastSeenElapsedSeconds += thinkIntervalSeconds;
        float giveUpSeconds = GetLastSeenGiveUpSeconds(state);
        float distance = Vector2.Distance(enemy.position, state.lastSeenPosition);
        if (distance <= arriveDistance)
        {
            if (giveUpSeconds > 0f && state.lastSeenElapsedSeconds >= giveUpSeconds)
            {
                EnterReturningHome(state);
                return;
            }

            return;
        }

        if (float.IsPositiveInfinity(state.lastSeenStartDistance))
        {
            state.lastSeenStartDistance = distance;
        }

        if (giveUpSeconds > 0f && state.lastSeenElapsedSeconds >= giveUpSeconds)
        {
            EnterReturningHome(state);
        }
    }

    private void UpdateReturningHomeState(PlayerObject enemy, StoryEnemyAiAgentState state)
    {
        if (!state.hasHomePosition)
        {
            EnterIdle(state);
            return;
        }

        state.lastSeenPosition = state.homePosition;
        state.hasLastSeenPosition = true;

        float distance = Vector2.Distance(enemy.position, state.homePosition);
        if (distance <= arriveDistance)
        {
            EnterIdle(state);
        }
    }

    private void UpdatePlan(PlayerObject enemy, StoryEnemyAiAgentState state)
    {
        if (state.awareness == StoryEnemyAwarenessState.Idle)
        {
            state.plan = StoryEnemyLocomotionPlan.Idle;
            state.verticalInput = 0f;
            state.horizontalInputScale = 1f;
            return;
        }

        state.horizontalInputScale = state.awareness == StoryEnemyAwarenessState.ReturningHome
            ? HomeReturnHorizontalInput
            : 1f;

        Vector2 targetPosition = state.lastSeenPosition;

        float deltaX = targetPosition.x - enemy.position.x;
        if (Mathf.Abs(deltaX) <= arriveDistance)
        {
            if (targetPosition.y - enemy.position.y >= targetAboveJumpThreshold)
            {
                state.plan = StoryEnemyLocomotionPlan.DirectRun;
                state.moveDirection = 0;
                state.verticalInput = 0f;
                return;
            }

            state.plan = StoryEnemyLocomotionPlan.Idle;
            state.moveDirection = 0;
            state.verticalInput = 0f;
            return;
        }

        state.plan = StoryEnemyLocomotionPlan.DirectRun;
        state.moveDirection = deltaX > 0f ? 1 : -1;
        state.verticalInput = 0f;
    }

    private void UpdateTerrainPlan(
        PlayerObject enemy,
        StageCollisionSystem collisionSystem,
        StoryEnemyAiAgentState state)
    {
        if (enemy == null || state.plan == StoryEnemyLocomotionPlan.Idle)
        {
            return;
        }

        bool blockedAhead = IsBlockedAhead(enemy, collisionSystem, state.moveDirection);
        bool canJumpTowardHigherTarget = CanJumpTowardHigherTarget(enemy, collisionSystem, state.lastSeenPosition);

        state.intent = SelectLocomotionIntent(
            enemy,
            state,
            blockedAhead,
            canJumpTowardHigherTarget);

        ApplyLocomotionIntent(enemy, state);
    }

    private StoryEnemyLocomotionIntent SelectLocomotionIntent(
        PlayerObject enemy,
        StoryEnemyAiAgentState state,
        bool blockedAhead,
        bool canJumpTowardHigherTarget)
    {
        if (enemy.isOnWall)
        {
            return StoryEnemyLocomotionIntent.ClimbCurrentWall;
        }

        if (state.isStuck)
        {
            return StoryEnemyLocomotionIntent.RecoverFromStuck;
        }

        if (enemy.isGrounded && state.jumpCooldownTimer <= 0f && (blockedAhead || canJumpTowardHigherTarget))
        {
            return StoryEnemyLocomotionIntent.JumpToHigherTarget;
        }

        return StoryEnemyLocomotionIntent.DirectApproach;
    }

    private void ApplyLocomotionIntent(PlayerObject enemy, StoryEnemyAiAgentState state)
    {
        switch (state.intent)
        {
            case StoryEnemyLocomotionIntent.ClimbCurrentWall:
                PlanWallMovement(enemy, state);
                break;
            case StoryEnemyLocomotionIntent.JumpToHigherTarget:
                TryStartGroundJump(enemy, state);
                break;
            case StoryEnemyLocomotionIntent.RecoverFromStuck:
                ApplyStuckRecovery(enemy, state);
                break;
        }
    }

    private void PlanWallMovement(PlayerObject enemy, StoryEnemyAiAgentState state)
    {
        if (enemy.wallDirX != 0 && state.moveDirection == 0)
        {
            state.moveDirection = -enemy.wallDirX;
        }

        bool movingIntoWall = enemy.wallDirX != 0 && state.moveDirection == -enemy.wallDirX;
        if (movingIntoWall)
        {
            state.verticalInput = wallClimbVerticalInput;
            state.reverseInputTimer = 0f;
            state.reverseDirection = 0;
            return;
        }
    }

    private void ApplyInput(PlayerObject enemy, StoryEnemyAiAgentState state)
    {
        if (enemy == null)
        {
            return;
        }

        if (enemy.stunnedTimer > 0f || enemy.BlocksInput || enemy.BlocksMovement || state.plan == StoryEnemyLocomotionPlan.Idle)
        {
            ResetMovementPlan(state);
            ServerPlayerSystem.ClearInput(enemy);
            return;
        }

        float horizontalInput = state.moveDirection * state.horizontalInputScale;
        float verticalInput = state.verticalInput;
        bool wantsJumpInput = state.jumpInputTimer > 0f;

        if (!enemy.isOnWall && state.reverseInputTimer > 0f && state.reverseDirection != 0)
        {
            horizontalInput = state.reverseDirection * state.horizontalInputScale;
        }
        else if (wantsJumpInput)
        {
            verticalInput = 1f;
        }

        enemy.input = new Vector2(horizontalInput, verticalInput);
        enemy.buttons = PlayerInputButtons.None;
        enemy.hasAimInput = false;
        enemy.isSkillPressed = false;
        enemy.skillQueued = false;
        enemy.jumpQueued = wantsJumpInput && !enemy.isJumpPressed;
        enemy.isJumpPressed = wantsJumpInput;
    }

    private void TickActionTimers(StoryEnemyAiAgentState state, float deltaTime)
    {
        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        state.jumpCooldownTimer = Mathf.Max(0f, state.jumpCooldownTimer - safeDeltaTime);
        state.jumpInputTimer = Mathf.Max(0f, state.jumpInputTimer - safeDeltaTime);
        state.reverseInputTimer = Mathf.Max(0f, state.reverseInputTimer - safeDeltaTime);
        state.jumpBlockedTimer = Mathf.Max(0f, state.jumpBlockedTimer - safeDeltaTime);
    }

    private bool TryStartGroundJump(PlayerObject enemy, StoryEnemyAiAgentState state)
    {
        if (enemy == null || state.jumpBlockedTimer > 0f)
        {
            return false;
        }

        if (maxRepeatedJumpAttempts > 0)
        {
            Vector2 jumpStartPosition = enemy.position;
            bool repeatedJump = state.hasLastJumpStartPosition
                && Vector2.Distance(jumpStartPosition, state.lastJumpStartPosition) <= repeatedJumpPositionTolerance;

            state.repeatedJumpAttempts = repeatedJump ? state.repeatedJumpAttempts + 1 : 1;
            state.lastJumpStartPosition = jumpStartPosition;
            state.hasLastJumpStartPosition = true;

            if (state.repeatedJumpAttempts > maxRepeatedJumpAttempts)
            {
                state.jumpBlockedTimer = repeatedJumpBlockSeconds;
                state.repeatedJumpAttempts = 0;
                return false;
            }
        }

        state.jumpInputTimer = jumpInputHoldSeconds;
        state.jumpCooldownTimer = jumpCooldownSeconds;
        state.isStuck = false;
        state.stuckElapsedSeconds = 0f;
        return true;
    }

    private void ApplyStuckRecovery(PlayerObject enemy, StoryEnemyAiAgentState state)
    {
        if (enemy == null)
        {
            return;
        }

        if (enemy.isGrounded && state.jumpCooldownTimer <= 0f && TryStartGroundJump(enemy, state))
        {
            return;
        }

        if (!enemy.isOnWall && stuckReverseSeconds > 0f && state.reverseInputTimer <= 0f && state.moveDirection != 0)
        {
            state.reverseDirection = -state.moveDirection;
            state.reverseInputTimer = stuckReverseSeconds;
        }
    }

    private bool IsBlockedAhead(
        PlayerObject enemy,
        StageCollisionSystem collisionSystem,
        int moveDirection)
    {
        if (enemy == null || collisionSystem == null || moveDirection == 0 || obstacleProbeDistance <= 0f)
        {
            return false;
        }

        Vector2 center = enemy.position + enemy.collisionOffset;
        Vector2 aheadCenter = center + new Vector2(moveDirection * obstacleProbeDistance, 0f);
        if (!collisionSystem.IsAreaBlocked(aheadCenter, enemy.collisionHalfExtent, lineOfSightSampleStep))
        {
            return false;
        }

        if (jumpClearanceHeight <= 0f)
        {
            return true;
        }

        Vector2 raisedCenter = aheadCenter + new Vector2(0f, jumpClearanceHeight);
        return !collisionSystem.IsAreaBlocked(raisedCenter, enemy.collisionHalfExtent, lineOfSightSampleStep);
    }

    private bool CanJumpTowardHigherTarget(
        PlayerObject enemy,
        StageCollisionSystem collisionSystem,
        Vector2 targetPosition)
    {
        if (enemy == null || collisionSystem == null)
        {
            return false;
        }

        float deltaY = targetPosition.y - enemy.position.y;
        if (deltaY < targetAboveJumpThreshold)
        {
            return false;
        }

        float deltaX = targetPosition.x - enemy.position.x;
        float absDeltaX = Mathf.Abs(deltaX);
        if (targetAboveMaxHorizontalDistance > 0f && absDeltaX > targetAboveMaxHorizontalDistance)
        {
            return false;
        }

        float jumpReachHeight = enemy.moveStats.jumpHeight + higherTargetJumpHeightMargin;
        if (deltaY > jumpReachHeight)
        {
            return false;
        }

        Vector2 center = enemy.position + enemy.collisionOffset;
        Vector2 overheadCenter = center + Vector2.up * Mathf.Min(jumpReachHeight, Mathf.Max(deltaY, jumpClearanceHeight));
        if (collisionSystem.IsAreaBlocked(overheadCenter, enemy.collisionHalfExtent, lineOfSightSampleStep))
        {
            return false;
        }

        if (absDeltaX <= arriveDistance)
        {
            return true;
        }

        float direction = Mathf.Sign(deltaX);
        float forwardDistance = Mathf.Min(absDeltaX, Mathf.Max(obstacleProbeDistance, enemy.collisionHalfExtent.x));
        Vector2 forwardRaisedCenter = center + new Vector2(direction * forwardDistance, Mathf.Min(deltaY, jumpReachHeight));
        return !collisionSystem.IsAreaBlocked(forwardRaisedCenter, enemy.collisionHalfExtent, lineOfSightSampleStep);
    }

    private void UpdateStuckState(PlayerObject enemy, StoryEnemyAiAgentState state)
    {
        if (enemy == null || state.plan == StoryEnemyLocomotionPlan.Idle)
        {
            state.stuckCheckTimer = 0f;
            state.stuckElapsedSeconds = 0f;
            state.isStuck = false;
            state.hasLastStuckCheckPosition = false;
            return;
        }

        if (!state.hasLastStuckCheckPosition)
        {
            state.lastStuckCheckPosition = enemy.position;
            state.hasLastStuckCheckPosition = true;
            state.stuckCheckTimer = 0f;
            state.stuckElapsedSeconds = 0f;
            state.isStuck = false;
            return;
        }

        state.stuckCheckTimer += thinkIntervalSeconds;
        if (state.stuckCheckTimer < stuckCheckSeconds)
        {
            return;
        }

        float movedDistance = Vector2.Distance(enemy.position, state.lastStuckCheckPosition);
        bool barelyMoved = movedDistance <= stuckMinMoveDistance;
        state.stuckElapsedSeconds = barelyMoved
            ? state.stuckElapsedSeconds + state.stuckCheckTimer
            : 0f;
        state.isStuck = barelyMoved;
        state.lastStuckCheckPosition = enemy.position;
        state.stuckCheckTimer = 0f;

        if (!state.isStuck)
        {
            return;
        }

        if (state.awareness == StoryEnemyAwarenessState.MovingToLastSeen
            && stuckGiveUpSeconds > 0f
            && state.stuckElapsedSeconds >= stuckGiveUpSeconds)
        {
            EnterReturningHome(state);
            return;
        }

        if (state.awareness == StoryEnemyAwarenessState.ReturningHome
            && stuckGiveUpSeconds > 0f
            && state.stuckElapsedSeconds >= stuckGiveUpSeconds)
        {
            EnterIdle(state);
            return;
        }
    }

    private static void ResetMovementPlan(StoryEnemyAiAgentState state)
    {
        state.intent = StoryEnemyLocomotionIntent.None;
        state.moveDirection = 0;
        state.verticalInput = 0f;
        state.horizontalInputScale = 1f;
        state.jumpInputTimer = 0f;
        state.reverseInputTimer = 0f;
        state.reverseDirection = 0;
    }

    private static void EnterChase(StoryEnemyAiAgentState state, Vector2 targetPosition)
    {
        state.awareness = StoryEnemyAwarenessState.ChasingPlayer;
        state.usePostStunLastSeenGiveUp = false;
        state.targetLostSightSeconds = 0f;
        state.lastSeenPosition = targetPosition;
        state.hasLastSeenPosition = true;
    }

    private static void EnterReturningHome(StoryEnemyAiAgentState state)
    {
        if (!state.hasHomePosition)
        {
            EnterIdle(state);
            return;
        }

        state.awareness = StoryEnemyAwarenessState.ReturningHome;
        state.usePostStunLastSeenGiveUp = false;
        state.lastSeenPosition = state.homePosition;
        state.hasLastSeenPosition = true;
        state.targetLostSightSeconds = 0f;
        state.lastSeenElapsedSeconds = 0f;
        state.lastSeenStartDistance = float.PositiveInfinity;
        state.stuckCheckTimer = 0f;
        state.stuckElapsedSeconds = 0f;
        state.isStuck = false;
        state.hasLastStuckCheckPosition = false;
    }

    private static void EnterIdle(StoryEnemyAiAgentState state)
    {
        state.awareness = StoryEnemyAwarenessState.Idle;
        state.plan = StoryEnemyLocomotionPlan.Idle;
        ResetMovementPlan(state);
        state.hasLastSeenPosition = false;
        state.usePostStunLastSeenGiveUp = false;
        state.wasStunned = false;
        state.targetLostSightSeconds = 0f;
        state.lastSeenElapsedSeconds = 0f;
        state.lastSeenStartDistance = float.PositiveInfinity;
        state.stuckCheckTimer = 0f;
        state.stuckElapsedSeconds = 0f;
        state.isStuck = false;
        state.hasLastStuckCheckPosition = false;
        state.hasLastJumpStartPosition = false;
        state.repeatedJumpAttempts = 0;
        state.jumpBlockedTimer = 0f;
    }

    private StoryEnemyAiAgentState GetOrCreateState(PlayerObject enemy)
    {
        if (!agentStates.TryGetValue(enemy.playerId, out StoryEnemyAiAgentState state))
        {
            state = new StoryEnemyAiAgentState
            {
                awareness = StoryEnemyAwarenessState.Idle,
                plan = StoryEnemyLocomotionPlan.Idle,
                thinkTimer = 0f,
                hasHomePosition = true,
                homePosition = enemy.position,
                lastSeenStartDistance = float.PositiveInfinity,
                jumpCooldownTimer = 0f
            };
            agentStates.Add(enemy.playerId, state);
        }

        return state;
    }

    private float GetLastSeenGiveUpSeconds(StoryEnemyAiAgentState state)
    {
        float multiplier = state.usePostStunLastSeenGiveUp
            ? PostStunLastSeenGiveUpMultiplier
            : 1f;
        return lastSeenGiveUpSeconds * multiplier;
    }

    private void RemoveMissingAgents(IReadOnlyDictionary<ulong, PlayerObject> players)
    {
        removeTargets.Clear();
        foreach (var pair in agentStates)
        {
            if (!players.ContainsKey(pair.Key))
            {
                removeTargets.Add(pair.Key);
            }
        }

        for (int i = 0; i < removeTargets.Count; i++)
        {
            agentStates.Remove(removeTargets[i]);
        }
    }

    private static PlayerObject FindTargetPlayer(
        IReadOnlyDictionary<ulong, PlayerObject> players,
        StoryGameMode storyGameMode)
    {
        foreach (var pair in players)
        {
            PlayerObject player = pair.Value;
            if (player != null && !storyGameMode.IsEnemyPlayer(player.playerId))
            {
                return player;
            }
        }

        return null;
    }

    private static void ClearEnemyInputs(
        IReadOnlyDictionary<ulong, PlayerObject> players,
        StoryGameMode storyGameMode)
    {
        foreach (var pair in players)
        {
            PlayerObject player = pair.Value;
            if (player != null && storyGameMode.IsEnemyPlayer(player.playerId))
            {
                ServerPlayerSystem.ClearInput(player);
            }
        }
    }

    private sealed class StoryEnemyAiAgentState
    {
        public StoryEnemyAwarenessState awareness;
        public StoryEnemyLocomotionPlan plan;
        public StoryEnemyLocomotionIntent intent;
        public float thinkTimer;
        public bool hasLastSeenPosition;
        public Vector2 lastSeenPosition;
        public float targetLostSightSeconds;
        public float lastSeenElapsedSeconds;
        public float lastSeenStartDistance;
        public bool wasStunned;
        public bool usePostStunLastSeenGiveUp;
        public bool hasHomePosition;
        public Vector2 homePosition;
        public int moveDirection;
        public float verticalInput;
        public float horizontalInputScale = 1f;
        public float jumpCooldownTimer;
        public float jumpInputTimer;
        public float reverseInputTimer;
        public int reverseDirection;
        public bool hasLastJumpStartPosition;
        public Vector2 lastJumpStartPosition;
        public int repeatedJumpAttempts;
        public float jumpBlockedTimer;
        public bool hasLastStuckCheckPosition;
        public Vector2 lastStuckCheckPosition;
        public float stuckCheckTimer;
        public float stuckElapsedSeconds;
        public bool isStuck;
    }
}

using UnityEngine;

[SkillLogic("hook")]
public sealed class SkillStateMachine_Hook : SkillStateMachine
{
    private enum RopeMotionState
    {
        None = 0,
        Slack = 1,
        Taut = 2
    }

    private const byte HookObjectIndex = 0;
    private const float DefaultHookSpeed = 18f;
    private const float DefaultReturnSpeedMultiplier = 2f;
    private const float DefaultHookHitHalfExtent = 0.08f;
    private const float DefaultReturnCompleteDistance = 0.18f;
    private const float DefaultSwingGravity = 50f;
    private const float DefaultSwingForwardAcceleration = 18f;
    private const float DefaultSwingBrakeAcceleration = 24f;
    private const float DefaultSwingMaxTangentSpeed = 18f;
    private const float DefaultSwingIdleDampingPerSecond = 0.98f;
    private const float DefaultSwingDetachBoost = 15f;
    private const float DefaultRopeTautTolerance = 0.03f;
    private const bool DefaultPullReelUnlocked = true;
    private const float DefaultReelSpeed = 5f;
    private const float DefaultMinRopeLength = 1.5f;
    private const float MovementInputThreshold = 0.0001f;
    private const float DirectionThresholdSqr = 0.000001f;

    private readonly HookSkillConfig config;

    private ulong ownerClientId;
    private Vector2 hookPosition;
    private Vector2 hookVelocity;
    private Vector2 fireDirection = Vector2.right;
    private Vector2 hookDirection = Vector2.right;
    private Vector2 fireStartPosition;
    private Vector2 anchorPosition;
    private float ropeLength;
    private float currentRange;
    private RopeMotionState ropeMotionState;

    // - Role: Create hook skill state machine.
    public SkillStateMachine_Hook(SkillDefinition definition)
        : base(definition)
    {
        config = definition != null ? definition.GetConfig<HookSkillConfig>() : null;
    }

    // - Role: Find owner movement override mode.
    public override MovementOverrideMode GetOwnerMovementOverride(PlayerObject player)
    {
        return State == SkillObjectState.Active && player != null && !player.isGrounded && IsRopeTaut(player)
            ? MovementOverrideMode.FullOverride
            : MovementOverrideMode.Normal;
    }

    // - Role: Constrain owner movement.
    public override void ConstrainOwner(
        PlayerObject player,
        float deltaTime)
    {
        if (State != SkillObjectState.Active)
        {
            return;
        }

        hookPosition = anchorPosition;
        hookVelocity = Vector2.zero;
        currentRange = GetRange(player);

        if (!EnsureRopeLength(player))
        {
            return;
        }

        ApplyPullReel(player, deltaTime);
        if (!TryGetRadialDirection(player, out Vector2 direction, out float distance))
        {
            return;
        }

        hookDirection = direction;
        UpdateRopeMotionState(distance);

        if (!IsRopeTaut(distance) && !WillReachRopeTaut(player, deltaTime))
        {
            return;
        }

        RemoveOutwardRopeVelocity(player, direction);
    }

    // - Role: Try to simulate owner movement.
    public override bool TrySimulateOwnerMovement(
        PlayerObject player,
        StageCollisionSystem collisionSystem,
        StageDefinition stageDefinition,
        float deltaTime,
        out StageCollisionMoveResult moveResult)
    {
        moveResult = default;
        if (State != SkillObjectState.Active
            || player == null
            || collisionSystem == null
            || deltaTime <= 0f)
        {
            return false;
        }

        hookPosition = anchorPosition;
        hookVelocity = Vector2.zero;
        currentRange = GetRange(player);

        if (!EnsureRopeLength(player))
        {
            ropeMotionState = RopeMotionState.None;
            return false;
        }

        if (!TryGetRadialDirection(player, out Vector2 radialDirection, out float distance))
        {
            ropeMotionState = RopeMotionState.None;
            return false;
        }

        if (!IsRopeTaut(distance))
        {
            UpdateRopeMotionState(distance);
            return false;
        }

        ropeMotionState = RopeMotionState.Taut;
        ApplyPullReel(player, deltaTime);

        Vector2 startAnchor = GetPlayerAnchorPosition(player);
        Vector2 velocity = player.velocity + Vector2.down * SwingGravity * deltaTime;
        velocity = ApplySwingInput(player, radialDirection, velocity, deltaTime, out bool hasSwingInput);
        if (!hasSwingInput)
        {
            velocity = ApplySwingIdleDamping(radialDirection, velocity, deltaTime);
        }

        Vector2 targetAnchor = ConstrainAnchorToRope(startAnchor + velocity * deltaTime, out bool constrained);

        moveResult = collisionSystem.MoveDetailed(
            startAnchor,
            targetAnchor - startAnchor,
            player.collisionHalfExtent);

        Vector2 actualAnchor = moveResult.position;
        Vector2 actualDelta = actualAnchor - startAnchor;
        player.position = actualAnchor - player.collisionOffset;
        player.velocity = actualDelta / deltaTime;
        player.isGrounded = moveResult.isGrounded;
        if (moveResult.isGrounded)
        {
            player.groundSurface = moveResult.groundSurface;
        }

        if (TryGetRadialDirection(actualAnchor, out Vector2 actualRadialDirection, out float actualDistance))
        {
            if (constrained || IsRopeTaut(actualDistance))
            {
                player.velocity = RemoveRopeRadialVelocity(player.velocity, actualRadialDirection);
            }

            hookDirection = actualRadialDirection;
            UpdateRopeMotionState(actualDistance);
        }

        player.isOnWall = false;
        player.wallDirX = 0;
        return true;
    }

    // - Role: Simulate this object.
    public override void Simulate(
        PlayerObject player,
        float deltaTime,
        bool skillPressedThisTick)
    {
        ownerClientId = player.playerId;
        currentRange = GetRange(player);
        TickCooldown(deltaTime);

        if (skillPressedThisTick)
        {
            HandleSkillPressed(player);
        }

        switch (State)
        {
            case SkillObjectState.Spawning:
                break;
            case SkillObjectState.Active:
                SimulateAttached();
                break;
            case SkillObjectState.Destroying:
                SimulateReturning(player, deltaTime);
                break;
        }
    }

    // - Role: Sync skill objects.
    public override void SyncSkillObjects(Skill skill)
    {
        if (skill == null)
        {
            return;
        }

        if (State == SkillObjectState.None)
        {
            skill.RemoveObject(HookObjectIndex);
            return;
        }

        SkillObject skillObject = skill.UpsertObject(HookObjectIndex);
        skillObject.ownerId = ownerClientId;
        skillObject.skillId = SkillId;
        skillObject.skillType = SkillType;
        skillObject.skillObjectId = HookObjectIndex;
        skillObject.objectState = State;
        skillObject.position = hookPosition;
        skillObject.velocity = hookVelocity;
        skillObject.rotation = Mathf.Atan2(hookDirection.y, hookDirection.x) * Mathf.Rad2Deg;
        skillObject.collider = new WorldCollider(Vector2.zero, new Vector2(HookHitHalfExtent, HookHitHalfExtent));
        skillObject.stageMode = State == SkillObjectState.Spawning
            ? SkillStageMode.MoveWithStageCollision
            : SkillStageMode.None;
        skillObject.stageSearchDistance = 0;
    }

    // - Role: Handle stage move result.
    public override void OnStageMoveResult(SkillObject self, StageCollisionMoveResult moveResult)
    {
        if (self == null || self.skillObjectId != HookObjectIndex || State != SkillObjectState.Spawning)
        {
            return;
        }

        hookPosition = self.position;
        if (moveResult.isGrounded || moveResult.hitCeiling || moveResult.hitWall)
        {
            PlayerObject owner = ResolveOwnerPlayer(self);
            anchorPosition = hookPosition;
            hookVelocity = Vector2.zero;
            hookDirection = -fireDirection;
            State = SkillObjectState.Active;
            InitializeRopeState(owner);
            QueueFeedback(
                owner,
                ServerFeedbackType.HookHit,
                ownerClientId,
                0,
                anchorPosition,
                Mathf.Atan2(hookDirection.y, hookDirection.x) * Mathf.Rad2Deg);
            return;
        }

        if (Vector2.Distance(fireStartPosition, hookPosition) >= CurrentRange)
        {
            StartReturning();
        }
    }

    // - Role: Handle skill pressed.
    private void HandleSkillPressed(PlayerObject player)
    {
        if (State == SkillObjectState.Active)
        {
            ApplyDetachBoost(player);
            StartReturning();
            return;
        }

        if (State == SkillObjectState.Spawning)
        {
            StartReturning();
            return;
        }

        if (State == SkillObjectState.Destroying || CooldownRemaining > 0f)
        {
            return;
        }

        fireDirection = player.aim.sqrMagnitude > 0.0001f
            ? player.aim.normalized
            : Vector2.right;
        hookDirection = -fireDirection;

        hookPosition = GetPlayerAnchorPosition(player);
        fireStartPosition = hookPosition;
        hookVelocity = fireDirection * HookSpeed;
        anchorPosition = hookPosition;
        ropeLength = 0f;
        ropeMotionState = RopeMotionState.None;
        State = SkillObjectState.Spawning;
        QueueFeedback(
            player,
            ServerFeedbackType.HookFire,
            player.playerId,
            0,
            hookPosition,
            Mathf.Atan2(fireDirection.y, fireDirection.x) * Mathf.Rad2Deg);
    }

    // - Role: Simulate attached hook state.
    private void SimulateAttached()
    {
        hookPosition = anchorPosition;
        hookVelocity = Vector2.zero;
    }

    // - Role: Apply pull and reel input.
    private void ApplyPullReel(PlayerObject player, float deltaTime)
    {
        if (!PullReelUnlocked || CurrentRange <= 0f || ropeLength <= 0f)
        {
            return;
        }

        float verticalInput = PlayerMovementController.GetVerticalInput(player.input);
        if (Mathf.Abs(verticalInput) <= MovementInputThreshold)
        {
            return;
        }

        float maxLength = CurrentRange;
        float minLength = Mathf.Min(MinRopeLength, maxLength);
        if (verticalInput > 0f)
        {
            ropeLength = Mathf.Max(minLength, ropeLength - ReelSpeed * deltaTime);
        }
        else
        {
            ropeLength = Mathf.Min(maxLength, ropeLength + ReelSpeed * deltaTime);
        }
    }

    // - Role: Apply swing input.
    private Vector2 ApplySwingInput(
        PlayerObject player,
        Vector2 radialDirection,
        Vector2 velocity,
        float deltaTime,
        out bool hasSwingInput)
    {
        hasSwingInput = false;
        float horizontalInput = Mathf.Clamp(player.input.x, -1f, 1f);
        if (Mathf.Abs(horizontalInput) <= MovementInputThreshold)
        {
            return velocity;
        }

        Vector2 inputTangent = ResolveInputTangent(radialDirection, horizontalInput);
        Vector2 positiveTangent = GetPositiveTangent(radialDirection);
        float tangentDirection = Vector2.Dot(inputTangent, positiveTangent) >= 0f ? 1f : -1f;
        float radialSpeed = Vector2.Dot(velocity, radialDirection);
        float tangentSpeed = Vector2.Dot(velocity, positiveTangent);
        float speedAlongInput = tangentSpeed * tangentDirection;
        hasSwingInput = true;

        if (Mathf.Abs(tangentSpeed) <= MovementInputThreshold || speedAlongInput >= 0f)
        {
            float accelerationDelta = Mathf.Abs(horizontalInput) * SwingForwardAcceleration * deltaTime;
            if (SwingMaxTangentSpeed > 0f)
            {
                if (speedAlongInput >= SwingMaxTangentSpeed)
                {
                    return radialDirection * radialSpeed + positiveTangent * tangentSpeed;
                }

                accelerationDelta = Mathf.Min(
                    accelerationDelta,
                    SwingMaxTangentSpeed - Mathf.Max(0f, speedAlongInput));
            }

            if (accelerationDelta > 0f)
            {
                tangentSpeed += tangentDirection * accelerationDelta;
            }

            return radialDirection * radialSpeed + positiveTangent * tangentSpeed;
        }

        speedAlongInput = Mathf.MoveTowards(
            speedAlongInput,
            0f,
            SwingBrakeAcceleration * Mathf.Abs(horizontalInput) * deltaTime);
        tangentSpeed = speedAlongInput * tangentDirection;
        return radialDirection * radialSpeed + positiveTangent * tangentSpeed;
    }

    // - Role: Apply detach boost.
    private void ApplyDetachBoost(PlayerObject player)
    {
        Vector2 playerAnchor = GetPlayerAnchorPosition(player);
        Vector2 toHook = anchorPosition - playerAnchor;
        if (toHook.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector2 boostDirection = ResolveDetachBoostDirection(player, toHook);
        if (boostDirection.sqrMagnitude <= DirectionThresholdSqr)
        {
            return;
        }

        player.velocity += boostDirection.normalized * ResolveDetachBoost();
    }

    // - Role: Find detach boost direction.
    private Vector2 ResolveDetachBoostDirection(
        PlayerObject player,
        Vector2 toHook)
    {
        if (Mathf.Abs(player.input.x) <= MovementInputThreshold)
        {
            return toHook.normalized;
        }

        Vector2 radialDirection = -toHook.normalized;
        Vector2 tangentVelocity = player.velocity
            - radialDirection * Vector2.Dot(player.velocity, radialDirection);
        if (tangentVelocity.sqrMagnitude > DirectionThresholdSqr)
        {
            return tangentVelocity.normalized;
        }

        Vector2 tangent = new Vector2(-radialDirection.y, radialDirection.x);
        if (Vector2.Dot(tangent, Vector2.right * player.input.x) < 0f)
        {
            tangent = -tangent;
        }

        return tangent;
    }

    // - Role: Find detach boost.
    private float ResolveDetachBoost()
    {
        float normalizedLength = CurrentRange > 0f
            ? Mathf.Clamp01(ropeLength / CurrentRange)
            : 0f;

        if (normalizedLength <= 0.5f)
        {
            return SwingDetachBoost * Mathf.Lerp(0.35f, 1f, normalizedLength / 0.5f);
        }

        return SwingDetachBoost * Mathf.Lerp(1f, 2f, (normalizedLength - 0.5f) / 0.5f);
    }

    // - Role: Apply idle swing damping.
    private Vector2 ApplySwingIdleDamping(Vector2 radialDirection, Vector2 velocity, float deltaTime)
    {
        float damping = Mathf.Pow(SwingIdleDampingPerSecond, deltaTime);
        Vector2 tangent = GetPositiveTangent(radialDirection);
        float radialSpeed = Vector2.Dot(velocity, radialDirection);
        float tangentSpeed = Vector2.Dot(velocity, tangent) * damping;
        return radialDirection * radialSpeed + tangent * tangentSpeed;
    }

    // - Role: Remove rope radial velocity.
    private static Vector2 RemoveRopeRadialVelocity(Vector2 velocity, Vector2 radialDirection)
    {
        return velocity - radialDirection * Vector2.Dot(velocity, radialDirection);
    }

    // - Role: Remove outward rope velocity.
    private static void RemoveOutwardRopeVelocity(
        PlayerObject player,
        Vector2 radialDirection)
    {
        float radialVelocity = Vector2.Dot(player.velocity, radialDirection);
        if (radialVelocity <= 0f)
        {
            return;
        }

        player.velocity -= radialDirection * radialVelocity;
    }

    // - Role: Simulate hook return state.
    private void SimulateReturning(PlayerObject player, float deltaTime)
    {
        Vector2 target = GetPlayerAnchorPosition(player);
        Vector2 toTarget = target - hookPosition;
        float distance = toTarget.magnitude;

        if (distance <= ReturnCompleteDistance)
        {
            State = SkillObjectState.None;
            hookVelocity = Vector2.zero;
            StartCooldown(player);
            return;
        }

        Vector2 direction = distance > 0.0001f ? toTarget / distance : Vector2.zero;
        fireDirection = direction.sqrMagnitude > 0.0001f ? direction : fireDirection;
        hookDirection = fireDirection;
        hookVelocity = direction * HookSpeed * ReturnSpeedMultiplier;
        hookPosition += hookVelocity * deltaTime;
    }

    // - Role: Start returning.
    private void StartReturning()
    {
        State = SkillObjectState.Destroying;
        ropeLength = 0f;
        ropeMotionState = RopeMotionState.None;
    }

    // - Role: Get player anchor position.
    private static Vector2 GetPlayerAnchorPosition(PlayerObject player)
    {
        return player.position + player.collisionOffset;
    }

    // - Role: Ensure rope length exists.
    private bool EnsureRopeLength(PlayerObject player)
    {
        if (ropeLength > 0.0001f)
        {
            return true;
        }

        if (player == null)
        {
            return false;
        }

        ropeLength = Vector2.Distance(anchorPosition, GetPlayerAnchorPosition(player));
        return ropeLength > 0.0001f;
    }

    // - Role: Update current rope motion state.
    private void UpdateRopeMotionState(float distance)
    {
        ropeMotionState = IsRopeTaut(distance)
            ? RopeMotionState.Taut
            : RopeMotionState.Slack;
    }

    // - Role: Check if rope is taut for player.
    private bool IsRopeTaut(PlayerObject player)
    {
        return ropeLength > 0.0001f
            && player != null
            && TryGetRadialDirection(player, out _, out float distance)
            && IsRopeTaut(distance);
    }

    // - Role: Check if rope is taut for distance.
    private bool IsRopeTaut(float distance)
    {
        return ropeLength > 0.0001f
            && distance >= Mathf.Max(0f, ropeLength - RopeTautTolerance);
    }

    // - Role: Check if current velocity would reach taut rope.
    private bool WillReachRopeTaut(PlayerObject player, float deltaTime)
    {
        if (player == null || deltaTime <= 0f)
        {
            return false;
        }

        Vector2 predictedAnchor = GetPlayerAnchorPosition(player) + player.velocity * deltaTime;
        return TryGetRadialDirection(predictedAnchor, out _, out float predictedDistance)
            && IsRopeTaut(predictedDistance);
    }

    // - Role: Constrain anchor position to rope length.
    private Vector2 ConstrainAnchorToRope(Vector2 targetAnchor, out bool constrained)
    {
        constrained = false;
        if (!TryGetRadialDirection(targetAnchor, out Vector2 radialDirection, out float distance))
        {
            return targetAnchor;
        }

        hookDirection = radialDirection;
        if (!IsRopeTaut(distance))
        {
            ropeMotionState = RopeMotionState.Slack;
            return targetAnchor;
        }

        ropeMotionState = RopeMotionState.Taut;
        if (distance <= ropeLength)
        {
            return targetAnchor;
        }

        constrained = true;
        return anchorPosition + radialDirection * ropeLength;
    }

    // - Role: Initialize rope state from owner position.
    private void InitializeRopeState(PlayerObject player)
    {
        if (player == null || !TryGetRadialDirection(player, out Vector2 radialDirection, out float distance))
        {
            ropeMotionState = RopeMotionState.None;
            return;
        }

        ropeLength = Mathf.Max(0.0001f, distance);
        hookDirection = radialDirection;
        UpdateRopeMotionState(distance);
    }

    // - Role: Try to get radial direction from hook anchor to anchor position.
    private bool TryGetRadialDirection(Vector2 playerAnchor, out Vector2 radialDirection, out float distance)
    {
        radialDirection = hookDirection.sqrMagnitude > DirectionThresholdSqr
            ? hookDirection.normalized
            : Vector2.down;
        Vector2 anchorToPlayer = playerAnchor - anchorPosition;
        distance = anchorToPlayer.magnitude;
        if (distance <= 0.0001f)
        {
            return radialDirection.sqrMagnitude > DirectionThresholdSqr;
        }

        radialDirection = anchorToPlayer / distance;
        return true;
    }

    // - Role: Try to get radial direction from hook anchor to player.
    private bool TryGetRadialDirection(PlayerObject player, out Vector2 radialDirection, out float distance)
    {
        if (player == null)
        {
            radialDirection = hookDirection.sqrMagnitude > DirectionThresholdSqr
                ? hookDirection.normalized
                : Vector2.down;
            distance = 0f;
            return false;
        }

        return TryGetRadialDirection(GetPlayerAnchorPosition(player), out radialDirection, out distance);
    }

    // - Role: Get positive tangent.
    private static Vector2 GetPositiveTangent(Vector2 radialDirection)
    {
        return new Vector2(-radialDirection.y, radialDirection.x);
    }

    // - Role: Resolve tangent from input direction.
    private static Vector2 ResolveInputTangent(Vector2 radialDirection, float horizontalInput)
    {
        Vector2 tangent = new Vector2(-radialDirection.y, radialDirection.x);
        if (Vector2.Dot(tangent, Vector2.right * horizontalInput) < 0f)
        {
            tangent = -tangent;
        }

        return tangent;
    }

    // - Role: Find owner player.
    private static PlayerObject ResolveOwnerPlayer(SkillObject self)
    {
        if (self != null && self.gamePlay != null && self.gamePlay.TryGetPlayer(self.ownerId, out PlayerObject player))
        {
            return player;
        }

        return null;
    }

    private float HookSpeed => config != null ? config.HookSpeed : DefaultHookSpeed;
    private float ReturnSpeedMultiplier => config != null ? config.ReturnSpeedMultiplier : DefaultReturnSpeedMultiplier;
    private float CurrentRange => Mathf.Max(0f, currentRange);
    private float HookHitHalfExtent => config != null ? config.HookHitHalfExtent : DefaultHookHitHalfExtent;
    private float ReturnCompleteDistance => config != null
        ? config.ReturnCompleteDistance
        : DefaultReturnCompleteDistance;
    private float SwingGravity => config != null
        ? config.SwingGravity
        : DefaultSwingGravity;
    private float SwingForwardAcceleration => config != null
        ? config.SwingForwardAcceleration
        : DefaultSwingForwardAcceleration;
    private float SwingBrakeAcceleration => config != null
        ? config.SwingBrakeAcceleration
        : DefaultSwingBrakeAcceleration;
    private float SwingMaxTangentSpeed => config != null
        ? config.SwingMaxTangentSpeed
        : DefaultSwingMaxTangentSpeed;
    private float SwingIdleDampingPerSecond => config != null
        ? config.SwingIdleDampingPerSecond
        : DefaultSwingIdleDampingPerSecond;
    private float SwingDetachBoost => config != null ? config.SwingDetachBoost : DefaultSwingDetachBoost;
    private float RopeTautTolerance => config != null ? config.RopeTautTolerance : DefaultRopeTautTolerance;
    private bool PullReelUnlocked => config != null ? config.PullReelUnlocked : DefaultPullReelUnlocked;
    private float ReelSpeed => config != null ? config.ReelSpeed : DefaultReelSpeed;
    private float MinRopeLength => config != null ? config.MinRopeLength : DefaultMinRopeLength;
}

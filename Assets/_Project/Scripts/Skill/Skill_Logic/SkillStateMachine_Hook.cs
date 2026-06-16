using UnityEngine;

[SkillLogic("hook")]
public sealed class SkillStateMachine_Hook : SkillStateMachine
{
    private const byte HookObjectIndex = 0;
    private const float DefaultHookSpeed = 18f;
    private const float DefaultReturnSpeedMultiplier = 2f;
    private const float DefaultHookHitHalfExtent = 0.08f;
    private const float DefaultReturnCompleteDistance = 0.18f;
    private const float DefaultSwingInputAcceleration = 18f;
    private const float DefaultSwingDetachBoost = 15f;
    private const float DefaultSwingDampingPerSecond = 0.85f;
    private const float DefaultRopeTautTolerance = 0.03f;
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

    // - Role: Create hook skill state machine.
    public SkillStateMachine_Hook(SkillDefinition definition)
        : base(definition)
    {
        config = definition != null ? definition.GetConfig<HookSkillConfig>() : null;
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

        Vector2 playerAnchor = GetPlayerAnchorPosition(player);
        if (ropeLength <= 0f)
        {
            ropeLength = Vector2.Distance(anchorPosition, playerAnchor);
        }

        Vector2 anchorToPlayer = playerAnchor - anchorPosition;
        float distance = anchorToPlayer.magnitude;
        if (distance <= 0.0001f)
        {
            return;
        }

        Vector2 direction = anchorToPlayer / distance;
        hookDirection = direction;

        bool isRopeTaut = distance >= ropeLength - RopeTautTolerance;
        if (!isRopeTaut)
        {
            return;
        }

        if (distance > ropeLength)
        {
            Vector2 correctedAnchor = anchorPosition + direction * ropeLength;
            Vector2 correctionDelta = correctedAnchor - playerAnchor;
            if (correctionDelta.sqrMagnitude > 0.000001f)
            {
                player.position += correctionDelta;
                if (correctionDelta.y > 0.0001f)
                {
                    player.isGrounded = false;
                }

                playerAnchor = GetPlayerAnchorPosition(player);
                anchorToPlayer = playerAnchor - anchorPosition;
                distance = anchorToPlayer.magnitude;
                if (distance <= 0.0001f)
                {
                    return;
                }

                direction = anchorToPlayer / distance;
                hookDirection = direction;
            }
        }

        if (player.isGrounded)
        {
            RemoveOutwardRopeVelocity(player, direction);
            return;
        }

        ApplySwingInput(player, direction, deltaTime);
        RemoveRopeRadialVelocity(player, direction);
        ApplySwingDamping(player, direction, deltaTime);
        player.isOnWall = false;
        player.wallDirX = 0;
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
            anchorPosition = hookPosition;
            hookVelocity = Vector2.zero;
            hookDirection = -fireDirection;
            State = SkillObjectState.Active;
            QueueFeedback(
                ResolveOwnerPlayer(self),
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

    // - Role: Apply swing input.
    private void ApplySwingInput(
        PlayerObject player,
        Vector2 radialDirection,
        float deltaTime)
    {
        float horizontalInput = Mathf.Clamp(player.input.x, -1f, 1f);
        if (Mathf.Abs(horizontalInput) <= MovementInputThreshold)
        {
            return;
        }

        Vector2 tangent = new Vector2(-radialDirection.y, radialDirection.x);
        if (Vector2.Dot(tangent, Vector2.right * horizontalInput) < 0f)
        {
            tangent = -tangent;
        }

        player.velocity += tangent * (Mathf.Abs(horizontalInput) * SwingInputAcceleration * deltaTime);
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

    // - Role: Apply swing damping.
    private void ApplySwingDamping(
        PlayerObject player,
        Vector2 radialDirection,
        float deltaTime)
    {
        Vector2 tangentVelocity = player.velocity - radialDirection * Vector2.Dot(player.velocity, radialDirection);
        if (tangentVelocity.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        Vector2 radialVelocity = player.velocity - tangentVelocity;
        float damping = Mathf.Pow(SwingDampingPerSecond, deltaTime);
        player.velocity = radialVelocity + tangentVelocity * damping;
    }

    // - Role: Remove rope radial velocity.
    private static void RemoveRopeRadialVelocity(
        PlayerObject player,
        Vector2 radialDirection)
    {
        float radialVelocity = Vector2.Dot(player.velocity, radialDirection);
        player.velocity -= radialDirection * radialVelocity;
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
    }

    // - Role: Get player anchor position.
    private static Vector2 GetPlayerAnchorPosition(PlayerObject player)
    {
        return player.position + player.collisionOffset;
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
    private float SwingInputAcceleration => config != null
        ? config.SwingInputAcceleration
        : DefaultSwingInputAcceleration;
    private float SwingDetachBoost => config != null ? config.SwingDetachBoost : DefaultSwingDetachBoost;
    private float SwingDampingPerSecond => config != null
        ? config.SwingDampingPerSecond
        : DefaultSwingDampingPerSecond;
    private float RopeTautTolerance => config != null ? config.RopeTautTolerance : DefaultRopeTautTolerance;
}

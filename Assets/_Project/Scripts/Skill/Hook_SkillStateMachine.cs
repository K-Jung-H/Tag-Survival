using UnityEngine;

public sealed class Hook_SkillStateMachine : Skill_StateMachine
{
    private const byte HookObjectIndex = 0;
    private const float HookSpeed = 18f;
    private const float ReturnSpeedMultiplier = 2f;
    private const float MaxRopeLength = 12f;
    private const float HookHitHalfExtent = 0.08f;
    private const float ReturnCompleteDistance = 0.18f;
    private const float SwingInputAcceleration = 18f;
    private const float SwingDetachBoost = 15f;
    private const float SwingDampingPerSecond = 0.85f;
    private const float RopeTautTolerance = 0.03f;
    private const float MovementInputThreshold = 0.0001f;
    private const float DirectionThresholdSqr = 0.000001f;

    private readonly SkillObjectSnapshotPacket[] snapshotObjects = new SkillObjectSnapshotPacket[1];

    private ulong ownerClientId;
    private Vector2 hookPosition;
    private Vector2 hookVelocity;
    private Vector2 fireDirection = Vector2.right;
    private Vector2 hookDirection = Vector2.right;
    private Vector2 fireStartPosition;
    private Vector2 anchorPosition;
    private Vector2 previousPlayerAnchor;
    private float ropeLength;
    private bool hasPreviousPlayerAnchor;

    public Hook_SkillStateMachine(SkillDefinition definition)
        : base(definition)
    {
    }

    public override bool UsesSwingMovement => State == SkillObjectState.Active;

    public override void PrepareMovement(
        ref Server_GamePlay.PlayerState player,
        StageCollisionSystem collisionSystem,
        float deltaTime)
    {
        if (State != SkillObjectState.Active)
        {
            hasPreviousPlayerAnchor = false;
            return;
        }

        hookPosition = anchorPosition;
        hookVelocity = Vector2.zero;

        if (player.isGrounded)
        {
            hasPreviousPlayerAnchor = false;
            return;
        }

        Vector2 playerAnchor = GetPlayerAnchorPosition(player);
        if (ropeLength <= 0f)
        {
            ropeLength = Vector2.Distance(anchorPosition, playerAnchor);
        }

        Vector2 anchorToPlayer = playerAnchor - anchorPosition;
        float distance = anchorToPlayer.magnitude;
        if (distance <= 0.0001f)
        {
            hasPreviousPlayerAnchor = false;
            return;
        }

        Vector2 direction = anchorToPlayer / distance;
        hookDirection = direction;

        bool isRopeTaut = distance >= ropeLength - RopeTautTolerance;
        if (!isRopeTaut)
        {
            hasPreviousPlayerAnchor = false;
            return;
        }

        ApplySwingInputAcceleration(ref player, direction, deltaTime);
        RemoveRopeRadialVelocity(ref player, direction);

        player.isWallSticking = false;
        player.wallNormalX = 0;

        previousPlayerAnchor = playerAnchor;
        hasPreviousPlayerAnchor = true;
    }

    public override void Simulate(
        ref Server_GamePlay.PlayerState player,
        StageCollisionSystem collisionSystem,
        float deltaTime,
        bool skillPressedThisTick)
    {
        ownerClientId = player.clientId;
        TickCooldown(deltaTime);

        if (skillPressedThisTick)
        {
            HandleSkillPressed(ref player);
        }

        switch (State)
        {
            case SkillObjectState.Spawning:
                SimulateFlyingOut(collisionSystem, deltaTime);
                break;
            case SkillObjectState.Active:
                SimulateAttached(ref player, collisionSystem, deltaTime);
                break;
            case SkillObjectState.Destroying:
                SimulateReturning(ref player, deltaTime);
                break;
        }
    }

    public override bool TryGetSnapshot(out SkillSnapshotPacket snapshot)
    {
        snapshot = default;
        if (State == SkillObjectState.None)
        {
            return false;
        }

        snapshotObjects[0] = new SkillObjectSnapshotPacket
        {
            skillObjectId = HookObjectIndex,
            position = hookPosition,
            rotation = Mathf.Atan2(hookDirection.y, hookDirection.x) * Mathf.Rad2Deg,
            velocity = hookVelocity
        };

        snapshot = new SkillSnapshotPacket
        {
            ownerClientId = ownerClientId,
            skillId = SkillId,
            skillType = SkillType,
            skillState = State,
            skillObjectCount = 1,
            skillObjects = snapshotObjects
        };

        return true;
    }

    private void HandleSkillPressed(ref Server_GamePlay.PlayerState player)
    {
        if (State == SkillObjectState.Active)
        {
            ApplyDetachBoost(ref player);
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
        hasPreviousPlayerAnchor = false;
        State = SkillObjectState.Spawning;
    }

    private void SimulateFlyingOut(StageCollisionSystem collisionSystem, float deltaTime)
    {
        Vector2 delta = hookVelocity * deltaTime;
        StageCollisionMoveResult moveResult = collisionSystem.MovePlayerWithStageCollisionDetailed(
            hookPosition,
            delta,
            new Vector2(HookHitHalfExtent, HookHitHalfExtent));

        hookPosition = moveResult.position;
        if (moveResult.isGrounded || moveResult.hitCeiling || moveResult.hitWall)
        {
            anchorPosition = hookPosition;
            hookVelocity = Vector2.zero;
            hookDirection = -fireDirection;
            hasPreviousPlayerAnchor = false;
            State = SkillObjectState.Active;
            return;
        }

        if (Vector2.Distance(fireStartPosition, hookPosition) >= MaxRopeLength)
        {
            StartReturning();
        }
    }

    private void SimulateAttached(
        ref Server_GamePlay.PlayerState player,
        StageCollisionSystem collisionSystem,
        float deltaTime)
    {
        hookPosition = anchorPosition;
        hookVelocity = Vector2.zero;
        bool wasGroundedBeforeRope = player.isGrounded;

        Vector2 playerAnchor = GetPlayerAnchorPosition(player);
        if (ropeLength <= 0f)
        {
            ropeLength = Vector2.Distance(anchorPosition, playerAnchor);
        }

        Vector2 anchorToPlayer = playerAnchor - anchorPosition;
        float distance = anchorToPlayer.magnitude;
        if (distance <= 0.0001f)
        {
            hasPreviousPlayerAnchor = false;
            return;
        }

        Vector2 direction = anchorToPlayer / distance;
        hookDirection = direction;

        bool isRopeTaut = distance >= ropeLength - RopeTautTolerance;
        if (!isRopeTaut)
        {
            hasPreviousPlayerAnchor = false;
            return;
        }

        if (distance > ropeLength)
        {
            Vector2 correctedAnchor = anchorPosition + direction * ropeLength;
            Vector2 correctionDelta = correctedAnchor - playerAnchor;
            if (correctionDelta.sqrMagnitude > 0.000001f)
            {
                StageCollisionMoveResult moveResult = collisionSystem.MovePlayerWithStageCollisionDetailed(
                    playerAnchor,
                    correctionDelta,
                    player.collisionHalfExtent);

                player.position = moveResult.position - player.collisionOffset;
                player.isGrounded = moveResult.isGrounded
                    || (wasGroundedBeforeRope && correctionDelta.y <= 0.0001f);

                if (moveResult.isGrounded && player.velocity.y < 0f)
                {
                    player.velocity.y = 0f;
                    player.coyoteTimeRemaining = player.movementStats.coyoteTime;
                }

                if (moveResult.hitCeiling && player.velocity.y > 0f)
                {
                    player.velocity.y = 0f;
                }

                playerAnchor = GetPlayerAnchorPosition(player);
                anchorToPlayer = playerAnchor - anchorPosition;
                distance = anchorToPlayer.magnitude;
                if (distance <= 0.0001f)
                {
                    hasPreviousPlayerAnchor = false;
                    return;
                }

                direction = anchorToPlayer / distance;
                hookDirection = direction;
            }
        }

        if (wasGroundedBeforeRope || player.isGrounded)
        {
            RemoveOutwardRopeVelocity(ref player, direction);
            hasPreviousPlayerAnchor = false;
            return;
        }

        RebuildVelocityFromConstrainedPosition(ref player, deltaTime);
        RemoveRopeRadialVelocity(ref player, direction);
        ApplySwingDamping(ref player, direction, deltaTime);

        player.isWallSticking = false;
        player.wallNormalX = 0;
    }

    private void ApplySwingInputAcceleration(
        ref Server_GamePlay.PlayerState player,
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

    private void ApplyDetachBoost(ref Server_GamePlay.PlayerState player)
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

    private Vector2 ResolveDetachBoostDirection(
        Server_GamePlay.PlayerState player,
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

    private float ResolveDetachBoost()
    {
        float normalizedLength = MaxRopeLength > 0f
            ? Mathf.Clamp01(ropeLength / MaxRopeLength)
            : 0f;

        if (normalizedLength <= 0.5f)
        {
            return SwingDetachBoost * Mathf.Lerp(0.35f, 1f, normalizedLength / 0.5f);
        }

        return SwingDetachBoost * Mathf.Lerp(1f, 2f, (normalizedLength - 0.5f) / 0.5f);
    }

    private void ApplySwingDamping(
        ref Server_GamePlay.PlayerState player,
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

    private static void RemoveRopeRadialVelocity(
        ref Server_GamePlay.PlayerState player,
        Vector2 radialDirection)
    {
        float radialVelocity = Vector2.Dot(player.velocity, radialDirection);
        player.velocity -= radialDirection * radialVelocity;
    }

    private static void RemoveOutwardRopeVelocity(
        ref Server_GamePlay.PlayerState player,
        Vector2 radialDirection)
    {
        float radialVelocity = Vector2.Dot(player.velocity, radialDirection);
        if (radialVelocity <= 0f)
        {
            return;
        }

        player.velocity -= radialDirection * radialVelocity;
    }

    private void RebuildVelocityFromConstrainedPosition(
        ref Server_GamePlay.PlayerState player,
        float deltaTime)
    {
        if (!hasPreviousPlayerAnchor || deltaTime <= 0f)
        {
            return;
        }

        Vector2 currentPlayerAnchor = GetPlayerAnchorPosition(player);
        player.velocity = (currentPlayerAnchor - previousPlayerAnchor) / deltaTime;
        hasPreviousPlayerAnchor = false;
    }

    private void SimulateReturning(ref Server_GamePlay.PlayerState player, float deltaTime)
    {
        Vector2 target = GetPlayerAnchorPosition(player);
        Vector2 toTarget = target - hookPosition;
        float distance = toTarget.magnitude;

        if (distance <= ReturnCompleteDistance)
        {
            State = SkillObjectState.None;
            hookVelocity = Vector2.zero;
            StartCooldown();
            return;
        }

        Vector2 direction = distance > 0.0001f ? toTarget / distance : Vector2.zero;
        fireDirection = direction.sqrMagnitude > 0.0001f ? direction : fireDirection;
        hookDirection = fireDirection;
        hookVelocity = direction * HookSpeed * ReturnSpeedMultiplier;
        hookPosition += hookVelocity * deltaTime;
    }

    private void StartReturning()
    {
        State = SkillObjectState.Destroying;
        ropeLength = 0f;
        hasPreviousPlayerAnchor = false;
    }

    private static Vector2 GetPlayerAnchorPosition(Server_GamePlay.PlayerState player)
    {
        return player.position + player.collisionOffset;
    }
}

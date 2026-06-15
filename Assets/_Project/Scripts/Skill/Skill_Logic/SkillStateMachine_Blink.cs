using UnityEngine;

[SkillLogic("blink")]
public sealed class SkillStateMachine_Blink : SkillStateMachine
{
    private const float DefaultEnterDuration = 0.2f;
    private const float DefaultExitDuration = 0.2f;

    private readonly BlinkSkillConfig config;

    private BlinkPhase phase;
    private float phaseTimer;
    private Vector2 startPosition;
    private Vector2 targetPosition;

    // - Role: Create blink skill state machine.
    public SkillStateMachine_Blink(SkillDefinition definition)
        : base(definition)
    {
        config = definition != null ? definition.GetConfig<BlinkSkillConfig>() : null;
    }

    // - Role: Constrain owner movement.
    public override void ConstrainOwner(
        PlayerObject player,
        float deltaTime)
    {
        if (phase == BlinkPhase.None || player == null)
        {
            return;
        }

        player.velocity = Vector2.zero;
        player.isOnWall = false;
        player.wallDirX = 0;
        player.wallSurface = StageSurfaceType.Normal;
        ServerPlayerSystem.ClearInput(player);
    }

    // - Role: Simulate this object.
    public override void Simulate(
        PlayerObject player,
        float deltaTime,
        bool skillPressedThisTick)
    {
        TickCooldown(deltaTime);

        if (phase != BlinkPhase.None)
        {
            if (player != null && player.stunnedTimer > 0f)
            {
                CancelBlinkForStun(player);
                return;
            }

            TickBlink(player, deltaTime);
            return;
        }

        State = SkillObjectState.None;
        if (skillPressedThisTick && CooldownRemaining <= 0f)
        {
            StartBlink(player);
        }
    }

    // - Role: Start blink.
    private void StartBlink(PlayerObject player)
    {
        if (player == null)
        {
            return;
        }

        startPosition = player.position;
        targetPosition = ResolveTargetPosition(player);
        StartCooldown(player);
        StartPhase(player, BlinkPhase.Enter);
    }

    // - Role: Tick blink.
    private void TickBlink(PlayerObject player, float deltaTime)
    {
        if (player == null)
        {
            phase = BlinkPhase.None;
            State = SkillObjectState.None;
            return;
        }

        player.velocity = Vector2.zero;
        ApplyLockedPosition(player);
        phaseTimer -= Mathf.Max(0f, deltaTime);
        ApplyPhaseRenderState(player);
        ApplyBlinkCollisionWindow(player);

        if (phaseTimer > 0f)
        {
            return;
        }

        if (phase == BlinkPhase.Enter)
        {
            TeleportPlayer(player);
            StartPhase(player, BlinkPhase.Exit);
            return;
        }

        FinishBlink(player);
    }

    // - Role: Start one blink phase.
    private void StartPhase(PlayerObject player, BlinkPhase nextPhase)
    {
        phase = nextPhase;
        phaseTimer = nextPhase == BlinkPhase.Enter
            ? EnterDuration
            : ExitDuration;
        player.velocity = Vector2.zero;
        ApplyBlinkCollisionWindow(player);
        ApplyLockedPosition(player);
        ApplyPhaseRenderState(player);

        if (phaseTimer <= 0f)
        {
            TickBlink(player, 0f);
        }
    }

    // - Role: Finish blink.
    private void FinishBlink(PlayerObject player)
    {
        phase = BlinkPhase.None;
        State = SkillObjectState.None;
        player.interactionState = PlayerInteractionState.None;
        player.locomotionState = LocomotionState.Idle;
        ServerPlayerSystem.UpdateRenderState(player);
        ServerPlayerSystem.UpdateCharacterStateMachine(player);
    }

    // - Role: Cancel blink when stun starts.
    private void CancelBlinkForStun(PlayerObject player)
    {
        phase = BlinkPhase.None;
        State = SkillObjectState.None;
        if (player == null)
        {
            return;
        }

        player.interactionState = PlayerInteractionState.None;
        ServerPlayerSystem.UpdateRenderState(player);
        ServerPlayerSystem.UpdateCharacterStateMachine(player);
    }

    // - Role: Apply blink render state.
    private void ApplyPhaseRenderState(PlayerObject player)
    {
        State = phase == BlinkPhase.Enter
            ? SkillObjectState.Spawning
            : SkillObjectState.Destroying;
        player.locomotionState = phase == BlinkPhase.Enter
            ? LocomotionState.BlinkEnter
            : LocomotionState.BlinkExit;
        player.SyncCharacterStateMachine();
    }

    // - Role: Teleport player.
    private void TeleportPlayer(PlayerObject player)
    {
        player.position = targetPosition - player.collisionOffset;
        player.velocity = Vector2.zero;
        player.isGrounded = false;
        player.groundSurface = StageSurfaceType.Normal;
        player.isOnWall = false;
        player.wallDirX = 0;
        player.wallSurface = StageSurfaceType.Normal;
    }

    // - Role: Keep player fixed during blink animation.
    private void ApplyLockedPosition(PlayerObject player)
    {
        if (player == null)
        {
            return;
        }

        player.position = phase == BlinkPhase.Enter
            ? startPosition
            : targetPosition - player.collisionOffset;
    }

    // - Role: Apply player collision window during blink.
    private void ApplyBlinkCollisionWindow(PlayerObject player)
    {
        if (player == null)
        {
            return;
        }

        WorldObjectLayer collisionMask = IsPlayerCollisionEnabledInCurrentPhase()
            ? WorldObjectLayer.Player
            : WorldObjectLayer.None;
        player.interactionState = PlayerInteractionState.Locked(
            collisionMask,
            disablesPlayerPush: true);
    }

    // - Role: Check if player collision is enabled in blink phase.
    private bool IsPlayerCollisionEnabledInCurrentPhase()
    {
        if (phase == BlinkPhase.Enter)
        {
            float elapsed = Mathf.Max(0f, EnterDuration - phaseTimer);
            return elapsed <= EnterCollisionSeconds;
        }

        if (phase == BlinkPhase.Exit)
        {
            float elapsed = Mathf.Max(0f, ExitDuration - phaseTimer);
            return elapsed >= ExitCollisionDelaySeconds;
        }

        return false;
    }

    // - Role: Resolve blink target.
    private Vector2 ResolveTargetPosition(PlayerObject player)
    {
        Vector2 playerCenter = player.position + player.collisionOffset;
        Vector2 direction = player.aim.sqrMagnitude > 0.0001f
            ? player.aim.normalized
            : Vector2.right;
        float range = ResolveRange(player);
        Vector2 searchOrigin = playerCenter + direction * range;
        StageCollisionSystem collisionSystem = player.gamePlay != null
            ? player.gamePlay.CollisionSystem
            : null;
        if (collisionSystem == null)
        {
            return playerCenter;
        }

        float cellSize = collisionSystem.CellSize;
        int searchDistance = Mathf.Max(0, Mathf.CeilToInt(range / cellSize) + 1);
        float rangeSqr = range * range;
        bool success = collisionSystem.TryFindNearestEmptyTile(
            searchOrigin,
            searchDistance,
            cell =>
            {
                Vector2 center = collisionSystem.GetCellCenter(cell);
                return (center - playerCenter).sqrMagnitude > rangeSqr + 0.0001f;
            },
            out _,
            out Vector2 target);

        return success ? target : playerCenter;
    }

    // - Role: Resolve range.
    private float ResolveRange(PlayerObject player)
    {
        return GetRange(player);
    }

    private float EnterDuration => config != null ? config.EnterDuration : DefaultEnterDuration;
    private float ExitDuration => config != null ? config.ExitDuration : DefaultExitDuration;
    private float EnterCollisionSeconds => config != null ? config.EnterCollisionSeconds : 0.05f;
    private float ExitCollisionDelaySeconds => config != null ? config.ExitCollisionDelaySeconds : 0.05f;

    private enum BlinkPhase
    {
        None = 0,
        Enter = 1,
        Exit = 2
    }
}

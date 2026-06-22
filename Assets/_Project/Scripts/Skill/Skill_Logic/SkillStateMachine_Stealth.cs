using UnityEngine;

[SkillLogic("stealth")]
public sealed class SkillStateMachine_Stealth : SkillStateMachine
{
    private readonly StealthSkillConfig config;
    private bool warnedMissingConfig;
    private float stealthTimer;

    // - Role: Create stealth skill state machine.
    public SkillStateMachine_Stealth(SkillDefinition definition)
        : base(definition)
    {
        config = definition != null ? definition.GetConfig<StealthSkillConfig>() : null;
    }

    // - Role: Simulate this object.
    public override void Simulate(
        PlayerObject player,
        float deltaTime,
        bool skillPressedThisTick)
    {
        TickCooldown(deltaTime);

        if (State == SkillObjectState.Active)
        {
            TickStealth(player, deltaTime);
            return;
        }

        State = SkillObjectState.None;
        if (player != null)
        {
            player.isStealthed = false;
        }

        if (skillPressedThisTick && CooldownRemaining <= 0f)
        {
            StartStealth(player);
        }
    }

    // - Role: Start stealth.
    private void StartStealth(PlayerObject player)
    {
        if (player == null)
        {
            return;
        }

        if (config == null)
        {
            WarnMissingConfig();
            return;
        }

        stealthTimer = ResolveDurationSeconds(player);
        State = SkillObjectState.Active;
        player.isStealthed = true;
        StartCooldown(player);
        QueueFeedback(
            player,
            ServerFeedbackType.SpreadSmoke,
            player.playerId,
            0,
            player.position + player.collisionOffset,
            0f);
    }

    // - Role: Tick stealth state.
    private void TickStealth(PlayerObject player, float deltaTime)
    {
        if (player == null)
        {
            stealthTimer = 0f;
            State = SkillObjectState.None;
            return;
        }

        player.isStealthed = true;
        stealthTimer -= Mathf.Max(0f, deltaTime);
        if (stealthTimer > 0f)
        {
            return;
        }

        player.isStealthed = false;
        stealthTimer = 0f;
        State = SkillObjectState.None;
    }

    // - Role: Warn missing config once.
    private void WarnMissingConfig()
    {
        if (warnedMissingConfig)
        {
            return;
        }

        Debug.LogError("[SkillStateMachine_Stealth] StealthSkillConfig is required.");
        warnedMissingConfig = true;
    }

    // - Role: Resolve stealth duration with item modifiers.
    private float ResolveDurationSeconds(PlayerObject player)
    {
        float baseDuration = config != null ? config.DurationSeconds : 0f;
        float duration = player != null && player.itemEffects != null
            ? player.itemEffects.EvaluateSkillFloat(baseDuration, player.skill, SkillModifierParameterKeys.Duration)
            : baseDuration;
        return Mathf.Max(0.0001f, duration);
    }
}

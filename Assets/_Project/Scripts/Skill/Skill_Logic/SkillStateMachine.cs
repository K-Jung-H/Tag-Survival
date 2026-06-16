using UnityEngine;

public abstract class SkillStateMachine
{
    protected readonly SkillDefinition definition;

    // - Role: Create skill state machine.
    protected SkillStateMachine(SkillDefinition definition)
    {
        this.definition = definition;
    }

    public byte SkillId => definition != null ? definition.SkillId : (byte)0;
    public SkillType SkillType => definition != null ? definition.SkillType : SkillType.None;
    public SkillObjectState State { get; protected set; }
    public float CooldownRemaining { get; protected set; }

    // - Role: Get range for player.
    public float GetRange(PlayerObject player)
    {
        float baseRange = definition != null ? definition.Range : 0f;
        float range = player != null && player.itemEffects != null
            ? player.itemEffects.EvaluateSkillFloat(baseRange, player.skill, SkillModifierParameterKeys.Range)
            : baseRange;
        return Mathf.Max(0f, range);
    }

    // - Role: Get cooldown for player.
    public float GetCooldownSeconds(PlayerObject player)
    {
        float baseCooldown = definition != null ? definition.Cooldown : 0f;
        float cooldown = player != null && player.itemEffects != null
            ? player.itemEffects.EvaluateSkillFloat(baseCooldown, player.skill, SkillModifierParameterKeys.Cooldown)
            : baseCooldown;
        return Mathf.Max(0f, cooldown);
    }

    // - Role: Scale current cooldown.
    public void ScaleCooldown(float multiplier)
    {
        CooldownRemaining = Mathf.Max(0f, CooldownRemaining * Mathf.Max(0f, multiplier));
    }

    // - Role: Constrain owner movement.
    public virtual void ConstrainOwner(
        PlayerObject player,
        float deltaTime)
    {
    }

    // - Role: Simulate this object.
    public abstract void Simulate(
        PlayerObject player,
        float deltaTime,
        bool skillPressedThisTick);

    // - Role: Handle collision.
    public virtual void OnCollision(SkillObject self, IWorldObject other)
    {
    }

    // - Role: Handle stage move result.
    public virtual void OnStageMoveResult(SkillObject self, StageCollisionMoveResult moveResult)
    {
    }

    // - Role: Check if stage placement cell blocked is true.
    public virtual bool IsStagePlacementCellBlocked(SkillObject self, Vector2Int cell)
    {
        return false;
    }

    // - Role: Handle stage placement result.
    public virtual void OnStagePlacementResult(
        SkillObject self,
        bool success,
        Vector2Int cell,
        Vector2 position,
        Vector2 halfExtent)
    {
    }

    // - Role: Sync skill objects.
    public virtual void SyncSkillObjects(Skill skill)
    {
    }

    // - Role: Update cooldown by time.
    protected void TickCooldown(float deltaTime)
    {
        if (CooldownRemaining > 0f)
        {
            CooldownRemaining = Mathf.Max(0f, CooldownRemaining - deltaTime);
        }
    }

    // - Role: Start cooldown for player.
    protected void StartCooldown(PlayerObject player)
    {
        CooldownRemaining = GetCooldownSeconds(player);
    }

    // - Role: Queue a server-synchronized feedback event.
    protected static void QueueFeedback(
        PlayerObject player,
        ServerFeedbackType feedbackType,
        ulong subjectClientId,
        ulong targetClientId,
        Vector2 position,
        float rotation = 0f)
    {
        if (player == null || player.gamePlay == null || player.gamePlay.GameEventQueue == null)
        {
            return;
        }

        player.gamePlay.GameEventQueue.QueueFeedback(
            player.gamePlay.Tick,
            feedbackType,
            subjectClientId,
            targetClientId,
            position,
            rotation);
    }
}

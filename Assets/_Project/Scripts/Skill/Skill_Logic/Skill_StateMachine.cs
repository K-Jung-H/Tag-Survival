using UnityEngine;

public abstract class Skill_StateMachine
{
    protected readonly SkillDefinition definition;

    protected Skill_StateMachine(SkillDefinition definition)
    {
        this.definition = definition;
    }

    public byte SkillId => definition != null ? definition.SkillId : (byte)0;
    public SkillType SkillType => definition != null ? definition.SkillType : SkillType.None;
    public SkillObjectState State { get; protected set; }
    public float CooldownRemaining { get; protected set; }
    public virtual bool UsesSwingMovement => false;

    // 이동 계산 전에 스킬이 플레이어 속도/상태를 보정해야 할 때 호출합니다.
    public virtual void PrepareMovement(
        ref PlayerState player,
        StageCollisionSystem collisionSystem,
        float deltaTime)
    {
    }

    public abstract void Simulate(
        ref PlayerState player,
        StageCollisionSystem collisionSystem,
        float deltaTime,
        bool skillPressedThisTick);

    // 상태머신이 관리하는 스킬 오브젝트 목록을 Skill 컨테이너에 동기화합니다.
    public virtual void SyncSkillObjects(Skill skill)
    {
    }

    protected void TickCooldown(float deltaTime)
    {
        if (CooldownRemaining > 0f)
        {
            CooldownRemaining = Mathf.Max(0f, CooldownRemaining - deltaTime);
        }
    }

    protected void StartCooldown()
    {
        CooldownRemaining = definition != null ? definition.Cooldown : 0f;
    }
}

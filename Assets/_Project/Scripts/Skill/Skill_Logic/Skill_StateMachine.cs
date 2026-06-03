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

    // Role: 플레이어 이동 전에 스킬이 속도 제약을 먼저 반영한다.
    // Parameters:
    // - player: 스킬을 사용하는 플레이어 상태
    // - collisionSystem: Stage 충돌 계산 시스템
    // - deltaTime: 이번 시뮬레이션에 사용할 시간
    public virtual void PrepareMovement(
        ref Server_GamePlay.PlayerState player,
        StageCollisionSystem collisionSystem,
        float deltaTime)
    {
    }

    // Role: 스킬 입력과 서버 물리 상태를 사용해 스킬 상태를 갱신한다.
    // Parameters:
    // - player: 스킬을 사용하는 플레이어 상태
    // - collisionSystem: Stage 충돌 계산 시스템
    // - deltaTime: 이번 시뮬레이션에 사용할 시간
    // - skillPressedThisTick: 이번 tick에 새로 눌린 스킬 입력 여부
    public abstract void Simulate(
        ref Server_GamePlay.PlayerState player,
        StageCollisionSystem collisionSystem,
        float deltaTime,
        bool skillPressedThisTick);

    // Role: 네트워크로 전송할 스킬 스냅샷을 만든다.
    // Parameters:
    // - snapshot: 생성된 스킬 스냅샷
    public abstract bool TryGetSnapshot(out SkillSnapshotPacket snapshot);

    // Role: 서버 월드에서 처리해야 할 스킬 상호작용 정보를 collector에 등록한다.
    // Parameters:
    // - collector: 스킬 월드 상호작용 정보를 수집하는 객체
    public virtual void CollectWorldContributions(SkillWorldContributionCollector collector)
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

using UnityEngine;

public static class SkillStateMachineFactory
{
    // SkillDefinition 타입에 맞는 서버 스킬 상태머신을 생성합니다.
    public static Skill_StateMachine Create(SkillDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        switch (definition.SkillType)
        {
            case SkillType.None:
                return null;
            case SkillType.HookGrappling:
                return new Hook_SkillStateMachine(definition);
            case SkillType.Portal:
                return new Portal_SkillStateMachine(definition);
            default:
                Debug.LogWarning(
                    $"[SkillStateMachineFactory] SkillType {definition.SkillType} is not implemented yet. " +
                    $"skillId={definition.SkillId}");
                return null;
        }
    }
}

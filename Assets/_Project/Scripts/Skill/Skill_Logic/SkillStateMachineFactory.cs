using UnityEngine;

public static class SkillStateMachineFactory
{
    // - Role: Create a skill state machine.
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

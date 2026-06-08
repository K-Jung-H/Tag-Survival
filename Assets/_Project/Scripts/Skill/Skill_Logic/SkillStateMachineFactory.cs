using UnityEngine;

public static class SkillStateMachineFactory
{
    // - Role: Create a skill state machine.
    public static SkillStateMachine Create(SkillDefinition definition)
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
                return new SkillStateMachine_Hook(definition);
            case SkillType.Portal:
                return new SkillStateMachine_Portal(definition);
            default:
                Debug.LogWarning(
                    $"[SkillStateMachineFactory] SkillType {definition.SkillType} is not implemented yet. " +
                    $"skillId={definition.SkillId}");
                return null;
        }
    }
}

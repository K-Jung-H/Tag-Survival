public static class SkillStateMachineFactory
{
    // - Role: Create a skill state machine.
    public static SkillStateMachine Create(SkillDefinition definition)
    {
        return SkillStateMachineRegistry.Create(definition);
    }
}

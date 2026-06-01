public static class SkillStateMachineFactory
{
    // Role: SkillDefinition의 타입에 맞는 서버 스킬 상태 머신을 생성한다.
    // Parameters:
    // - definition: 생성에 사용할 스킬 정의
    public static Skill_StateMachine Create(SkillDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        switch (definition.SkillType)
        {
            case SkillType.HookGrappling:
                return new Hook_SkillStateMachine(definition);
            default:
                return null;
        }
    }
}

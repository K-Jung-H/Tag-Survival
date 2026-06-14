using System;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SkillLogicAttribute : Attribute
{
    public SkillLogicAttribute(string logicKey)
    {
        LogicKey = logicKey;
    }

    public string LogicKey { get; }
}

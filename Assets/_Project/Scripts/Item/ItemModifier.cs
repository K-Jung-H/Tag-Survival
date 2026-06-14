using System;
using UnityEngine;

public enum StatItemEffect : byte
{
    None = 0,
    Speed = 1,
    Jump = 2,
    FallSpeed = 3,
}

public enum ItemModifierTarget : byte
{
    None = 0,
    Stat = 1,
    SkillParameter = 2
}

public enum ItemModifierOperation : byte
{
    Add = 0,
    Multiply = 1,
    Override = 2
}

public enum ItemModifierSkillScope : byte
{
    Any = 0,
    SkillId = 1,
    LogicKey = 2
}

public static class SkillModifierParameterKeys
{
    public const string Cooldown = "cooldown";
    public const string Range = "range";
    public const string MaxPortalCount = "maxPortalCount";
}

public readonly struct ItemModifier
{
    public readonly ItemModifierTarget target;
    public readonly ItemModifierOperation operation;
    public readonly float value;
    public readonly StatItemEffect statEffect;
    public readonly ItemModifierSkillScope skillScope;
    public readonly byte skillId;
    public readonly string logicKey;
    public readonly string parameterKey;

    public ItemModifier(
        ItemModifierTarget target,
        ItemModifierOperation operation,
        float value,
        StatItemEffect statEffect = StatItemEffect.None,
        ItemModifierSkillScope skillScope = ItemModifierSkillScope.Any,
        byte skillId = 0,
        string logicKey = "",
        string parameterKey = "")
    {
        this.target = target;
        this.operation = operation;
        this.value = value;
        this.statEffect = statEffect;
        this.skillScope = skillScope;
        this.skillId = skillId;
        this.logicKey = logicKey;
        this.parameterKey = parameterKey;
    }

    // - Role: Check if this modifier can be used by item type.
    public bool IsValidFor(ItemType itemType)
    {
        if (target == ItemModifierTarget.Stat)
        {
            return itemType == ItemType.Stats && statEffect != StatItemEffect.None;
        }

        if (target == ItemModifierTarget.SkillParameter)
        {
            return itemType == ItemType.Skill && !string.IsNullOrWhiteSpace(parameterKey);
        }

        return false;
    }

    // - Role: Check if this modifier applies to skill definition.
    public bool AppliesTo(SkillDefinition skillDefinition)
    {
        if (target != ItemModifierTarget.SkillParameter || skillDefinition == null)
        {
            return false;
        }

        return skillScope switch
        {
            ItemModifierSkillScope.Any => true,
            ItemModifierSkillScope.SkillId => skillDefinition.SkillId == skillId,
            ItemModifierSkillScope.LogicKey => string.Equals(skillDefinition.LogicKey, logicKey, StringComparison.Ordinal),
            _ => false
        };
    }

    // - Role: Check parameter key.
    public bool MatchesParameter(string key)
    {
        return string.Equals(parameterKey, key, StringComparison.Ordinal);
    }

    // - Role: Apply operation to value.
    public float Apply(float baseValue)
    {
        return operation switch
        {
            ItemModifierOperation.Add => baseValue + value,
            ItemModifierOperation.Multiply => baseValue * Mathf.Max(0f, value),
            ItemModifierOperation.Override => value,
            _ => baseValue
        };
    }

}

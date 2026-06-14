using System;
using UnityEngine;

public enum ItemIconViewType : byte
{
    Sprite = 0,
    AnimationClip = 1
}

[Serializable]
public struct ItemIconData
{
    public ItemIconViewType viewType;
    public Sprite sprite;
    public AnimationClip animationClip;
}

[Serializable]
public struct ItemCatalogEntry
{
    public int id;
    public float duration;
    public ItemIconData icon;
    public string title;
    [TextArea] public string description;
}

[Serializable]
public struct StatModifierInput
{
    public StatItemEffect statEffect;
    public ItemModifierOperation operation;
    public float value;

    // - Role: Convert to runtime modifier.
    public ItemModifier ToModifier()
    {
        return new ItemModifier(
            ItemModifierTarget.Stat,
            operation,
            value,
            statEffect);
    }
}

[Serializable]
public struct SkillModifierInput
{
    public string parameterKey;
    public ItemModifierOperation operation;
    public float value;

    // - Role: Convert to runtime modifier.
    public ItemModifier ToModifier(
        ItemModifierSkillScope skillScope,
        byte skillId,
        string logicKey)
    {
        return new ItemModifier(
            ItemModifierTarget.SkillParameter,
            operation,
            value,
            skillScope: skillScope,
            skillId: skillId,
            logicKey: logicKey,
            parameterKey: parameterKey);
    }
}

[Serializable]
public struct StatItemEntry
{
    public ItemCatalogEntry entry;
    public StatModifierInput[] modifiers;
}

[Serializable]
public struct SkillItemEntry
{
    public ItemCatalogEntry entry;
    public SkillModifierInput[] modifiers;
}

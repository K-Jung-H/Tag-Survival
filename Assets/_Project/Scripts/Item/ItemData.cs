using System;
using UnityEngine;

public enum ItemType : byte
{
    None = 0,
    Stats = 1,
    Skill = 2
}

public readonly struct ItemData
{
    public readonly int id;
    public readonly ItemType type;
    public readonly float duration;
    public readonly ItemModifier[] modifiers;
    public readonly ItemIconData icon;
    public readonly string title;
    public readonly string description;

    public ItemData(
        int id,
        ItemType type,
        float duration,
        ItemIconData icon,
        string title,
        string description,
        ItemModifier[] modifiers)
    {
        this.id = id;
        this.type = type;
        this.duration = duration;
        this.icon = icon;
        this.title = title;
        this.description = description;
        this.modifiers = modifiers ?? Array.Empty<ItemModifier>();
    }

    // - Role: Check if this data can apply.
    public bool IsValid()
    {
        return id > 0
            && type != ItemType.None
            && HasValidModifier();
    }

    // - Role: Check if item has a modifier for its type.
    public bool HasValidModifier()
    {
        if (modifiers == null)
        {
            return false;
        }

        for (int i = 0; i < modifiers.Length; i++)
        {
            if (modifiers[i].IsValidFor(type))
            {
                return true;
            }
        }

        return false;
    }

    // - Role: Check if this skill item can be offered to skill.
    public bool CanApplyToSkill(SkillDefinition skillDefinition)
    {
        if (type != ItemType.Skill || skillDefinition == null || modifiers == null)
        {
            return false;
        }

        for (int i = 0; i < modifiers.Length; i++)
        {
            ItemModifier modifier = modifiers[i];
            if (modifier.IsValidFor(ItemType.Skill) && modifier.AppliesTo(skillDefinition))
            {
                return true;
            }
        }

        return false;
    }

}

using System;
using UnityEngine;

public enum ItemType : byte
{
    None = 0,
    Stats = 1,
    Skill = 2
}

public enum StatItemEffect : byte
{
    None = 0,
    Speed = 1,
    Jump = 2
}

public enum SkillItemEffect : byte
{
    None = 0,
    Cooldown = 1,
    Range = 2
}

[Serializable]
public struct ItemData
{
    public int id;
    public ItemType type;
    public StatItemEffect statEffect;
    public SkillItemEffect skillEffect;
    public float value;
    public float duration;
    public Sprite icon;
    public string title;
    [TextArea] public string description;

    // - Role: Check if this data can apply.
    public bool IsValid()
    {
        if (id <= 0)
        {
            return false;
        }

        if (type == ItemType.Stats)
        {
            return statEffect != StatItemEffect.None;
        }

        if (type == ItemType.Skill)
        {
            return skillEffect != SkillItemEffect.None;
        }

        return false;
    }

    // - Role: Get safe multiplier.
    public float GetMultiplier()
    {
        return Mathf.Max(0f, value);
    }
}

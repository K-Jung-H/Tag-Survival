using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerItemEffects
{
    private readonly List<ActiveItemData> activeItems = new();

    // - Role: Add item data.
    public void Add(ItemData itemData)
    {
        if (!itemData.IsValid())
        {
            return;
        }

        activeItems.Add(new ActiveItemData
        {
            data = itemData,
            remainingSeconds = itemData.duration
        });
    }

    // - Role: Clear all item data.
    public void Clear()
    {
        activeItems.Clear();
    }

    // - Role: Tick active item data.
    public void Tick(float deltaTime)
    {
        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        for (int i = activeItems.Count - 1; i >= 0; i--)
        {
            ActiveItemData activeItem = activeItems[i];
            if (activeItem.data.duration <= 0f)
            {
                continue;
            }

            activeItem.remainingSeconds -= safeDeltaTime;
            if (activeItem.remainingSeconds <= 0f)
            {
                activeItems.RemoveAt(i);
                continue;
            }

            activeItems[i] = activeItem;
        }
    }

    // - Role: Apply movement item effects.
    public CharacterMovementStats ApplyMovementStats(CharacterMovementStats baseStats)
    {
        CharacterMovementStats stats = baseStats;
        float speedMultiplier = GetStackedStatMultiplier(StatItemEffect.Speed);
        float jumpMultiplier = GetStackedStatMultiplier(StatItemEffect.Jump);
        stats.moveSpeed *= speedMultiplier;
        stats.jumpVelocity *= jumpMultiplier;
        stats.jumpHeight *= jumpMultiplier;
        return stats;
    }

    // - Role: Get skill cooldown multiplier.
    public float GetSkillCooldownMultiplier()
    {
        return GetStackedSkillMultiplier(SkillItemEffect.Cooldown);
    }

    // - Role: Get skill range multiplier.
    public float GetSkillRangeMultiplier()
    {
        return GetStackedSkillMultiplier(SkillItemEffect.Range);
    }

    // - Role: Get stacked stat multiplier.
    private float GetStackedStatMultiplier(StatItemEffect effect)
    {
        float multiplier = 1f;
        for (int i = 0; i < activeItems.Count; i++)
        {
            ItemData data = activeItems[i].data;
            if (data.type == ItemType.Stats && data.statEffect == effect)
            {
                multiplier *= data.GetMultiplier();
            }
        }

        return multiplier;
    }

    // - Role: Get stacked skill multiplier.
    private float GetStackedSkillMultiplier(SkillItemEffect effect)
    {
        float multiplier = 1f;
        for (int i = 0; i < activeItems.Count; i++)
        {
            ItemData data = activeItems[i].data;
            if (data.type == ItemType.Skill && data.skillEffect == effect)
            {
                multiplier *= data.GetMultiplier();
            }
        }

        return multiplier;
    }

    private struct ActiveItemData
    {
        public ItemData data;
        public float remainingSeconds;
    }
}

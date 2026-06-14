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
        float speedBonus = EvaluateStat(StatItemEffect.Speed, 0f);
        float jumpBonus = EvaluateStat(StatItemEffect.Jump, 0f);
        float fallSpeedBonuus = EvaluateStat(StatItemEffect.FallSpeed, 0f);

        return CharacterMovementStats.Create(
            Mathf.Max(0f, baseStats.moveSpeed + speedBonus),
            Mathf.Max(0f, baseStats.jumpHeight + jumpBonus),
            baseStats.jumpRiseTime,
            baseStats.fallTime,
            Mathf.Max(0f, baseStats.maxFallSpeed + fallSpeedBonuus),
            baseStats.moveAccel,
            baseStats.moveDecel,
            baseStats.airAccel,
            baseStats.airDecel,
            baseStats.overSpeedDecel,
            baseStats.wallMoveRate,
            baseStats.lateJumpTime);
    }

    // - Role: Evaluate one skill float parameter.
    public float EvaluateSkillFloat(
        float baseValue,
        Skill skill,
        string parameterKey)
    {
        if (skill == null || string.IsNullOrWhiteSpace(parameterKey))
        {
            return baseValue;
        }

        float value = baseValue;
        for (int i = 0; i < activeItems.Count; i++)
        {
            ItemData data = activeItems[i].data;
            ItemModifier[] modifiers = data.modifiers;
            if (data.type != ItemType.Skill || modifiers == null)
            {
                continue;
            }

            for (int j = 0; j < modifiers.Length; j++)
            {
                ItemModifier modifier = modifiers[j];
                if (modifier.IsValidFor(ItemType.Skill)
                    && modifier.MatchesParameter(parameterKey)
                    && modifier.AppliesTo(skill.Definition))
                {
                    value = modifier.Apply(value);
                }
            }
        }

        return value;
    }

    // - Role: Evaluate one skill int parameter.
    public int EvaluateSkillInt(
        int baseValue,
        Skill skill,
        string parameterKey)
    {
        float evaluated = EvaluateSkillFloat(baseValue, skill, parameterKey);
        return Mathf.Max(0, Mathf.RoundToInt(evaluated));
    }

    // - Role: Get cooldown scale from item.
    public float GetCooldownScaleFor(ItemData itemData, Skill skill)
    {
        if (skill == null || itemData.type != ItemType.Skill || itemData.modifiers == null)
        {
            return 1f;
        }

        float scale = 1f;
        for (int i = 0; i < itemData.modifiers.Length; i++)
        {
            ItemModifier modifier = itemData.modifiers[i];
            if (modifier.IsValidFor(ItemType.Skill)
                && modifier.operation == ItemModifierOperation.Multiply
                && modifier.MatchesParameter(SkillModifierParameterKeys.Cooldown)
                && modifier.AppliesTo(skill.Definition))
            {
                scale *= Mathf.Max(0f, modifier.value);
            }
        }

        return scale;
    }

    // - Role: Evaluate stat modifiers.
    private float EvaluateStat(StatItemEffect effect, float baseValue)
    {
        float value = baseValue;
        for (int i = 0; i < activeItems.Count; i++)
        {
            ItemData data = activeItems[i].data;
            ItemModifier[] modifiers = data.modifiers;
            if (data.type != ItemType.Stats || modifiers == null)
            {
                continue;
            }

            for (int j = 0; j < modifiers.Length; j++)
            {
                ItemModifier modifier = modifiers[j];
                if (modifier.IsValidFor(ItemType.Stats) && modifier.statEffect == effect)
                {
                    value = modifier.Apply(value);
                }
            }
        }

        return value;
    }

    private struct ActiveItemData
    {
        public ItemData data;
        public float remainingSeconds;
    }
}

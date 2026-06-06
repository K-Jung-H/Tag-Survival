using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Item/Item Effect Catalog")]
public sealed class ItemEffectCatalog : ScriptableObject
{
    [SerializeField] private ItemData[] statEffects = new ItemData[0];
    [SerializeField] private ItemData[] skillEffects = new ItemData[0];

    // - Role: Check if this type has effects.
    public bool HasEffects(ItemType type)
    {
        ItemData[] effects = GetEffects(type);
        for (int i = 0; i < effects.Length; i++)
        {
            if (IsValidForType(effects[i], type))
            {
                return true;
            }
        }

        return false;
    }

    // - Role: Pick random effect candidates.
    public bool TryGetRandomCandidates(
        ItemType type,
        int count,
        System.Random random,
        List<ItemData> target)
    {
        if (target == null)
        {
            return false;
        }

        target.Clear();
        ItemData[] source = GetEffects(type);
        if (source.Length == 0)
        {
            return false;
        }

        int safeCount = Mathf.Clamp(count, 1, source.Length);
        List<int> indices = new List<int>(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            if (IsValidForType(source[i], type))
            {
                indices.Add(i);
            }
        }

        if (indices.Count == 0)
        {
            return false;
        }

        safeCount = Mathf.Min(safeCount, indices.Count);
        System.Random safeRandom = random ?? new System.Random();
        for (int i = 0; i < safeCount; i++)
        {
            int pickIndex = safeRandom.Next(i, indices.Count);
            int temp = indices[i];
            indices[i] = indices[pickIndex];
            indices[pickIndex] = temp;
            ItemData candidate = source[indices[i]];
            candidate.type = type;
            target.Add(candidate);
        }

        return target.Count > 0;
    }

    // - Role: Get effects for type.
    private ItemData[] GetEffects(ItemType type)
    {
        return type switch
        {
            ItemType.Stats => statEffects ?? new ItemData[0],
            ItemType.Skill => skillEffects ?? new ItemData[0],
            _ => new ItemData[0]
        };
    }

    // - Role: Check if data matches type.
    private static bool IsValidForType(ItemData itemData, ItemType type)
    {
        return type switch
        {
            ItemType.Stats => itemData.statEffect != StatItemEffect.None,
            ItemType.Skill => itemData.skillEffect != SkillItemEffect.None,
            _ => false
        };
    }
}

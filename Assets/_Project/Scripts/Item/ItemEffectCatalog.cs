using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Item/Item Effect Catalog")]
public sealed class ItemEffectCatalog : ScriptableObject
{
    [SerializeField] private ItemData[] statEffects = new ItemData[0];
    [SerializeField] private ItemData[] skillEffects = new ItemData[0];

    private readonly Dictionary<int, ItemData> effectsById = new();
    private bool isLookupDirty = true;

    // - Role: Build lookup when enabled.
    private void OnEnable()
    {
        RebuildLookup();
    }

    // - Role: Validate item data.
    private void OnValidate()
    {
        NormalizeEffects(ItemType.Stats, ref statEffects);
        NormalizeEffects(ItemType.Skill, ref skillEffects);
        ValidateIds();
        isLookupDirty = true;
    }

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

    // - Role: Try to get effect by id.
    public bool TryGetById(int id, out ItemData itemData)
    {
        EnsureLookup();
        return effectsById.TryGetValue(id, out itemData);
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

    // - Role: Ensure lookup is ready.
    private void EnsureLookup()
    {
        if (isLookupDirty)
        {
            RebuildLookup();
        }
    }

    // - Role: Build id lookup.
    private void RebuildLookup()
    {
        effectsById.Clear();
        AddEffectsToLookup(statEffects);
        AddEffectsToLookup(skillEffects);
        isLookupDirty = false;
    }

    // - Role: Add effects to lookup.
    private void AddEffectsToLookup(ItemData[] effects)
    {
        if (effects == null)
        {
            return;
        }

        for (int i = 0; i < effects.Length; i++)
        {
            ItemData itemData = effects[i];
            if (itemData.id <= 0 || effectsById.ContainsKey(itemData.id))
            {
                continue;
            }

            effectsById.Add(itemData.id, itemData);
        }
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

    // - Role: Normalize effects for type.
    private static void NormalizeEffects(ItemType type, ref ItemData[] effects)
    {
        if (effects == null)
        {
            effects = new ItemData[0];
        }

        for (int i = 0; i < effects.Length; i++)
        {
            ItemData itemData = effects[i];
            itemData.type = type;
            if (type == ItemType.Stats)
            {
                itemData.skillEffect = SkillItemEffect.None;
            }
            else if (type == ItemType.Skill)
            {
                itemData.statEffect = StatItemEffect.None;
            }

            effects[i] = itemData;
        }

        Array.Sort(effects, CompareById);
    }

    // - Role: Compare item ids.
    private static int CompareById(ItemData first, ItemData second)
    {
        return first.id.CompareTo(second.id);
    }

    // - Role: Validate ids.
    private void ValidateIds()
    {
        HashSet<int> ids = new();
        ValidateIds(statEffects, ids);
        ValidateIds(skillEffects, ids);
    }

    // - Role: Validate ids in array.
    private void ValidateIds(ItemData[] effects, HashSet<int> ids)
    {
        if (effects == null)
        {
            return;
        }

        for (int i = 0; i < effects.Length; i++)
        {
            int id = effects[i].id;
            if (id <= 0)
            {
                Debug.LogWarning($"[ItemEffectCatalog] Item effect id must be positive. index={i}", this);
                continue;
            }

            if (!ids.Add(id))
            {
                Debug.LogWarning($"[ItemEffectCatalog] Duplicate item effect id: {id}", this);
            }
        }
    }

    // - Role: Check if data matches type.
    private static bool IsValidForType(ItemData itemData, ItemType type)
    {
        return type switch
        {
            ItemType.Stats => itemData.id > 0 && itemData.statEffect != StatItemEffect.None,
            ItemType.Skill => itemData.id > 0 && itemData.skillEffect != SkillItemEffect.None,
            _ => false
        };
    }
}

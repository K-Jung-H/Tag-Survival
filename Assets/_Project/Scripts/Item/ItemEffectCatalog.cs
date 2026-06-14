using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Item/Item Effect Catalog")]
public sealed class ItemEffectCatalog : ScriptableObject
{
    [SerializeField] private StatItemSet[] statItemSets = Array.Empty<StatItemSet>();
    [SerializeField] private SkillCommonItemSet[] skillCommonItemSets = Array.Empty<SkillCommonItemSet>();
    [SerializeField] private SkillSpecialItemSet[] skillSpecialItemSets = Array.Empty<SkillSpecialItemSet>();

    private readonly Dictionary<int, ItemData> effectsById = new();
    private readonly List<ItemData> statRuntimeItems = new();
    private readonly List<ItemData> skillCommonRuntimeItems = new();
    private readonly List<SkillSpecialRuntimeItemSet> skillSpecialRuntimeItemSets = new();
    private bool isLookupDirty = true;

    // - Role: Build lookup when enabled.
    private void OnEnable()
    {
        RebuildLookup();
    }

    // - Role: Validate item data.
    private void OnValidate()
    {
        ValidateSetReferences();
        isLookupDirty = true;
    }

    // - Role: Check if this type has effects.
    public bool HasEffects(ItemType type)
    {
        EnsureLookup();
        return type switch
        {
            ItemType.Stats => statRuntimeItems.Count > 0,
            ItemType.Skill => skillCommonRuntimeItems.Count > 0 || skillSpecialRuntimeItemSets.Count > 0,
            _ => false
        };
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
        return TryGetRandomCandidates(type, count, random, target, null);
    }

    // - Role: Pick random effect candidates for player.
    public bool TryGetRandomCandidates(
        ItemType type,
        int count,
        System.Random random,
        List<ItemData> target,
        PlayerObject player)
    {
        if (target == null)
        {
            return false;
        }

        EnsureLookup();
        target.Clear();

        List<ItemData> candidates = new();
        AddCandidates(type, player, candidates);
        if (candidates.Count == 0)
        {
            return false;
        }

        int safeCount = Mathf.Clamp(count, 1, candidates.Count);
        System.Random safeRandom = random ?? new System.Random();
        for (int i = 0; i < safeCount; i++)
        {
            int pickIndex = safeRandom.Next(i, candidates.Count);
            ItemData temp = candidates[i];
            candidates[i] = candidates[pickIndex];
            candidates[pickIndex] = temp;
            target.Add(candidates[i]);
        }

        return target.Count > 0;
    }

    // - Role: Add valid candidates.
    private void AddCandidates(
        ItemType type,
        PlayerObject player,
        List<ItemData> target)
    {
        if (target == null)
        {
            return;
        }

        if (type == ItemType.Stats)
        {
            target.AddRange(statRuntimeItems);
            return;
        }

        if (type != ItemType.Skill)
        {
            return;
        }

        target.AddRange(skillCommonRuntimeItems);
        SkillDefinition skillDefinition = player != null && player.skill != null
            ? player.skill.Definition
            : null;
        if (skillDefinition == null)
        {
            return;
        }

        for (int i = 0; i < skillSpecialRuntimeItemSets.Count; i++)
        {
            SkillSpecialRuntimeItemSet set = skillSpecialRuntimeItemSets[i];
            if (set.skillDefinition == skillDefinition)
            {
                target.AddRange(set.items);
            }
        }
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
        statRuntimeItems.Clear();
        skillCommonRuntimeItems.Clear();
        skillSpecialRuntimeItemSets.Clear();

        AddStatItemSets();
        AddSkillCommonItemSets();
        AddSkillSpecialItemSets();
        isLookupDirty = false;
    }

    // - Role: Add stat item sets.
    private void AddStatItemSets()
    {
        if (statItemSets == null)
        {
            return;
        }

        for (int setIndex = 0; setIndex < statItemSets.Length; setIndex++)
        {
            StatItemSet set = statItemSets[setIndex];
            if (set == null)
            {
                continue;
            }

            StatItemEntry[] items = set.Items;
            for (int i = 0; i < items.Length; i++)
            {
                AddRuntimeItem(CreateStatItem(items[i]), statRuntimeItems);
            }
        }
    }

    // - Role: Add common skill item sets.
    private void AddSkillCommonItemSets()
    {
        if (skillCommonItemSets == null)
        {
            return;
        }

        for (int setIndex = 0; setIndex < skillCommonItemSets.Length; setIndex++)
        {
            SkillCommonItemSet set = skillCommonItemSets[setIndex];
            if (set == null)
            {
                continue;
            }

            SkillItemEntry[] items = set.Items;
            for (int i = 0; i < items.Length; i++)
            {
                AddRuntimeItem(CreateSkillCommonItem(items[i]), skillCommonRuntimeItems);
            }
        }
    }

    // - Role: Add special skill item sets.
    private void AddSkillSpecialItemSets()
    {
        if (skillSpecialItemSets == null)
        {
            return;
        }

        for (int setIndex = 0; setIndex < skillSpecialItemSets.Length; setIndex++)
        {
            SkillSpecialItemSet set = skillSpecialItemSets[setIndex];
            if (set == null || set.SkillDefinition == null)
            {
                continue;
            }

            SkillSpecialRuntimeItemSet runtimeSet = new SkillSpecialRuntimeItemSet
            {
                skillDefinition = set.SkillDefinition,
                items = new List<ItemData>()
            };

            SkillItemEntry[] items = set.Items;
            for (int i = 0; i < items.Length; i++)
            {
                AddRuntimeItem(CreateSkillSpecialItem(items[i], set.SkillDefinition), runtimeSet.items);
            }

            if (runtimeSet.items.Count > 0)
            {
                skillSpecialRuntimeItemSets.Add(runtimeSet);
            }
        }
    }

    // - Role: Add one runtime item.
    private void AddRuntimeItem(ItemData itemData, List<ItemData> target)
    {
        if (target == null || !itemData.IsValid())
        {
            return;
        }

        if (effectsById.ContainsKey(itemData.id))
        {
            Debug.LogWarning($"[ItemEffectCatalog] Duplicate item effect id ignored: {itemData.id}", this);
            return;
        }

        effectsById.Add(itemData.id, itemData);
        target.Add(itemData);
    }

    // - Role: Convert stat item.
    private static ItemData CreateStatItem(StatItemEntry source)
    {
        return new ItemData(
            source.entry.id,
            ItemType.Stats,
            source.entry.duration,
            source.entry.icon,
            source.entry.title,
            source.entry.description,
            ConvertStatModifiers(source.modifiers));
    }

    // - Role: Convert common skill item.
    private static ItemData CreateSkillCommonItem(SkillItemEntry source)
    {
        return new ItemData(
            source.entry.id,
            ItemType.Skill,
            source.entry.duration,
            source.entry.icon,
            source.entry.title,
            source.entry.description,
            ConvertSkillModifiers(
                source.modifiers,
                ItemModifierSkillScope.Any,
                0,
                string.Empty));
    }

    // - Role: Convert special skill item.
    private static ItemData CreateSkillSpecialItem(
        SkillItemEntry source,
        SkillDefinition skillDefinition)
    {
        return new ItemData(
            source.entry.id,
            ItemType.Skill,
            source.entry.duration,
            source.entry.icon,
            source.entry.title,
            source.entry.description,
            ConvertSkillModifiers(
                source.modifiers,
                ItemModifierSkillScope.LogicKey,
                skillDefinition != null ? skillDefinition.SkillId : (byte)0,
                skillDefinition != null ? skillDefinition.LogicKey : string.Empty));
    }

    // - Role: Convert stat modifiers.
    private static ItemModifier[] ConvertStatModifiers(StatModifierInput[] source)
    {
        if (source == null || source.Length == 0)
        {
            return Array.Empty<ItemModifier>();
        }

        ItemModifier[] modifiers = new ItemModifier[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            modifiers[i] = source[i].ToModifier();
        }

        return modifiers;
    }

    // - Role: Convert skill modifiers.
    private static ItemModifier[] ConvertSkillModifiers(
        SkillModifierInput[] source,
        ItemModifierSkillScope skillScope,
        byte skillId,
        string logicKey)
    {
        if (source == null || source.Length == 0)
        {
            return Array.Empty<ItemModifier>();
        }

        ItemModifier[] modifiers = new ItemModifier[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            modifiers[i] = source[i].ToModifier(skillScope, skillId, logicKey);
        }

        return modifiers;
    }

    // - Role: Validate set refs.
    private void ValidateSetReferences()
    {
        ValidateSetReferences(statItemSets, "Stat Item Set");
        ValidateSetReferences(skillCommonItemSets, "Skill Common Item Set");
        ValidateSetReferences(skillSpecialItemSets, "Skill Special Item Set");
    }

    // - Role: Validate set refs.
    private void ValidateSetReferences(UnityEngine.Object[] sets, string label)
    {
        if (sets == null)
        {
            return;
        }

        HashSet<UnityEngine.Object> seen = new();
        for (int i = 0; i < sets.Length; i++)
        {
            UnityEngine.Object set = sets[i];
            if (set == null)
            {
                continue;
            }

            if (!seen.Add(set))
            {
                Debug.LogWarning($"[ItemEffectCatalog] Duplicate {label} reference: {set.name}", this);
            }
        }
    }

    private sealed class SkillSpecialRuntimeItemSet
    {
        public SkillDefinition skillDefinition;
        public List<ItemData> items;
    }
}

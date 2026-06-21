using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Skill/Skill Catalog")]
public sealed class SkillCatalog : ScriptableObject
{
    [SerializeField] private SkillDefinition fallbackDefinition;
    [SerializeField] private SkillDefinition[] definitions = new SkillDefinition[0];

    private readonly Dictionary<byte, SkillDefinition> definitionsById = new();
    private bool isCacheDirty = true;

    public int Count => definitions != null ? definitions.Length : 0;
    public byte FallbackSkillId => fallbackDefinition != null ? fallbackDefinition.SkillId : (byte)0;

    // - Role: Turn on links when this object is enabled.
    private void OnEnable()
    {
        RebuildCache();
    }

    // - Role: Check editor values after they change.
    private void OnValidate()
    {
        isCacheDirty = true;
    }

    // - Role: Try to get a skill definition.
    public bool TryGet(byte skillId, out SkillDefinition definition)
    {
        return TryGetById(skillId, out definition);
    }

    // - Role: Try to get by ID.
    public bool TryGetById(byte skillId, out SkillDefinition definition)
    {
        EnsureCache();
        return definitionsById.TryGetValue(skillId, out definition) && definition != null;
    }

    public bool TryGetPlayable(
        byte skillId,
        out SkillDefinition definition,
        out string invalidReason)
    {
        TryGetById(skillId, out definition);
        return IsPlayableDefinition(definition, out invalidReason);
    }

    public bool TryGetFallbackPlayable(
        out SkillDefinition definition,
        out string invalidReason)
    {
        definition = fallbackDefinition;
        return IsPlayableDefinition(definition, out invalidReason);
    }

    public bool TryResolvePlayableId(
        byte requestedSkillId,
        out byte resolvedSkillId,
        out SkillDefinition definition,
        out bool usedFallback,
        out string invalidReason)
    {
        if (TryGetPlayable(requestedSkillId, out definition, out invalidReason))
        {
            resolvedSkillId = requestedSkillId;
            usedFallback = false;
            return true;
        }

        resolvedSkillId = FallbackSkillId;
        usedFallback = true;
        string requestedInvalidReason = invalidReason;
        bool fallbackSuccess = TryGetFallbackPlayable(out definition, out string fallbackInvalidReason);
        if (!fallbackSuccess)
        {
            invalidReason = $"{requestedInvalidReason}; Fallback Invalid: {fallbackInvalidReason}";
        }

        return fallbackSuccess;
    }

    private static bool IsPlayableDefinition(SkillDefinition definition, out string invalidReason)
    {
        invalidReason = string.Empty;
        if (definition == null)
        {
            invalidReason = "SkillDefinition Not Found";
            return false;
        }

        if (definition.Config == null)
        {
            invalidReason = "SkillConfig Missing";
            return false;
        }

        if (definition.Config.SkillType != definition.SkillType)
        {
            invalidReason = $"SkillConfig Type Mismatch: config={definition.Config.SkillType}, definition={definition.SkillType}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(definition.LogicKey))
        {
            invalidReason = "LogicKey Missing";
            return false;
        }

        return true;
    }

    // - Role: Try to get by index.
    public bool TryGetByIndex(int index, out SkillDefinition definition)
    {
        if (definitions != null && index >= 0 && index < definitions.Length)
        {
            definition = definitions[index];
            return definition != null;
        }

        definition = null;
        return false;
    }

    public bool TryGetIndexById(byte skillId, out int index)
    {
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                SkillDefinition definition = definitions[i];
                if (definition != null && definition.SkillId == skillId)
                {
                    index = i;
                    return true;
                }
            }
        }

        index = -1;
        return false;
    }

    // - Role: Make sure the cache is ready.
    private void EnsureCache()
    {
        if (!isCacheDirty)
        {
            return;
        }

        RebuildCache();
    }

    // - Role: Rebuild the cache.
    private void RebuildCache()
    {
        definitionsById.Clear();

        if (definitions != null)
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                SkillDefinition definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }

                definitionsById[definition.SkillId] = definition;
            }
        }

        isCacheDirty = false;
    }
}

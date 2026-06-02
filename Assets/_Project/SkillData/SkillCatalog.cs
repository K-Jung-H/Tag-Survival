using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Skill/Skill Catalog")]
public sealed class SkillCatalog : ScriptableObject
{
    [SerializeField] private SkillDefinition fallbackDefinition;
    [SerializeField] private SkillDefinition[] definitions = new SkillDefinition[0];

    private readonly Dictionary<byte, SkillDefinition> definitionsById = new();
    private bool isCacheDirty = true;

    private void OnEnable()
    {
        RebuildCache();
    }

    private void OnValidate()
    {
        isCacheDirty = true;
    }

    // Role: skillId에 맞는 SkillDefinition을 조회하고, 없으면 fallback을 반환한다.
    // Parameters:
    // - skillId: 조회할 스킬 ID
    // - definition: 조회된 스킬 정의
    public bool TryGet(byte skillId, out SkillDefinition definition)
    {
        if (TryGetById(skillId, out definition))
        {
            return true;
        }

        definition = fallbackDefinition;
        return definition != null;
    }

    // Role: skillId에 정확히 대응되는 SkillDefinition을 조회한다.
    // Parameters:
    // - skillId: 조회할 스킬 ID
    // - definition: 조회된 스킬 정의
    public bool TryGetById(byte skillId, out SkillDefinition definition)
    {
        EnsureCache();
        return definitionsById.TryGetValue(skillId, out definition) && definition != null;
    }

    // Role: Catalog 배열 인덱스에 대응되는 SkillDefinition을 조회한다.
    // Parameters:
    // - index: Catalog 배열 인덱스
    // - definition: 조회된 스킬 정의
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

    private void EnsureCache()
    {
        if (!isCacheDirty)
        {
            return;
        }

        RebuildCache();
    }

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

using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Skill/Skill Catalog")]
public sealed class SkillCatalog : ScriptableObject
{
    [SerializeField] private SkillDefinition fallbackDefinition;
    [SerializeField] private SkillDefinition[] definitions = new SkillDefinition[0];

    // Role: skillId에 대응되는 SkillDefinition을 조회한다.
    // Parameters:
    // - skillId: 조회할 스킬 ID
    // - definition: 조회된 스킬 정의
    public bool TryGet(byte skillId, out SkillDefinition definition)
    {
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                SkillDefinition candidate = definitions[i];
                if (candidate != null && candidate.SkillId == skillId)
                {
                    definition = candidate;
                    return true;
                }
            }
        }

        if (fallbackDefinition != null)
        {
            definition = fallbackDefinition;
            return true;
        }

        definition = null;
        return false;
    }
}

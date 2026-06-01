using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Character/Character Catalog")]
public sealed class CharacterCatalog : ScriptableObject
{
    [SerializeField] private CharacterDefinition fallbackDefinition;
    [SerializeField] private CharacterDefinition[] definitions;

    // Role: characterId에 맞는 캐릭터 정의 조회를 시도한다.
    // Parameters:
    // - characterId: 조회할 캐릭터 ID
    // - definition: 조회된 캐릭터 정의
    public bool TryGet(byte characterId, out CharacterDefinition definition)
    {
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                CharacterDefinition candidate = definitions[i];
                if (candidate != null && candidate.CharacterId == characterId)
                {
                    definition = candidate;
                    return true;
                }
            }
        }

        definition = fallbackDefinition;
        return definition != null;
    }
}

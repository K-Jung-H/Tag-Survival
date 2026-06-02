using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Character/Character Catalog")]
public sealed class CharacterCatalog : ScriptableObject
{
    [SerializeField] private CharacterDefinition fallbackDefinition;
    [SerializeField] private CharacterDefinition[] definitions;

    private readonly Dictionary<byte, CharacterDefinition> definitionsById = new();
    private bool isCacheDirty = true;

    private void OnEnable()
    {
        RebuildCache();
    }

    private void OnValidate()
    {
        isCacheDirty = true;
    }

    // Role: characterId에 맞는 CharacterDefinition을 조회하고, 없으면 fallback을 반환한다.
    // Parameters:
    // - characterId: 조회할 캐릭터 ID
    // - definition: 조회된 캐릭터 정의
    public bool TryGet(byte characterId, out CharacterDefinition definition)
    {
        if (TryGetById(characterId, out definition))
        {
            return true;
        }

        definition = fallbackDefinition;
        return definition != null;
    }

    // Role: characterId에 정확히 대응되는 CharacterDefinition을 조회한다.
    // Parameters:
    // - characterId: 조회할 캐릭터 ID
    // - definition: 조회된 캐릭터 정의
    public bool TryGetById(byte characterId, out CharacterDefinition definition)
    {
        EnsureCache();
        return definitionsById.TryGetValue(characterId, out definition) && definition != null;
    }

    // Role: Catalog 배열 인덱스에 대응되는 CharacterDefinition을 조회한다.
    // Parameters:
    // - index: Catalog 배열 인덱스
    // - definition: 조회된 캐릭터 정의
    public bool TryGetByIndex(int index, out CharacterDefinition definition)
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
                CharacterDefinition definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }

                definitionsById[definition.CharacterId] = definition;
            }
        }

        isCacheDirty = false;
    }
}

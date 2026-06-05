using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Character/Character Catalog")]
public sealed class CharacterCatalog : ScriptableObject
{
    [SerializeField] private CharacterDefinition fallbackDefinition;
    [SerializeField] private CharacterDefinition[] definitions;

    private readonly Dictionary<byte, CharacterDefinition> definitionsById = new();
    private bool isCacheDirty = true;

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

    // - Role: Try to get a character definition.
    public bool TryGet(byte characterId, out CharacterDefinition definition)
    {
        if (TryGetById(characterId, out definition))
        {
            return true;
        }

        definition = fallbackDefinition;
        return definition != null;
    }

    // - Role: Try to get by ID.
    public bool TryGetById(byte characterId, out CharacterDefinition definition)
    {
        EnsureCache();
        return definitionsById.TryGetValue(characterId, out definition) && definition != null;
    }

    // - Role: Try to get by index.
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

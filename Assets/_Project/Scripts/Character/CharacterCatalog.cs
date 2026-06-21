using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Character/Character Catalog")]
public sealed class CharacterCatalog : ScriptableObject
{
    [SerializeField] private CharacterDefinition fallbackDefinition;
    [SerializeField] private CharacterDefinition[] definitions;

    private readonly Dictionary<byte, CharacterDefinition> definitionsById = new();
    private bool isCacheDirty = true;

    public int Count => definitions != null ? definitions.Length : 0;
    public byte FallbackCharacterId => fallbackDefinition != null ? fallbackDefinition.CharacterId : (byte)0;

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
        return TryGetById(characterId, out definition);
    }

    // - Role: Try to get by ID.
    public bool TryGetById(byte characterId, out CharacterDefinition definition)
    {
        EnsureCache();
        return definitionsById.TryGetValue(characterId, out definition) && definition != null;
    }

    public bool TryGetFallback(out CharacterDefinition definition)
    {
        definition = fallbackDefinition;
        return definition != null;
    }

    public bool TryResolveId(
        byte requestedCharacterId,
        out byte resolvedCharacterId,
        out CharacterDefinition definition,
        out bool usedFallback)
    {
        if (TryGetById(requestedCharacterId, out definition))
        {
            resolvedCharacterId = requestedCharacterId;
            usedFallback = false;
            return true;
        }

        resolvedCharacterId = FallbackCharacterId;
        usedFallback = true;
        return TryGetFallback(out definition);
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

    public bool TryGetIndexById(byte characterId, out int index)
    {
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                CharacterDefinition definition = definitions[i];
                if (definition != null && definition.CharacterId == characterId)
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

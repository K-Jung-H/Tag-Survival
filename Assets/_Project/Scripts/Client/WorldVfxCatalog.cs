using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/VFX/World VFX Catalog")]
public sealed class WorldVfxCatalog : ScriptableObject
{
    [SerializeField] private WorldVfxDefinition[] definitions = Array.Empty<WorldVfxDefinition>();

    public WorldVfxDefinition[] Definitions => definitions ?? Array.Empty<WorldVfxDefinition>();

    // - Role: Try to get a VFX definition.
    public bool TryGet(GameVfxType type, out WorldVfxDefinition definition)
    {
        WorldVfxDefinition[] definitionArray = Definitions;
        for (int i = 0; i < definitionArray.Length; i++)
        {
            if (definitionArray[i].type != type)
            {
                continue;
            }

            definition = definitionArray[i];
            return true;
        }

        definition = default;
        return false;
    }

    // - Role: Check editor values after they change.
    private void OnValidate()
    {
        WorldVfxDefinition[] definitionArray = Definitions;
        HashSet<GameVfxType> registeredTypes = new();

        for (int i = 0; i < definitionArray.Length; i++)
        {
            GameVfxType type = definitionArray[i].type;
            if (type == GameVfxType.None)
            {
                continue;
            }

            if (!registeredTypes.Add(type))
            {
                Debug.LogWarning(
                    $"[WorldVfxCatalog] Duplicate VFX type '{type}' found at index {i}. The first definition will be used.",
                    this);
            }
        }
    }
}

[Serializable]
public struct WorldVfxDefinition
{
    public GameVfxType type;
    public GameObject prefab;
    public float lifetimeSeconds;
    public bool useServerRotation;
    public float spawnZ;
}

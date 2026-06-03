using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/VFX/World VFX Definition Data")]
public sealed class WorldVfxDefinitionDataSO : ScriptableObject
{
    [SerializeField] private WorldVfxDefinition[] definitions = Array.Empty<WorldVfxDefinition>();

    public WorldVfxDefinition[] Definitions => definitions ?? Array.Empty<WorldVfxDefinition>();

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
                    $"[WorldVfxDefinitionDataSO] Duplicate VFX type '{type}' found at index {i}. The first definition will be used.",
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

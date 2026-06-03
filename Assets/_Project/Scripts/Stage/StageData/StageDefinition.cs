using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct StagePhysicsModifier
{
    public StageSurfacePhysicType surfacePhysicType;
    public float moveSpeedMultiplier;
    public float groundAccelerationMultiplier;
    public float groundDecelerationMultiplier;
    public float overSpeedDecelerationMultiplier;
    public float jumpVelocityMultiplier;
    public float gravityScale;
    public float maxFallSpeedMultiplier;
    public float wallUpMoveMultiplier;
    public float wallDownMoveMultiplier;
    public float wallIdleSlideAcceleration;
    public float wallMaxSlideSpeed;

    public float MoveSpeedMultiplier => Mathf.Max(0f, moveSpeedMultiplier);
    public float GroundAccelerationMultiplier => Mathf.Max(0f, groundAccelerationMultiplier);
    public float GroundDecelerationMultiplier => Mathf.Max(0f, groundDecelerationMultiplier);
    public float OverSpeedDecelerationMultiplier => Mathf.Max(0f, overSpeedDecelerationMultiplier);
    public float JumpVelocityMultiplier => Mathf.Max(0f, jumpVelocityMultiplier);
    public float GravityScale => Mathf.Max(0f, gravityScale);
    public float MaxFallSpeedMultiplier => Mathf.Max(0.0001f, maxFallSpeedMultiplier);
    public float WallUpMoveMultiplier => Mathf.Max(0f, wallUpMoveMultiplier);
    public float WallDownMoveMultiplier => Mathf.Max(0f, wallDownMoveMultiplier);
    public float WallIdleSlideAcceleration => Mathf.Max(0f, wallIdleSlideAcceleration);
    public float WallMaxSlideSpeed => Mathf.Max(0f, wallMaxSlideSpeed);

    public static StagePhysicsModifier Normal => new StagePhysicsModifier
    {
        surfacePhysicType = StageSurfacePhysicType.Normal,
        moveSpeedMultiplier = 1f,
        groundAccelerationMultiplier = 1f,
        groundDecelerationMultiplier = 1f,
        overSpeedDecelerationMultiplier = 1f,
        jumpVelocityMultiplier = 1f,
        gravityScale = 1f,
        maxFallSpeedMultiplier = 1f,
        wallUpMoveMultiplier = 1f,
        wallDownMoveMultiplier = 1f,
        wallIdleSlideAcceleration = 0f,
        wallMaxSlideSpeed = 0f,
    };
}

[CreateAssetMenu(menuName = "Tag Survival/Stage/Stage Definition")]
public sealed class StageDefinition : ScriptableObject
{
    [SerializeField] private StageBakeData stageBakeData;
    [SerializeField] private Grid stageGridPrefab;
    [SerializeField] private StagePhysicsModifier[] physicsModifiers =
    {
        StagePhysicsModifier.Normal
    };

    public StageBakeData StageBakeData => stageBakeData;
    public Grid StageGridPrefab => stageGridPrefab;
    public StagePhysicsModifier[] PhysicsModifiers => physicsModifiers ?? Array.Empty<StagePhysicsModifier>();
    public float GravityScale => ResolvePhysicsModifier(StageSurfacePhysicType.Normal).GravityScale;
    public float MaxFallSpeedMultiplier => ResolvePhysicsModifier(StageSurfacePhysicType.Normal).MaxFallSpeedMultiplier;

    public bool TryGetPhysicsModifier(StageSurfacePhysicType surfacePhysicType, out StagePhysicsModifier modifier)
    {
        StagePhysicsModifier[] modifiers = PhysicsModifiers;
        for (int i = 0; i < modifiers.Length; i++)
        {
            if (modifiers[i].surfacePhysicType != surfacePhysicType)
            {
                continue;
            }

            modifier = modifiers[i];
            return true;
        }

        modifier = default;
        return false;
    }

    public StagePhysicsModifier ResolvePhysicsModifier(StageSurfacePhysicType surfacePhysicType)
    {
        if (TryGetPhysicsModifier(surfacePhysicType, out StagePhysicsModifier modifier))
        {
            return modifier;
        }

        if (surfacePhysicType != StageSurfacePhysicType.Normal
            && TryGetPhysicsModifier(StageSurfacePhysicType.Normal, out StagePhysicsModifier normalModifier))
        {
            return normalModifier;
        }

        if (surfacePhysicType != StageSurfacePhysicType.Default
            && TryGetPhysicsModifier(StageSurfacePhysicType.Default, out StagePhysicsModifier defaultModifier))
        {
            return defaultModifier;
        }

        return StagePhysicsModifier.Normal;
    }

    private void OnValidate()
    {
        HashSet<StageSurfacePhysicType> modifierTypes = CollectModifierTypes();
        if (stageBakeData == null || stageBakeData.Cells == null || stageBakeData.Cells.Length == 0)
        {
            return;
        }

        HashSet<StageSurfacePhysicType> bakedSurfaceTypes = CollectBakedSurfaceTypes();
        foreach (StageSurfacePhysicType bakedSurfaceType in bakedSurfaceTypes)
        {
            if (!modifierTypes.Contains(bakedSurfaceType))
            {
                Debug.LogWarning(
                    $"[StageDefinition] StageBakeData uses surface type '{bakedSurfaceType}', but StagePhysicsModifier is not defined.",
                    this);
            }
        }

        foreach (StageSurfacePhysicType modifierType in modifierTypes)
        {
            if (!bakedSurfaceTypes.Contains(modifierType))
            {
                Debug.LogWarning(
                    $"[StageDefinition] StagePhysicsModifier defines surface type '{modifierType}', but StageBakeData does not use it.",
                    this);
            }
        }
    }

    private HashSet<StageSurfacePhysicType> CollectModifierTypes()
    {
        HashSet<StageSurfacePhysicType> modifierTypes = new HashSet<StageSurfacePhysicType>();
        StagePhysicsModifier[] modifiers = PhysicsModifiers;
        for (int i = 0; i < modifiers.Length; i++)
        {
            StageSurfacePhysicType type = modifiers[i].surfacePhysicType;
            if (!modifierTypes.Add(type))
            {
                Debug.LogWarning(
                    $"[StageDefinition] Duplicate StagePhysicsModifier for surface type '{type}' found at index {i}. The first definition will be used.",
                    this);
            }
        }

        return modifierTypes;
    }

    private HashSet<StageSurfacePhysicType> CollectBakedSurfaceTypes()
    {
        HashSet<StageSurfacePhysicType> bakedSurfaceTypes = new HashSet<StageSurfacePhysicType>();
        StageTileCellData[] cells = stageBakeData.Cells;
        for (int i = 0; i < cells.Length; i++)
        {
            bakedSurfaceTypes.Add(cells[i].surfacePhysicType);
        }

        return bakedSurfaceTypes;
    }
}

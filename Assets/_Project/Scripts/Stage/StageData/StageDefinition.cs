using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct StagePhysicsModifier
{
    public StageSurfaceType surfacePhysicType;
    public float moveSpeedRate;
    public float moveAccelRate;
    public float moveDecelRate;
    public float overSpeedDecelRate;
    public float jumpStartSpeedRate;
    public float gravityScale;
    public float maxFallSpeedRate;
    public float wallUpMoveRate;
    public float wallDownMoveRate;
    public float wallIdleSlideAcceleration;
    public float wallMaxSlideSpeed;

    public float MoveSpeedRate => Mathf.Max(0f, moveSpeedRate);
    public float MoveAccelRate => Mathf.Max(0f, moveAccelRate);
    public float MoveDecelRate => Mathf.Max(0f, moveDecelRate);
    public float OverSpeedDecelRate => Mathf.Max(0f, overSpeedDecelRate);
    public float JumpStartSpeedRate => Mathf.Max(0f, jumpStartSpeedRate);
    public float GravityScale => Mathf.Max(0f, gravityScale);
    public float MaxFallSpeedRate => Mathf.Max(0.0001f, maxFallSpeedRate);
    public float WallUpMoveRate => Mathf.Max(0f, wallUpMoveRate);
    public float WallDownMoveRate => Mathf.Max(0f, wallDownMoveRate);
    public float WallIdleSlideAcceleration => Mathf.Max(0f, wallIdleSlideAcceleration);
    public float WallMaxSlideSpeed => Mathf.Max(0f, wallMaxSlideSpeed);

    public static StagePhysicsModifier Normal => new StagePhysicsModifier
    {
        surfacePhysicType = StageSurfaceType.Normal,
        moveSpeedRate = 1f,
        moveAccelRate = 1f,
        moveDecelRate = 1f,
        overSpeedDecelRate = 1f,
        jumpStartSpeedRate = 1f,
        gravityScale = 1f,
        maxFallSpeedRate = 1f,
        wallUpMoveRate = 1f,
        wallDownMoveRate = 1f,
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
    public float GravityScale => ResolvePhysicsModifier(StageSurfaceType.Normal).GravityScale;
    public float MaxFallSpeedRate => ResolvePhysicsModifier(StageSurfaceType.Normal).MaxFallSpeedRate;

    // - Role: Try to get physics modifier.
    public bool TryGetPhysicsModifier(StageSurfaceType surfacePhysicType, out StagePhysicsModifier modifier)
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

    // - Role: Find physics modifier.
    public StagePhysicsModifier ResolvePhysicsModifier(StageSurfaceType surfacePhysicType)
    {
        if (TryGetPhysicsModifier(surfacePhysicType, out StagePhysicsModifier modifier))
        {
            return modifier;
        }

        if (surfacePhysicType != StageSurfaceType.Normal
            && TryGetPhysicsModifier(StageSurfaceType.Normal, out StagePhysicsModifier normalModifier))
        {
            return normalModifier;
        }

        if (surfacePhysicType != StageSurfaceType.Default
            && TryGetPhysicsModifier(StageSurfaceType.Default, out StagePhysicsModifier defaultModifier))
        {
            return defaultModifier;
        }

        return StagePhysicsModifier.Normal;
    }

    // - Role: Check editor values after they change.
    private void OnValidate()
    {
        HashSet<StageSurfaceType> modifierTypes = CollectModifierTypes();
        if (stageBakeData == null || stageBakeData.Cells == null || stageBakeData.Cells.Length == 0)
        {
            return;
        }

        HashSet<StageSurfaceType> bakedSurfaceTypes = CollectBakedSurfaceTypes();
        foreach (StageSurfaceType bakedSurfaceType in bakedSurfaceTypes)
        {
            if (!modifierTypes.Contains(bakedSurfaceType))
            {
                Debug.LogWarning(
                    $"[StageDefinition] StageBakeData uses surface type '{bakedSurfaceType}', but StagePhysicsModifier is not defined.",
                    this);
            }
        }

        foreach (StageSurfaceType modifierType in modifierTypes)
        {
            if (!bakedSurfaceTypes.Contains(modifierType))
            {
                Debug.LogWarning(
                    $"[StageDefinition] StagePhysicsModifier defines surface type '{modifierType}', but StageBakeData does not use it.",
                    this);
            }
        }
    }

    // - Role: Collect modifier types.
    private HashSet<StageSurfaceType> CollectModifierTypes()
    {
        HashSet<StageSurfaceType> modifierTypes = new HashSet<StageSurfaceType>();
        StagePhysicsModifier[] modifiers = PhysicsModifiers;
        for (int i = 0; i < modifiers.Length; i++)
        {
            StageSurfaceType type = modifiers[i].surfacePhysicType;
            if (!modifierTypes.Add(type))
            {
                Debug.LogWarning(
                    $"[StageDefinition] Duplicate StagePhysicsModifier for surface type '{type}' found at index {i}. " +
                    "The first definition will be used.",
                    this);
            }
        }

        return modifierTypes;
    }

    // - Role: Collect baked surface types.
    private HashSet<StageSurfaceType> CollectBakedSurfaceTypes()
    {
        HashSet<StageSurfaceType> bakedSurfaceTypes = new HashSet<StageSurfaceType>();
        StageTileCellData[] cells = stageBakeData.Cells;
        for (int i = 0; i < cells.Length; i++)
        {
            bakedSurfaceTypes.Add(cells[i].surfacePhysicType);
        }

        return bakedSurfaceTypes;
    }
}

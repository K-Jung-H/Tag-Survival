using System;

// - Role: List stage surface physics types.
public enum StageSurfacePhysicType
{
    Default = 0,
    Normal = 1,
    Ice = 2,
    Mud = 3,
}

// - Role: Store stage tile flags.
[Flags]
public enum StageTileFlags
{
    None = 0,
    Solid = 1 << 0,
    OneWay = 1 << 1,
    Destructible = 1 << 2,
    Hazard = 1 << 3,
}

// - Role: List baked collider shapes.
public enum StageColliderType
{
    Rect = 0,
    ConvexPolygon = 1,
}

// - Role: List stage edge rules.
public enum StageBoundaryMode
{
    Open = 0,
    Solid = 1,
    Kill = 2,
    Wrap = 3,
}

// - Role: Store one tilemap layer rule.
[Serializable]
public struct StageLayerDefinition
{
    public StageSurfacePhysicType surfacePhysicType;
    public StageTileFlags flags;

    // - Role: Get the default solid layer rule.
    public static StageLayerDefinition Default => new StageLayerDefinition
    {
        surfacePhysicType = StageSurfacePhysicType.Normal,
        flags = StageTileFlags.Solid,
    };
}

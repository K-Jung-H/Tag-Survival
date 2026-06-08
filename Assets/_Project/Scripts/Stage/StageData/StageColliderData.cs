using System;
using UnityEngine;

// - Role: Store stage size and edge rules.
[Serializable]
public struct StageBoundsData
{
    public Vector2Int sizeInCells;
    public StageBoundaryMode left;
    public StageBoundaryMode right;
    public StageBoundaryMode bottom;
    public StageBoundaryMode top;
}

// - Role: Store one baked tile cell.
[Serializable]
public struct StageTileCellData
{
    public Vector2Int cell;
    public StageSurfaceType surfacePhysicType;
    public StageTileFlags flags;
    public int layerPriority;
}

// - Role: Store one baked server collider.
[Serializable]
public struct StageColliderData
{
    public StageColliderType type;
    public StageSurfaceType surfacePhysicType;
    public StageTileFlags flags;
    public int layerPriority;
    public Rect rect;
    public Vector2[] points;
}

// - Role: Store one grid bucket and its collider IDs.
[Serializable]
public struct StageSpatialBucketData
{
    public Vector2Int coord;
    public int[] colliderIndices;
}

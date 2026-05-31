using System;
using UnityEngine;

// Role: Stage 로컬 셀 좌표계 기준 크기와 외곽 경계 동작을 저장한다.
[Serializable]
public struct StageBoundsData
{
    public Vector2Int sizeInCells;
    public StageBoundaryMode left;
    public StageBoundaryMode right;
    public StageBoundaryMode bottom;
    public StageBoundaryMode top;
}

// Role: 레이어 우선순위가 반영된 단일 Bake 타일 셀 정보를 저장한다.
[Serializable]
public struct StageTileCellData
{
    public Vector2Int cell;
    public StageSurfacePhysicType surfacePhysicType;
    public StageTileFlags flags;
    public int layerPriority;
}

// Role: Bake된 셀에서 생성된 서버 충돌체 정보를 저장한다.
[Serializable]
public struct StageColliderData
{
    public StageColliderType type;
    public StageSurfacePhysicType surfacePhysicType;
    public StageTileFlags flags;
    public int layerPriority;
    public Rect rect;
    public Vector2[] points;
}

// Role: Uniform Grid 버킷 좌표와 해당 버킷에 포함된 충돌체 인덱스를 저장한다.
[Serializable]
public struct StageSpatialBucketData
{
    public Vector2Int coord;
    public int[] colliderIndices;
}

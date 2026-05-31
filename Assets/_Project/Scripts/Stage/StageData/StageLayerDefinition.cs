using System;

// Role: Stage 타일이 사용할 표면 물리 속성을 구분한다.
public enum StageSurfacePhysicType
{
    Default = 0,
    Normal = 1,
    Ice = 2,
    Mud = 3,
}

// Role: Stage 타일의 충돌/상태 속성을 비트 플래그로 표현한다.
[Flags]
public enum StageTileFlags
{
    None = 0,
    Solid = 1 << 0,
    OneWay = 1 << 1,
    Destructible = 1 << 2,
    Hazard = 1 << 3,
}

// Role: Bake 결과 충돌체의 형태를 구분한다.
public enum StageColliderType
{
    Rect = 0,
    ConvexPolygon = 1,
}

// Role: Stage 외곽 경계의 처리 방식을 구분한다.
public enum StageBoundaryMode
{
    Open = 0,
    Solid = 1,
    Kill = 2,
    Wrap = 3,
}

// Role: Tilemap 레이어 하나에 적용할 표면 속성과 타일 플래그를 저장한다.
[Serializable]
public struct StageLayerDefinition
{
    public StageSurfacePhysicType surfacePhysicType;
    public StageTileFlags flags;

    // Role: 일반 Solid 지형에 사용할 기본 레이어 정의를 반환한다.
    public static StageLayerDefinition Default => new StageLayerDefinition
    {
        surfacePhysicType = StageSurfacePhysicType.Normal,
        flags = StageTileFlags.Solid,
    };
}

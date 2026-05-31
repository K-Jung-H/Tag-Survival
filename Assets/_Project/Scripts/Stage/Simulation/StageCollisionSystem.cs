using System.Collections.Generic;
using UnityEngine;

public sealed class StageCollisionSystem
{
    private readonly HashSet<int> candidateSet = new HashSet<int>();
    private readonly List<int> candidateBuffer = new List<int>();

    private StageBakeData stageBakeData;
    private float playerHalfExtent;
    private float skinWidth;

    // Role: StageBakeData 기반 충돌 연산 시스템을 생성한다.
    // Parameters:
    // - stageBakeData: 사용할 Stage Bake 결과 데이터
    // - playerHalfExtent: 플레이어 사각형의 반지름 크기
    // - skinWidth: 충돌 여유 폭
    public StageCollisionSystem(
        StageBakeData stageBakeData,
        float playerHalfExtent,
        float skinWidth)
    {
        this.stageBakeData = stageBakeData;
        this.playerHalfExtent = Mathf.Max(0.0001f, playerHalfExtent);
        this.skinWidth = Mathf.Max(0f, skinWidth);
    }

    public StageBakeData StageBakeData => stageBakeData;

    // Role: 충돌 연산에 사용할 StageBakeData를 교체한다.
    // Parameters:
    // - newStageBakeData: 새로 사용할 Stage Bake 결과 데이터
    public void SetStageBakeData(StageBakeData newStageBakeData)
    {
        stageBakeData = newStageBakeData;
    }

    // Role: Stage bounds 기준 중앙 위치를 반환한다.
    public Vector2 GetStageCenterPosition()
    {
        if (stageBakeData == null)
        {
            return Vector2.zero;
        }

        Vector2Int sizeInCells = stageBakeData.Bounds.sizeInCells;
        return new Vector2(
            sizeInCells.x * stageBakeData.CellSize * 0.5f,
            sizeInCells.y * stageBakeData.CellSize * 0.5f);
    }

    // Role: 플레이어 이동을 Stage 충돌과 경계 조건에 맞게 보정한다.
    // Parameters:
    // - startPosition: 이동 시작 위치
    // - delta: 이번 이동량
    public Vector2 MovePlayerWithStageCollision(Vector2 startPosition, Vector2 delta)
    {
        if (!HasStageCollision())
        {
            return ResolveStageBoundaries(startPosition + delta);
        }

        Vector2 movedPosition = MoveWithStageCollision(startPosition, delta, allowSlide: true);
        return ResolveStageBoundaries(movedPosition);
    }

    // Role: 두 플레이어 사각형의 SAT 충돌 여부와 보정 방향을 계산한다.
    // Parameters:
    // - firstPosition: 첫 번째 플레이어 위치
    // - secondPosition: 두 번째 플레이어 위치
    // - firstId: 첫 번째 플레이어 클라이언트 ID
    // - secondId: 두 번째 플레이어 클라이언트 ID
    // - normal: 첫 번째 플레이어에서 두 번째 플레이어로 향하는 보정 방향
    // - penetration: 겹친 깊이
    public bool TryGetPlayerSatCollision(
        Vector2 firstPosition,
        Vector2 secondPosition,
        ulong firstId,
        ulong secondId,
        out Vector2 normal,
        out float penetration)
    {
        normal = Vector2.zero;
        penetration = 0f;

        Vector2 delta = secondPosition - firstPosition;
        float overlapX = playerHalfExtent * 2f - Mathf.Abs(delta.x);

        if (overlapX <= 0f)
        {
            return false;
        }

        float overlapY = playerHalfExtent * 2f - Mathf.Abs(delta.y);

        if (overlapY <= 0f)
        {
            return false;
        }

        if (overlapX < overlapY)
        {
            normal = delta.x >= 0f ? Vector2.right : Vector2.left;
            penetration = overlapX;
            return true;
        }

        if (overlapY < overlapX)
        {
            normal = delta.y >= 0f ? Vector2.up : Vector2.down;
            penetration = overlapY;
            return true;
        }

        normal = GetFallbackSatNormal(delta, firstId, secondId);
        penetration = overlapX;
        return true;
    }

    // Role: 충돌 법선 방향으로 파고드는 속도 성분을 제거한다.
    // Parameters:
    // - velocity: 보정할 속도
    // - normal: 제거 기준이 되는 충돌 법선
    public Vector2 RemoveVelocityIntoNormal(Vector2 velocity, Vector2 normal)
    {
        float intoNormal = Vector2.Dot(velocity, normal);

        if (intoNormal <= 0f)
        {
            return velocity;
        }

        return velocity - normal * intoNormal;
    }

    // Role: Stage 충돌체를 대상으로 이동과 슬라이딩을 처리한다.
    // Parameters:
    // - startPosition: 이동 시작 위치
    // - delta: 이번 이동량
    // - allowSlide: 충돌 후 남은 이동량을 벽면 방향으로 미끄러뜨릴지 여부
    private Vector2 MoveWithStageCollision(Vector2 startPosition, Vector2 delta, bool allowSlide)
    {
        float distance = delta.magnitude;

        if (distance <= 0.000001f)
        {
            return ResolveStageOverlaps(startPosition);
        }

        if (!TrySweepStage(startPosition, delta, out StageSweepHit hit))
        {
            return ResolveStageOverlaps(startPosition + delta);
        }

        Vector2 resolvedPosition = startPosition + delta * hit.fraction;
        resolvedPosition = ResolveStageOverlaps(resolvedPosition);

        if (!allowSlide)
        {
            return resolvedPosition;
        }

        Vector2 remainingDelta = delta * (1f - hit.fraction);
        Vector2 slideDelta = remainingDelta - hit.normal * Vector2.Dot(remainingDelta, hit.normal);

        if (slideDelta.sqrMagnitude <= 0.000001f)
        {
            return resolvedPosition;
        }

        return MoveWithStageCollision(resolvedPosition, slideDelta, allowSlide: false);
    }

    // Role: 이동 경로에서 가장 먼저 부딪히는 Stage 충돌체를 찾는다.
    // Parameters:
    // - startPosition: 이동 시작 위치
    // - delta: 이번 이동량
    // - closestHit: 가장 가까운 Sweep 충돌 결과
    private bool TrySweepStage(Vector2 startPosition, Vector2 delta, out StageSweepHit closestHit)
    {
        closestHit = default;
        closestHit.fraction = 1f;

        Rect sweepRect = CreatePlayerSweepRect(startPosition, delta);
        CollectCandidateColliders(sweepRect);

        bool hasHit = false;
        for (int i = 0; i < candidateBuffer.Count; i++)
        {
            StageColliderData collider = stageBakeData.Colliders[candidateBuffer[i]];
            if (!IsBlockingCollider(collider))
            {
                continue;
            }

            if (!TrySweepPointAgainstExpandedRect(
                startPosition,
                delta,
                collider.rect,
                out StageSweepHit hit))
            {
                continue;
            }

            if (hit.fraction >= closestHit.fraction)
            {
                continue;
            }

            closestHit = hit;
            hasHit = true;
        }

        return hasHit;
    }

    // Role: 이동하는 점과 확장된 사각형 충돌체의 Sweep 충돌을 계산한다.
    // Parameters:
    // - startPosition: 이동 시작 위치
    // - delta: 이번 이동량
    // - colliderRect: 검사할 원본 충돌체 사각형
    // - hit: Sweep 충돌 결과
    private bool TrySweepPointAgainstExpandedRect(
        Vector2 startPosition,
        Vector2 delta,
        Rect colliderRect,
        out StageSweepHit hit)
    {
        hit = default;

        Rect expandedRect = ExpandRect(colliderRect, playerHalfExtent + skinWidth);
        if (expandedRect.Contains(startPosition))
        {
            return false;
        }

        float xEntry;
        float xExit;
        if (Mathf.Abs(delta.x) <= 0.000001f)
        {
            if (startPosition.x < expandedRect.xMin || startPosition.x > expandedRect.xMax)
            {
                return false;
            }

            xEntry = float.NegativeInfinity;
            xExit = float.PositiveInfinity;
        }
        else
        {
            float invEntry = delta.x > 0f
                ? expandedRect.xMin - startPosition.x
                : expandedRect.xMax - startPosition.x;
            float invExit = delta.x > 0f
                ? expandedRect.xMax - startPosition.x
                : expandedRect.xMin - startPosition.x;

            xEntry = invEntry / delta.x;
            xExit = invExit / delta.x;
        }

        float yEntry;
        float yExit;
        if (Mathf.Abs(delta.y) <= 0.000001f)
        {
            if (startPosition.y < expandedRect.yMin || startPosition.y > expandedRect.yMax)
            {
                return false;
            }

            yEntry = float.NegativeInfinity;
            yExit = float.PositiveInfinity;
        }
        else
        {
            float invEntry = delta.y > 0f
                ? expandedRect.yMin - startPosition.y
                : expandedRect.yMax - startPosition.y;
            float invExit = delta.y > 0f
                ? expandedRect.yMax - startPosition.y
                : expandedRect.yMin - startPosition.y;

            yEntry = invEntry / delta.y;
            yExit = invExit / delta.y;
        }

        float entryTime = Mathf.Max(xEntry, yEntry);
        float exitTime = Mathf.Min(xExit, yExit);

        if (entryTime > exitTime || entryTime < 0f || entryTime > 1f)
        {
            return false;
        }

        hit.fraction = Mathf.Clamp01(entryTime);
        if (xEntry > yEntry)
        {
            hit.normal = delta.x > 0f ? Vector2.left : Vector2.right;
        }
        else
        {
            hit.normal = delta.y > 0f ? Vector2.down : Vector2.up;
        }

        return true;
    }

    // Role: 이미 Stage 충돌체 안에 들어간 위치를 가장 가까운 바깥 방향으로 밀어낸다.
    // Parameters:
    // - position: 보정할 플레이어 위치
    private Vector2 ResolveStageOverlaps(Vector2 position)
    {
        CollectCandidateColliders(CreatePlayerBounds(position));

        Vector2 resolvedPosition = position;
        for (int iteration = 0; iteration < 4; iteration++)
        {
            bool resolvedAny = false;
            for (int i = 0; i < candidateBuffer.Count; i++)
            {
                StageColliderData collider = stageBakeData.Colliders[candidateBuffer[i]];
                if (!IsBlockingCollider(collider))
                {
                    continue;
                }

                Rect expandedRect = ExpandRect(collider.rect, playerHalfExtent);
                if (!expandedRect.Contains(resolvedPosition))
                {
                    continue;
                }

                Vector2 push = GetSmallestPushOut(resolvedPosition, expandedRect);
                resolvedPosition += push;
                resolvedAny = true;
            }

            if (!resolvedAny)
            {
                break;
            }
        }

        return resolvedPosition;
    }

    // Role: queryRect와 겹칠 수 있는 Stage 충돌체 후보 목록을 수집한다.
    // Parameters:
    // - queryRect: 후보를 찾을 월드 사각형 범위
    private void CollectCandidateColliders(Rect queryRect)
    {
        candidateSet.Clear();
        candidateBuffer.Clear();

        if (stageBakeData == null)
        {
            return;
        }

        StageColliderData[] colliders = stageBakeData.Colliders;
        StageSpatialBucketData[] buckets = stageBakeData.SpatialBuckets;

        if (colliders == null || colliders.Length == 0)
        {
            return;
        }

        if (buckets == null || buckets.Length == 0 || stageBakeData.UniformGridSize <= 0)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                candidateBuffer.Add(i);
            }

            return;
        }

        float bucketSize = Mathf.Max(0.0001f, stageBakeData.CellSize * stageBakeData.UniformGridSize);
        int xMin = Mathf.FloorToInt(queryRect.xMin / bucketSize);
        int yMin = Mathf.FloorToInt(queryRect.yMin / bucketSize);
        int xMax = Mathf.FloorToInt((queryRect.xMax - 0.0001f) / bucketSize);
        int yMax = Mathf.FloorToInt((queryRect.yMax - 0.0001f) / bucketSize);

        for (int y = yMin; y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                AddBucketColliderCandidates(new Vector2Int(x, y), buckets);
            }
        }
    }

    // Role: 특정 Uniform Grid 버킷에 등록된 충돌체 후보를 추가한다.
    // Parameters:
    // - bucketCoord: 조회할 버킷 좌표
    // - buckets: StageBakeData에 저장된 공간 분할 버킷 목록
    private void AddBucketColliderCandidates(Vector2Int bucketCoord, StageSpatialBucketData[] buckets)
    {
        for (int i = 0; i < buckets.Length; i++)
        {
            if (buckets[i].coord != bucketCoord)
            {
                continue;
            }

            int[] colliderIndices = buckets[i].colliderIndices;
            if (colliderIndices == null)
            {
                return;
            }

            for (int j = 0; j < colliderIndices.Length; j++)
            {
                int colliderIndex = colliderIndices[j];
                if (candidateSet.Add(colliderIndex))
                {
                    candidateBuffer.Add(colliderIndex);
                }
            }

            return;
        }
    }

    // Role: 플레이어 위치를 기준으로 충돌 검사 사각형을 만든다.
    // Parameters:
    // - position: 플레이어 중심 위치
    private Rect CreatePlayerBounds(Vector2 position)
    {
        float extent = playerHalfExtent + skinWidth;
        return new Rect(
            position.x - extent,
            position.y - extent,
            extent * 2f,
            extent * 2f);
    }

    // Role: 이동 시작과 끝 위치를 모두 포함하는 Sweep 검사 범위를 만든다.
    // Parameters:
    // - startPosition: 이동 시작 위치
    // - delta: 이번 이동량
    private Rect CreatePlayerSweepRect(Vector2 startPosition, Vector2 delta)
    {
        Rect startBounds = CreatePlayerBounds(startPosition);
        Rect endBounds = CreatePlayerBounds(startPosition + delta);
        float xMin = Mathf.Min(startBounds.xMin, endBounds.xMin);
        float yMin = Mathf.Min(startBounds.yMin, endBounds.yMin);
        float xMax = Mathf.Max(startBounds.xMax, endBounds.xMax);
        float yMax = Mathf.Max(startBounds.yMax, endBounds.yMax);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    // Role: 사각형을 지정한 크기만큼 바깥으로 확장한다.
    // Parameters:
    // - rect: 확장할 사각형
    // - amount: 각 방향으로 확장할 크기
    private Rect ExpandRect(Rect rect, float amount)
    {
        return Rect.MinMaxRect(
            rect.xMin - amount,
            rect.yMin - amount,
            rect.xMax + amount,
            rect.yMax + amount);
    }

    // Role: 확장된 사각형 내부의 점을 가장 짧은 방향으로 밖으로 밀어낼 벡터를 구한다.
    // Parameters:
    // - position: 사각형 내부의 위치
    // - expandedRect: 충돌 여유가 반영된 사각형
    private Vector2 GetSmallestPushOut(Vector2 position, Rect expandedRect)
    {
        float left = Mathf.Abs(position.x - expandedRect.xMin);
        float right = Mathf.Abs(expandedRect.xMax - position.x);
        float bottom = Mathf.Abs(position.y - expandedRect.yMin);
        float top = Mathf.Abs(expandedRect.yMax - position.y);

        float min = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
        if (Mathf.Approximately(min, left))
        {
            return Vector2.left * left;
        }

        if (Mathf.Approximately(min, right))
        {
            return Vector2.right * right;
        }

        if (Mathf.Approximately(min, bottom))
        {
            return Vector2.down * bottom;
        }

        return Vector2.up * top;
    }

    // Role: 현재 StageBakeData에 사용할 수 있는 충돌체가 있는지 판단한다.
    private bool HasStageCollision()
    {
        return stageBakeData != null
            && stageBakeData.Colliders != null
            && stageBakeData.Colliders.Length > 0;
    }

    // Role: Stage 경계 설정에 따라 플레이어 위치를 제한한다.
    // Parameters:
    // - position: 경계 보정을 적용할 위치
    private Vector2 ResolveStageBoundaries(Vector2 position)
    {
        if (stageBakeData == null)
        {
            return position;
        }

        StageBoundsData bounds = stageBakeData.Bounds;
        float width = bounds.sizeInCells.x * stageBakeData.CellSize;
        float height = bounds.sizeInCells.y * stageBakeData.CellSize;

        Vector2 resolvedPosition = position;
        if (bounds.left == StageBoundaryMode.Solid)
        {
            resolvedPosition.x = Mathf.Max(resolvedPosition.x, playerHalfExtent);
        }

        if (bounds.right == StageBoundaryMode.Solid)
        {
            resolvedPosition.x = Mathf.Min(resolvedPosition.x, width - playerHalfExtent);
        }

        if (bounds.bottom == StageBoundaryMode.Solid)
        {
            resolvedPosition.y = Mathf.Max(resolvedPosition.y, playerHalfExtent);
        }

        if (bounds.top == StageBoundaryMode.Solid)
        {
            resolvedPosition.y = Mathf.Min(resolvedPosition.y, height - playerHalfExtent);
        }

        return resolvedPosition;
    }

    // Role: StageColliderData가 플레이어를 막는 충돌체인지 판단한다.
    // Parameters:
    // - collider: 검사할 Stage 충돌체 데이터
    private bool IsBlockingCollider(StageColliderData collider)
    {
        return collider.type == StageColliderType.Rect
            && (collider.flags & StageTileFlags.Solid) != 0;
    }

    // Role: SAT 축이 완전히 같은 경우 안정적인 기본 보정 방향을 만든다.
    // Parameters:
    // - delta: 두 플레이어 위치 차이
    // - firstId: 첫 번째 플레이어 클라이언트 ID
    // - secondId: 두 번째 플레이어 클라이언트 ID
    private Vector2 GetFallbackSatNormal(Vector2 delta, ulong firstId, ulong secondId)
    {
        if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
        {
            return delta.x >= 0f ? Vector2.right : Vector2.left;
        }

        if (Mathf.Abs(delta.y) > 0.0001f)
        {
            return delta.y >= 0f ? Vector2.up : Vector2.down;
        }

        uint hash = (uint)((firstId * 73856093) ^ (secondId * 19349663));
        return (hash & 1u) == 0u ? Vector2.right : Vector2.up;
    }

    private struct StageSweepHit
    {
        public float fraction;
        public Vector2 normal;
    }
}

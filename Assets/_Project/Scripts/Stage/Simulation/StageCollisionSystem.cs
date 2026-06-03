using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class StageCollisionSystem
{
    private readonly HashSet<int> candidateSet = new HashSet<int>();
    private readonly List<int> candidateBuffer = new List<int>();
    private readonly Dictionary<Vector2Int, int> bucketIndexByCoord = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, StageTileCellData> stageCellByCoord = new Dictionary<Vector2Int, StageTileCellData>();
    private static readonly Vector2Int[] NeighborCellOffsets =
    {
        new Vector2Int(1, 0),
        new Vector2Int(1, 1),
        new Vector2Int(0, 1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1, 0),
        new Vector2Int(-1, -1),
        new Vector2Int(0, -1),
        new Vector2Int(1, -1),
    };

    private StageBakeData stageBakeData;
    private Vector2 defaultPlayerHalfExtent;
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
        : this(stageBakeData, new Vector2(playerHalfExtent, playerHalfExtent), skinWidth)
    {
    }

    // Role: StageBakeData 기반 충돌 연산 시스템을 생성한다.
    // Parameters:
    // - stageBakeData: 사용할 Stage Bake 결과 데이터
    // - playerHalfExtent: 플레이어 사각형의 X/Y 반지름 크기
    // - skinWidth: 충돌 여유 폭
    public StageCollisionSystem(
        StageBakeData stageBakeData,
        Vector2 playerHalfExtent,
        float skinWidth)
    {
        this.stageBakeData = stageBakeData;
        this.defaultPlayerHalfExtent = ClampHalfExtent(playerHalfExtent);
        this.skinWidth = Mathf.Max(0f, skinWidth);
        RebuildLookups();
    }

    public StageBakeData StageBakeData => stageBakeData;

    // Role: 충돌 연산에 사용할 StageBakeData를 교체한다.
    // Parameters:
    // - newStageBakeData: 새로 사용할 Stage Bake 결과 데이터
    public void SetStageBakeData(StageBakeData newStageBakeData)
    {
        stageBakeData = newStageBakeData;
        RebuildLookups();
    }

    public float CellSize => stageBakeData != null ? Mathf.Max(0.0001f, stageBakeData.CellSize) : 1f;

    public bool TryFindPortalPlacementCell(
        Vector2 playerCenter,
        Vector2 playerHalfExtent,
        Vector2 aim,
        out Vector2Int placementCell,
        out Vector2 placementCenter,
        Func<Vector2Int, bool> isCellBlocked = null)
    {
        placementCell = default;
        placementCenter = default;

        if (stageBakeData == null)
        {
            return false;
        }

        if (!TryResolvePlayerOccupiedCell(playerCenter, playerHalfExtent, out Vector2Int originCell))
        {
            return false;
        }

        Vector2 aimDirection = aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.right;
        int testedMask = 0;

        for (int i = 0; i < NeighborCellOffsets.Length; i++)
        {
            int bestIndex = -1;
            float bestScore = float.NegativeInfinity;
            int bestDistanceSqr = int.MaxValue;

            for (int j = 0; j < NeighborCellOffsets.Length; j++)
            {
                int testBit = 1 << j;
                if ((testedMask & testBit) != 0)
                {
                    continue;
                }

                Vector2 offsetDirection = ((Vector2)NeighborCellOffsets[j]).normalized;
                float score = Vector2.Dot(aimDirection, offsetDirection);
                int distanceSqr = NeighborCellOffsets[j].sqrMagnitude;
                if (score > bestScore + 0.000001f
                    || (Mathf.Abs(score - bestScore) <= 0.000001f && distanceSqr < bestDistanceSqr))
                {
                    bestIndex = j;
                    bestScore = score;
                    bestDistanceSqr = distanceSqr;
                }
            }

            if (bestIndex < 0)
            {
                break;
            }

            testedMask |= 1 << bestIndex;
            Vector2Int candidateCell = originCell + NeighborCellOffsets[bestIndex];
            if (!IsCellInsideStageBounds(candidateCell)
                || IsCellSolid(candidateCell)
                || (isCellBlocked != null && isCellBlocked(candidateCell)))
            {
                continue;
            }

            placementCell = candidateCell;
            placementCenter = GetCellCenter(candidateCell);
            return true;
        }

        return false;
    }

    public Vector2 GetCellCenter(Vector2Int cell)
    {
        float cellSize = CellSize;
        return new Vector2(
            (cell.x + 0.5f) * cellSize,
            (cell.y + 0.5f) * cellSize);
    }

    public bool IsCellInsideStageBounds(Vector2Int cell)
    {
        if (stageBakeData == null)
        {
            return false;
        }

        Vector2Int sizeInCells = stageBakeData.Bounds.sizeInCells;
        return cell.x >= 0
            && cell.y >= 0
            && cell.x < sizeInCells.x
            && cell.y < sizeInCells.y;
    }

    public bool IsCellSolid(Vector2Int cell)
    {
        return stageCellByCoord.TryGetValue(cell, out StageTileCellData data)
            && (data.flags & StageTileFlags.Solid) != 0;
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
        return MovePlayerWithStageCollision(startPosition, delta, defaultPlayerHalfExtent);
    }

    // Role: 플레이어 이동을 Stage 충돌과 경계 조건에 맞게 보정한다.
    // Parameters:
    // - startPosition: 이동 시작 위치
    // - delta: 이번 이동량
    // - playerHalfExtent: 플레이어 사각형의 X/Y 반지름 크기
    public Vector2 MovePlayerWithStageCollision(Vector2 startPosition, Vector2 delta, Vector2 playerHalfExtent)
    {
        return MovePlayerWithStageCollisionDetailed(startPosition, delta, playerHalfExtent).position;
    }

    // Role: 플레이어 이동 결과와 Stage 접촉 방향을 함께 반환한다.
    // Parameters:
    // - startPosition: 이동 시작 위치
    // - delta: 이번 이동량
    // - playerHalfExtent: 플레이어 사각형의 X/Y 반지름 크기
    public StageCollisionMoveResult MovePlayerWithStageCollisionDetailed(
        Vector2 startPosition,
        Vector2 delta,
        Vector2 playerHalfExtent)
    {
        Vector2 halfExtent = ClampHalfExtent(playerHalfExtent);
        StageCollisionMoveResult result = new StageCollisionMoveResult
        {
            position = startPosition + delta,
        };

        if (!HasStageCollision())
        {
            result.position = ResolveStageBoundaries(result.position, halfExtent, ref result);
            return result;
        }

        result.position = MoveWithStageCollision(startPosition, delta, halfExtent, allowSlide: true, ref result);
        result.position = ResolveStageBoundaries(result.position, halfExtent, ref result);
        return result;
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
        return TryGetPlayerSatCollision(
            firstPosition,
            secondPosition,
            defaultPlayerHalfExtent,
            defaultPlayerHalfExtent,
            firstId,
            secondId,
            out normal,
            out penetration);
    }

    // Role: 두 플레이어 사각형의 SAT 충돌 여부와 보정 방향을 계산한다.
    // Parameters:
    // - firstPosition: 첫 번째 플레이어 위치
    // - secondPosition: 두 번째 플레이어 위치
    // - firstHalfExtent: 첫 번째 플레이어 사각형의 X/Y 반지름 크기
    // - secondHalfExtent: 두 번째 플레이어 사각형의 X/Y 반지름 크기
    // - firstId: 첫 번째 플레이어 클라이언트 ID
    // - secondId: 두 번째 플레이어 클라이언트 ID
    // - normal: 첫 번째 플레이어에서 두 번째 플레이어로 향하는 보정 방향
    // - penetration: 겹친 깊이
    public bool TryGetPlayerSatCollision(
        Vector2 firstPosition,
        Vector2 secondPosition,
        Vector2 firstHalfExtent,
        Vector2 secondHalfExtent,
        ulong firstId,
        ulong secondId,
        out Vector2 normal,
        out float penetration)
    {
        normal = Vector2.zero;
        penetration = 0f;

        firstHalfExtent = ClampHalfExtent(firstHalfExtent);
        secondHalfExtent = ClampHalfExtent(secondHalfExtent);
        Vector2 delta = secondPosition - firstPosition;
        float overlapX = firstHalfExtent.x + secondHalfExtent.x - Mathf.Abs(delta.x);

        if (overlapX <= 0f)
        {
            return false;
        }

        float overlapY = firstHalfExtent.y + secondHalfExtent.y - Mathf.Abs(delta.y);

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
    private Vector2 MoveWithStageCollision(
        Vector2 startPosition,
        Vector2 delta,
        Vector2 playerHalfExtent,
        bool allowSlide,
        ref StageCollisionMoveResult result)
    {
        float distance = delta.magnitude;

        if (distance <= 0.000001f)
        {
            return ResolveStageOverlaps(startPosition, playerHalfExtent, ref result);
        }

        if (!TrySweepStage(startPosition, delta, playerHalfExtent, out StageSweepHit hit))
        {
            return ResolveStageOverlaps(startPosition + delta, playerHalfExtent, ref result);
        }

        AddContactNormal(hit.normal, ref result);
        Vector2 resolvedPosition = startPosition + delta * hit.fraction;
        resolvedPosition = ResolveStageOverlaps(resolvedPosition, playerHalfExtent, ref result);

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

        return MoveWithStageCollision(
            resolvedPosition,
            slideDelta,
            playerHalfExtent,
            allowSlide: false,
            ref result);
    }

    // Role: 이동 경로에서 가장 먼저 부딪히는 Stage 충돌체를 찾는다.
    // Parameters:
    // - startPosition: 이동 시작 위치
    // - delta: 이번 이동량
    // - closestHit: 가장 가까운 Sweep 충돌 결과
    private bool TrySweepStage(
        Vector2 startPosition,
        Vector2 delta,
        Vector2 playerHalfExtent,
        out StageSweepHit closestHit)
    {
        closestHit = default;
        closestHit.fraction = 1f;

        Rect sweepRect = CreatePlayerSweepRect(startPosition, delta, playerHalfExtent);
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
                playerHalfExtent,
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
        Vector2 playerHalfExtent,
        Rect colliderRect,
        out StageSweepHit hit)
    {
        hit = default;

        Rect expandedRect = ExpandRect(colliderRect, playerHalfExtent + new Vector2(skinWidth, skinWidth));
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
    private Vector2 ResolveStageOverlaps(
        Vector2 position,
        Vector2 playerHalfExtent,
        ref StageCollisionMoveResult result)
    {
        CollectCandidateColliders(CreatePlayerBounds(position, playerHalfExtent));

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
                AddPushContact(push, ref result);
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
        if (!bucketIndexByCoord.TryGetValue(bucketCoord, out int bucketIndex))
        {
            return;
        }

        if (bucketIndex < 0 || bucketIndex >= buckets.Length)
        {
            return;
        }

        int[] colliderIndices = buckets[bucketIndex].colliderIndices;
        if (colliderIndices == null)
        {
            return;
        }

        for (int i = 0; i < colliderIndices.Length; i++)
        {
            int colliderIndex = colliderIndices[i];
            if (candidateSet.Add(colliderIndex))
            {
                candidateBuffer.Add(colliderIndex);
            }
        }
    }

    private void RebuildBucketLookup()
    {
        bucketIndexByCoord.Clear();

        if (stageBakeData == null)
        {
            return;
        }

        StageSpatialBucketData[] buckets = stageBakeData.SpatialBuckets;
        if (buckets == null)
        {
            return;
        }

        for (int i = 0; i < buckets.Length; i++)
        {
            bucketIndexByCoord[buckets[i].coord] = i;
        }
    }

    private void RebuildCellLookup()
    {
        stageCellByCoord.Clear();

        if (stageBakeData == null || stageBakeData.Cells == null)
        {
            return;
        }

        StageTileCellData[] cells = stageBakeData.Cells;
        for (int i = 0; i < cells.Length; i++)
        {
            stageCellByCoord[cells[i].cell] = cells[i];
        }
    }

    private void RebuildLookups()
    {
        RebuildBucketLookup();
        RebuildCellLookup();
    }

    private bool TryResolvePlayerOccupiedCell(
        Vector2 playerCenter,
        Vector2 playerHalfExtent,
        out Vector2Int occupiedCell)
    {
        occupiedCell = default;

        if (stageBakeData == null)
        {
            return false;
        }

        Vector2 halfExtent = ClampHalfExtent(playerHalfExtent);
        Rect bounds = Rect.MinMaxRect(
            playerCenter.x - halfExtent.x,
            playerCenter.y - halfExtent.y,
            playerCenter.x + halfExtent.x,
            playerCenter.y + halfExtent.y);

        float cellSize = CellSize;
        int xMin = Mathf.FloorToInt(bounds.xMin / cellSize);
        int yMin = Mathf.FloorToInt(bounds.yMin / cellSize);
        int xMax = Mathf.FloorToInt((bounds.xMax - 0.0001f) / cellSize);
        int yMax = Mathf.FloorToInt((bounds.yMax - 0.0001f) / cellSize);

        float bestArea = float.NegativeInfinity;
        Vector2Int bestCell = default;
        bool hasCell = false;

        for (int y = yMin; y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!IsCellInsideStageBounds(cell))
                {
                    continue;
                }

                Rect cellRect = new Rect(x * cellSize, y * cellSize, cellSize, cellSize);
                float overlapArea = GetOverlapArea(bounds, cellRect);
                if (overlapArea > bestArea)
                {
                    bestArea = overlapArea;
                    bestCell = cell;
                    hasCell = true;
                }
            }
        }

        occupiedCell = bestCell;
        return hasCell;
    }

    private static float GetOverlapArea(Rect first, Rect second)
    {
        float xMin = Mathf.Max(first.xMin, second.xMin);
        float xMax = Mathf.Min(first.xMax, second.xMax);
        float yMin = Mathf.Max(first.yMin, second.yMin);
        float yMax = Mathf.Min(first.yMax, second.yMax);

        if (xMax <= xMin || yMax <= yMin)
        {
            return 0f;
        }

        return (xMax - xMin) * (yMax - yMin);
    }

    // Role: 플레이어 위치를 기준으로 충돌 검사 사각형을 만든다.
    // Parameters:
    // - position: 플레이어 중심 위치
    private Rect CreatePlayerBounds(Vector2 position, Vector2 playerHalfExtent)
    {
        Vector2 extent = playerHalfExtent + new Vector2(skinWidth, skinWidth);
        return new Rect(
            position.x - extent.x,
            position.y - extent.y,
            extent.x * 2f,
            extent.y * 2f);
    }

    // Role: 이동 시작과 끝 위치를 모두 포함하는 Sweep 검사 범위를 만든다.
    // Parameters:
    // - startPosition: 이동 시작 위치
    // - delta: 이번 이동량
    private Rect CreatePlayerSweepRect(Vector2 startPosition, Vector2 delta, Vector2 playerHalfExtent)
    {
        Rect startBounds = CreatePlayerBounds(startPosition, playerHalfExtent);
        Rect endBounds = CreatePlayerBounds(startPosition + delta, playerHalfExtent);
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
    private Rect ExpandRect(Rect rect, Vector2 amount)
    {
        return Rect.MinMaxRect(
            rect.xMin - amount.x,
            rect.yMin - amount.y,
            rect.xMax + amount.x,
            rect.yMax + amount.y);
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
    private Vector2 ResolveStageBoundaries(Vector2 position, Vector2 playerHalfExtent)
    {
        StageCollisionMoveResult result = default;
        return ResolveStageBoundaries(position, playerHalfExtent, ref result);
    }

    // Role: Stage 경계 보정 결과를 접촉 상태에 반영한다.
    // Parameters:
    // - position: 경계 보정 전 위치
    // - playerHalfExtent: 플레이어 사각형의 X/Y 반지름 크기
    // - result: 갱신할 이동 결과
    private Vector2 ResolveStageBoundaries(
        Vector2 position,
        Vector2 playerHalfExtent,
        ref StageCollisionMoveResult result)
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
            float clampedX = Mathf.Max(resolvedPosition.x, playerHalfExtent.x);
            resolvedPosition.x = clampedX;
        }

        if (bounds.right == StageBoundaryMode.Solid)
        {
            float clampedX = Mathf.Min(resolvedPosition.x, width - playerHalfExtent.x);
            resolvedPosition.x = clampedX;
        }

        if (bounds.bottom == StageBoundaryMode.Solid)
        {
            float clampedY = Mathf.Max(resolvedPosition.y, playerHalfExtent.y);
            if (clampedY > resolvedPosition.y)
            {
                result.isGrounded = true;
            }

            resolvedPosition.y = clampedY;
        }

        if (bounds.top == StageBoundaryMode.Solid)
        {
            float clampedY = Mathf.Min(resolvedPosition.y, height - playerHalfExtent.y);
            if (clampedY < resolvedPosition.y)
            {
                result.hitCeiling = true;
            }

            resolvedPosition.y = clampedY;
        }

        return resolvedPosition;
    }

    // Role: 충돌 반지름 값이 너무 작아지지 않도록 보정한다.
    // Parameters:
    // - halfExtent: 보정할 X/Y 반지름 크기
    private static Vector2 ClampHalfExtent(Vector2 halfExtent)
    {
        return new Vector2(
            Mathf.Max(0.0001f, halfExtent.x),
            Mathf.Max(0.0001f, halfExtent.y));
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

    // Role: Sweep 충돌 법선으로 바닥, 천장, 벽 접촉 상태를 갱신한다.
    // Parameters:
    // - normal: 충돌 표면이 플레이어를 밀어내는 방향
    // - result: 갱신할 이동 결과
    private static void AddContactNormal(Vector2 normal, ref StageCollisionMoveResult result)
    {
        if (normal.y > 0.5f)
        {
            result.isGrounded = true;
        }
        else if (normal.y < -0.5f)
        {
            result.hitCeiling = true;
        }

        if (Mathf.Abs(normal.x) > 0.5f)
        {
            result.hitWall = true;
            result.wallNormalX = normal.x > 0f ? (sbyte)1 : (sbyte)-1;
        }
    }

    // Role: Overlap 보정 방향으로 바닥, 천장, 벽 접촉 상태를 갱신한다.
    // Parameters:
    // - push: 겹침을 해소하기 위해 플레이어에 적용한 이동량
    // - result: 갱신할 이동 결과
    private static void AddPushContact(Vector2 push, ref StageCollisionMoveResult result)
    {
        if (push.y > 0.000001f)
        {
            result.isGrounded = true;
        }
        else if (push.y < -0.000001f)
        {
            result.hitCeiling = true;
        }

        if (Mathf.Abs(push.x) > 0.000001f)
        {
            result.hitWall = true;
            result.wallNormalX = push.x > 0f ? (sbyte)1 : (sbyte)-1;
        }
    }

    private struct StageSweepHit
    {
        public float fraction;
        public Vector2 normal;
    }
}

public struct StageCollisionMoveResult
{
    public Vector2 position;
    public bool isGrounded;
    public bool hitCeiling;
    public bool hitWall;
    public sbyte wallNormalX;
}

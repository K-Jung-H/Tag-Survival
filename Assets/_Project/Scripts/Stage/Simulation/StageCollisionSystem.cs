using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class StageCollisionSystem
{
    private readonly HashSet<int> candidateSet = new HashSet<int>();
    private readonly List<int> candidateBuffer = new List<int>();
    private readonly Dictionary<Vector2Int, int> bucketIndexByCoord = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, StageTileCellData> stageCellByCoord = new Dictionary<Vector2Int, StageTileCellData>();
    private StageBakeData stageBakeData;
    private Vector2 defaultPlayerHalfExtent;
    private float skinWidth;

    // - Role: Create stage collision system.
    public StageCollisionSystem(
        StageBakeData stageBakeData,
        float playerHalfExtent,
        float skinWidth)
        : this(stageBakeData, new Vector2(playerHalfExtent, playerHalfExtent), skinWidth)
    {
    }

    // - Role: Create stage collision system.
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

    // - Role: Set stage bake data.
    public void SetStageBakeData(StageBakeData newStageBakeData)
    {
        stageBakeData = newStageBakeData;
        RebuildLookups();
    }

    public float CellSize => stageBakeData != null ? Mathf.Max(0.0001f, stageBakeData.CellSize) : 1f;

    // - Role: Try to find nearest empty tile.
    public bool TryFindNearestEmptyTile(
        Vector2 origin,
        int maxSearchDistance,
        Func<Vector2Int, bool> isCellBlocked,
        out Vector2Int emptyCell,
        out Vector2 emptyCellCenter)
    {
        emptyCell = default;
        emptyCellCenter = default;

        if (stageBakeData == null)
        {
            return false;
        }

        Vector2Int originCell = GetCellFromWorldPosition(origin);
        int searchDistance = Mathf.Max(0, maxSearchDistance);
        bool found = false;
        float bestDistanceSqr = float.PositiveInfinity;
        int bestOffsetDistanceSqr = int.MaxValue;

        for (int y = -searchDistance; y <= searchDistance; y++)
        {
            for (int x = -searchDistance; x <= searchDistance; x++)
            {
                Vector2Int candidateCell = originCell + new Vector2Int(x, y);
                if (!IsCellInsideStageBounds(candidateCell)
                    || IsCellSolid(candidateCell)
                    || (isCellBlocked != null && isCellBlocked(candidateCell)))
                {
                    continue;
                }

                Vector2 candidateCenter = GetCellCenter(candidateCell);
                float distanceSqr = (candidateCenter - origin).sqrMagnitude;
                int offsetDistanceSqr = x * x + y * y;
                if (distanceSqr < bestDistanceSqr - 0.000001f
                    || (Mathf.Abs(distanceSqr - bestDistanceSqr) <= 0.000001f
                        && offsetDistanceSqr < bestOffsetDistanceSqr))
                {
                    emptyCell = candidateCell;
                    emptyCellCenter = candidateCenter;
                    bestDistanceSqr = distanceSqr;
                    bestOffsetDistanceSqr = offsetDistanceSqr;
                    found = true;
                }
            }
        }

        return found;
    }

    // - Role: Get cell center.
    public Vector2 GetCellCenter(Vector2Int cell)
    {
        float cellSize = CellSize;
        return new Vector2(
            (cell.x + 0.5f) * cellSize,
            (cell.y + 0.5f) * cellSize);
    }

    // - Role: Check if cell inside stage bounds is true.
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

    // - Role: Check if cell solid is true.
    public bool IsCellSolid(Vector2Int cell)
    {
        return stageCellByCoord.TryGetValue(cell, out StageTileCellData data)
            && (data.flags & StageTileFlags.Solid) != 0;
    }

    // - Role: Get stage center position.
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

    // - Role: Move a player with stage collision.
    public Vector2 MovePlayerWithStageCollision(Vector2 startPosition, Vector2 delta)
    {
        return MovePlayerWithStageCollision(startPosition, delta, defaultPlayerHalfExtent);
    }

    // - Role: Move a player with stage collision.
    public Vector2 MovePlayerWithStageCollision(Vector2 startPosition, Vector2 delta, Vector2 playerHalfExtent)
    {
        return MovePlayerWithStageCollisionDetailed(startPosition, delta, playerHalfExtent).position;
    }

    // - Role: Move a player and return collision details.
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

    // - Role: Try to get player SAT collision.
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

    // - Role: Try to get player SAT collision.
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

    // - Role: Remove velocity into normal.
    public Vector2 RemoveVelocityIntoNormal(Vector2 velocity, Vector2 normal)
    {
        float intoNormal = Vector2.Dot(velocity, normal);

        if (intoNormal <= 0f)
        {
            return velocity;
        }

        return velocity - normal * intoNormal;
    }

    // - Role: Move with stage collision.
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

        AddContactNormal(hit.normal, hit.surfacePhysicType, ref result);
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

    // - Role: Try to sweep stage.
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

            Vector2 contactPosition = startPosition + delta * hit.fraction;
            if (IsTopSurfaceApproachHit(collider, hit, startPosition, delta, playerHalfExtent))
            {
                continue;
            }

            if (IsInternalHorizontalFaceHit(collider, hit, contactPosition, playerHalfExtent))
            {
                continue;
            }

            if (hit.fraction >= closestHit.fraction)
            {
                continue;
            }

            hit.surfacePhysicType = collider.surfacePhysicType;
            closestHit = hit;
            hasHit = true;
        }

        return hasHit;
    }

    // - Role: Check if top surface approach hit is true.
    private bool IsTopSurfaceApproachHit(
        StageColliderData collider,
        StageSweepHit hit,
        Vector2 startPosition,
        Vector2 delta,
        Vector2 playerHalfExtent)
    {
        if (Mathf.Abs(hit.normal.x) <= 0.5f)
        {
            return false;
        }

        if (delta.y > 0.000001f)
        {
            return false;
        }

        float footTolerance = Mathf.Max(skinWidth * 2f, 0.001f);
        float playerBottomAtStart = startPosition.y - playerHalfExtent.y;
        return playerBottomAtStart >= collider.rect.yMax - footTolerance;
    }

    // - Role: Check if internal horizontal face hit is true.
    private bool IsInternalHorizontalFaceHit(
        StageColliderData collider,
        StageSweepHit hit,
        Vector2 contactPosition,
        Vector2 playerHalfExtent)
    {
        if (Mathf.Abs(hit.normal.x) <= 0.5f)
        {
            return false;
        }

        float cellSize = CellSize;
        float epsilon = 0.0001f;
        bool hitLeftFace = hit.normal.x < 0f;
        int insideX = hitLeftFace
            ? Mathf.FloorToInt((collider.rect.xMin + epsilon) / cellSize)
            : Mathf.FloorToInt((collider.rect.xMax - epsilon) / cellSize);
        int outsideX = insideX + (hitLeftFace ? -1 : 1);

        float contactBottom = contactPosition.y - playerHalfExtent.y - skinWidth;
        float contactTop = contactPosition.y + playerHalfExtent.y + skinWidth;
        float faceBottom = Mathf.Max(collider.rect.yMin, contactBottom);
        float faceTop = Mathf.Min(collider.rect.yMax, contactTop);

        if (faceTop < faceBottom)
        {
            return false;
        }

        int yMin = Mathf.FloorToInt((faceBottom + epsilon) / cellSize);
        int yMax = Mathf.FloorToInt((faceTop - epsilon) / cellSize);
        bool checkedAnyFaceCell = false;

        for (int y = yMin; y <= yMax; y++)
        {
            Vector2Int insideCell = new Vector2Int(insideX, y);
            if (!IsCellSolid(insideCell))
            {
                continue;
            }

            checkedAnyFaceCell = true;
            Vector2Int outsideCell = new Vector2Int(outsideX, y);
            if (!IsCellSolid(outsideCell))
            {
                return false;
            }
        }

        return checkedAnyFaceCell;
    }

    // - Role: Check if internal horizontal push is true.
    private bool IsInternalHorizontalPush(
        StageColliderData collider,
        Vector2 push,
        Vector2 position,
        Vector2 playerHalfExtent)
    {
        if (Mathf.Abs(push.x) <= 0.000001f)
        {
            return false;
        }

        StageSweepHit hit = new StageSweepHit
        {
            normal = push.x > 0f ? Vector2.right : Vector2.left,
        };
        return IsInternalHorizontalFaceHit(collider, hit, position, playerHalfExtent);
    }

    // - Role: Try to sweep point against expanded rect.
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

    // - Role: Find stage overlaps.
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
                if (IsTopSurfaceOverlap(collider, resolvedPosition, playerHalfExtent))
                {
                    push = GetTopSurfacePushOut(resolvedPosition, expandedRect);
                }
                else if (IsInternalHorizontalPush(collider, push, resolvedPosition, playerHalfExtent))
                {
                    push = GetSmallestVerticalPushOut(resolvedPosition, expandedRect);
                }

                resolvedPosition += push;
                AddPushContact(push, collider.surfacePhysicType, ref result);
                resolvedAny = true;
            }

            if (!resolvedAny)
            {
                break;
            }
        }

        return resolvedPosition;
    }

    // - Role: Check if top surface overlap is true.
    private bool IsTopSurfaceOverlap(
        StageColliderData collider,
        Vector2 position,
        Vector2 playerHalfExtent)
    {
        float footTolerance = Mathf.Max(skinWidth * 2f, 0.001f);
        float playerBottom = position.y - playerHalfExtent.y;
        return playerBottom >= collider.rect.yMax - footTolerance;
    }

    // - Role: Collect possible colliders.
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

    // - Role: Add bucket collider candidates.
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

    // - Role: Rebuild the bucket lookup.
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

    // - Role: Rebuild the cell lookup.
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

    // - Role: Rebuild all lookups.
    private void RebuildLookups()
    {
        RebuildBucketLookup();
        RebuildCellLookup();
    }

    // - Role: Get cell from world position.
    private Vector2Int GetCellFromWorldPosition(Vector2 worldPosition)
    {
        float cellSize = CellSize;
        return new Vector2Int(
            Mathf.FloorToInt(worldPosition.x / cellSize),
            Mathf.FloorToInt(worldPosition.y / cellSize));
    }

    // - Role: Create player bounds.
    private Rect CreatePlayerBounds(Vector2 position, Vector2 playerHalfExtent)
    {
        Vector2 extent = playerHalfExtent + new Vector2(skinWidth, skinWidth);
        return new Rect(
            position.x - extent.x,
            position.y - extent.y,
            extent.x * 2f,
            extent.y * 2f);
    }

    // - Role: Create player sweep rect.
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

    // - Role: Expand a rectangle.
    private Rect ExpandRect(Rect rect, Vector2 amount)
    {
        return Rect.MinMaxRect(
            rect.xMin - amount.x,
            rect.yMin - amount.y,
            rect.xMax + amount.x,
            rect.yMax + amount.y);
    }

    // - Role: Get smallest push out.
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

    // - Role: Get smallest vertical push out.
    private Vector2 GetSmallestVerticalPushOut(Vector2 position, Rect expandedRect)
    {
        float bottom = Mathf.Abs(position.y - expandedRect.yMin);
        float top = Mathf.Abs(expandedRect.yMax - position.y);
        return bottom < top ? Vector2.down * bottom : Vector2.up * top;
    }

    // - Role: Get top surface push out.
    private Vector2 GetTopSurfacePushOut(Vector2 position, Rect expandedRect)
    {
        float top = expandedRect.yMax - position.y;
        return top > 0f ? Vector2.up * top : Vector2.zero;
    }

    // - Role: Check if stage collision exists.
    private bool HasStageCollision()
    {
        return stageBakeData != null
            && stageBakeData.Colliders != null
            && stageBakeData.Colliders.Length > 0;
    }

    // - Role: Find stage boundaries.
    private Vector2 ResolveStageBoundaries(Vector2 position, Vector2 playerHalfExtent)
    {
        StageCollisionMoveResult result = default;
        return ResolveStageBoundaries(position, playerHalfExtent, ref result);
    }

    // - Role: Find stage boundaries.
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
                result.groundSurfacePhysicType = StageSurfacePhysicType.Normal;
            }

            resolvedPosition.y = clampedY;
        }

        if (bounds.top == StageBoundaryMode.Solid)
        {
            float clampedY = Mathf.Min(resolvedPosition.y, height - playerHalfExtent.y);
            if (clampedY < resolvedPosition.y)
            {
                result.hitCeiling = true;
                result.ceilingSurfacePhysicType = StageSurfacePhysicType.Normal;
            }

            resolvedPosition.y = clampedY;
        }

        return resolvedPosition;
    }

    // - Role: Clamp the half size.
    private static Vector2 ClampHalfExtent(Vector2 halfExtent)
    {
        return new Vector2(
            Mathf.Max(0.0001f, halfExtent.x),
            Mathf.Max(0.0001f, halfExtent.y));
    }

    // - Role: Check if blocking collider is true.
    private bool IsBlockingCollider(StageColliderData collider)
    {
        return collider.type == StageColliderType.Rect
            && (collider.flags & StageTileFlags.Solid) != 0;
    }

    // - Role: Get fallback SAT normal.
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

    // - Role: Add contact normal.
    private static void AddContactNormal(
        Vector2 normal,
        StageSurfacePhysicType surfacePhysicType,
        ref StageCollisionMoveResult result)
    {
        if (normal.y > 0.5f)
        {
            result.isGrounded = true;
            result.groundSurfacePhysicType = surfacePhysicType;
        }
        else if (normal.y < -0.5f)
        {
            result.hitCeiling = true;
            result.ceilingSurfacePhysicType = surfacePhysicType;
        }

        if (Mathf.Abs(normal.x) > 0.5f)
        {
            result.hitWall = true;
            result.wallNormalX = normal.x > 0f ? (sbyte)1 : (sbyte)-1;
            result.wallSurfacePhysicType = surfacePhysicType;
        }
    }

    // - Role: Add push contact.
    private static void AddPushContact(
        Vector2 push,
        StageSurfacePhysicType surfacePhysicType,
        ref StageCollisionMoveResult result)
    {
        if (push.y > 0.000001f)
        {
            result.isGrounded = true;
            result.groundSurfacePhysicType = surfacePhysicType;
        }
        else if (push.y < -0.000001f)
        {
            result.hitCeiling = true;
            result.ceilingSurfacePhysicType = surfacePhysicType;
        }

        if (Mathf.Abs(push.x) > 0.000001f)
        {
            result.hitWall = true;
            result.wallNormalX = push.x > 0f ? (sbyte)1 : (sbyte)-1;
            result.wallSurfacePhysicType = surfacePhysicType;
        }
    }

    private struct StageSweepHit
    {
        public float fraction;
        public Vector2 normal;
        public StageSurfacePhysicType surfacePhysicType;
    }
}

public struct StageCollisionMoveResult
{
    public Vector2 position;
    public bool isGrounded;
    public bool hitCeiling;
    public bool hitWall;
    public sbyte wallNormalX;
    public StageSurfacePhysicType groundSurfacePhysicType;
    public StageSurfacePhysicType wallSurfacePhysicType;
    public StageSurfacePhysicType ceilingSurfacePhysicType;
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class StageBakeLayerInput
{
    public Tilemap tilemap;
    public StageLayerDefinition definition;
    public int priority;
    public bool includeInBake = true;
}

public sealed class StageBakeRequest
{
    public string stageId;
    public Grid grid;
    public StageBakeData output;
    public RectInt cellBounds;
    public StageBoundaryMode leftBoundary;
    public StageBoundaryMode rightBoundary;
    public StageBoundaryMode bottomBoundary;
    public StageBoundaryMode topBoundary;
    public bool mergeRectColliders;
    public bool generateSpatialIndex;
    public int uniformGridSize;
    public IReadOnlyList<StageBakeLayerInput> layers;
}

public sealed class StageBakeReport
{
    public int scannedCellCount;
    public int bakedCellCount;
    public int colliderCount;
    public int spatialBucketCount;
    public readonly List<string> warnings = new();
    public readonly List<string> errors = new();

    public bool HasErrors => errors.Count > 0;
}

public static class StageBaker
{
    private struct CellWinner
    {
        public StageBakeLayerInput layer;
    }

    private struct ColliderMergeKey
    {
        public StageSurfaceType surfacePhysicType;
        public StageTileFlags flags;
        public int priority;

        // - Role: Check if the cell matches.
        public bool Matches(StageTileCellData cell)
        {
            return surfacePhysicType == cell.surfacePhysicType
                && flags == cell.flags
                && priority == cell.layerPriority;
        }
    }

    // - Role: Validate the request.
    public static StageBakeReport Validate(StageBakeRequest request)
    {
        StageBakeReport report = new StageBakeReport();
        ValidateRequest(request, report);
        return report;
    }

    // - Role: Bake stage data.
    public static StageBakeReport Bake(StageBakeRequest request)
    {
        StageBakeReport report = new StageBakeReport();
        ValidateRequest(request, report);

        if (report.HasErrors)
        {
            return report;
        }

        RectInt sourceBounds = request.cellBounds;
        Dictionary<Vector2Int, CellWinner> winners = new Dictionary<Vector2Int, CellWinner>();

        foreach (StageBakeLayerInput layer in request.layers)
        {
            if (!IsLayerReady(layer))
            {
                continue;
            }

            Tilemap tilemap = layer.tilemap;
            for (int y = sourceBounds.yMin; y < sourceBounds.yMax; y++)
            {
                for (int x = sourceBounds.xMin; x < sourceBounds.xMax; x++)
                {
                    report.scannedCellCount++;

                    Vector3Int tileCell = new Vector3Int(x, y, 0);
                    if (!tilemap.HasTile(tileCell))
                    {
                        continue;
                    }

                    Vector2Int localCell = new Vector2Int(x - sourceBounds.xMin, y - sourceBounds.yMin);
                    if (!winners.TryGetValue(localCell, out CellWinner current))
                    {
                        winners.Add(localCell, new CellWinner { layer = layer });
                        continue;
                    }

                    int priorityCompare = layer.priority.CompareTo(current.layer.priority);
                    if (priorityCompare > 0)
                    {
                        winners[localCell] = new CellWinner { layer = layer };
                    }
                    else if (priorityCompare == 0 && !DefinitionsMatch(layer.definition, current.layer.definition))
                    {
                        report.warnings.Add(
                            $"Same-priority overlap at local cell {localCell}. Keeping {current.layer.tilemap.name}, ignoring {layer.tilemap.name}.");
                    }
                }
            }
        }

        StageTileCellData[] cells = BuildCellData(winners);
        float cellSize = ResolveCellSize(request.grid);
        StageColliderData[] colliders = BuildRectColliders(cells, cellSize, request.mergeRectColliders);
        StageSpatialBucketData[] spatialBuckets = request.generateSpatialIndex
            ? BuildSpatialBuckets(colliders, cellSize, request.uniformGridSize)
            : new StageSpatialBucketData[0];

        StageBoundsData boundsData = new StageBoundsData
        {
            sizeInCells = new Vector2Int(sourceBounds.width, sourceBounds.height),
            left = request.leftBoundary,
            right = request.rightBoundary,
            bottom = request.bottomBoundary,
            top = request.topBoundary,
        };

        request.output.SetBakeResult(
            request.stageId,
            new Vector2Int(sourceBounds.xMin, sourceBounds.yMin),
            cellSize,
            request.uniformGridSize,
            boundsData,
            cells,
            colliders,
            spatialBuckets);

        EditorUtility.SetDirty(request.output);
        AssetDatabase.SaveAssets();

        report.bakedCellCount = cells.Length;
        report.colliderCount = colliders.Length;
        report.spatialBucketCount = spatialBuckets.Length;
        return report;
    }

    // - Role: Validate the bake request.
    private static void ValidateRequest(StageBakeRequest request, StageBakeReport report)
    {
        if (request == null)
        {
            report.errors.Add("Stage bake request is null.");
            return;
        }

        if (request.output == null)
        {
            report.errors.Add("StageBakeData output is not assigned.");
        }

        if (request.grid == null)
        {
            report.errors.Add("Grid is not assigned.");
        }

        if (request.cellBounds.width <= 0 || request.cellBounds.height <= 0)
        {
            report.errors.Add("Cell bounds must have positive width and height.");
        }

        if (request.generateSpatialIndex && request.uniformGridSize <= 0)
        {
            report.errors.Add("Uniform grid size must be greater than zero.");
        }

        if (request.layers == null || request.layers.Count == 0)
        {
            report.errors.Add("No tilemap layers are assigned.");
            return;
        }

        for (int i = 0; i < request.layers.Count; i++)
        {
            StageBakeLayerInput layer = request.layers[i];
            if (layer == null)
            {
                report.errors.Add($"Tilemap layer slot {i} is empty.");
                continue;
            }

            if (!layer.includeInBake)
            {
                continue;
            }

            if (layer.tilemap == null)
            {
                report.errors.Add($"Tilemap layer slot {i} has no Tilemap.");
            }
        }
    }

    // - Role: Check if layer ready is true.
    private static bool IsLayerReady(StageBakeLayerInput layer)
    {
        return layer != null && layer.includeInBake && layer.tilemap != null;
    }

    // - Role: Check if definitions match.
    private static bool DefinitionsMatch(StageLayerDefinition a, StageLayerDefinition b)
    {
        return a.surfacePhysicType == b.surfacePhysicType && a.flags == b.flags;
    }

    // - Role: Build cell data.
    private static StageTileCellData[] BuildCellData(Dictionary<Vector2Int, CellWinner> winners)
    {
        List<StageTileCellData> cells = new List<StageTileCellData>(winners.Count);
        foreach (KeyValuePair<Vector2Int, CellWinner> pair in winners)
        {
            StageBakeLayerInput layer = pair.Value.layer;
            cells.Add(new StageTileCellData
            {
                cell = pair.Key,
                surfacePhysicType = layer.definition.surfacePhysicType,
                flags = layer.definition.flags,
                layerPriority = layer.priority,
            });
        }

        cells.Sort((a, b) =>
        {
            int yCompare = a.cell.y.CompareTo(b.cell.y);
            return yCompare != 0 ? yCompare : a.cell.x.CompareTo(b.cell.x);
        });

        return cells.ToArray();
    }

    // - Role: Build rect colliders.
    private static StageColliderData[] BuildRectColliders(
        StageTileCellData[] cells,
        float cellSize,
        bool mergeRectColliders)
    {
        Dictionary<Vector2Int, StageTileCellData> lookup = new Dictionary<Vector2Int, StageTileCellData>();
        foreach (StageTileCellData cell in cells)
        {
            if (!ShouldCreateCollider(cell))
            {
                continue;
            }

            lookup[cell.cell] = cell;
        }

        List<StageColliderData> colliders = new List<StageColliderData>(lookup.Count);
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        foreach (StageTileCellData cell in cells)
        {
            if (!ShouldCreateCollider(cell) || visited.Contains(cell.cell))
            {
                continue;
            }

            ColliderMergeKey key = new ColliderMergeKey
            {
                surfacePhysicType = cell.surfacePhysicType,
                flags = cell.flags,
                priority = cell.layerPriority,
            };

            int width = mergeRectColliders ? FindMergeWidth(lookup, visited, cell.cell, key) : 1;
            int height = mergeRectColliders ? FindMergeHeight(lookup, visited, cell.cell, key, width) : 1;
            MarkVisited(visited, cell.cell, width, height);
            colliders.Add(CreateRectCollider(cell, width, height, cellSize));
        }

        return colliders.ToArray();
    }

    // - Role: Check if create collider should happen.
    private static bool ShouldCreateCollider(StageTileCellData cell)
    {
        return cell.flags != StageTileFlags.None;
    }

    // - Role: Find merge width.
    private static int FindMergeWidth(
        Dictionary<Vector2Int, StageTileCellData> lookup,
        HashSet<Vector2Int> visited,
        Vector2Int origin,
        ColliderMergeKey key)
    {
        int width = 0;
        while (CanMergeCell(lookup, visited, new Vector2Int(origin.x + width, origin.y), key))
        {
            width++;
        }

        return Mathf.Max(1, width);
    }

    // - Role: Find merge height.
    private static int FindMergeHeight(
        Dictionary<Vector2Int, StageTileCellData> lookup,
        HashSet<Vector2Int> visited,
        Vector2Int origin,
        ColliderMergeKey key,
        int width)
    {
        int height = 1;
        bool canExpand = true;

        while (canExpand)
        {
            int y = origin.y + height;
            for (int xOffset = 0; xOffset < width; xOffset++)
            {
                Vector2Int coord = new Vector2Int(origin.x + xOffset, y);
                if (!CanMergeCell(lookup, visited, coord, key))
                {
                    canExpand = false;
                    break;
                }
            }

            if (canExpand)
            {
                height++;
            }
        }

        return height;
    }

    // - Role: Check if merge cell can happen.
    private static bool CanMergeCell(
        Dictionary<Vector2Int, StageTileCellData> lookup,
        HashSet<Vector2Int> visited,
        Vector2Int coord,
        ColliderMergeKey key)
    {
        return !visited.Contains(coord)
            && lookup.TryGetValue(coord, out StageTileCellData cell)
            && key.Matches(cell);
    }

    // - Role: Mark cells as visited.
    private static void MarkVisited(HashSet<Vector2Int> visited, Vector2Int origin, int width, int height)
    {
        for (int yOffset = 0; yOffset < height; yOffset++)
        {
            for (int xOffset = 0; xOffset < width; xOffset++)
            {
                visited.Add(new Vector2Int(origin.x + xOffset, origin.y + yOffset));
            }
        }
    }

    // - Role: Create rect collider.
    private static StageColliderData CreateRectCollider(
        StageTileCellData originCell,
        int widthInCells,
        int heightInCells,
        float cellSize)
    {
        return new StageColliderData
        {
            type = StageColliderType.Rect,
            surfacePhysicType = originCell.surfacePhysicType,
            flags = originCell.flags,
            layerPriority = originCell.layerPriority,
            rect = new Rect(
                originCell.cell.x * cellSize,
                originCell.cell.y * cellSize,
                widthInCells * cellSize,
                heightInCells * cellSize),
            points = new Vector2[0],
        };
    }

    // - Role: Build spatial buckets.
    private static StageSpatialBucketData[] BuildSpatialBuckets(
        StageColliderData[] colliders,
        float cellSize,
        int bucketSizeInCells)
    {
        Dictionary<Vector2Int, List<int>> bucketMap = new Dictionary<Vector2Int, List<int>>();
        float bucketSize = Mathf.Max(1, bucketSizeInCells) * cellSize;

        for (int i = 0; i < colliders.Length; i++)
        {
            Rect rect = colliders[i].rect;
            int xMin = Mathf.FloorToInt(rect.xMin / bucketSize);
            int yMin = Mathf.FloorToInt(rect.yMin / bucketSize);
            int xMax = Mathf.FloorToInt((rect.xMax - 0.0001f) / bucketSize);
            int yMax = Mathf.FloorToInt((rect.yMax - 0.0001f) / bucketSize);

            for (int y = yMin; y <= yMax; y++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    Vector2Int bucketCoord = new Vector2Int(x, y);
                    if (!bucketMap.TryGetValue(bucketCoord, out List<int> indices))
                    {
                        indices = new List<int>();
                        bucketMap.Add(bucketCoord, indices);
                    }

                    indices.Add(i);
                }
            }
        }

        List<StageSpatialBucketData> buckets = new List<StageSpatialBucketData>(bucketMap.Count);
        foreach (KeyValuePair<Vector2Int, List<int>> pair in bucketMap)
        {
            buckets.Add(new StageSpatialBucketData
            {
                coord = pair.Key,
                colliderIndices = pair.Value.ToArray(),
            });
        }

        buckets.Sort((a, b) =>
        {
            int yCompare = a.coord.y.CompareTo(b.coord.y);
            return yCompare != 0 ? yCompare : a.coord.x.CompareTo(b.coord.x);
        });

        return buckets.ToArray();
    }

    // - Role: Find cell size.
    private static float ResolveCellSize(Grid grid)
    {
        if (grid == null)
        {
            return 1f;
        }

        Vector3 cellSize = grid.cellSize;
        return Mathf.Max(0.0001f, Mathf.Max(cellSize.x, cellSize.y));
    }
}

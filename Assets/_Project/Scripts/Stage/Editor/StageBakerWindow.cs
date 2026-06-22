using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class StageBakerWindow : EditorWindow
{
    [Serializable]
    private sealed class TilemapEntry
    {
        public Tilemap tilemap;
        public bool includeInBake = true;
        public int priority;
        public StageLayerDefinition definition = StageLayerDefinition.Default;
    }

    [SerializeField] private StageBakeData output;
    [SerializeField] private StageDefinition stageDefinition;
    [SerializeField] private string stageId = "Stage";
    [SerializeField] private Grid grid;
    [SerializeField] private Tilemap spawnPosTilemap;
    [SerializeField] private Transform backgroundRoot;
    [SerializeField] private Transform environmentRoot;
    [SerializeField] private Transform foregroundRoot;
    [SerializeField] private RectInt cellBounds = new RectInt(0, 0, 64, 32);
    [SerializeField] private StageBoundaryMode leftBoundary = StageBoundaryMode.Solid;
    [SerializeField] private StageBoundaryMode rightBoundary = StageBoundaryMode.Solid;
    [SerializeField] private StageBoundaryMode bottomBoundary = StageBoundaryMode.Solid;
    [SerializeField] private StageBoundaryMode topBoundary = StageBoundaryMode.Open;
    [SerializeField] private bool mergeRectColliders = true;
    [SerializeField] private bool generateSpatialIndex = true;
    [SerializeField] private int uniformGridSize = 8;
    [SerializeField] private List<TilemapEntry> tilemapEntries = new List<TilemapEntry>();
    [SerializeField] private bool showBounds = true;
    [SerializeField] private bool showBakeSettings = true;
    [SerializeField] private bool showVisualRoots = true;
    [SerializeField] private bool showTilemapLayers = true;

    private Vector2 scrollPosition;

    // - Role: Open the editor window.
    [MenuItem("Tools/StageBaker")]
    public static void Open()
    {
        StageBakerWindow window = GetWindow<StageBakerWindow>("StageBaker");
        window.Show();
    }

    // - Role: Draw simple debug GUI.
    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        DrawOutputSection();
        DrawBoundsSection();
        DrawBakeSettingsSection();
        DrawVisualRootSection();
        DrawTilemapSection();
        DrawActionSection();

        EditorGUILayout.EndScrollView();
    }

    // - Role: Draw output section.
    private void DrawOutputSection()
    {
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        output = (StageBakeData)EditorGUILayout.ObjectField("Stage Bake Data", output, typeof(StageBakeData), false);
        stageDefinition = (StageDefinition)EditorGUILayout.ObjectField("Stage Definition", stageDefinition, typeof(StageDefinition), false);
        stageId = EditorGUILayout.TextField("Stage Id", stageId);
        grid = (Grid)EditorGUILayout.ObjectField("Grid", grid, typeof(Grid), true);
        spawnPosTilemap = (Tilemap)EditorGUILayout.ObjectField("Spawn Pos Tilemap", spawnPosTilemap, typeof(Tilemap), true);
        EditorGUILayout.Space(8f);
    }

    // - Role: Draw bounds section.
    private void DrawBoundsSection()
    {
        showBounds = EditorGUILayout.Foldout(showBounds, "Bounds", true, EditorStyles.foldoutHeader);
        if (!showBounds)
        {
            EditorGUILayout.Space(8f);
            return;
        }

        cellBounds = DrawRectInt("Cell Bounds", cellBounds);

        using (new EditorGUILayout.HorizontalScope())
        {
            leftBoundary = (StageBoundaryMode)EditorGUILayout.EnumPopup("Left", leftBoundary);
            rightBoundary = (StageBoundaryMode)EditorGUILayout.EnumPopup("Right", rightBoundary);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            bottomBoundary = (StageBoundaryMode)EditorGUILayout.EnumPopup("Bottom", bottomBoundary);
            topBoundary = (StageBoundaryMode)EditorGUILayout.EnumPopup("Top", topBoundary);
        }

        if (GUILayout.Button("Fit Bounds From Tilemaps"))
        {
            FitBoundsFromTilemaps();
        }

        EditorGUILayout.Space(8f);
    }

    // - Role: Draw bake settings section.
    private void DrawBakeSettingsSection()
    {
        showBakeSettings = EditorGUILayout.Foldout(showBakeSettings, "Bake Settings", true, EditorStyles.foldoutHeader);
        if (!showBakeSettings)
        {
            EditorGUILayout.Space(8f);
            return;
        }

        mergeRectColliders = EditorGUILayout.Toggle("Merge Rect Colliders", mergeRectColliders);
        generateSpatialIndex = EditorGUILayout.Toggle("Generate Spatial Index", generateSpatialIndex);
        uniformGridSize = Mathf.Max(1, EditorGUILayout.IntField("Uniform Grid Size", uniformGridSize));
        EditorGUILayout.Space(8f);
    }

    // - Role: Draw visual root section.
    private void DrawVisualRootSection()
    {
        showVisualRoots = EditorGUILayout.Foldout(showVisualRoots, "Visual Roots", true, EditorStyles.foldoutHeader);
        if (!showVisualRoots)
        {
            EditorGUILayout.Space(8f);
            return;
        }

        backgroundRoot = (Transform)EditorGUILayout.ObjectField("Background Root", backgroundRoot, typeof(Transform), true);
        environmentRoot = (Transform)EditorGUILayout.ObjectField("Environment Root", environmentRoot, typeof(Transform), true);
        foregroundRoot = (Transform)EditorGUILayout.ObjectField("Foreground Root", foregroundRoot, typeof(Transform), true);
        EditorGUILayout.Space(8f);
    }

    // - Role: Draw tilemap section.
    private void DrawTilemapSection()
    {
        showTilemapLayers = EditorGUILayout.Foldout(showTilemapLayers, "Tilemap_Layers", true, EditorStyles.foldoutHeader);
        if (!showTilemapLayers)
        {
            EditorGUILayout.Space(8f);
            return;
        }

        for (int i = 0; i < tilemapEntries.Count; i++)
        {
            DrawTilemapEntry(i);
        }

        if (GUILayout.Button("Add Tilemap Layer"))
        {
            tilemapEntries.Add(new TilemapEntry());
        }

        EditorGUILayout.Space(8f);
    }

    // - Role: Draw tilemap entry.
    private void DrawTilemapEntry(int index)
    {
        TilemapEntry entry = tilemapEntries[index];

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        bool shouldRemove = false;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"Layer {index + 1}", EditorStyles.boldLabel);
            if (GUILayout.Button("Remove", GUILayout.Width(72f)))
            {
                shouldRemove = true;
            }
        }

        if (shouldRemove)
        {
            tilemapEntries.RemoveAt(index);
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUI.BeginChangeCheck();
        entry.tilemap = (Tilemap)EditorGUILayout.ObjectField("Tilemap", entry.tilemap, typeof(Tilemap), true);
        if (EditorGUI.EndChangeCheck() && grid == null && entry.tilemap != null)
        {
            grid = entry.tilemap.GetComponentInParent<Grid>();
        }

        if (entry.tilemap != null)
        {
            entry.includeInBake = EditorGUILayout.Toggle("Include", entry.includeInBake);
            entry.priority = EditorGUILayout.IntField("Priority", entry.priority);
            entry.definition.surfacePhysicType = (StageSurfaceType)EditorGUILayout.EnumPopup("Surface Type", entry.definition.surfacePhysicType);
            entry.definition.flags = (StageTileFlags)EditorGUILayout.EnumFlagsField("Flags", entry.definition.flags);
        }

        EditorGUILayout.EndVertical();
    }

    // - Role: Draw action section.
    private void DrawActionSection()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Validate"))
            {
                LogReport("Stage validation", StageBaker.Validate(BuildRequest()));
            }

            if (GUILayout.Button("Bake"))
            {
                LogReport("Stage bake", StageBaker.Bake(BuildRequest()));
            }
        }
    }

    // - Role: Draw rect int.
    private RectInt DrawRectInt(string label, RectInt value)
    {
        EditorGUILayout.LabelField(label);
        EditorGUI.indentLevel++;
        int x = EditorGUILayout.IntField("X", value.x);
        int y = EditorGUILayout.IntField("Y", value.y);
        int width = Mathf.Max(1, EditorGUILayout.IntField("Width", value.width));
        int height = Mathf.Max(1, EditorGUILayout.IntField("Height", value.height));
        EditorGUI.indentLevel--;
        return new RectInt(x, y, width, height);
    }

    // - Role: Fit bounds from tilemaps.
    private void FitBoundsFromTilemaps()
    {
        bool hasBounds = false;
        RectInt combined = new RectInt();

        foreach (TilemapEntry entry in tilemapEntries)
        {
            if (entry.tilemap == null || !entry.includeInBake)
            {
                continue;
            }

            if (!TryGetOccupiedTileBounds(entry.tilemap, out RectInt bounds))
            {
                continue;
            }

            if (!hasBounds)
            {
                combined = bounds;
                hasBounds = true;
                continue;
            }

            int xMin = Mathf.Min(combined.xMin, bounds.xMin);
            int yMin = Mathf.Min(combined.yMin, bounds.yMin);
            int xMax = Mathf.Max(combined.xMax, bounds.xMax);
            int yMax = Mathf.Max(combined.yMax, bounds.yMax);
            combined = new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        if (!hasBounds)
        {
            Debug.LogWarning("No included tilemap has occupied cells.");
            return;
        }

        cellBounds = combined;
    }

    // - Role: Get actual occupied tile bounds instead of Unity's possibly stale Tilemap.cellBounds.
    private static bool TryGetOccupiedTileBounds(Tilemap tilemap, out RectInt bounds)
    {
        bounds = default;
        if (tilemap == null)
        {
            return false;
        }

        BoundsInt cellBounds = tilemap.cellBounds;
        bool hasTile = false;
        int xMin = int.MaxValue;
        int yMin = int.MaxValue;
        int xMax = int.MinValue;
        int yMax = int.MinValue;

        for (int z = cellBounds.zMin; z < cellBounds.zMax; z++)
        {
            for (int y = cellBounds.yMin; y < cellBounds.yMax; y++)
            {
                for (int x = cellBounds.xMin; x < cellBounds.xMax; x++)
                {
                    Vector3Int cell = new Vector3Int(x, y, z);
                    if (!tilemap.HasTile(cell))
                    {
                        continue;
                    }

                    hasTile = true;
                    xMin = Mathf.Min(xMin, x);
                    yMin = Mathf.Min(yMin, y);
                    xMax = Mathf.Max(xMax, x);
                    yMax = Mathf.Max(yMax, y);
                }
            }
        }

        if (!hasTile)
        {
            return false;
        }

        bounds = new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
        return true;
    }

    // - Role: Build request.
    private StageBakeRequest BuildRequest()
    {
        List<StageBakeLayerInput> layers = new List<StageBakeLayerInput>(tilemapEntries.Count);
        foreach (TilemapEntry entry in tilemapEntries)
        {
            layers.Add(new StageBakeLayerInput
            {
                tilemap = entry.tilemap,
                includeInBake = entry.includeInBake,
                priority = entry.priority,
                definition = entry.definition,
            });
        }

        return new StageBakeRequest
        {
            stageId = stageId,
            grid = grid,
            spawnPosTilemap = spawnPosTilemap,
            backgroundRoot = backgroundRoot,
            environmentRoot = environmentRoot,
            foregroundRoot = foregroundRoot,
            output = output,
            stageDefinition = stageDefinition,
            cellBounds = cellBounds,
            leftBoundary = leftBoundary,
            rightBoundary = rightBoundary,
            bottomBoundary = bottomBoundary,
            topBoundary = topBoundary,
            mergeRectColliders = mergeRectColliders,
            generateSpatialIndex = generateSpatialIndex,
            uniformGridSize = uniformGridSize,
            layers = layers,
        };
    }

    // - Role: Log report.
    private static void LogReport(string label, StageBakeReport report)
    {
        foreach (string error in report.errors)
        {
            Debug.LogError(error);
        }

        foreach (string warning in report.warnings)
        {
            Debug.LogWarning(warning);
        }

        if (report.HasErrors)
        {
            Debug.LogError($"{label} failed.");
            return;
        }

        string renderPrefabMessage = string.IsNullOrWhiteSpace(report.renderPrefabPath)
            ? string.Empty
            : $", render prefab: {report.renderPrefabPath}";
        Debug.Log(
            $"{label} complete. Baked cells: {report.bakedCellCount}, " +
            $"colliders: {report.colliderCount}, spatial buckets: {report.spatialBucketCount}, " +
            $"spawn points: {report.spawnPointCount}, " +
            $"scanned cells: {report.scannedCellCount}{renderPrefabMessage}.");
    }
}

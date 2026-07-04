using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(GridLayoutGroup))]
public sealed class StoryGridLayoutFitter : MonoBehaviour
{
    [SerializeField, Min(1)] private int columns = 1;
    [SerializeField] private int rows;
    [SerializeField] private Vector2 spacing = new(8f, 8f);
    [SerializeField] private RectOffset padding = new();
    [SerializeField] private TextAnchor childAlignment = TextAnchor.MiddleCenter;
    [SerializeField] private bool keepSquareCells;

    private RectTransform rectTransform;
    private GridLayoutGroup gridLayoutGroup;

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void OnRectTransformDimensionsChange()
    {
        Apply();
    }

    public void Configure(int columnCount, int rowCount, Vector2 gridSpacing, RectOffset gridPadding, bool squareCells)
    {
        columns = Mathf.Max(1, columnCount);
        rows = Mathf.Max(0, rowCount);
        spacing = gridSpacing;
        padding = ClonePadding(gridPadding);
        keepSquareCells = squareCells;
        Apply();
    }

    public void Apply()
    {
        EnsureReferences();

        if (rectTransform == null || gridLayoutGroup == null)
        {
            return;
        }

        int effectiveRows = rows > 0
            ? rows
            : Mathf.Max(1, Mathf.CeilToInt(transform.childCount / (float)Mathf.Max(1, columns)));

        Rect rect = rectTransform.rect;
        float availableWidth = Mathf.Max(0f, rect.width - padding.left - padding.right - spacing.x * (columns - 1));
        float availableHeight = Mathf.Max(0f, rect.height - padding.top - padding.bottom - spacing.y * (effectiveRows - 1));
        Vector2 cellSize = new(
            columns > 0 ? availableWidth / columns : 0f,
            effectiveRows > 0 ? availableHeight / effectiveRows : 0f);

        if (keepSquareCells)
        {
            float size = Mathf.Min(cellSize.x, cellSize.y);
            cellSize = new Vector2(size, size);
        }

        gridLayoutGroup.padding = ClonePadding(padding);
        gridLayoutGroup.spacing = spacing;
        gridLayoutGroup.childAlignment = childAlignment;
        gridLayoutGroup.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayoutGroup.constraintCount = columns;
        gridLayoutGroup.cellSize = cellSize;
    }

    private void EnsureReferences()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        if (gridLayoutGroup == null)
        {
            gridLayoutGroup = GetComponent<GridLayoutGroup>();
        }
    }

    private static RectOffset ClonePadding(RectOffset source)
    {
        return source != null
            ? new RectOffset(source.left, source.right, source.top, source.bottom)
            : new RectOffset();
    }
}

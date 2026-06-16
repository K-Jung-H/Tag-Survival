using System.Collections.Generic;
using UnityEngine;

public class OffScreenIndicatorView : MonoBehaviour
{
    private static readonly Color TaggerColor = new Color(1f, 80f / 255f, 80f / 255f, 1f);

    [SerializeField] private Client_WorldView worldView;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private string indicatorObjectName = "OffScreenIndicator";
    [SerializeField] private float viewportPadding = 0.06f;
    [SerializeField] private float rotationOffset = 0f;
    [SerializeField] private bool showInsideView = false;
    [SerializeField, Range(0.01f, 1f)] private float minimumScale = 0.3f;
    [SerializeField] private float distanceForMinimumScale = 20f;

    private struct IndicatorEntry
    {
        public ulong clientId;
        public Transform playerRoot;
        public SpriteRenderer indicator;
        public Color defaultColor;
        public Vector3 defaultScale;
        public float fixedZ;
    }

    private readonly Dictionary<ulong, IndicatorEntry> indicators = new();
    private readonly List<ClientWorldPlayerViewRef> existingPlayerViews = new();
    private readonly List<ulong> removeTargets = new();

    public int IndicatorCount => indicators.Count;

    // - Role: Set up needed links before start.
    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    // - Role: Turn on links when this object is enabled.
    private void OnEnable()
    {
        if (worldView == null)
            return;

        worldView.PlayerViewCreated += OnPlayerViewCreated;
        worldView.PlayerViewRemoved += OnPlayerViewRemoved;

        SyncExistingPlayerViews();
    }

    // - Role: Turn off links when this object is disabled.
    private void OnDisable()
    {
        if (worldView == null)
            return;

        worldView.PlayerViewCreated -= OnPlayerViewCreated;
        worldView.PlayerViewRemoved -= OnPlayerViewRemoved;
    }

    // - Role: Update this object after normal updates.
    private void LateUpdate()
    {
        if (!CanUpdateIndicators())
            return;

        UpdateIndicators();
        RemoveInvalidIndicators();
    }

    // - Role: Sync existing player views.
    private void SyncExistingPlayerViews()
    {
        worldView.CopyPlayerViewsTo(existingPlayerViews);

        for (int i = 0; i < existingPlayerViews.Count; i++)
        {
            OnPlayerViewCreated(existingPlayerViews[i]);
        }
    }

    // - Role: Handle player view created.
    private void OnPlayerViewCreated(ClientWorldPlayerViewRef viewRef)
    {
        if (viewRef.root == null)
            return;

        if (indicators.ContainsKey(viewRef.clientId))
            return;

        TryCreateIndicator(viewRef);
    }

    // - Role: Handle player view removed.
    private void OnPlayerViewRemoved(ulong clientId)
    {
        RemoveIndicator(clientId);
    }

    // - Role: Check if update indicators can happen.
    private bool CanUpdateIndicators()
    {
        if (worldView == null)
            return false;

        if (targetCamera == null)
            return false;

        return true;
    }

    // - Role: Try to create indicator.
    private void TryCreateIndicator(ClientWorldPlayerViewRef viewRef)
    {
        Transform sourceTransform = FindChildRecursive(viewRef.root, indicatorObjectName);

        if (sourceTransform == null)
        {
            Debug.LogWarning($"[OffScreenIndicatorView] Missing indicator source: {indicatorObjectName}");
            return;
        }

        SpriteRenderer sourceRenderer = sourceTransform.GetComponent<SpriteRenderer>();

        if (sourceRenderer == null)
        {
            Debug.LogWarning($"[OffScreenIndicatorView] Missing SpriteRenderer: {indicatorObjectName}");
            return;
        }

        sourceRenderer.enabled = false;

        SpriteRenderer indicator = Instantiate(sourceRenderer, transform);
        indicator.gameObject.name = $"OffScreenIndicator_{viewRef.clientId}";
        indicator.gameObject.SetActive(true);
        indicator.enabled = false;

        indicators.Add(viewRef.clientId, new IndicatorEntry
        {
            clientId = viewRef.clientId,
            playerRoot = viewRef.root,
            indicator = indicator,
            defaultColor = indicator.color,
            defaultScale = indicator.transform.localScale,
            fixedZ = indicator.transform.position.z
        });
    }

    // - Role: Update indicators.
    private void UpdateIndicators()
    {
        if (!TryGetLocalTaggerSnapshot(out ClientSnapshotState localSnapshot))
        {
            HideIndicators();
            return;
        }

        foreach (var pair in indicators)
        {
            IndicatorEntry entry = pair.Value;

            if (entry.playerRoot == null || entry.indicator == null)
                continue;

            UpdateIndicator(entry, localSnapshot);
        }
    }

    // - Role: Update indicator.
    private void UpdateIndicator(IndicatorEntry entry, ClientSnapshotState localSnapshot)
    {
        if (entry.clientId == worldView.LocalClientId)
        {
            entry.indicator.enabled = false;
            return;
        }

        Vector3 playerPosition = entry.playerRoot.position;
        Vector3 viewportPosition = targetCamera.WorldToViewportPoint(playerPosition);
        bool isInsideView = IsInsideViewport(viewportPosition);

        if (isInsideView && !showInsideView)
        {
            entry.indicator.enabled = false;
            return;
        }

        Vector3 indicatorPosition = GetClampedWorldPosition(viewportPosition, entry.fixedZ);
        Transform indicatorTransform = entry.indicator.transform;

        indicatorTransform.position = indicatorPosition;
        RotateToTarget(indicatorTransform, indicatorPosition, playerPosition);

        entry.indicator.color = GetIndicatorColor(entry.clientId, entry.defaultColor);
        indicatorTransform.localScale = entry.defaultScale * ResolveDistanceScale(localSnapshot.position, playerPosition);
        entry.indicator.enabled = true;
    }

    // - Role: Try to get local tagger snapshot.
    private bool TryGetLocalTaggerSnapshot(out ClientSnapshotState localSnapshot)
    {
        localSnapshot = default;

        return worldView != null
            && worldView.TryGetLocalPlayerSnapshot(out localSnapshot)
            && localSnapshot.isTagger;
    }

    // - Role: Hide all indicators.
    private void HideIndicators()
    {
        foreach (var pair in indicators)
        {
            SpriteRenderer indicator = pair.Value.indicator;
            if (indicator != null)
            {
                indicator.enabled = false;
            }
        }
    }

    // - Role: Resolve scale by distance from local player.
    private float ResolveDistanceScale(Vector2 localPosition, Vector3 targetPosition)
    {
        float safeDistanceForMinimumScale = Mathf.Max(0.0001f, distanceForMinimumScale);
        float distance = Vector2.Distance(localPosition, targetPosition);
        float t = Mathf.Clamp01(distance / safeDistanceForMinimumScale);
        return Mathf.Lerp(1f, Mathf.Clamp(minimumScale, 0.01f, 1f), t);
    }

    // - Role: Get indicator color.
    private Color GetIndicatorColor(ulong clientId, Color defaultColor)
    {
        if (worldView != null
            && worldView.TryGetPlayerSnapshot(clientId, out ClientSnapshotState snapshotState)
            && snapshotState.isTagger)
        {
            return TaggerColor;
        }

        return defaultColor;
    }

    // - Role: Check if inside viewport is true.
    private bool IsInsideViewport(Vector3 viewportPosition)
    {
        if (viewportPosition.z <= 0f)
            return false;

        if (viewportPosition.x < 0f || viewportPosition.x > 1f)
            return false;

        if (viewportPosition.y < 0f || viewportPosition.y > 1f)
            return false;

        return true;
    }

    // - Role: Get clamped world position.
    private Vector3 GetClampedWorldPosition(Vector3 viewportPosition, float fixedZ)
    {
        if (viewportPosition.z < 0f)
        {
            viewportPosition.x = 1f - viewportPosition.x;
            viewportPosition.y = 1f - viewportPosition.y;
            viewportPosition.z = Mathf.Abs(viewportPosition.z);
        }

        viewportPosition.x = Mathf.Clamp(viewportPosition.x, viewportPadding, 1f - viewportPadding);

        viewportPosition.y = Mathf.Clamp(viewportPosition.y, viewportPadding, 1f - viewportPadding);

        Vector3 worldPosition = targetCamera.ViewportToWorldPoint(viewportPosition);
        worldPosition.z = fixedZ;

        return worldPosition;
    }

    // - Role: Rotate toward the target.
    private void RotateToTarget(
        Transform indicatorTransform,
        Vector3 fromPosition,
        Vector3 targetPosition
    )
    {
        Vector2 direction = targetPosition - fromPosition;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        indicatorTransform.rotation = Quaternion.Euler(0f, 0f, angle + rotationOffset);
    }

    // - Role: Remove invalid indicators.
    private void RemoveInvalidIndicators()
    {
        removeTargets.Clear();

        foreach (var pair in indicators)
        {
            if (pair.Value.playerRoot == null || pair.Value.indicator == null)
            {
                removeTargets.Add(pair.Key);
            }
        }

        for (int i = 0; i < removeTargets.Count; i++)
        {
            RemoveIndicator(removeTargets[i]);
        }
    }

    // - Role: Remove indicator.
    private void RemoveIndicator(ulong clientId)
    {
        if (!indicators.TryGetValue(clientId, out IndicatorEntry entry))
            return;

        if (entry.indicator != null)
        {
            Destroy(entry.indicator.gameObject);
        }

        indicators.Remove(clientId);
    }

    // - Role: Find child recursive.
    private Transform FindChildRecursive(Transform root, string objectName)
    {
        if (root.name == objectName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindChildRecursive(root.GetChild(i), objectName);

            if (result != null)
                return result;
        }

        return null;
    }
}

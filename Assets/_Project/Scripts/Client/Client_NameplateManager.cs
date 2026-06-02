using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class Client_NameplateManager : MonoBehaviour
{
    private struct NameplateEntry
    {
        public ulong clientId;
        public Transform anchor;
        public TMP_Text label;
        public RectTransform labelRect;
    }

    [SerializeField] private Client_WorldView worldView;
    [SerializeField] private Client_RosterReceiver rosterReceiver;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private RectTransform labelLayer;
    [SerializeField] private TMP_Text labelPrefab;
    [SerializeField] private Vector2 screenOffset = Vector2.zero;
    [SerializeField] private Color defaultTextColor = Color.white;
    [SerializeField] private Color taggerTextColor = new Color(1f, 80f / 255f, 80f / 255f, 1f);
    [SerializeField] private bool hideWhenBehindCamera = true;

    private readonly Dictionary<ulong, NameplateEntry> nameplates = new();
    private readonly List<ClientWorldPlayerViewRef> existingPlayerViews = new();
    private readonly List<ulong> removeTargets = new();
    private bool hasLoggedMissingReferences;

    private void Awake()
    {
        if (worldCamera == null)
        {
            worldCamera = Camera.main;
        }

        if (labelLayer == null)
        {
            labelLayer = transform as RectTransform;
        }

        if (targetCanvas == null && labelLayer != null)
        {
            targetCanvas = labelLayer.GetComponentInParent<Canvas>();
        }

        LogMissingReferencesOnce();
    }

    private void OnEnable()
    {
        if (worldView != null)
        {
            worldView.PlayerViewCreated += OnPlayerViewCreated;
            worldView.PlayerViewRemoved += OnPlayerViewRemoved;
            SyncExistingPlayerViews();
        }

        if (rosterReceiver != null)
        {
            rosterReceiver.RosterUpdated += RefreshAllLabelTexts;
        }
    }

    private void OnDisable()
    {
        if (worldView != null)
        {
            worldView.PlayerViewCreated -= OnPlayerViewCreated;
            worldView.PlayerViewRemoved -= OnPlayerViewRemoved;
        }

        if (rosterReceiver != null)
        {
            rosterReceiver.RosterUpdated -= RefreshAllLabelTexts;
        }
    }

    private void LateUpdate()
    {
        if (!CanUpdateNameplates())
            return;

        UpdateNameplates();
        RemoveInvalidNameplates();
    }

    private void SyncExistingPlayerViews()
    {
        if (worldView == null)
            return;

        worldView.CopyPlayerViewsTo(existingPlayerViews);
        for (int i = 0; i < existingPlayerViews.Count; i++)
        {
            OnPlayerViewCreated(existingPlayerViews[i]);
        }
    }

    private void OnPlayerViewCreated(ClientWorldPlayerViewRef viewRef)
    {
        if (viewRef.root == null)
            return;

        if (nameplates.ContainsKey(viewRef.clientId))
            return;

        TryCreateNameplate(viewRef);
    }

    private void OnPlayerViewRemoved(ulong clientId)
    {
        RemoveNameplate(clientId);
    }

    private bool CanUpdateNameplates()
    {
        if (worldCamera == null)
            return false;

        if (labelLayer == null)
            return false;

        return true;
    }

    private void TryCreateNameplate(ClientWorldPlayerViewRef viewRef)
    {
        if (labelPrefab == null || labelLayer == null)
        {
            LogMissingReferencesOnce();
            return;
        }

        TMP_Text label = Instantiate(labelPrefab, labelLayer);
        label.gameObject.name = $"Nameplate_{viewRef.clientId}";
        label.gameObject.SetActive(true);
        label.color = defaultTextColor;
        label.text = ResolveNickname(viewRef.clientId);

        RectTransform labelRect = label.transform as RectTransform;
        if (labelRect == null)
        {
            Debug.LogWarning("[Client_NameplateManager] Label prefab must use RectTransform.", this);
            Destroy(label.gameObject);
            return;
        }

        nameplates.Add(viewRef.clientId, new NameplateEntry
        {
            clientId = viewRef.clientId,
            anchor = viewRef.nameplateAnchor != null ? viewRef.nameplateAnchor : viewRef.root,
            label = label,
            labelRect = labelRect
        });
    }

    private void UpdateNameplates()
    {
        foreach (var pair in nameplates)
        {
            NameplateEntry entry = pair.Value;
            if (entry.anchor == null || entry.label == null || entry.labelRect == null)
            {
                continue;
            }

            UpdateNameplate(entry);
        }
    }

    private void UpdateNameplate(NameplateEntry entry)
    {
        Vector3 screenPosition = worldCamera.WorldToScreenPoint(entry.anchor.position);
        bool isBehindCamera = screenPosition.z <= 0f;

        if (hideWhenBehindCamera && isBehindCamera)
        {
            entry.label.gameObject.SetActive(false);
            return;
        }

        if (!entry.label.gameObject.activeSelf)
        {
            entry.label.gameObject.SetActive(true);
        }

        entry.label.text = ResolveNickname(entry.clientId);
        entry.label.color = ResolveNameplateColor(entry.clientId);
        screenPosition.x += screenOffset.x;
        screenPosition.y += screenOffset.y;

        if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            entry.labelRect.position = screenPosition;
            return;
        }

        Camera uiCamera = ResolveUiCamera();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            labelLayer,
            screenPosition,
            uiCamera,
            out Vector2 localPoint))
        {
            entry.labelRect.anchoredPosition = localPoint;
        }
    }

    private Camera ResolveUiCamera()
    {
        if (targetCanvas == null)
        {
            return null;
        }

        if (targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return targetCanvas.worldCamera != null ? targetCanvas.worldCamera : worldCamera;
    }

    private string ResolveNickname(ulong clientId)
    {
        if (rosterReceiver != null && rosterReceiver.TryGetNickname(clientId, out string nickname))
        {
            return nickname;
        }

        return $"Client {clientId}";
    }

    private Color ResolveNameplateColor(ulong clientId)
    {
        if (worldView != null
            && worldView.TryGetPlayerSnapshot(clientId, out ClientSnapshotState snapshotState)
            && snapshotState.isTagger)
        {
            return taggerTextColor;
        }

        return defaultTextColor;
    }

    private void RefreshAllLabelTexts()
    {
        foreach (var pair in nameplates)
        {
            NameplateEntry entry = pair.Value;
            if (entry.label == null)
            {
                continue;
            }

            entry.label.text = ResolveNickname(entry.clientId);
            entry.label.color = ResolveNameplateColor(entry.clientId);
        }
    }

    private void RemoveInvalidNameplates()
    {
        removeTargets.Clear();

        foreach (var pair in nameplates)
        {
            if (pair.Value.anchor == null || pair.Value.label == null)
            {
                removeTargets.Add(pair.Key);
            }
        }

        for (int i = 0; i < removeTargets.Count; i++)
        {
            RemoveNameplate(removeTargets[i]);
        }
    }

    private void RemoveNameplate(ulong clientId)
    {
        if (!nameplates.TryGetValue(clientId, out NameplateEntry entry))
            return;

        if (entry.label != null)
        {
            Destroy(entry.label.gameObject);
        }

        nameplates.Remove(clientId);
    }

    private void LogMissingReferencesOnce()
    {
        if (hasLoggedMissingReferences)
            return;

        hasLoggedMissingReferences = true;

        if (worldView == null)
        {
            Debug.LogWarning("[Client_NameplateManager] WorldView is not assigned.", this);
        }

        if (rosterReceiver == null)
        {
            Debug.LogWarning("[Client_NameplateManager] RosterReceiver is not assigned.", this);
        }

        if (worldCamera == null)
        {
            Debug.LogWarning("[Client_NameplateManager] WorldCamera is not assigned.", this);
        }

        if (labelLayer == null)
        {
            Debug.LogWarning("[Client_NameplateManager] LabelLayer is not assigned.", this);
        }

        if (labelPrefab == null)
        {
            Debug.LogWarning("[Client_NameplateManager] LabelPrefab is not assigned.", this);
        }
    }
}

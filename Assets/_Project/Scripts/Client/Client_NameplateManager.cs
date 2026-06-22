using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class Client_NameplateManager : MonoBehaviour
{
    private struct NameplateEntry
    {
        public ulong clientId;
        public Transform anchor;
        public TextMeshPro label;
    }

    [SerializeField] private Client_WorldView worldView;
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private Material fontMaterial;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1f, 0f);
    [SerializeField] private float fontSize = 2f;
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder;
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private Color defaultTextColor = Color.white;
    [SerializeField] private Color taggerTextColor = new Color(1f, 80f / 255f, 80f / 255f, 1f);
    [SerializeField] private bool hideWhenBehindCamera = true;

    private readonly Dictionary<ulong, NameplateEntry> nameplates = new();
    private readonly List<ClientWorldPlayerViewRef> existingPlayerViews = new();
    private readonly List<ulong> removeTargets = new();
    private Transform runtimeRoot;
    private bool hasLoggedMissingReferences;

    // - Role: Set up needed links before start.
    private void Awake()
    {
        if (worldCamera == null)
        {
            worldCamera = Camera.main;
        }

        LogMissingReferencesOnce();
    }

    // - Role: Turn on links when this object is enabled.
    private void OnEnable()
    {
        if (worldView != null)
        {
            worldView.PlayerViewCreated += OnPlayerViewCreated;
            worldView.PlayerViewRemoved += OnPlayerViewRemoved;
            SyncExistingPlayerViews();
        }

        if (syncManager != null)
        {
            syncManager.RosterUpdated += RefreshAllLabelTexts;
        }
    }

    // - Role: Turn off links when this object is disabled.
    private void OnDisable()
    {
        if (worldView != null)
        {
            worldView.PlayerViewCreated -= OnPlayerViewCreated;
            worldView.PlayerViewRemoved -= OnPlayerViewRemoved;
        }

        if (syncManager != null)
        {
            syncManager.RosterUpdated -= RefreshAllLabelTexts;
        }

        ClearNameplates();
    }

    // - Role: Clean up runtime world labels.
    private void OnDestroy()
    {
        ClearNameplates();
        if (runtimeRoot != null)
        {
            Destroy(runtimeRoot.gameObject);
            runtimeRoot = null;
        }
    }

    // - Role: Update this object after normal updates.
    private void LateUpdate()
    {
        if (!CanUpdateNameplates())
            return;

        UpdateNameplates();
        RemoveInvalidNameplates();
    }

    // - Role: Sync existing player views.
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

    // - Role: Handle player view created.
    private void OnPlayerViewCreated(ClientWorldPlayerViewRef viewRef)
    {
        if (viewRef.root == null)
            return;

        if (nameplates.ContainsKey(viewRef.clientId))
            return;

        TryCreateNameplate(viewRef);
    }

    // - Role: Handle player view removed.
    private void OnPlayerViewRemoved(ulong clientId)
    {
        RemoveNameplate(clientId);
    }

    // - Role: Check if update nameplates can happen.
    private bool CanUpdateNameplates()
    {
        if (worldCamera == null)
            return false;

        if (fontAsset == null)
            return false;

        return true;
    }

    // - Role: Try to create nameplate.
    private void TryCreateNameplate(ClientWorldPlayerViewRef viewRef)
    {
        if (fontAsset == null)
        {
            LogMissingReferencesOnce();
            return;
        }

        TextMeshPro label = CreateWorldLabel(viewRef.clientId);

        nameplates.Add(viewRef.clientId, new NameplateEntry
        {
            clientId = viewRef.clientId,
            anchor = viewRef.nameplateAnchor != null ? viewRef.nameplateAnchor : viewRef.root,
            label = label
        });
    }

    // - Role: Create one world-space TMP label.
    private TextMeshPro CreateWorldLabel(ulong clientId)
    {
        EnsureRuntimeRoot();

        GameObject labelObject = new GameObject($"Nameplate_{clientId}");
        labelObject.transform.SetParent(runtimeRoot, false);

        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
        label.font = fontAsset;
        if (fontMaterial != null)
        {
            label.fontSharedMaterial = fontMaterial;
        }

        label.text = ResolveNickname(clientId);
        label.color = defaultTextColor;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;

        RectTransform labelRect = label.rectTransform;
        if (labelRect != null)
        {
            labelRect.sizeDelta = new Vector2(4f, 1f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
        }

        Renderer labelRenderer = label.GetComponent<Renderer>();
        if (labelRenderer != null)
        {
            if (!string.IsNullOrWhiteSpace(sortingLayerName))
            {
                labelRenderer.sortingLayerName = sortingLayerName;
            }

            labelRenderer.sortingOrder = sortingOrder;
        }

        return label;
    }

    // - Role: Create a scene root that is not under Canvas.
    private void EnsureRuntimeRoot()
    {
        if (runtimeRoot != null)
        {
            return;
        }

        GameObject rootObject = new GameObject("Runtime_Nameplates");
        runtimeRoot = rootObject.transform;
    }

    // - Role: Update nameplates.
    private void UpdateNameplates()
    {
        foreach (var pair in nameplates)
        {
            NameplateEntry entry = pair.Value;
            if (entry.anchor == null || entry.label == null)
            {
                continue;
            }

            UpdateNameplate(entry);
        }
    }

    // - Role: Update nameplate.
    private void UpdateNameplate(NameplateEntry entry)
    {
        if (ShouldHideForStealth(entry.clientId))
        {
            entry.label.gameObject.SetActive(false);
            return;
        }

        Vector3 labelPosition = entry.anchor.position + worldOffset;
        Vector3 viewportPosition = worldCamera.WorldToViewportPoint(labelPosition);
        bool isBehindCamera = viewportPosition.z <= 0f;

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
        entry.label.transform.position = labelPosition;
        if (faceCamera)
        {
            entry.label.transform.rotation = worldCamera.transform.rotation;
        }
    }

    // - Role: Find nickname.
    private string ResolveNickname(ulong clientId)
    {
        if (syncManager != null && syncManager.TryGetNickname(clientId, out string nickname))
        {
            return nickname;
        }

        return $"Client {clientId}";
    }

    // - Role: Find nameplate color.
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

    // - Role: Check if nameplate should hide for remote stealth.
    private bool ShouldHideForStealth(ulong clientId)
    {
        if (syncManager != null && clientId == syncManager.LocalClientId)
        {
            return false;
        }

        return worldView != null
            && worldView.TryGetPlayerSnapshot(clientId, out ClientSnapshotState snapshotState)
            && snapshotState.isStealthed;
    }

    // - Role: Refresh all label text.
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

    // - Role: Remove invalid nameplates.
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

    // - Role: Remove nameplate.
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

    // - Role: Remove all runtime labels.
    private void ClearNameplates()
    {
        foreach (var pair in nameplates)
        {
            if (pair.Value.label != null)
            {
                Destroy(pair.Value.label.gameObject);
            }
        }

        nameplates.Clear();
        removeTargets.Clear();
    }

    // - Role: Log missing references once.
    private void LogMissingReferencesOnce()
    {
        if (hasLoggedMissingReferences)
            return;

        hasLoggedMissingReferences = true;

        if (worldView == null)
        {
            Debug.LogWarning("[Client_NameplateManager] WorldView is not assigned.", this);
        }

        if (syncManager == null)
        {
            Debug.LogWarning("[Client_NameplateManager] SyncManager is not assigned.", this);
        }

        if (worldCamera == null)
        {
            Debug.LogWarning("[Client_NameplateManager] WorldCamera is not assigned.", this);
        }

        if (fontAsset == null)
        {
            Debug.LogWarning("[Client_NameplateManager] FontAsset is not assigned.", this);
        }
    }
}

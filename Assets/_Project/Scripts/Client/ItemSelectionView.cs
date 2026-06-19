using System.Collections.Generic;
using UnityEngine;

public sealed class ItemSelectionView : MonoBehaviour
{
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private ItemEffectCatalog itemEffectCatalog;
    [SerializeField] private ItemSelectionPanel selectionPanel;
    [SerializeField] private float resultCloseDelay = 0.65f;

    private uint currentRequestId;
    private float timeoutSeconds;
    private float remainingSeconds;
    private float closeTimer;
    private bool isOpen;
    private bool isWaitingResult;
    private bool hasResult;
    private bool hasLoggedMissingCatalog;
    private bool hasLoggedMissingPanel;
    private readonly int[] candidateIds = new int[3];
    private readonly Queue<ItemSelectionOfferPacket> queuedOffers = new Queue<ItemSelectionOfferPacket>();

    // - Role: Set up view before start.
    private void Awake()
    {
        SetPanelVisible(false);
    }

    // - Role: Subscribe to receiver.
    private void OnEnable()
    {
        if (syncManager == null)
        {
            Debug.LogWarning("[ItemSelectionView] SyncManager is not assigned.", this);
            return;
        }

        syncManager.ItemSelectionOfferReceived += OnOfferReceived;
        syncManager.ItemSelectionResultReceived += OnResultReceived;
    }

    // - Role: Unsubscribe from receiver.
    private void OnDisable()
    {
        queuedOffers.Clear();
        if (syncManager != null)
        {
            syncManager.ItemSelectionOfferReceived -= OnOfferReceived;
            syncManager.ItemSelectionResultReceived -= OnResultReceived;
        }
    }

    // - Role: Update timer.
    private void Update()
    {
        if (!isOpen)
        {
            return;
        }

        if (hasResult)
        {
            closeTimer -= Time.unscaledDeltaTime;
            if (closeTimer <= 0f)
            {
                Close();
            }

            return;
        }

        remainingSeconds = Mathf.Max(0f, remainingSeconds - Time.unscaledDeltaTime);
        UpdateTimer();
        if (remainingSeconds <= 0f && !isWaitingResult)
        {
            isWaitingResult = true;
            SetButtonsInteractable(false);
        }
    }

    // - Role: Handle offer received.
    private void OnOfferReceived(ItemSelectionOfferPacket packet)
    {
        if (isOpen)
        {
            queuedOffers.Enqueue(packet);
            return;
        }

        if (!HasPanel())
        {
            return;
        }

        currentRequestId = packet.requestId;
        timeoutSeconds = Mathf.Max(0.1f, packet.timeoutSeconds);
        remainingSeconds = timeoutSeconds;
        closeTimer = 0f;
        isOpen = true;
        isWaitingResult = false;
        hasResult = false;
        ClearCandidateIds();

        selectionPanel.Open(packet.itemType);
        SetupOption(0, packet.candidateId0);
        SetupOption(1, packet.candidateId1);
        SetupOption(2, packet.candidateId2);
        UpdateTimer();
    }

    // - Role: Handle result received.
    private void OnResultReceived(ItemSelectionResultPacket packet)
    {
        if (!isOpen || packet.requestId != currentRequestId || !HasPanel())
        {
            return;
        }

        hasResult = true;
        isWaitingResult = true;
        closeTimer = resultCloseDelay;
        remainingSeconds = 0f;
        UpdateTimer();
        SetButtonsInteractable(false);
        selectionPanel.MarkSelected(packet.success ? FindCandidateIndex(packet.selectedId) : -1);
    }

    // - Role: Setup option view.
    private void SetupOption(int index, int itemDataId)
    {
        SetCandidateId(index, itemDataId);
        if (itemDataId <= 0 || itemEffectCatalog == null)
        {
            WarnMissingCatalogOrData(itemDataId);
            selectionPanel.SetMissingOption(index);
            return;
        }

        if (!itemEffectCatalog.TryGetById(itemDataId, out ItemData itemData))
        {
            WarnMissingCatalogOrData(itemDataId);
            selectionPanel.SetMissingOption(index);
            return;
        }

        selectionPanel.SetOption(index, itemData, () => Select(itemDataId));
    }

    // - Role: Select item id.
    private void Select(int selectedId)
    {
        if (!isOpen || isWaitingResult)
        {
            return;
        }

        isWaitingResult = true;
        SetButtonsInteractable(false);
        if (syncManager != null)
        {
            syncManager.SendItemSelectionChoice(currentRequestId, selectedId);
        }
    }

    // - Role: Set buttons interactable.
    private void SetButtonsInteractable(bool interactable)
    {
        if (selectionPanel != null)
        {
            selectionPanel.SetButtonsInteractable(interactable);
        }
    }

    // - Role: Update timer view.
    private void UpdateTimer()
    {
        if (selectionPanel != null)
        {
            selectionPanel.SetTimer(timeoutSeconds > 0f ? Mathf.Clamp01(remainingSeconds / timeoutSeconds) : 0f);
        }
    }

    // - Role: Close view.
    private void Close()
    {
        isOpen = false;
        isWaitingResult = false;
        hasResult = false;
        currentRequestId = 0;
        ClearCandidateIds();
        if (queuedOffers.Count > 0)
        {
            SetPanelVisible(false);
            OnOfferReceived(queuedOffers.Dequeue());
            return;
        }

        SetPanelVisible(false);
    }

    // - Role: Set candidate id.
    private void SetCandidateId(int index, int itemDataId)
    {
        if (index >= 0 && index < candidateIds.Length)
        {
            candidateIds[index] = itemDataId;
        }
    }

    // - Role: Clear candidate ids.
    private void ClearCandidateIds()
    {
        for (int i = 0; i < candidateIds.Length; i++)
        {
            candidateIds[i] = 0;
        }
    }

    // - Role: Find candidate index.
    private int FindCandidateIndex(int itemDataId)
    {
        for (int i = 0; i < candidateIds.Length; i++)
        {
            if (candidateIds[i] == itemDataId)
            {
                return i;
            }
        }

        return -1;
    }

    // - Role: Set panel visible.
    private void SetPanelVisible(bool active)
    {
        if (selectionPanel != null)
        {
            selectionPanel.SetVisible(active);
        }
    }

    // - Role: Check panel is ready.
    private bool HasPanel()
    {
        if (selectionPanel != null)
        {
            return true;
        }

        if (!hasLoggedMissingPanel)
        {
            hasLoggedMissingPanel = true;
            Debug.LogWarning("[ItemSelectionView] ItemSelectionPanel is not assigned.", this);
        }

        return false;
    }

    // - Role: Warn missing data.
    private void WarnMissingCatalogOrData(int itemDataId)
    {
        if (itemEffectCatalog == null)
        {
            if (!hasLoggedMissingCatalog)
            {
                hasLoggedMissingCatalog = true;
                Debug.LogWarning("[ItemSelectionView] ItemEffectCatalog is not assigned.", this);
            }

            return;
        }

        if (itemDataId > 0)
        {
            Debug.LogWarning($"[ItemSelectionView] ItemData id {itemDataId} is not found.", this);
        }
    }
}

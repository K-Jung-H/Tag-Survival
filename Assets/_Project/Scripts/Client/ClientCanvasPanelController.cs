using UnityEngine;

public enum ClientStageUiMode
{
    LocalHost,
    OnlineGuest
}

public sealed class ClientCanvasPanelController : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private GameObject hudPanel;
    [SerializeField] private GameObject nameplatePanel;
    [SerializeField] private GameObject mobileInputPanel;

    [Header("Online")]
    [SerializeField] private GameObject connectionPanel;
    [SerializeField] private GameObject networkDelayPanel;

    [Header("Overlay")]
    [SerializeField] private GameObject itemSelectionRoot;

    public bool HasRequiredReferences(out string missingReferenceName)
    {
        if (hudPanel == null)
        {
            missingReferenceName = nameof(hudPanel);
            return false;
        }

        if (nameplatePanel == null)
        {
            missingReferenceName = nameof(nameplatePanel);
            return false;
        }

        if (mobileInputPanel == null)
        {
            missingReferenceName = nameof(mobileInputPanel);
            return false;
        }

        if (connectionPanel == null)
        {
            missingReferenceName = nameof(connectionPanel);
            return false;
        }

        if (networkDelayPanel == null)
        {
            missingReferenceName = nameof(networkDelayPanel);
            return false;
        }

        if (itemSelectionRoot == null)
        {
            missingReferenceName = nameof(itemSelectionRoot);
            return false;
        }

        missingReferenceName = string.Empty;
        return true;
    }

    public void ApplyMode(ClientStageUiMode mode)
    {
        switch (mode)
        {
            case ClientStageUiMode.LocalHost:
                ApplyLocalHost();
                break;
            case ClientStageUiMode.OnlineGuest:
                ApplyOnlineGuest();
                break;
        }
    }

    public void ApplyOnlineConnectionState(bool connected)
    {
        SetActive(connectionPanel, !connected);
        SetActive(networkDelayPanel, connected);
        SetActive(hudPanel, connected);
        SetActive(nameplatePanel, true);
        SetActive(mobileInputPanel, true);
    }

    private void ApplyLocalHost()
    {
        SetActive(hudPanel, true);
        SetActive(nameplatePanel, true);
        SetActive(mobileInputPanel, true);
        SetActive(connectionPanel, false);
        SetActive(networkDelayPanel, false);
        SetActive(itemSelectionRoot, true);
    }

    private void ApplyOnlineGuest()
    {
        SetActive(hudPanel, false);
        SetActive(nameplatePanel, true);
        SetActive(mobileInputPanel, true);
        SetActive(connectionPanel, true);
        SetActive(networkDelayPanel, false);
        SetActive(itemSelectionRoot, true);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null)
        {
            target.SetActive(active);
        }
    }
}

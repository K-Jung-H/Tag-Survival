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

    [Header("Overlay")]
    [SerializeField] private GameObject itemSelectionRoot;
    [SerializeField] private GameObject gameResultPanel;

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

    private void ApplyLocalHost()
    {
        SetActive(hudPanel, true);
        SetActive(nameplatePanel, true);
        SetActive(mobileInputPanel, true);
        SetActive(itemSelectionRoot, true);
        SetActive(gameResultPanel, false);
    }

    private void ApplyOnlineGuest()
    {
        SetActive(hudPanel, true);
        SetActive(nameplatePanel, true);
        SetActive(mobileInputPanel, true);
        SetActive(itemSelectionRoot, true);
        SetActive(gameResultPanel, false);
    }

    public void SetGameResultVisible(bool visible)
    {
        SetActive(gameResultPanel, visible);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null)
        {
            target.SetActive(active);
        }
    }
}

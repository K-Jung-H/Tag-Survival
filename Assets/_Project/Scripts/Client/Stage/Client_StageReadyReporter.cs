using UnityEngine;

[DefaultExecutionOrder(300)]
public sealed class Client_StageReadyReporter : MonoBehaviour
{
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private Client_WorldView worldView;

    private bool hasSentReady;

    public void ResetReadyState()
    {
        hasSentReady = false;
    }

    private void OnEnable()
    {
        ResetReadyState();
    }

    private void LateUpdate()
    {
        if (hasSentReady || syncManager == null || worldView == null)
        {
            return;
        }

        int expectedPlayerViewCount = syncManager.Snapshots.Count;
        if (expectedPlayerViewCount <= 0 || worldView.PlayerViewCount < expectedPlayerViewCount)
        {
            return;
        }

        syncManager.SendStageReady();
        hasSentReady = true;
    }
}

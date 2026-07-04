using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(350)]
public sealed class Client_StageIntroDirector : MonoBehaviour
{
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private Client_WorldView worldView;
    [SerializeField] private Client_CameraController cameraController;
    [SerializeField] private Client_StageCountdownView countdownView;

    [Header("Camera")]
    [SerializeField] private float introZoomSize = 4f;
    [SerializeField] private float zoomSeconds = 0.35f;
    [SerializeField] private float cameraMoveSeconds = 1f;

    [Header("Spawn")]
    [SerializeField] private float postSpawnHoldSeconds = 0.15f;

    [Header("Safety")]
    [SerializeField] private float viewReadyTimeoutSeconds = 5f;

    private Coroutine flowRoutine;

    public bool HasRequiredReferences(out string missingReferenceName)
    {
        if (syncManager == null)
        {
            missingReferenceName = nameof(syncManager);
            return false;
        }

        if (worldView == null)
        {
            missingReferenceName = nameof(worldView);
            return false;
        }

        if (cameraController == null)
        {
            missingReferenceName = nameof(cameraController);
            return false;
        }

        if (countdownView == null)
        {
            missingReferenceName = nameof(countdownView);
            return false;
        }

        if (!countdownView.HasRequiredReferences(out string missingCountdownReference))
        {
            missingReferenceName = $"{nameof(countdownView)}.{missingCountdownReference}";
            return false;
        }

        missingReferenceName = string.Empty;
        return true;
    }

    private void OnEnable()
    {
        if (syncManager != null)
        {
            syncManager.StageFlowCommandReceived += OnStageFlowCommandReceived;
        }

        countdownView?.Clear();
    }

    private void OnDisable()
    {
        if (syncManager != null)
        {
            syncManager.StageFlowCommandReceived -= OnStageFlowCommandReceived;
        }

        StopFlowRoutine();
    }

    private void OnStageFlowCommandReceived(ServerStageFlowCommandPacket packet)
    {
        switch (packet.commandType)
        {
            case StageFlowCommandType.IntroStart:
                StartFlowRoutine(PlayIntroRoutine());
                break;
            case StageFlowCommandType.CountdownStart:
                StartFlowRoutine(PlayCountdownRoutine(packet));
                break;
            case StageFlowCommandType.GameStart:
                StartFlowRoutine(FinishStageIntroRoutine());
                break;
        }
    }

    private IEnumerator PlayIntroRoutine()
    {
        if (!HasRequiredReferences(out string missingReference))
        {
            Debug.LogError($"[Client_StageIntroDirector] Missing reference: {missingReference}.", this);
            syncManager?.SendStageIntroReady();
            yield break;
        }

        yield return WaitForPlayerViewsReady();

        if (!TryResolveIntroTargets(
            out ClientSnapshotState taggerSnapshot,
            out ClientSnapshotState localSnapshot,
            out Client_CharacterView taggerView,
            out Client_CharacterView localView))
        {
            Debug.LogWarning("[Client_StageIntroDirector] Intro target is not ready. Sending IntroReady.", this);
            syncManager.SendStageIntroReady();
            yield break;
        }

        bool isLocalTagger = taggerSnapshot.clientId == localSnapshot.clientId;
        cameraController.SetFollowEnabled(false);
        cameraController.StopManualMotion();
        countdownView.Clear();

        worldView.SetAllPlayerRenderVisible(false);
        taggerView.SetRenderVisible(true);

        cameraController.SnapTo(taggerSnapshot.position);
        cameraController.SetZoom(introZoomSize);
        float taggerSpawnSeconds = taggerView.PlaySpawnAnimation();
        yield return new WaitForSeconds(taggerSpawnSeconds + Mathf.Max(0f, postSpawnHoldSeconds));

        if (!isLocalTagger)
        {
            cameraController.LerpTo(localSnapshot.position, cameraMoveSeconds);
            yield return new WaitForSeconds(Mathf.Max(0f, cameraMoveSeconds));
        }

        worldView.SetAllPlayerRenderVisible(true);
        float localSpawnSeconds = isLocalTagger
            ? 0f
            : localView.PlaySpawnAnimation();
        cameraController.LerpToGameplayZoom(zoomSeconds);
        yield return new WaitForSeconds(Mathf.Max(localSpawnSeconds, zoomSeconds) + Mathf.Max(0f, postSpawnHoldSeconds));

        cameraController.SetFollowEnabled(true);
        syncManager.SendStageIntroReady();
        flowRoutine = null;
    }

    private IEnumerator PlayCountdownRoutine(ServerStageFlowCommandPacket packet)
    {
        if (countdownView == null)
        {
            flowRoutine = null;
            yield break;
        }

        float totalSeconds = Mathf.Max(0f, packet.countdownSeconds);
        float remaining = Mathf.Max(0f, totalSeconds - Mathf.Max(0f, packet.elapsedSeconds));

        while (remaining > 0f)
        {
            countdownView.ShowMessage(Mathf.CeilToInt(remaining).ToString());
            yield return null;
            remaining -= Time.deltaTime;
        }

        countdownView.ShowMessage("Start");
        yield return new WaitForSeconds(0.5f);
        countdownView.Clear();
        flowRoutine = null;
    }

    private IEnumerator WaitForPlayerViewsReady()
    {
        float startTime = Time.realtimeSinceStartup;
        while (!ArePlayerViewsReady())
        {
            if (Time.realtimeSinceStartup - startTime >= Mathf.Max(0f, viewReadyTimeoutSeconds))
            {
                yield break;
            }

            yield return null;
        }
    }

    private bool ArePlayerViewsReady()
    {
        return syncManager != null
            && worldView != null
            && syncManager.Snapshots.Count > 0
            && worldView.PlayerViewCount >= syncManager.Snapshots.Count;
    }

    private bool TryResolveIntroTargets(
        out ClientSnapshotState taggerSnapshot,
        out ClientSnapshotState localSnapshot,
        out Client_CharacterView taggerView,
        out Client_CharacterView localView)
    {
        taggerSnapshot = default;
        localSnapshot = default;
        taggerView = null;
        localView = null;

        bool hasTagger = false;
        ulong localClientId = syncManager.LocalClientId;
        foreach (var pair in syncManager.Snapshots)
        {
            ClientSnapshotState snapshot = pair.Value;
            if (snapshot.clientId == localClientId)
            {
                localSnapshot = snapshot;
            }

            if (snapshot.isTagger)
            {
                taggerSnapshot = snapshot;
                hasTagger = true;
            }
        }

        if (!hasTagger)
        {
            return false;
        }

        if (!worldView.TryGetPlayerView(taggerSnapshot.clientId, out taggerView))
        {
            return false;
        }

        return worldView.TryGetPlayerView(localClientId, out localView);
    }

    private IEnumerator FinishStageIntroRoutine()
    {
        worldView?.SetAllPlayerRenderVisible(true);
        if (cameraController != null)
        {
            cameraController.StopManualMotion();
            cameraController.SetGameplayZoom();
            cameraController.SetFollowEnabled(true);
        }

        if (countdownView != null)
        {
            countdownView.ShowMessage("Start");
            yield return new WaitForSeconds(0.5f);
            countdownView.Clear();
        }

        flowRoutine = null;
    }

    private void StartFlowRoutine(IEnumerator routine)
    {
        StopFlowRoutine();
        flowRoutine = StartCoroutine(routine);
    }

    private void StopFlowRoutine()
    {
        if (flowRoutine == null)
        {
            return;
        }

        StopCoroutine(flowRoutine);
        flowRoutine = null;
    }
}

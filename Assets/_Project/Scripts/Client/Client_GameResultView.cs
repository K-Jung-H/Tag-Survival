using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class Client_GameResultView : MonoBehaviour
{
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private ClientCanvasPanelController canvasPanelController;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button rematchButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private float endPacketFallbackSeconds = 3f;

    private bool hasShownResult;
    private bool isWaitingEndPacket;
    private float waitingEndStartedAt;
    private bool hasRequestedRematch;
    private bool hasReceivedRematchCommand;
    private float rematchWaitingStartedAt;

    private void OnEnable()
    {
        if (syncManager != null)
        {
            syncManager.GameEndReceived += OnGameEndReceived;
            syncManager.ResultCommandReceived += OnResultCommandReceived;
        }

        canvasPanelController?.SetGameResultVisible(false);
    }

    private void OnDisable()
    {
        if (syncManager != null)
        {
            syncManager.GameEndReceived -= OnGameEndReceived;
            syncManager.ResultCommandReceived -= OnResultCommandReceived;
        }
    }

    private void Update()
    {
        if (hasRequestedRematch
            && !hasReceivedRematchCommand
            && syncManager != null
            && syncManager.SyncMode != ClientSyncMode.LocalServer)
        {
            UpdateRematchWaitingStatus();
        }

        if (hasShownResult || syncManager == null)
        {
            return;
        }

        if (!syncManager.TryGetGameState(out ClientGameStateSnapshotState state) || !state.isGameEnded)
        {
            isWaitingEndPacket = false;
            return;
        }

        if (!isWaitingEndPacket)
        {
            isWaitingEndPacket = true;
            waitingEndStartedAt = Time.realtimeSinceStartup;
            return;
        }

        if (Time.realtimeSinceStartup - waitingEndStartedAt >= Mathf.Max(0.1f, endPacketFallbackSeconds))
        {
            ShowResultFromState(state, "Result confirmed locally.");
        }
    }

    private void OnGameEndReceived(ServerGameEndPacket packet)
    {
        if (hasShownResult)
        {
            return;
        }

        ShowResult(
            packet.gameModeType,
            packet.entries,
            packet.entryCount,
            "Game ended.");
    }

    private void OnResultCommandReceived(ServerResultCommandPacket packet)
    {
        switch (packet.command)
        {
            case GameResultCommand.RematchToRoom:
                hasReceivedRematchCommand = true;
                if (syncManager != null && syncManager.SyncMode == ClientSyncMode.LocalServer)
                {
                    SetButtonsInteractable(rematchInteractable: false, exitInteractable: false);
                    SetStatus("Returning to room...");
                    GameFlowManager.Instance?.ReturnToRoomFromStage();
                    return;
                }

                if (hasRequestedRematch)
                {
                    SetButtonsInteractable(rematchInteractable: false, exitInteractable: false);
                    SetStatus("Returning to room...");
                    GameFlowManager.Instance?.ReturnToRoomFromStage();
                    return;
                }

                SetButtonsInteractable(rematchInteractable: true, exitInteractable: true);
                SetStatus("Client Status: Host ready.");
                break;
            case GameResultCommand.RoomClosed:
                SetButtonsInteractable(rematchInteractable: false, exitInteractable: false);
                SetStatus("Room closed.");
                GameFlowManager.Instance?.ExitStageToOnline();
                break;
        }
    }

    public void ClickRematch()
    {
        if (syncManager == null)
        {
            return;
        }

        if (syncManager.SyncMode == ClientSyncMode.LocalServer)
        {
            SetButtonsInteractable(rematchInteractable: false, exitInteractable: false);
            SetStatus("Returning to room...");
            syncManager.SendResultChoice(GameResultChoice.Rematch);
            return;
        }

        if (hasReceivedRematchCommand)
        {
            SetButtonsInteractable(rematchInteractable: false, exitInteractable: false);
            SetStatus("Returning to room...");
            GameFlowManager.Instance?.ReturnToRoomFromStage();
            return;
        }

        hasRequestedRematch = true;
        rematchWaitingStartedAt = Time.realtimeSinceStartup;
        SetButtonsInteractable(rematchInteractable: false, exitInteractable: true);
        UpdateRematchWaitingStatus();
        syncManager.SendResultChoice(GameResultChoice.Rematch);
    }

    public void ClickExit()
    {
        SetButtonsInteractable(rematchInteractable: false, exitInteractable: false);

        if (syncManager != null && syncManager.SyncMode == ClientSyncMode.LocalServer)
        {
            SetStatus("Closing room...");
            syncManager.SendResultChoice(GameResultChoice.Exit);
            return;
        }

        SetStatus("Leaving...");
        GameFlowManager.Instance?.ExitStageToOnline();
    }

    private void ShowResultFromState(ClientGameStateSnapshotState state, string status)
    {
        ShowResult(state.gameModeType, state.entries, state.entryCount, status);
    }

    private void ShowResult(
        GameModeType gameModeType,
        GameStateEntryPacket[] entries,
        ushort entryCount,
        string status)
    {
        hasShownResult = true;
        hasRequestedRematch = false;
        hasReceivedRematchCommand = false;
        canvasPanelController?.SetGameResultVisible(true);
        SetButtonsInteractable(rematchInteractable: true, exitInteractable: true);
        SetStatus(status);

        bool isWinner = IsLocalWinner(gameModeType, entries, entryCount);
        if (resultText != null)
        {
            resultText.text = isWinner ? "You Win!" : "You Lose...";
        }
    }

    private bool IsLocalWinner(GameModeType gameModeType, GameStateEntryPacket[] entries, ushort entryCount)
    {
        if (syncManager == null || entries == null || entryCount <= 0)
        {
            return false;
        }

        int count = Mathf.Min(entryCount, entries.Length);
        uint winningScore = entries[0].scoreValue;
        for (int i = 1; i < count; i++)
        {
            uint score = entries[i].scoreValue;
            if (IsBetterScore(gameModeType, score, winningScore))
            {
                winningScore = score;
            }
        }

        ulong localClientId = syncManager.LocalClientId;
        for (int i = 0; i < count; i++)
        {
            if (entries[i].clientId == localClientId && entries[i].scoreValue == winningScore)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBetterScore(GameModeType gameModeType, uint candidate, uint current)
    {
        return gameModeType == GameModeType.CoinCollect
            ? candidate > current
            : candidate < current;
    }

    private void SetButtonsInteractable(bool rematchInteractable, bool exitInteractable)
    {
        if (rematchButton != null)
        {
            rematchButton.interactable = rematchInteractable;
        }

        if (exitButton != null)
        {
            exitButton.interactable = exitInteractable;
        }
    }

    private void UpdateRematchWaitingStatus()
    {
        int seconds = Mathf.FloorToInt(Mathf.Max(0f, Time.realtimeSinceStartup - rematchWaitingStartedAt));
        SetStatus($"Client Status: Waiting... {seconds}s");
    }

    private void SetStatus(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
    }
}

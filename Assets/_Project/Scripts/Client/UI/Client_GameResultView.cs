using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class Client_GameResultView : MonoBehaviour
{
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private ClientCanvasPanelController canvasPanelController;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button rematchButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private Color winMsgColor = Color.white;
    [SerializeField] private Color loseMsgColor = Color.white;

    private bool hasShownResult;
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

        bool isWinner = IsLocalWinner(gameModeType, entries, entryCount, out GameStateEntryPacket localEntry, out bool hasLocalEntry);
        Color messageColor = isWinner ? winMsgColor : loseMsgColor;
        if (resultText != null)
        {
            resultText.text = isWinner ? "You Win!" : "You Lose...";
            resultText.color = messageColor;
        }

        if (scoreText != null)
        {
            scoreText.text = FormatLocalScore(gameModeType, entries, entryCount, localEntry, hasLocalEntry);
            scoreText.color = messageColor;
        }
    }

    private bool IsLocalWinner(
        GameModeType gameModeType,
        GameStateEntryPacket[] entries,
        ushort entryCount,
        out GameStateEntryPacket localEntry,
        out bool hasLocalEntry)
    {
        localEntry = default;
        hasLocalEntry = false;
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
            if (entries[i].clientId != localClientId)
            {
                continue;
            }

            localEntry = entries[i];
            hasLocalEntry = true;
            return entries[i].scoreValue == winningScore;
        }

        return false;
    }

    private static bool IsBetterScore(GameModeType gameModeType, uint candidate, uint current)
    {
        return gameModeType == GameModeType.CoinCollect
            ? candidate > current
            : candidate < current;
    }

    private static string FormatLocalScore(
        GameModeType gameModeType,
        GameStateEntryPacket[] entries,
        ushort entryCount,
        GameStateEntryPacket localEntry,
        bool hasLocalEntry)
    {
        if (!hasLocalEntry)
        {
            return "Grade: #--";
        }

        int rank = ResolveLocalRank(gameModeType, entries, entryCount, localEntry);
        return rank > 0
            ? $"Grade: #{rank}"
            : "Grade: #--";
    }

    private static int ResolveLocalRank(
        GameModeType gameModeType,
        GameStateEntryPacket[] entries,
        ushort entryCount,
        GameStateEntryPacket localEntry)
    {
        if (entries == null || entryCount <= 0)
        {
            return -1;
        }

        int rank = 1;
        int count = Mathf.Min(entryCount, entries.Length);
        for (int i = 0; i < count; i++)
        {
            if (entries[i].clientId != localEntry.clientId
                && IsBetterScore(gameModeType, entries[i].scoreValue, localEntry.scoreValue))
            {
                rank++;
            }
        }

        return rank;
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

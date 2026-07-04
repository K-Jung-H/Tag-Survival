using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class Client_GameHudView : MonoBehaviour
{
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI leaderboardTitleText;
    [SerializeField] private List<TextMeshProUGUI> leaderboardRows = new();
    [SerializeField] private TextMeshProUGUI localPlayerOutsideRow;
    [SerializeField] private int leaderboardDisplayCount = 5;
    [SerializeField] private Color localTopRankColor = Color.green;
    [SerializeField] private Color taggerColor = new Color(1f, 80f / 255f, 80f / 255f, 1f);
    [SerializeField] private Color defaultTextColor = Color.white;

    private bool hasLoggedMissingReferences;
    private bool showLeaderboard = true;

    public void SetLeaderboardVisible(bool visible)
    {
        showLeaderboard = visible;
        SetLeaderboardObjectsVisible(visible);
    }

    // - Role: Set up needed links before start.
    private void Awake()
    {
        if (leaderboardTitleText != null)
        {
            leaderboardTitleText.text = "Win Leaderboard";
        }

        LogMissingReferencesOnce();
    }

    // - Role: Update this object after normal updates.
    private void LateUpdate()
    {
        if (syncManager == null || !syncManager.TryGetGameState(out ClientGameStateSnapshotState state))
        {
            ClearHud();
            return;
        }

        RenderTimer(state.remainingSeconds, state.isGameEnded);
        if (showLeaderboard)
        {
            RenderLeaderboard(state.entries, state.entryCount, state.gameModeType);
        }
    }

    // - Role: Render the game timer.
    private void RenderTimer(ushort remainingSeconds, bool isGameEnded)
    {
        if (timerText == null)
            return;

        timerText.text = isGameEnded
            ? FormatTimer(0)
            : FormatTimer(remainingSeconds);
    }

    // - Role: Render the leaderboard.
    private void RenderLeaderboard(GameStateEntryPacket[] entries, int entryCount, GameModeType gameModeType)
    {
        if (entries == null)
        {
            entries = System.Array.Empty<GameStateEntryPacket>();
        }

        int requestedCount = Mathf.Max(1, leaderboardDisplayCount);
        int validEntryCount = Mathf.Clamp(entryCount, 0, entries.Length);
        int displayLimit = Mathf.Min(requestedCount, validEntryCount);
        int renderedCount = Mathf.Min(displayLimit, leaderboardRows.Count);
        ulong localClientId = ResolveLocalClientId();
        int localRankIndex = FindClientRankIndex(entries, validEntryCount, localClientId);

        for (int i = 0; i < leaderboardRows.Count; i++)
        {
            TextMeshProUGUI row = leaderboardRows[i];
            if (row == null)
            {
                continue;
            }

            bool hasEntry = i < renderedCount;
            row.gameObject.SetActive(hasEntry);
            if (!hasEntry)
            {
                row.text = string.Empty;
                continue;
            }

            GameStateEntryPacket entry = entries[i];
            bool isLocalPlayer = entry.clientId == localClientId;
            row.text = FormatLeaderboardRow(i, entry, gameModeType);
            row.color = ResolveLeaderboardColor(entry, isLocalPlayer);
        }

        if (localPlayerOutsideRow == null)
            return;

        bool hasLocalPlayer = localRankIndex >= 0;
        localPlayerOutsideRow.gameObject.SetActive(hasLocalPlayer);
        if (!hasLocalPlayer)
        {
            localPlayerOutsideRow.text = string.Empty;
            return;
        }

        localPlayerOutsideRow.text = FormatLeaderboardRow(localRankIndex, entries[localRankIndex], gameModeType);
        localPlayerOutsideRow.color = ResolveLeaderboardColor(entries[localRankIndex], true);
    }

    // - Role: Clear HUD.
    private void ClearHud()
    {
        if (timerText != null)
        {
            timerText.text = "--:--";
        }

        for (int i = 0; i < leaderboardRows.Count; i++)
        {
            if (leaderboardRows[i] == null)
            {
                continue;
            }

            leaderboardRows[i].gameObject.SetActive(false);
            leaderboardRows[i].text = string.Empty;
        }

        if (localPlayerOutsideRow != null)
        {
            localPlayerOutsideRow.gameObject.SetActive(false);
            localPlayerOutsideRow.text = string.Empty;
        }
    }

    private void SetLeaderboardObjectsVisible(bool visible)
    {
        if (leaderboardTitleText != null)
        {
            leaderboardTitleText.gameObject.SetActive(visible);
        }

        for (int i = 0; i < leaderboardRows.Count; i++)
        {
            if (leaderboardRows[i] != null)
            {
                leaderboardRows[i].gameObject.SetActive(visible);
            }
        }

        if (localPlayerOutsideRow != null)
        {
            localPlayerOutsideRow.gameObject.SetActive(visible);
        }
    }

    // - Role: Format leaderboard row.
    private string FormatLeaderboardRow(int zeroBasedRank, GameStateEntryPacket entry, GameModeType gameModeType)
    {
        return gameModeType switch
        {
            GameModeType.CoinCollect => $"{zeroBasedRank + 1}. {ResolvePlayerName(entry.clientId)}  {entry.scoreValue}",
            _ => $"{zeroBasedRank + 1}. {ResolvePlayerName(entry.clientId)}  {Mathf.FloorToInt(entry.scoreValue / 1000f)}s"
        };
    }

    // - Role: Find player name.
    private string ResolvePlayerName(ulong clientId)
    {
        if (syncManager != null && syncManager.TryGetNickname(clientId, out string nickname))
        {
            return nickname;
        }

        return $"Client {clientId}";
    }

    // - Role: Format timer.
    private string FormatTimer(int totalSeconds)
    {
        int safeSeconds = Mathf.Max(0, totalSeconds);
        int minutes = safeSeconds / 60;
        int seconds = safeSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    // - Role: Find leaderboard color.
    private Color ResolveLeaderboardColor(GameStateEntryPacket entry, bool isLocalPlayer)
    {
        if (entry.isTagger)
        {
            return taggerColor;
        }

        return isLocalPlayer ? localTopRankColor : defaultTextColor;
    }

    // - Role: Find client rank index.
    private int FindClientRankIndex(GameStateEntryPacket[] entries, int entryCount, ulong clientId)
    {
        int count = Mathf.Clamp(entryCount, 0, entries.Length);
        for (int i = 0; i < count; i++)
        {
            if (entries[i].clientId == clientId)
            {
                return i;
            }
        }

        return -1;
    }

    // - Role: Find local client ID.
    private ulong ResolveLocalClientId()
    {
        return syncManager != null ? syncManager.LocalClientId : ulong.MaxValue;
    }

    // - Role: Log missing references once.
    private void LogMissingReferencesOnce()
    {
        if (hasLoggedMissingReferences)
            return;

        hasLoggedMissingReferences = true;

        if (syncManager == null)
        {
            Debug.LogWarning("[Client_GameHudView] SyncManager is not assigned.", this);
        }

        if (timerText == null)
        {
            Debug.LogWarning("[Client_GameHudView] TimerText is not assigned.", this);
        }

        if (leaderboardTitleText == null)
        {
            Debug.LogWarning("[Client_GameHudView] LeaderboardTitleText is not assigned.", this);
        }

        if (leaderboardRows.Count == 0)
        {
            Debug.LogWarning("[Client_GameHudView] LeaderboardRows are not assigned.", this);
        }

        if (localPlayerOutsideRow == null)
        {
            Debug.LogWarning("[Client_GameHudView] LocalPlayerOutsideRow is not assigned.", this);
        }
    }
}

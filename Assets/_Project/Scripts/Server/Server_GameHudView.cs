using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class Server_GameHudView : MonoBehaviour
{
    [SerializeField] private Server_GamePlayRunner gamePlayRunner;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI leaderboardTitleText;
    [SerializeField] private List<TextMeshProUGUI> leaderboardRows = new();
    [SerializeField] private Color taggerColor = new Color(1f, 80f / 255f, 80f / 255f, 1f);
    [SerializeField] private Color defaultTextColor = Color.white;
    [SerializeField] private float refreshInterval = 0.25f;

    private readonly List<GameStateEntryPacket> gameStateEntries = new();
    private float refreshTimer;
    private bool hasLoggedMissingReferences;

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
        refreshTimer += Time.deltaTime;
        if (refreshTimer < Mathf.Max(0.05f, refreshInterval))
        {
            return;
        }

        refreshTimer = 0f;
        RenderHud();
    }

    // - Role: Render the HUD.
    private void RenderHud()
    {
        if (gamePlayRunner == null || gamePlayRunner.GamePlay == null)
        {
            ClearRows();
            if (timerText != null)
            {
                timerText.text = "--s";
            }

            return;
        }

        Server_GamePlay gamePlay = gamePlayRunner.GamePlay;

        if (timerText != null)
        {
            timerText.text = gamePlay.IsGameEnded
                ? FormatTimer(0)
                : FormatTimer(Mathf.CeilToInt(gamePlay.RemainingSeconds));
        }

        gamePlay.CopyGameStateEntriesTo(gameStateEntries, taggersOnly: false);

        int displayCount = Mathf.Min(GameNetProtocol.MaxPlayers, gameStateEntries.Count, leaderboardRows.Count);
        for (int i = 0; i < leaderboardRows.Count; i++)
        {
            TextMeshProUGUI row = leaderboardRows[i];
            if (row == null)
            {
                continue;
            }

            bool hasEntry = i < displayCount;
            row.gameObject.SetActive(hasEntry);
            if (!hasEntry)
            {
                row.text = string.Empty;
                continue;
            }

            row.text = FormatLeaderboardRow(i, gameStateEntries[i]);
            row.color = gameStateEntries[i].isTagger ? taggerColor : defaultTextColor;
        }
    }

    // - Role: Clear rows.
    private void ClearRows()
    {
        for (int i = 0; i < leaderboardRows.Count; i++)
        {
            if (leaderboardRows[i] == null)
            {
                continue;
            }

            leaderboardRows[i].gameObject.SetActive(false);
            leaderboardRows[i].text = string.Empty;
        }
    }

    // - Role: Format leaderboard row.
    private string FormatLeaderboardRow(int zeroBasedRank, GameStateEntryPacket entry)
    {
        int seconds = Mathf.FloorToInt(entry.taggerTimeMs / 1000f);
        return $"{zeroBasedRank + 1}. {ResolvePlayerName(entry.clientId)}  {seconds}s";
    }

    // - Role: Find player name.
    private string ResolvePlayerName(ulong clientId)
    {
        if (gamePlayRunner != null
            && gamePlayRunner.GamePlay != null
            && gamePlayRunner.GamePlay.TryGetPlayer(clientId, out PlayerObject player)
            && !string.IsNullOrWhiteSpace(player.nickname))
        {
            return player.nickname;
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

    // - Role: Log missing references once.
    private void LogMissingReferencesOnce()
    {
        if (hasLoggedMissingReferences)
            return;

        hasLoggedMissingReferences = true;

        if (gamePlayRunner == null)
        {
            Debug.LogWarning("[Server_GameHudView] GamePlayRunner is not assigned.", this);
        }

        if (timerText == null)
        {
            Debug.LogWarning("[Server_GameHudView] TimerText is not assigned.", this);
        }

        if (leaderboardTitleText == null)
        {
            Debug.LogWarning("[Server_GameHudView] LeaderboardTitleText is not assigned.", this);
        }

        if (leaderboardRows.Count < GameNetProtocol.MaxPlayers)
        {
            Debug.LogWarning("[Server_GameHudView] LeaderboardRows should contain 10 rows for the server view.", this);
        }
    }
}

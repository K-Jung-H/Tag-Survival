using System.Collections.Generic;
using UnityEngine;

public sealed class TimeAttackGameMode : TagGameModeBase
{
    private readonly List<ulong> playerIds = new();

    public TimeAttackGameMode(float tagStunDurationSeconds)
        : base(tagStunDurationSeconds)
    {
    }

    public override GameModeType ModeType => GameModeType.TimeAttack;

    public override void CopyGameStateEntriesTo(
        IReadOnlyDictionary<ulong, PlayerObject> players,
        List<GameStateEntryPacket> target,
        bool taggersOnly)
    {
        target.Clear();
        if (players == null)
        {
            return;
        }

        foreach (var pair in players)
        {
            PlayerObject player = pair.Value;
            if (player == null || (taggersOnly && !player.isTagger))
            {
                continue;
            }

            target.Add(new GameStateEntryPacket
            {
                clientId = player.playerId,
                scoreValue = SecondsToMilliseconds(player.taggerAccumulatedTime),
                isTagger = player.isTagger
            });
        }

        target.Sort(CompareLeaderboardEntries);
    }

    protected override bool OnPlayingTick(
        Dictionary<ulong, PlayerObject> players,
        float deltaTime,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (players == null)
        {
            return false;
        }

        playerIds.Clear();
        foreach (ulong clientId in players.Keys)
        {
            playerIds.Add(clientId);
        }

        for (int i = 0; i < playerIds.Count; i++)
        {
            ulong clientId = playerIds[i];
            PlayerObject player = players[clientId];
            if (player != null && player.isTagger)
            {
                player.taggerAccumulatedTime += deltaTime;
            }
        }

        return false;
    }

    private static uint SecondsToMilliseconds(float seconds)
    {
        float milliseconds = Mathf.Max(0f, seconds) * 1000f;
        if (milliseconds >= uint.MaxValue)
        {
            return uint.MaxValue;
        }

        return (uint)Mathf.Round(milliseconds);
    }

    private static int CompareLeaderboardEntries(
        GameStateEntryPacket first,
        GameStateEntryPacket second)
    {
        int scoreComparison = first.scoreValue.CompareTo(second.scoreValue);
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }

        return first.clientId.CompareTo(second.clientId);
    }
}

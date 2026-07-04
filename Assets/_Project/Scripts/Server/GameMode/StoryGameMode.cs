using System.Collections.Generic;
using UnityEngine;

public sealed class StoryGameMode : IServerGameMode
{
    private float gameDurationSeconds = 180f;
    private float gameElapsedSeconds;

    public GameModeType ModeType => GameModeType.Story;
    public GamePhase Phase { get; private set; } = GamePhase.Preparing;
    public float GameDurationSeconds => gameDurationSeconds;
    public float GameElapsedSeconds => gameElapsedSeconds;
    public float RemainingSeconds => Mathf.Max(0f, gameDurationSeconds - gameElapsedSeconds);
    public bool IsSimulationStarted => Phase == GamePhase.Countdown || Phase == GamePhase.Playing;
    public bool IsGameStarted => Phase == GamePhase.Playing;
    public bool IsGameEnded => Phase == GamePhase.Ended;

    public void SetGameDurationSeconds(float durationSeconds)
    {
        gameDurationSeconds = Mathf.Max(0f, durationSeconds);
        if (Phase == GamePhase.Ended)
        {
            gameElapsedSeconds = gameDurationSeconds;
        }
    }

    public bool BeginCountdown(
        Dictionary<ulong, PlayerObject> players,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (Phase != GamePhase.Preparing)
        {
            return false;
        }

        Phase = GamePhase.Countdown;
        gameElapsedSeconds = 0f;
        return true;
    }

    public bool OnPlayerAdded(
        Dictionary<ulong, PlayerObject> players,
        ulong clientId,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        return false;
    }

    public bool StartGame(
        Dictionary<ulong, PlayerObject> players,
        ulong starterClientId,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (Phase != GamePhase.Countdown)
        {
            return false;
        }

        Phase = GamePhase.Playing;
        gameElapsedSeconds = 0f;
        eventQueue.QueueGameStarted(serverTick, starterClientId, eventPosition);

        if (gameDurationSeconds <= 0f)
        {
            EndGame(eventQueue, serverTick, eventPosition);
        }

        return true;
    }

    public bool OnPlayerRemoved(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject removedPlayer,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        return false;
    }

    public bool Tick(
        Dictionary<ulong, PlayerObject> players,
        float deltaTime,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (Phase != GamePhase.Playing)
        {
            return false;
        }

        gameElapsedSeconds = Mathf.Min(gameDurationSeconds, gameElapsedSeconds + Mathf.Max(0f, deltaTime));
        if (gameElapsedSeconds < gameDurationSeconds)
        {
            return false;
        }

        EndGame(eventQueue, serverTick, eventPosition);
        return true;
    }

    public bool OnPlayerCollision(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject first,
        PlayerObject second,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        return false;
    }

    public void CopyGameStateEntriesTo(
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
            if (player == null)
            {
                continue;
            }

            target.Add(new GameStateEntryPacket
            {
                clientId = player.playerId,
                scoreValue = SecondsToMilliseconds(gameElapsedSeconds),
                isTagger = false
            });
        }
    }

    public void CopyWorldObjectsTo(List<IWorldObject> target)
    {
    }

    public void CopyCoinSnapshotsTo(List<CoinSnapshotPacket> target)
    {
        target?.Clear();
    }

    private void EndGame(
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (Phase == GamePhase.Ended)
        {
            return;
        }

        Phase = GamePhase.Ended;
        gameElapsedSeconds = gameDurationSeconds;
        eventQueue.QueueGameEnded(serverTick, eventPosition);
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
}

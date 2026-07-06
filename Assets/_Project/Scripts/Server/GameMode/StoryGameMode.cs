using System.Collections.Generic;
using UnityEngine;

public sealed class StoryGameMode : IServerGameMode
{
    private float gameDurationSeconds = 180f;
    private float gameElapsedSeconds;
    private StoryGoalObject goalObject;
    private bool isCleared;
    private ulong clearedPlayerId;
    private float clearElapsedSeconds;
    private Vector2 clearPosition;

    public GameModeType ModeType => GameModeType.Story;
    public GamePhase Phase { get; private set; } = GamePhase.Preparing;
    public float GameDurationSeconds => gameDurationSeconds;
    public float GameElapsedSeconds => gameElapsedSeconds;
    public float RemainingSeconds => Mathf.Max(0f, gameDurationSeconds - gameElapsedSeconds);
    public bool IsSimulationStarted => Phase == GamePhase.Countdown || (Phase == GamePhase.Playing && !isCleared);
    public bool IsGameStarted => Phase == GamePhase.Playing;
    public bool IsGameEnded => Phase == GamePhase.Ended;
    public bool IsCleared => isCleared;
    public ulong ClearedPlayerId => clearedPlayerId;
    public float ClearElapsedSeconds => clearElapsedSeconds;

    public void ConfigureGoal(StoryGoalData goalData)
    {
        goalObject = new StoryGoalObject(this, goalData);
    }

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
        isCleared = false;
        clearedPlayerId = 0;
        clearElapsedSeconds = 0f;
        clearPosition = Vector2.zero;
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
            EndGame(eventQueue, serverTick, eventPosition, forceDurationElapsed: true);
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

        if (isCleared)
        {
            return false;
        }

        gameElapsedSeconds = Mathf.Min(gameDurationSeconds, gameElapsedSeconds + Mathf.Max(0f, deltaTime));
        if (gameElapsedSeconds < gameDurationSeconds)
        {
            return false;
        }

        EndGame(eventQueue, serverTick, eventPosition, forceDurationElapsed: true);
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
        if (target == null || goalObject == null || Phase != GamePhase.Playing)
        {
            return;
        }

        target.Add(goalObject);
    }

    public void CopyCoinSnapshotsTo(List<CoinSnapshotPacket> target)
    {
        target?.Clear();
    }

    private void EndGame(
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition,
        bool forceDurationElapsed)
    {
        if (Phase == GamePhase.Ended)
        {
            return;
        }

        Phase = GamePhase.Ended;
        if (forceDurationElapsed)
        {
            gameElapsedSeconds = gameDurationSeconds;
        }

        eventQueue.QueueGameEnded(serverTick, eventPosition);
    }

    public bool TriggerGoalClear(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject player,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        if (Phase != GamePhase.Playing || isCleared || player == null)
        {
            return false;
        }

        isCleared = true;
        clearedPlayerId = player.playerId;
        clearElapsedSeconds = gameElapsedSeconds;
        clearPosition = player.position;
        LockPlayers(players);
        return true;
    }

    public bool CompleteGoalClear(ServerGameEventQueue eventQueue, uint serverTick)
    {
        if (Phase != GamePhase.Playing || !isCleared)
        {
            return false;
        }

        EndGame(eventQueue, serverTick, clearPosition, forceDurationElapsed: false);
        return true;
    }

    private static void LockPlayers(Dictionary<ulong, PlayerObject> players)
    {
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

            player.velocity = Vector2.zero;
            player.input = Vector2.zero;
            player.buttons = PlayerInputButtons.None;
            player.interactionState = PlayerInteractionState.Locked(WorldObjectLayer.None, disablesPlayerPush: true);
            ServerPlayerSystem.UpdateRenderState(player);
            player.SyncCharacterStateMachine();
        }
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
